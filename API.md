# LMS API Contract

## Quy ước chung

- Base URL REST: `{API_ORIGIN}/api`. SignalR hub: `{API_ORIGIN}/hubs/chat`.
- Local development mặc định: `https://localhost:7086` hoặc `http://localhost:5258`; Swagger chỉ bật ở môi trường `Development` tại `/swagger`.
- JSON sử dụng `camelCase`.
- UUID là chuỗi UUID, ví dụ: `"00000000-0000-0000-0000-000000000001"`.
- Thời gian là UTC theo ISO-8601, ví dụ: `"2026-09-04T10:00:00Z"`.
- Enum trong JSON dùng **tên dạng chuỗi**, phân biệt đúng tên khai báo, ví dụ `"Student"`, `"Open"`, `"Published"`.
- Trừ các endpoint trong nhóm Authentication được ghi `Public`, mọi REST endpoint khác đều cần header `Authorization: Bearer <accessToken>`.
- Lỗi nghiệp vụ trả về `ProblemDetails` với HTTP status `400`, `401`, `403`, `404`, `409` hoặc `500`. Rate limit có thể trả `429`.
- Phân trang: `page` mặc định `1`, `pageSize` mặc định `20`, tối đa `100`.
- Upload file dùng `multipart/form-data`; không gửi JSON cho các endpoint này.
- File cũ/orphan được ghi idempotent vào bảng cleanup bền vững bằng atomic upsert trong cùng transaction nghiệp vụ; worker retry với backoff và không mất task khi process restart. Cleanup sau lỗi là best-effort, fallback xóa trực tiếp khi queue unavailable và không che exception gốc.
- Request không có body được ghi là `Không có body`.
- Các ví dụ JSON dưới đây là JSON hợp lệ và có thể dùng làm mẫu request/response.

Ví dụ lỗi để FrontEnd đọc `status`, `title`, `detail`:

```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "Only waiting enrollments can change status."
}
```

### Enum dùng ở FrontEnd

- `Role`: `Student`, `Teacher`, `Admin`.
- `AccountStatus`: `Active`, `Inactive`, `Disabled`, `Banned`.
- `CourseStatus`: `Open`, `Closed`.
- `EnrollmentStatus`: `Waiting`, `Accepted`, `Rejected`, `Cancelled`.
- `LessonStatus`: `Draft`, `Published`.
- `ExerciseStatus`: `Draft`, `Published`, `Closed`.
- `SubmissionStatus`: `Draft`, `Submitted`, `Graded`.
- `QuestionType`: `MultipleChoice`, `FillInBlank`.
- `QuestionDifficulty`: `Easy`, `Medium`, `Hard`.
- `ExaminationStatus`: `Draft`, `Published`, `Closed`.
- `ExaminationAttemptStatus`: `InProgress`, `Disconnected`, `Submitted`, `AutoSubmitted`.
- `ExaminationSubmitReason`: `StudentSubmitted`, `DurationExpired`, `DisconnectTimeout`, `MaxViolationsExceeded`, `ExaminationClosed`.
- `ExaminationViolationType`: `ExitFullscreen`, `TabChanged`, `WindowBlurred`, `CopyAttempt`, `PasteAttempt`, `RightClickAttempt`, `DevToolsSuspected`.

### Flow khởi tạo FrontEnd

1. Gọi `POST /api/auth/login` và lưu token pair.
2. Gửi access token bằng Bearer header. Khi API trả `401`, gọi `POST /api/auth/refresh` một lần rồi retry request cũ.
3. Access token chứa claim user ID, email, role và `auth_version`. Mỗi request xác thực đối chiếu trạng thái, role và version với user trong PostgreSQL. Hiện chưa có endpoint `/me`; FrontEnd có thể đọc claim user ID rồi gọi `GET /api/users/{userId}/profile`.
4. Dùng `role` để chọn màn hình; backend vẫn là nguồn quyết định cuối cùng cho các thao tác nghiệp vụ.

## Authentication

### `POST /api/auth/login`

**Quyền:** Public. Chỉ tài khoản `Active` mới đăng nhập được.

**Request body:**
```json
{
  "email": "student@example.com",
  "password": "password123"
}
```

**Response `200 OK`:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-09-04T13:00:00Z",
  "refreshToken": "opaque-refresh-token",
  "refreshTokenExpiresAt": "2026-10-04T12:00:00Z"
}
```

Client gửi token ở các request cần đăng nhập:
`Authorization: Bearer <accessToken>`.

Flow: Controller → tìm user theo email → verify password hash → kiểm tra `AccountStatus.Active` → tạo JWT chứa `NameIdentifier`, `Email`, `Role`, `auth_version` → trả access token.

### `POST /api/auth/refresh`

**Quyền:** Public, cần refresh token hợp lệ.

**Request body:**
```json
{
  "refreshToken": "opaque-refresh-token"
}
```

**Response `200 OK`:** giống login và cấp cặp token mới:
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-09-04T13:00:00Z",
  "refreshToken": "new-opaque-refresh-token",
  "refreshTokenExpiresAt": "2026-10-04T12:00:00Z"
}
```
Refresh token cũ bị xoá ngay khi dùng (rotation), hash được lưu Redis TTL 30 ngày. Session giữ `AuthVersion` lúc phát hành và phải khớp `users.auth_version` trong PostgreSQL. Nếu request lỗi do downstream tạm thời sau khi consume, token cũ được phục hồi trong một phút để retry; lỗi xác thực hoặc trạng thái account hợp lệ không phục hồi token.

### `POST /api/auth/register`

**Quyền:** Public. Tài khoản mới luôn có role `Student` và status `Inactive`.

**Request body:**

```json
{
  "email": "student@example.com",
  "password": "password123",
  "passwordConfirm": "password123"
}
```

**Response `201 Created`:**

```json
{
  "userId": "00000000-0000-0000-0000-000000000001",
  "email": "student@example.com",
  "status": "Inactive",
  "verificationCodeExpiresAt": "2026-09-04T12:15:00Z"
}
```

Flow: validate → hash password → PostgreSQL → acquire cooldown atomic → Redis mã 6 ký tự với TTL 15 phút → SMTP. Nếu SMTP lỗi, verification state/cooldown được dọn và cùng request đăng ký có thể retry trên account local `Inactive` mà không tạo user trùng. Register retry phải dùng đúng password đã đăng ký và chịu cùng cooldown một phút như resend; request trong cooldown không thay code hiện hành.

### `POST /api/auth/verify-email`

**Quyền:** Public.

**Request body:**

