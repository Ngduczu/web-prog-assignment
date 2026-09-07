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
- `SubmissionStatus`: `Draft`, `Submitted`.
- `QuestionType`: `MultipleChoice`, `FillInBlank`.
- `QuestionDifficulty`: `Easy`, `Medium`, `Hard`.
- `ExaminationStatus`: `Draft`, `Published`, `Closed`.
- `ExaminationAttemptStatus`: `InProgress`, `Disconnected`, `Submitted`, `AutoSubmitted`.
- `ExaminationSubmitReason`: `StudentSubmitted`, `DurationExpired`, `DisconnectTimeout`, `MaxViolationsExceeded`.
- `ExaminationViolationType`: `ExitFullscreen`, `TabChanged`, `WindowBlurred`, `CopyAttempt`, `PasteAttempt`, `RightClickAttempt`, `DevToolsSuspected`.

### Flow khởi tạo FrontEnd

1. Gọi `POST /api/auth/login` và lưu token pair.
2. Gửi access token bằng Bearer header. Khi API trả `401`, gọi `POST /api/auth/refresh` một lần rồi retry request cũ.
3. Access token chứa claim user ID, email và role. Hiện chưa có endpoint `/me`; FrontEnd có thể đọc claim user ID rồi gọi `GET /api/users/{userId}/profile`.
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

Flow: Controller → tìm user theo email → verify password hash → kiểm tra `AccountStatus.Active` → tạo JWT chứa `NameIdentifier`, `Email`, `Role` → trả access token.

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
Refresh token cũ bị xoá ngay khi dùng (rotation), hash được lưu Redis TTL 30 ngày.

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

Flow: validate → hash password → PostgreSQL → Redis mã 6 ký tự với TTL 15 phút → SMTP.

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

### `POST /api/auth/resend-verification`

**Quyền:** Public, chỉ áp dụng cho local account đang `Inactive`.

**Request body:**

```json
{
  "email": "student@example.com"
}
```

**Response `204 No Content`:** Không có body. Cooldown gửi lại là 1 phút.

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

**Quyền:** Public. Đổi application login code lấy token pair. Code được lưu bằng key SHA-256, hết hạn sau 2 phút và được consume atomically nên chỉ một request đồng thời có thể thành công.

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

### `PATCH /api/users/{userId}/status`

**Quyền:** Admin.

**Request body:**

```json
{
  "status": "Disabled"
}
```

**Response `200 OK`:** User profile object.

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

### `PUT /api/courses/{courseId}`

**Quyền:** Teacher sở hữu course hoặc Admin.

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
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

### `POST /api/courses/{courseId}/enrollments`

**Quyền:** Student đang `Active`. **Response `201 Created`:** Enrollment object với `status: "Waiting"`.

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

### `POST /api/enrollments/{enrollmentId}/accept`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `200 OK`:** Enrollment object với `status: "Accepted"`.

### `POST /api/enrollments/{enrollmentId}/reject`

**Quyền:** Teacher sở hữu course hoặc Admin. **Response `200 OK`:** Enrollment object với `status: "Rejected"`.

### `POST /api/enrollments/{enrollmentId}/cancel`

**Quyền:** Student sở hữu enrollment và enrollment đang `Waiting`. **Response `200 OK`:** Enrollment object với `status: "Cancelled"`.

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

**Request:** `multipart/form-data`, field bắt buộc là `file` không rỗng. Material hiện không đặt giới hạn dung lượng riêng ở application service. **Response `200 OK`:** Material object.

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

**Response `201 Created`:** Exercise object.

### `GET /api/courses/{courseId}/exercises`

**Quyền:** Teacher sở hữu course, Admin hoặc student có enrollment `Accepted`.

**Response `200 OK`:** Mảng exercise object. Exercise hiện không có trạng thái draft/publish nên student đã được duyệt thấy ngay sau khi tạo.

### `GET /api/exercises/{exerciseId}`

**Quyền:** Giống endpoint danh sách exercise. **Response `200 OK`:** Exercise object.

### `GET /api/exercises/{exerciseId}/attachment?download=false`

**Quyền:** Giống endpoint xem exercise.

**Response `200 OK`:** Binary stream. `download=false` hoặc bỏ query để mở inline; `download=true` để tải với tên file gốc. Trả `404` nếu exercise chưa có attachment.

### `PUT /api/exercises/{exerciseId}`

**Quyền:** Teacher sở hữu course hoặc Admin. **Request body:** Cùng shape với request tạo exercise. **Response `200 OK`:** Exercise object.

### `POST /api/exercises/{exerciseId}/attachment`

**Quyền:** Teacher sở hữu course hoặc Admin. **Request:** `multipart/form-data`, field bắt buộc là `file` nhỏ hơn 20 MB. Upload mới sẽ thay file cũ. **Response `200 OK`:** Exercise object.

### Submission object

```json
{
  "id": "00000000-0000-0000-0000-000000000015",
  "exerciseId": "00000000-0000-0000-0000-000000000013",
  "studentId": "00000000-0000-0000-0000-000000000001",
  "status": "Draft",
  "submittedAt": null,
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

**Request:** `multipart/form-data`, field bắt buộc là `file` nhỏ hơn 20 MB. Tạo submission `Draft` nếu chưa có; upload lại sẽ thay file của draft hiện tại. Không được upload sau `dueAt` hoặc khi submission đang `Submitted`. **Response `200 OK`:** Submission object.

### `POST /api/submissions/{submissionId}/submit`

**Quyền:** Student sở hữu submission. Chỉ submit draft đã có attachment và chưa quá `dueAt`.

**Request body:** Không có body. **Response `200 OK`:** Submission object với status `Submitted` và `submittedAt` khác `null`.

### `POST /api/submissions/{submissionId}/unsubmit`

**Quyền:** Student sở hữu submission. Chỉ unsubmit submission đang `Submitted` và chưa quá `dueAt`.

**Request body:** Không có body. **Response `200 OK`:** Submission object với status `Draft` và `submittedAt: null`.

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
  "correctAnswers": 0,
  "totalQuestions": 20,
  "score": 0
}
```