```json
{
  "email": "student@example.com",
  "verificationCode": "AB23CD"
}
```

**Response `200 OK`:**

```json
{
  "userId": "00000000-0000-0000-0000-000000000001",
  "email": "student@example.com",
  "status": "Active",
  "verifiedAt": "2026-09-04T12:01:00Z"
}
```

Chỉ account đang `Inactive` được redeem mã. Account `Active`, `Disabled`, `Banned` hoặc trạng thái không hợp lệ bị từ chối, kể cả khi mã được phát hành trước lúc trạng thái thay đổi. Mã sai/hết hạn trả `400`, mã chỉ dùng một lần và được consume atomically. Vượt quá 5 lần thử trả `409`.

Nếu cập nhật PostgreSQL thất bại sau khi consume, backend khôi phục mã và trạng thái account để client có thể thử lại.

### `POST /api/auth/resend-verification`

**Quyền:** Public, chỉ áp dụng cho local account đang `Inactive`.

**Request body:**

```json
{
  "email": "student@example.com"
}
```

**Response `204 No Content`:** Không có body. Cooldown gửi lại là 1 phút; lỗi SMTP giải phóng code/cooldown để request có thể retry ngay.

### `POST /api/auth/forgot-password`

**Quyền:** Public. Không tiết lộ email có tồn tại hay không.

**Request body:**

```json
{
  "email": "student@example.com"
}
```

**Response `200 OK`:**

```json
{
  "accepted": true,
  "codeExpiresAt": "2026-09-04T12:15:00Z"
}
```

Nếu SMTP lỗi, reset code và cooldown vừa tạo được xóa để request có thể retry ngay; API vẫn giữ response che giấu việc email có tồn tại hay không.

### `POST /api/auth/reset-password`

**Quyền:** Public, chỉ tài khoản local.

**Request body:**

```json
{
  "email": "student@example.com",
  "resetCode": "AB23CD",
  "newPassword": "newPassword123",
  "passwordConfirm": "newPassword123"
}
```

**Response `200 OK`:**

```json
{
  "userId": "00000000-0000-0000-0000-000000000001",
  "email": "student@example.com",
  "status": "Active",
  "resetAt": "2026-09-04T12:05:00Z"
}
```

Reset code dùng namespace Redis riêng `auth:password-reset:*`, TTL 15 phút và tối đa 5 lần thử.
Password hash mới và `AuthVersion` tăng được commit atomic trong PostgreSQL. Sau khi thành công, mọi access/refresh token mang version cũ bị từ chối. Nếu cập nhật PostgreSQL thất bại, backend khôi phục password hash/version cũ và reset code để client có thể thử lại; lỗi dọn attempts/cooldown sau commit không làm response reset thành lỗi.

### `GET /api/auth/oauth/Google/authorize`

**Quyền:** Public. Bắt đầu luồng đăng nhập Google và trả `authorizationUrl`.

```json
{
  "authorizationUrl": "https://accounts.google.com/o/oauth2/v2/auth?..."
}
```

Frontend browser phải gọi endpoint này với cookie credentials, sau đó điều hướng trang tới `authorizationUrl`.

State có TTL 10 phút, dùng một lần, dùng PKCE và được gắn với cookie bảo mật của trình duyệt khởi tạo. Nếu Google trả về email đã thuộc một tài khoản local nhưng external identity chưa được liên kết, callback trả `409 Conflict`. Người dùng phải đăng nhập tài khoản local rồi dùng luồng liên kết bên dưới.

### `GET /api/auth/oauth/Google/link/authorize`

**Quyền:** User đã đăng nhập. Bắt đầu luồng liên kết Google với chính tài khoản lấy từ JWT. Client không được truyền `userId` cần liên kết.

State lưu riêng mục đích `Link` và user ID đã xác thực. State đăng nhập không thể dùng để liên kết và state liên kết không thể dùng để đăng nhập.

**Response `200 OK`:** `{ "authorizationUrl": "https://accounts.google.com/o/oauth2/v2/auth?..." }`.

### `GET /api/auth/oauth/Google/callback?code={code}&state={state}`

**Quyền:** Public, nhưng bắt buộc có state, PKCE verifier và browser-binding cookie hợp lệ.

- Với state đăng nhập, response chỉ chứa application login code dùng một lần, TTL 2 phút; callback không trả access/refresh token và không đặt credential vào URL:

```json
{
  "loginCode": "opaque-one-time-code",
  "expiresAt": "2026-09-04T12:02:00Z"
}
```

- Với state liên kết, response là:

```json
{
  "userId": "00000000-0000-0000-0000-000000000001",
  "provider": "Google",
  "externalEmail": "student@gmail.com"
}
```

Một external identity chỉ được thuộc một tài khoản; mỗi tài khoản chỉ được liên kết một identity cho mỗi provider.

### `POST /api/auth/login-code`

**Quyền:** Public. Đổi application login code lấy token pair. Code được lưu bằng key SHA-256, hết hạn sau 2 phút và được consume atomically nên chỉ một request đồng thời có thể thành công. Lỗi downstream tạm thời sau consume phục hồi code trong một phút để client retry.

```json
{
  "loginCode": "opaque-one-time-code"
}
```

**Response `200 OK`:** Token pair giống `POST /api/auth/login`. Code sai, hết hạn hoặc đã dùng trả `401 Unauthorized`.

## Users

### User profile object

Các endpoint trả user profile dùng object sau:

```json
{
  "id": "00000000-0000-0000-0000-000000000002",
  "email": "teacher@example.com",
  "fullName": "Teacher",
  "phone": "0900000000",
  "role": "Teacher",
  "status": "Active",
  "avatarUrl": null,
  "createdAt": "2026-09-04T10:00:00Z"
}
```

### `GET /api/users/{userId}/profile`

**Quyền:** User đã đăng nhập xem chính mình; Admin xem được mọi user. **Response `200 OK`:** User profile object.

### `GET /api/users?search=&page=1&pageSize=20`

**Quyền:** Admin. **Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000002",
      "email": "teacher@example.com",
      "fullName": "Teacher",
      "phone": null,
      "role": "Teacher",
      "status": "Active",
      "avatarUrl": null,
      "createdAt": "2026-09-04T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

### `POST /api/users`

**Quyền:** Admin. Có thể tạo role `Student`, `Teacher` hoặc `Admin`; tài khoản được tạo ở trạng thái `Active`.

**Request body:**

```json
{
  "email": "teacher@example.com",
  "fullName": "Teacher",
  "phone": "0900000000",
  "password": "password123",
  "role": "Teacher"
}
```

**Response `201 Created`:** User profile object.

### `PUT /api/users/{userId}`

**Quyền:** Admin.

**Request body:**

```json
{
  "fullName": "Updated Name",
  "phone": "0900000000",
  "role": "Teacher"
}
```

**Response `200 OK`:** User profile object.

Admin không được sửa admin khác. Admin cuối cùng không thể tự đổi role khỏi `Admin`; nếu còn admin khác thì được tự đổi role.

Teacher đang sở hữu course hoặc question bank không thể đổi sang role khác cho tới khi tài nguyên được chuyển quyền.

### `PATCH /api/users/{userId}/status`

**Quyền:** Admin.

**Request body:**

```json
{
  "status": "Disabled"
}
```

**Response `200 OK`:** User profile object.

Admin active cuối cùng không thể tự chuyển sang `Inactive`, `Disabled` hoặc `Banned`. Việc kiểm tra chạy trong transaction serializable.

Đổi role hoặc status thành công tăng `AuthVersion` trong cùng transaction và thu hồi access/refresh token đã phát hành trước đó.

Teacher đang sở hữu course hoặc question bank không thể bị chuyển khỏi trạng thái `Active`; phải chuyển quyền sở hữu tài nguyên trước.

## Courses

### Course object

```json
{
  "id": "00000000-0000-0000-0000-000000000003",
  "name": "C# Basics",
  "description": "Introduction",
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "maxStudents": 30,
  "status": "Open",
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z",
  "deletedAt": null
}
```

### `POST /api/courses`

**Quyền:** Teacher tự tạo; Admin tạo cho teacher đang `Active`.

**Request body:**

```json
{
  "name": "C# Basics",
  "description": "Introduction",
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "maxStudents": 30,
  "status": "Open"
}
```

**Response `201 Created`:** Course object.

### `GET /api/courses?search=&status=&teacherId=&includeDeleted=false&page=1&pageSize=20`

**Quyền:** User đã đăng nhập. Student luôn chỉ nhận course `Open`, kể cả khi gửi `status=Closed`. Teacher/Admin dùng được bộ lọc `status`. Course đã xóa mềm chỉ được trả về khi Admin truyền `includeDeleted=true`.

**Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000003",
      "name": "C# Basics",
      "description": "Introduction",
      "teacherId": "00000000-0000-0000-0000-000000000002",
      "maxStudents": 30,
      "status": "Open",
      "createdAt": "2026-09-04T10:00:00Z",
      "modifiedAt": "2026-09-04T10:00:00Z",
      "deletedAt": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

### `GET /api/courses/{courseId}`

**Quyền:** User đã đăng nhập. Student xem được course `Open`; course `Closed` chỉ xem được khi enrollment đã `Accepted`. Query `includeDeleted=true` chỉ dành cho Admin. **Response `200 OK`:** Course object.

### `GET /api/courses/mine`

**Quyền:** Student. Trả các course có enrollment `Accepted` của chính student, bao gồm cả course `Closed`, phục vụ màn hình lớp đang học và lịch sử. Course đã soft-delete không được trả.

### `PUT /api/courses/{courseId}`

**Quyền:** Teacher sở hữu course hoặc Admin.

`maxStudents` không được nhỏ hơn số enrollment đang `Accepted`.

**Request body:**

```json
{
  "name": "Advanced C#",
  "description": "Updated",
  "maxStudents": 40,
  "status": "Open"
}
```

**Response `200 OK`:** Course object.

### `DELETE /api/courses/{courseId}`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `204 No Content`:** Không có body; thao tác xóa là xóa mềm.

### `POST /api/courses/{courseId}/restore`

**Quyền:** Teacher sở hữu course hoặc Admin. **Request body:** Không có body. **Response `200 OK`:** Course object.

## Enrollments

### Enrollment object

```json
{
  "id": "00000000-0000-0000-0000-000000000004",
  "studentId": "00000000-0000-0000-0000-000000000001",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "status": "Waiting",
  "student": null,
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/courses/{courseId}/enrollments`

**Quyền:** Student đang `Active`. Nếu enrollment cũ là `Rejected` hoặc `Cancelled`, backend chuyển lại chính record đó về `Waiting`; không tạo duplicate. **Response `201 Created`:** Enrollment object với `status: "Waiting"`.

### `GET /api/enrollments/mine`

**Quyền:** Student. Trả toàn bộ enrollment hiện tại của chính student, gồm `Waiting`, `Accepted`, `Rejected`, `Cancelled`, để khôi phục trạng thái sau reload.

### `GET /api/courses/{courseId}/enrollments?page=1&pageSize=20`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000004",
      "studentId": "00000000-0000-0000-0000-000000000001",
      "courseId": "00000000-0000-0000-0000-000000000003",
      "status": "Waiting",
      "student": {
        "id": "00000000-0000-0000-0000-000000000001",
        "fullName": "Student",
        "email": "student@example.com"
      },
      "createdAt": "2026-09-04T10:00:00Z",
      "modifiedAt": "2026-09-04T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

Trường `student` được điền trong endpoint danh sách để teacher nhận diện người đăng ký mà không cần quyền đọc toàn bộ user profile. Các response thao tác đơn lẻ có thể trả `student: null`.

### `POST /api/enrollments/{enrollmentId}/accept`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ accept khi course còn `Open` và chưa đủ `maxStudents`. **Response `200 OK`:** Enrollment object với `status: "Accepted"`.

### `POST /api/enrollments/{enrollmentId}/reject`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `200 OK`:** Enrollment object với `status: "Rejected"`.

### `POST /api/enrollments/{enrollmentId}/cancel`

**Quyền:** Student sở hữu enrollment đang `Waiting` hoặc `Accepted`. Với `Accepted`, thao tác này có nghĩa rời course. **Response `200 OK`:** Enrollment object với `status: "Cancelled"`.

### `POST /api/enrollments/{enrollmentId}/revoke`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ áp dụng cho enrollment `Accepted`; dùng để loại student khỏi course. **Response `200 OK`:** Enrollment object với `status: "Cancelled"`.

## Question Banks

### Question bank object

```json
{
  "id": "00000000-0000-0000-0000-000000000005",
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "name": "C# Questions",
  "description": "Basics",
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/question-banks`

**Quyền:** Teacher tạo cho mình; Admin quản lý.

**Request body:**

```json
{
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "name": "C# Questions",
  "description": "Basics"
}
```

**Response `200 OK`:** Question bank object.

### `PUT /api/question-banks/{questionBankId}`