### `POST /api/examination-attempts/{examinationId}/start`

**Request body:** Không có body. Chỉ bắt đầu được exam `Published` trong khoảng `startAt <= now < dueAt`, student phải còn enrollment `Accepted`, và không được có attempt active của cùng exam.

**Response `200 OK`:** Examination attempt object. FrontEnd phải lưu `attempt.id` để gọi các endpoint tiếp theo.

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

### `POST /api/examination-attempts/{attemptId}/violations?violationType=TabChanged`

`violationType` dùng một giá trị trong enum `ExaminationViolationType`. Khi đạt `maxViolations` lớn hơn `0`, backend auto-submit.

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/submit`

Attempt phải đang active. Nếu đã hết giờ, submit reason trở thành `DurationExpired`; nếu chưa hết giờ là `StudentSubmitted`.

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object. Khi `showScoreImmediately=false`, các trường `correctAnswers`, `totalQuestions`, `score` là `null` sau khi nộp.

### `POST /api/examination-attempts/{attemptId}/auto-submit?reason=DurationExpired`

**Quyền:** Admin/server. FrontEnd student không gọi endpoint này.

**Request body:** Không có body. `reason` không được là `StudentSubmitted`. **Response `200 OK`:** Examination attempt object.

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
  "attachments": []
}
```

### `GET /api/chat/rooms`

**Response `200 OK`:** Mảng room mà user hiện còn quyền truy cập.

### `POST /api/chat/courses/{courseId}`

Lấy hoặc tạo course room. Course room dùng chung cho course. **Response `200 OK`:** Chat room object.

### `POST /api/chat/direct/{userId}`

Lấy hoặc tạo direct-message room với user đang `Active`. Không thể chat với chính mình. **Response `200 OK`:** Chat room object.

### `GET /api/chat/rooms/{roomId}/messages?page=1&pageSize=20`

**Response `200 OK`:** Mảng message, mới nhất trước. Đây không phải `PagedResult`; FrontEnd tăng `page` để tải lịch sử. Message đã xóa bị loại khỏi danh sách, bao gồm attachment của message đó.

### `POST /api/chat/rooms/{roomId}/messages`

Request body:

```json
{
  "content": "Hello",
  "replyToMessageId": null
}
```

`content` phải có nội dung. Nếu reply, `replyToMessageId` phải thuộc cùng room và chưa bị xóa. **Response `201 Created`:** Chat message object.

### `POST /api/chat/rooms/{roomId}/messages/attachment`

**Request:** `multipart/form-data`:

- `file`: bắt buộc, nhỏ hơn 20 MB.
- `content`: tùy chọn.
- `replyToMessageId`: tùy chọn.

**Response `201 Created`:** Chat message object có phần tử trong `attachments`.

### `PATCH /api/chat/messages/{messageId}`

**Request body:** `{ "content": "Updated message" }`. Chỉ sender được sửa; message đã xóa hoặc room archived không sửa được. **Response `200 OK`:** Chat message object.

### `DELETE /api/chat/messages/{messageId}`

Sender hoặc moderator course room được xóa. **Response `204 No Content`:** message không còn xuất hiện trong endpoint danh sách.

### `POST /api/chat/rooms/{roomId}/read/{messageId}`

Đánh dấu participant đã đọc tới message. **Response `204 No Content`**.

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

Frontend nên tải lịch sử bằng REST, sau đó `JoinRoom`, lắng nghe `MessageReceived`, và gửi text qua hub. Endpoint REST gửi attachment hiện chỉ trả message cho request gọi nó, chưa broadcast event SignalR.

## Khoảng trống backend cần biết khi nối FrontEnd

Đây là các thiếu hụt chức năng hiện tại, không phải yêu cầu hardening bảo mật:

- Backend chưa cấu hình CORS. Khi chạy FrontEnd ở origin khác, dùng dev proxy về API hoặc bổ sung origin FrontEnd vào cấu hình backend.
- Chưa có `GET /api/users/me`; FrontEnd phải đọc user ID từ JWT rồi gọi profile.
- Chưa có endpoint “enrollments của tôi”, nên sau khi reload student không lấy được danh sách course đã đăng ký và trạng thái `Waiting/Accepted` từ một API riêng.
- Danh sách course của student luôn chỉ trả course `Open`; course `Closed` đã enroll vẫn xem được bằng endpoint chi tiết nhưng không xuất hiện trong danh sách.
- Submission chỉ có upload/submit/unsubmit; chưa có GET submission hiện tại, danh sách bài nộp cho teacher, chấm điểm hoặc endpoint tải submission attachment.
- Examination attempt chưa có endpoint lấy attempt active/result theo exam. Nếu reload giữa lúc thi, FrontEnd không có API để tìm lại `attemptId`; gọi start lại sẽ nhận `409`.
- Chat attachment mới chỉ trả `objectKey`, chưa có endpoint tải/xem file. REST upload attachment cũng chưa broadcast `MessageReceived`.
- Exercise chưa có draft/publish; student có enrollment `Accepted` thấy bài tập ngay sau khi teacher tạo.
- Chưa có endpoint xóa lesson/exercise hoặc cập nhật/xóa question. FrontEnd không nên hiển thị các nút này cho tới khi backend bổ sung.

Các khoảng trống này nên được ưu tiên theo màn hình FrontEnd thực tế; không cần thêm abstraction hoặc hệ thống phức tạp cho đồ án.

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