**Quyền:** Teacher sở hữu question bank hoặc Admin.

**Request body:**

```json
{
  "name": "Updated",
  "description": "Updated"
}
```

**Response `200 OK`:** Question bank object.

### `DELETE /api/question-banks/{questionBankId}`

**Quyền:** Teacher sở hữu question bank hoặc Admin. Trả `409` nếu bank đang được selection rule của examination tham chiếu; nếu không, xóa cả source questions trong bank. **Response `204 No Content`**.

### `GET /api/question-banks?teacherId={teacherId}`

**Quyền:** Teacher hoặc Admin. Teacher bỏ `teacherId` để lấy bank của mình và không được xem bank của teacher khác. Admin nên truyền `teacherId`; nếu bỏ, backend tìm bank theo ID của chính admin và thường trả mảng rỗng. **Response `200 OK`:** Mảng question bank object.

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000005",
    "teacherId": "00000000-0000-0000-0000-000000000002",
    "name": "C# Questions",
    "description": "Basics",
    "createdAt": "2026-09-04T10:00:00Z",
    "modifiedAt": "2026-09-04T10:00:00Z"
  }
]
```

### Question object: multiple choice

```json
{
  "id": "00000000-0000-0000-0000-000000000006",
  "questionBankId": "00000000-0000-0000-0000-000000000005",
  "content": "2 + 2 = ?",
  "type": "MultipleChoice",
  "difficulty": "Easy",
  "shuffleOptions": true,
  "options": [
    {
      "id": "00000000-0000-0000-0000-000000000007",
      "content": "3",
      "order": 1,
      "isCorrect": false
    },
    {
      "id": "00000000-0000-0000-0000-000000000008",
      "content": "4",
      "order": 2,
      "isCorrect": true
    }
  ],
  "answers": [],
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/question-banks/{questionBankId}/multiple-choice`

**Quyền:** Teacher sở hữu question bank hoặc Admin. Phải có chính xác một option có `isCorrect: true`.

**Request body:**

```json
{
  "content": "2 + 2 = ?",
  "difficulty": "Easy",
  "shuffleOptions": true,
  "options": [
    {
      "content": "3",
      "isCorrect": false
    },
    {
      "content": "4",
      "isCorrect": true
    }
  ]
}
```

**Response `200 OK`:** Question object dạng multiple choice.

### Question object: fill in blank

```json
{
  "id": "00000000-0000-0000-0000-000000000009",
  "questionBankId": "00000000-0000-0000-0000-000000000005",
  "content": "Capital of France: ___",
  "type": "FillInBlank",
  "difficulty": "Easy",
  "shuffleOptions": false,
  "options": [],
  "answers": [
    {
      "id": "00000000-0000-0000-0000-000000000010",
      "blankOrder": 1,
      "expectedAnswer": "Paris"
    }
  ],
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/question-banks/{questionBankId}/fill-in-blank`

**Quyền:** Teacher sở hữu question bank hoặc Admin.

**Request body:**

```json
{
  "content": "Capital of France: ___",
  "difficulty": "Easy",
  "answers": [
    {
      "blankOrder": 1,
      "expectedAnswer": "Paris"
    }
  ]
}
```

**Response `200 OK`:** Question object dạng fill in blank.

### `GET /api/question-banks/{questionBankId}/questions`

**Quyền:** Teacher sở hữu question bank hoặc Admin. **Response `200 OK`:** Mảng question object. Mỗi phần tử có shape của một trong hai question object ở trên.

### `PUT /api/questions/{questionId}`

**Quyền:** Teacher sở hữu question bank hoặc Admin. Chỉ sửa `content` và `difficulty`; loại câu hỏi, options và answer keys không đổi.

```json
{
  "content": "Updated question",
  "difficulty": "Medium"
}
```

**Response `200 OK`:** Question object. Examination đã tạo dùng snapshot nên không bị thay đổi.

### `DELETE /api/questions/{questionId}`

**Quyền:** Teacher sở hữu question bank hoặc Admin. Xóa source question; snapshot trong examination đã tạo vẫn giữ nguyên. **Response `204 No Content`**.

## Course Content

Teacher sở hữu course hoặc Admin được tạo/sửa nội dung. Student có enrollment `Accepted` được đọc nội dung dành cho học viên.

### Lesson object

```json
{
  "id": "00000000-0000-0000-0000-000000000011",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "name": "Variables",
  "content": "...",
  "order": 1,
  "status": "Draft",
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/courses/{courseId}/lessons`

**Quyền:** Teacher sở hữu course hoặc Admin.

**Request body:**

```json
{
  "name": "Variables",
  "content": "...",
  "order": 1,
  "status": "Draft"
}
```

**Response `201 Created`:** Lesson object.

### `GET /api/courses/{courseId}/lessons`

**Quyền:** Teacher sở hữu course, Admin hoặc student có enrollment `Accepted`.

**Response `200 OK`:** Mảng lesson object, sắp theo `order`. Teacher/Admin thấy cả `Draft` và `Published`; student chỉ thấy `Published`.

### `GET /api/lessons/{lessonId}`

**Quyền:** Giống endpoint danh sách lesson. Student truy cập lesson `Draft` nhận `404`.

**Response `200 OK`:** Lesson object.

### `PUT /api/lessons/{lessonId}`

**Quyền:** Teacher sở hữu course hoặc Admin. Trong một course không được trùng `order`.

**Request body:**

```json
{
  "name": "Variables",
  "content": "Updated content",
  "order": 1,
  "status": "Published"
}
```

**Response `200 OK`:** Lesson object.

### `DELETE /api/lessons/{lessonId}`

**Quyền:** Teacher sở hữu course hoặc Admin. Xóa lesson, metadata material và đưa file vật lý vào hàng đợi cleanup. **Response `204 No Content`**.

### Material object

```json
{
  "id": "00000000-0000-0000-0000-000000000012",
  "lessonId": "00000000-0000-0000-0000-000000000011",
  "fileName": "lesson.pdf",
  "objectKey": "courses/00000000-0000-0000-0000-000000000003/lesson.pdf",
  "contentType": "application/pdf",
  "fileSize": 12345,
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

`objectKey` là khóa lưu trữ nội bộ. FrontEnd không ghép URL từ trường này; dùng endpoint `content` bên dưới.

### `GET /api/lessons/{lessonId}/materials`

**Quyền:** Teacher sở hữu course, Admin hoặc student có enrollment `Accepted`. Student chỉ đọc material của lesson `Published`.

**Response `200 OK`:** Mảng material object.

### `GET /api/materials/{materialId}/content?download=false`

**Quyền:** Giống endpoint danh sách material.

**Response `200 OK`:** Binary stream với `Content-Type` của file. `download=false` hoặc bỏ query để browser mở inline; `download=true` để trả file với tên gốc trong `Content-Disposition`.

### `POST /api/lessons/{lessonId}/materials`

**Quyền:** Teacher sở hữu course hoặc Admin.

**Request:** `multipart/form-data`, field bắt buộc là `file` không rỗng và nhỏ hơn 20 MB. **Response `200 OK`:** Material object.

### `DELETE /api/materials/{materialId}`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `204 No Content`:** Không có body.

### Exercise object

```json
{
  "id": "00000000-0000-0000-0000-000000000013",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "title": "Assignment 1",
  "description": "...",
  "dueAt": "2026-09-20T23:59:00Z",
  "status": "Draft",
  "attachment": null,
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

`attachment` khi có file:

```json
{
  "id": "00000000-0000-0000-0000-000000000014",
  "fileName": "assignment.pdf",
  "objectKey": "exercises/00000000-0000-0000-0000-000000000013/assignment.pdf",
  "contentType": "application/pdf",
  "fileSize": 12345
}
```

### `POST /api/courses/{courseId}/exercises`

**Quyền:** Teacher sở hữu course hoặc Admin.

**Request body:**

```json
{
  "title": "Assignment 1",
  "description": "...",
  "dueAt": "2026-09-20T23:59:00Z"
}
```

Exercise luôn được tạo ở trạng thái `Draft`; `dueAt` nếu có phải là UTC. **Response `201 Created`:** Exercise object.

`title` tối đa 200 ký tự; `description` tối đa 4000 ký tự. Input vượt giới hạn trả `400` trước khi ghi DB.

### `GET /api/courses/{courseId}/exercises`

**Quyền:** Teacher sở hữu course, Admin hoặc student có enrollment `Accepted`.

**Response `200 OK`:** Mảng exercise object. Teacher/Admin thấy mọi trạng thái; student thấy exercise `Published` và `Closed` để tiếp tục truy cập lịch sử bài nộp/điểm, nhưng không thấy bản nháp.

### `GET /api/exercises/{exerciseId}`

**Quyền:** Giống endpoint danh sách exercise, gồm quyền xem exercise `Closed` của student đã được nhận vào course. **Response `200 OK`:** Exercise object.

### `GET /api/exercises/{exerciseId}/attachment?download=false`

**Quyền:** Giống endpoint xem exercise.

**Response `200 OK`:** Binary stream. `download=false` hoặc bỏ query để mở inline; `download=true` để tải với tên file gốc. Trả `404` nếu exercise chưa có attachment.

### `PUT /api/exercises/{exerciseId}`

**Quyền:** Teacher sở hữu course hoặc Admin. **Request body:** Cùng shape với request tạo exercise. **Response `200 OK`:** Exercise object.

### `POST /api/exercises/{exerciseId}/attachment`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ exercise `Draft` được thêm hoặc thay attachment; file đã phát hành là bất biến. **Request:** `multipart/form-data`, field bắt buộc là `file` nhỏ hơn 20 MB. Upload mới sẽ thay file cũ. **Response `200 OK`:** Exercise object.

### `DELETE /api/exercises/{exerciseId}`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ exercise `Draft` được xóa để không làm mất submission đã phát hành; attachment được đưa vào hàng đợi cleanup. **Response `204 No Content`**.

### `POST /api/exercises/{exerciseId}/publish`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ exercise `Draft` được publish; nếu có `dueAt` thì deadline phải còn ở tương lai. **Response `200 OK`:** Exercise object với status `Published`.

### `POST /api/exercises/{exerciseId}/close`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ exercise `Published` được close. Student vẫn xem submission/điểm cũ nhưng không thể upload, submit hoặc unsubmit. **Response `200 OK`:** Exercise object với status `Closed`.

### Submission object

```json
{
  "id": "00000000-0000-0000-0000-000000000015",
  "exerciseId": "00000000-0000-0000-0000-000000000013",
  "studentId": "00000000-0000-0000-0000-000000000001",
  "status": "Draft",
  "submittedAt": null,
  "grade": null,
  "feedback": null,
  "gradedAt": null,
  "gradedById": null,
  "attachment": {
    "id": "00000000-0000-0000-0000-000000000016",
    "fileName": "answer.pdf",
    "objectKey": "submissions/00000000-0000-0000-0000-000000000015/answer.pdf",
    "contentType": "application/pdf",
    "fileSize": 10000
  },
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/exercises/{exerciseId}/submissions`

**Quyền:** Student có enrollment `Accepted`.

**Request:** `multipart/form-data`, field bắt buộc là `file` nhỏ hơn 20 MB. Tạo submission `Draft` nếu chưa có; upload lại chỉ thay file của draft hiện tại. Không được upload sau `dueAt` hoặc khi submission không còn là `Draft`. Upload/submit đồng thời được tuần tự hóa; nếu trạng thái đã đổi hoặc row-version xung đột, API trả `409 Conflict` và file mới được đưa vào cleanup. **Response `200 OK`:** Submission object.

### `GET /api/exercises/{exerciseId}/my-submission`

**Quyền:** Student có enrollment `Accepted`. Trả submission hiện tại của chính student để khôi phục trạng thái sau reload; trả `404` nếu chưa từng upload.

**Response `200 OK`:** Submission object, bao gồm điểm và nhận xét khi status là `Graded`.

### `GET /api/exercises/{exerciseId}/submissions?page=1&pageSize=20`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ trả submission đã `Submitted` hoặc `Graded`; không hiển thị draft của student.

**Response `200 OK`:** Paged result chứa các Submission object.

### `GET /api/submissions/{submissionId}/content?download=false`

**Quyền:** Student sở hữu submission, teacher sở hữu course hoặc Admin.

**Response `200 OK`:** Binary stream của attachment bài nộp. `download=true` trả kèm tên file gốc.

### `POST /api/submissions/{submissionId}/submit`

**Quyền:** Student sở hữu submission. Chỉ submit draft đã có attachment và chưa quá `dueAt`.

**Request body:** Không có body. **Response `200 OK`:** Submission object với status `Submitted` và `submittedAt` khác `null`.

### `POST /api/submissions/{submissionId}/unsubmit`

**Quyền:** Student sở hữu submission. Chỉ unsubmit submission đang `Submitted` và chưa quá `dueAt`.

**Request body:** Không có body. **Response `200 OK`:** Submission object với status `Draft` và `submittedAt: null`.

Submission đã `Graded` không thể unsubmit hoặc thay file.

Upload, submit, unsubmit và grade áp dụng transaction serializable cùng optimistic concurrency. Request thua trong xung đột đồng thời trả `409 Conflict`, không ghi đè trạng thái hoặc điểm của request đã hoàn tất.

### `PUT /api/submissions/{submissionId}/grade`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ chấm submission đã `Submitted`; cho phép cập nhật lại điểm của submission `Graded`.

```json
{
  "grade": 8.5,
  "feedback": "Good work"
}
```

`grade` nằm trong khoảng `0..10`; `feedback` tối đa 4000 ký tự. **Response `200 OK`:** Submission object với status `Graded`.

## Examination Authoring

Luồng teacher: tạo exam `Draft` và snapshot câu hỏi → chỉnh cấu hình khi còn `Draft` → publish → close. Khi tạo, mỗi rule chọn ngẫu nhiên `questionCount` câu theo question bank và difficulty; question bank phải thuộc teacher của course và phải đủ số câu.

### Examination object

```json
{
  "id": "00000000-0000-0000-0000-000000000017",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "title": "Midterm",
  "description": "...",
  "status": "Published",
  "startAt": "2026-09-10T08:00:00Z",
  "dueAt": "2026-09-10T10:00:00Z",
  "durationMinutes": 90,
  "shuffleQuestions": true,
  "showScoreImmediately": true,
  "security": {
    "requireFullscreen": true,
    "fullscreenGraceSeconds": 10,
    "maxViolations": 3,
    "blockCopyPaste": true,
    "blockRightClick": true,
    "detectTabChange": true,
    "detectDevTools": false,
    "maxDisconnectMinutes": 5
  },
  "questionSelectionRules": [
    {
      "id": "00000000-0000-0000-0000-000000000018",
      "questionBankId": "00000000-0000-0000-0000-000000000005",
      "difficulty": "Medium",
      "questionCount": 20
    }
  ],
  "questionCount": 20,
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/courses/{courseId}/examinations`

**Quyền:** Teacher sở hữu course hoặc Admin.

**Request body:**

```json
{
  "title": "Midterm",
  "description": "...",
  "startAt": "2026-09-10T08:00:00Z",
  "dueAt": "2026-09-10T10:00:00Z",
  "durationMinutes": 90,
  "shuffleQuestions": true,
  "showScoreImmediately": true,
  "security": {
    "requireFullscreen": true,
    "fullscreenGraceSeconds": 10,
    "maxViolations": 3,
    "blockCopyPaste": true,
    "blockRightClick": true,
    "detectTabChange": true,
    "detectDevTools": false,
    "maxDisconnectMinutes": 5
  },
  "questionSelectionRules": [
    {
      "questionBankId": "00000000-0000-0000-0000-000000000005",
      "difficulty": "Medium",
      "questionCount": 20
    }
  ]
}
```

`startAt` và `dueAt` bắt buộc là timestamp UTC (hậu tố `Z`); giá trị local hoặc không có timezone bị từ chối với `400`.

**Response `201 Created`:** Examination object.

### `GET /api/courses/{courseId}/examinations`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `200 OK`:** Mảng examination object, gồm cả `Draft`, `Published`, `Closed`.

### `GET /api/examinations/{examinationId}`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `200 OK`:** Examination object.

### `GET /api/courses/{courseId}/examinations/available`

**Quyền:** Student có enrollment `Accepted`; teacher sở hữu course và Admin cũng dùng được.

**Response `200 OK`:** Mảng student examination object. Với student, endpoint chỉ loại `Draft`; exam chưa tới giờ, đã quá hạn hoặc `Closed` vẫn có thể xuất hiện để hiển thị lịch sử/lịch thi. FrontEnd dùng `status`, `startAt`, `dueAt` để quyết định nhãn hiển thị; backend kiểm tra lại khi bắt đầu thi.

Student examination object giống examination object nhưng không có `questionSelectionRules`; vẫn có `questionCount` và cấu hình `security`.

### `GET /api/examinations/{examinationId}/student`

**Quyền:** Student có enrollment `Accepted`; không trả exam `Draft` cho student.

**Response `200 OK`:** Student examination object.

### `PUT /api/examinations/{examinationId}`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ exam `Draft` được sửa. **Request body:** Giống request tạo examination nhưng không có `questionSelectionRules`. **Response `200 OK`:** Examination object.

### `POST /api/examinations/{examinationId}/publish`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ publish exam `Draft`, phải có ít nhất một câu hỏi và `dueAt` còn ở tương lai.

**Request body:** Không có body. **Response `200 OK`:** Examination object với status `Published`.

### `POST /api/examinations/{examinationId}/close`

**Quyền:** Teacher sở hữu course hoặc Admin. Chỉ close exam `Published`.

Khi đóng, backend chấm và auto-submit ngay mọi attempt `InProgress`/`Disconnected` với `submitReason=ExaminationClosed` trong cùng transaction.

**Request body:** Không có body. **Response `200 OK`:** Examination object với status `Closed`.

## Examination Attempt

**Quyền:** Student có enrollment `Accepted`. Auto-submit do server hoặc worker thực hiện.

### Examination attempt object

```json
{
  "id": "00000000-0000-0000-0000-000000000019",
  "examinationId": "00000000-0000-0000-0000-000000000017",
  "studentId": "00000000-0000-0000-0000-000000000001",
  "status": "InProgress",
  "submitReason": null,
  "startedAt": "2026-09-10T08:00:00Z",
  "submittedAt": null,
  "disconnectedAt": null,
  "remainingSeconds": 5400,
  "lastActivityAt": "2026-09-10T08:15:00Z",
  "violationCount": 0,
  "correctAnswers": null,
  "totalQuestions": null,
  "score": null
}
```

`correctAnswers`, `totalQuestions` và `score` luôn là `null` khi attempt còn `InProgress`/`Disconnected`. Sau khi hoàn thành, các trường này mới tuân theo `showScoreImmediately` hoặc thời điểm phát hành điểm.

### `POST /api/examination-attempts/{examinationId}/start`

**Request body:** Không có body. Chỉ bắt đầu được exam `Published` trong khoảng `startAt <= now < dueAt`, student phải còn enrollment `Accepted`, và không được có attempt active của cùng exam.

**Response `200 OK`:** Examination attempt object. FrontEnd phải lưu `attempt.id` để gọi các endpoint tiếp theo.

### `GET /api/examinations/{examinationId}/my-attempt`

**Quyền:** Student có enrollment `Accepted` của course chứa examination.

Ưu tiên trả attempt đang `InProgress`/`Disconnected` của chính student; nếu không còn attempt active thì trả attempt hoàn thành gần nhất. FrontEnd gọi endpoint này khi reload để lấy lại `attempt.id`; nếu attempt đang `Disconnected` thì gọi tiếp endpoint `reconnect`. Trả `404` nếu student chưa từng bắt đầu examination.

**Response `200 OK`:** Examination attempt object.

### `GET /api/examination-attempts/{attemptId}/questions`

**Response `200 OK`:** Mảng câu hỏi snapshot dành cho student, không chứa đáp án đúng:

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000021",
    "content": "2 + 2 = ?",
    "type": "MultipleChoice",
    "order": 0,
    "options": [
      {
        "id": "00000000-0000-0000-0000-000000000008",
        "content": "4",
        "order": 0
      }
    ]
  }
]
```

Thứ tự shuffle ổn định theo `attemptId`; gọi lại endpoint vẫn nhận cùng thứ tự. `order` của câu hỏi/option là zero-based.

### Student answer object: multiple choice

```json
{
  "id": "00000000-0000-0000-0000-000000000020",
  "examinationQuestionId": "00000000-0000-0000-0000-000000000021",
  "questionType": "MultipleChoice",
  "selectedOptionId": "00000000-0000-0000-0000-000000000008",
  "fillInBlankAnswers": [],
  "modifiedAt": "2026-09-10T08:15:00Z"
}
```

### `PUT /api/examination-attempts/{attemptId}/questions/{questionId}/multiple-choice?selectedOptionId={optionId}`

**Request body:** Không có body. **Response `200 OK`:** Student answer object dạng multiple choice.

### Student answer object: fill in blank

```json
{
  "id": "00000000-0000-0000-0000-000000000022",
  "examinationQuestionId": "00000000-0000-0000-0000-000000000023",
  "questionType": "FillInBlank",
  "selectedOptionId": null,
  "fillInBlankAnswers": [
    {
      "blankOrder": 1,
      "answerText": "Paris"
    }
  ],
  "modifiedAt": "2026-09-10T08:15:00Z"
}
```

### `PUT /api/examination-attempts/{attemptId}/questions/{questionId}/fill-in-blank`

**Request body:**

```json
[
  {
    "blankOrder": 1,
    "answerText": "Paris"
  }
]
```

**Response `200 OK`:** Student answer object dạng fill in blank.

### `POST /api/examination-attempts/{attemptId}/disconnect`

Chỉ dùng khi attempt đang `InProgress`. Nếu hết thời gian, backend auto-submit; nếu chưa hết, status chuyển `Disconnected` và giữ `remainingSeconds`.

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/reconnect`

Chỉ dùng khi attempt đang `Disconnected`. Nếu quá thời gian disconnect hoặc hết giờ thi, backend auto-submit; nếu hợp lệ thì quay lại `InProgress`.

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/violations?violationType=TabChanged&durationSeconds=`

`violationType` dùng một giá trị trong enum `ExaminationViolationType`. Backend chỉ ghi nhận loại violation đã được bật bởi cấu hình tương ứng của examination. Với `ExitFullscreen`, `durationSeconds` là bắt buộc; thời gian nhỏ hơn hoặc bằng `fullscreenGraceSeconds` không bị tính vi phạm. Khi đạt `maxViolations` lớn hơn `0`, backend auto-submit.

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/submit`

Attempt phải đang active. Nếu đã hết giờ, submit reason trở thành `DurationExpired`; nếu chưa hết giờ là `StudentSubmitted`.

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object. Khi `showScoreImmediately=false`, các trường `correctAnswers`, `totalQuestions`, `score` là `null` sau khi nộp.

Nếu `showScoreImmediately=false`, điểm được phát hành khi examination chuyển sang `Closed` hoặc thời gian hiện tại đạt `dueAt`. Sau thời điểm đó student gọi `GET /api/examinations/{examinationId}/my-attempt` để lấy kết quả.

### `POST /api/examination-attempts/{attemptId}/auto-submit?reason=DurationExpired`

**Quyền:** Admin/server. FrontEnd student không gọi endpoint này.

**Request body:** Không có body. `reason` không được là `StudentSubmitted`; backend kiểm tra điều kiện thực tế tương ứng (deadline, disconnect timeout, ngưỡng violation hoặc examination đã đóng) trước khi kết thúc attempt. Reason chưa đúng điều kiện trả `409`. **Response `200 OK`:** Examination attempt object.

## Data Flow

`HTTP` → Controller binding → Application Service (validation và authorization) → Repository/Redis/FileStorage/SMTP → UnitOfWork → PostgreSQL/Redis/Storage → JSON response hoặc `ProblemDetails`.

## Chat

Chat yêu cầu JWT và kiểm tra membership ở application service. Course chat chỉ dành cho course teacher hiện tại, Admin hoặc student đang có enrollment `Accepted`; bản ghi `ChatParticipant` chỉ là lịch sử tham gia và không thay thế việc kiểm tra lại quyền course ở mỗi lần truy cập REST/SignalR. Room không còn quyền sẽ bị loại khỏi `GET /api/chat/rooms`.

Course room theo vòng đời course: chuyển sang archive khi course `Closed` hoặc bị xóa mềm, và được mở lại khi course chuyển về `Open` hoặc được khôi phục với trạng thái `Open`. Room của course `Closed` vẫn đọc được nhưng không nhận message mới hay chỉnh sửa message; course đã xóa không còn truy cập được.

### REST endpoints

Chat room object:

```json
{
  "id": "00000000-0000-0000-0000-000000000030",
  "type": "Course",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "participantIds": [
    "00000000-0000-0000-0000-000000000001"
  ],
  "createdAt": "2026-09-04T10:00:00Z",
  "archivedAt": null
}
```

Chat message object:

```json
{
  "id": "00000000-0000-0000-0000-000000000031",
  "chatRoomId": "00000000-0000-0000-0000-000000000030",
  "senderId": "00000000-0000-0000-0000-000000000001",
  "content": "Hello",
  "replyToMessageId": null,
  "createdAt": "2026-09-04T10:00:00Z",
  "editedAt": null,
  "deletedAt": null,
  "attachments": [
    {
      "id": "00000000-0000-0000-0000-000000000032",
      "fileName": "notes.pdf",
      "contentType": "application/pdf",
      "fileSize": 12345
    }
  ]
}
```

Chat attachment không lộ storage `objectKey`; client dùng endpoint `content` để đọc file.

### `GET /api/chat/rooms`

**Response `200 OK`:** Mảng room mà user hiện còn quyền truy cập.

### `POST /api/chat/courses/{courseId}`

Lấy hoặc tạo course room. Course room dùng chung cho course. **Response `200 OK`:** Chat room object.

### `POST /api/chat/direct/{userId}`

Lấy hoặc tạo direct-message room với user đang `Active`. Không thể chat với chính mình. **Response `200 OK`:** Chat room object.

### `GET /api/chat/rooms/{roomId}/messages?page=1&pageSize=20`

**Response `200 OK`:** Mảng message, mới nhất trước. Đây không phải `PagedResult`; FrontEnd tăng `page` để tải lịch sử. Message đã xóa bị loại khỏi danh sách, bao gồm attachment của message đó.

### `POST /api/chat/rooms/{roomId}/messages`

Nội dung text tối đa 4000 ký tự cho cả gửi mới và chỉnh sửa; input vượt giới hạn trả `400`.

Request body:

```json
{
  "content": "Hello",
  "replyToMessageId": null
}
```

`content` phải có nội dung. Nếu reply, `replyToMessageId` phải thuộc cùng room và chưa bị xóa. Sau khi lưu thành công, server broadcast `MessageReceived` tới SignalR group của room. **Response `201 Created`:** Chat message object.

### `POST /api/chat/rooms/{roomId}/messages/attachment`

**Request:** `multipart/form-data`:

- `file`: bắt buộc, nhỏ hơn 20 MB.
- `content`: tùy chọn.
- `replyToMessageId`: tùy chọn.

**Response `201 Created`:** Chat message object có phần tử trong `attachments`; server cũng broadcast `MessageReceived` sau khi lưu thành công.

REST upload cũng broadcast SignalR event `MessageReceived` tới các client đã join room.

### `GET /api/chat/messages/{messageId}/attachments/{attachmentId}/content?download=false`

Chỉ user hiện còn quyền truy cập room mới tải được. `download=false` hoặc bỏ query để mở inline; `download=true` để tải bằng tên file gốc. Message đã xóa trả `404`. **Response `200 OK`:** Binary stream.

### `PATCH /api/chat/messages/{messageId}`

**Request body:** `{ "content": "Updated message" }`. Chỉ sender được sửa; message đã xóa hoặc room archived không sửa được. **Response `200 OK`:** Chat message object.

### `DELETE /api/chat/messages/{messageId}`

Sender hoặc moderator course room được xóa. **Response `204 No Content`:** message không còn xuất hiện trong endpoint danh sách.

### `POST /api/chat/rooms/{roomId}/read/{messageId}`

Đánh dấu participant đã đọc tới message chưa bị xóa. Receipt chỉ tiến theo `(createdAt, id)`; request cũ đến trễ được bỏ qua và không làm unread tăng ngược. Khi message đang là receipt target bị xóa, receipt đó được xóa. **Response `204 No Content`**.

### Realtime WebSocket

SignalR hub: `/hubs/chat`. Client cần JWT và có thể gọi `JoinRoom(roomId)`, `LeaveRoom(roomId)`, `SendMessage(roomId, request)`. Server broadcast event `MessageReceived` với payload Chat message object tới các connection trong room.

`JoinRoom`/reconnect và `SendMessage` kiểm tra lại quyền course hiện tại. Room archived không được join hoặc gửi; connection đã tham gia trước đó cũng bị từ chối ở lần ghi tiếp theo nếu enrollment bị revoke hoặc course đã archive.

- Client native có thể gửi JWT bằng header `Authorization: Bearer {token}`.
- Với browser WebSocket/SSE, cấu hình SignalR client bằng `accessTokenFactory`; token query `access_token` chỉ được backend chấp nhận trên `/hubs/chat` và các endpoint con của hub, không áp dụng cho REST API hay path khác.
- Connection được server đóng khi JWT hết hạn. Client cần lấy access token mới và reconnect; nên dùng cơ chế automatic reconnect của SignalR client.

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/chat", {
    accessTokenFactory: () => accessToken
  })
  .withAutomaticReconnect()
  .build();
```

Frontend nên tải lịch sử bằng REST, sau đó `JoinRoom` và lắng nghe `MessageReceived`. Gửi text qua hub hoặc hai endpoint REST (text/attachment) đều phát cùng event sau khi lưu thành công.

## Lưu ý khi nối FrontEnd

- Backend chưa cấu hình CORS. Khi FrontEnd chạy ở origin khác, dùng dev proxy hoặc bổ sung origin vào cấu hình backend.
- Chưa có `GET /api/users/me`; FrontEnd đọc user ID từ JWT rồi gọi `GET /api/users/{userId}/profile`.
- Course `Closed` hiện có nghĩa là ngừng tuyển: student đã `Accepted` vẫn truy cập nội dung/lịch sử qua các endpoint `mine`.
- Hệ thống chỉ chặn hai attempt active đồng thời; student có thể bắt đầu attempt mới sau khi attempt trước hoàn thành vì chưa có cấu hình `MaxAttempts`.
- Khi enrollment bị cancel/revoke, student mất quyền đọc course content và lịch sử liên quan theo authorization hiện tại.

## OAuth provider contract

Phần này mô tả interface nội bộ backend, FrontEnd không gọi trực tiếp. FrontEnd chỉ dùng các endpoint OAuth trong nhóm Authentication.

`IOAuthProvider` hiện chuẩn hóa các hàm nội bộ sau:

### `BuildAuthorizationUrl`

**Input:**

```json
{
  "redirectUri": "https://client.example.com/oauth/callback",
  "state": "random-state",
  "codeChallenge": "pkce-challenge"
}
```

**Output:** chuỗi URL redirect của provider.

### `ExchangeCodeAsync`

**Input:** authorization `code` và `redirectUri`.

**Output:**

```json
{
  "accessToken": "provider-access-token",
  "refreshToken": "provider-refresh-token",
  "expiresAt": "2026-09-04T13:00:00Z",
  "tokenType": "Bearer"
}
```

### `GetUserInfoAsync`

**Input:** OAuth token object.

**Output:**

```json
{
  "providerUserId": "provider-user-id",
  "email": "user@example.com",
  "fullName": "User",
  "avatarUrl": "https://example.com/avatar.jpg",
  "emailVerified": true
}
```

Google provider, callback, PKCE, account linking và JWT đã được triển khai. Microsoft OAuth chưa được hỗ trợ.
