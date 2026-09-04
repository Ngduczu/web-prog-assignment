# LMS API Contract

## Quy ước chung

- Base URL: `/api`.
- JSON sử dụng `camelCase`.
- UUID là chuỗi UUID, ví dụ: `"00000000-0000-0000-0000-000000000001"`.
- Thời gian là UTC theo ISO-8601, ví dụ: `"2026-09-04T10:00:00Z"`.
- Lỗi trả về dạng `ProblemDetails` với HTTP status `400`, `401`, `403`, `404`, `409` hoặc `500`.
- Phân trang: `page` mặc định `1`, `pageSize` mặc định `20`, tối đa `100`.
- Quyền được kiểm tra ở application service bằng claims `NameIdentifier` và `Role`.
- Request không có body được ghi là `Không có body`.
- Các ví dụ JSON dưới đây là JSON hợp lệ và có thể dùng làm mẫu request/response.

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

Mã sai hoặc hết hạn trả `400`. Vượt quá 5 lần thử trả `409`.

### `POST /api/auth/resend-verification`

**Quyền:** Public, chỉ áp dụng cho local account chưa `Active`.

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

**Quyền:** Public. **Response `200 OK`:** User profile object.

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

**Quyền:** Public. Course đã xóa mềm chỉ được trả về khi Admin truyền `includeDeleted=true`.

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

**Quyền:** Public. Query `includeDeleted=true` chỉ dành cho Admin. **Response `200 OK`:** Course object.

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

**Quyền:** Teacher hoặc Admin. **Response `200 OK`:** Mảng question bank object.

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

**Quyền:** Lesson, exercise và material: Teacher sở hữu course hoặc Admin. Submission: Student sở hữu submission.

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

**Response `200 OK`:** Mảng lesson object.

### `PUT /api/lessons/{lessonId}`

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

### `POST /api/lessons/{lessonId}/materials`

**Request:** `multipart/form-data`, field bắt buộc là `file`. **Response `200 OK`:** Material object.

### `DELETE /api/materials/{materialId}`

**Response `204 No Content`:** Không có body.

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

**Response `200 OK`:** Mảng exercise object.

### `PUT /api/exercises/{exerciseId}`

**Request body:** Cùng shape với request tạo exercise. **Response `200 OK`:** Exercise object.

### `POST /api/exercises/{exerciseId}/attachment`

**Request:** `multipart/form-data`, field bắt buộc là `file`. **Response `200 OK`:** Exercise object.

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

**Request:** `multipart/form-data`, field bắt buộc là `file`. **Response `200 OK`:** Submission object với status `Draft`.

### `POST /api/submissions/{submissionId}/submit`

**Request body:** Không có body. **Response `200 OK`:** Submission object với status `Submitted` và `submittedAt` khác `null`.

### `POST /api/submissions/{submissionId}/unsubmit`

**Request body:** Không có body. **Response `200 OK`:** Submission object với status `Draft`.

## Examination Authoring

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

**Response `200 OK`:** Mảng examination object.

### `GET /api/examinations/{examinationId}`

**Response `200 OK`:** Examination object.

### `PUT /api/examinations/{examinationId}`

**Quyền:** Teacher sở hữu course hoặc Admin. **Request body:** Giống request tạo examination nhưng không có `questionSelectionRules`. **Response `200 OK`:** Examination object.

### `POST /api/examinations/{examinationId}/publish`

**Request body:** Không có body. **Response `200 OK`:** Examination object với status `Published`.

### `POST /api/examinations/{examinationId}/close`

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

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

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

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/reconnect`

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/violations?violationType=TabChanged`

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/submit`

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

### `POST /api/examination-attempts/{attemptId}/auto-submit?reason=DurationExpired`

**Request body:** Không có body. **Response `200 OK`:** Examination attempt object.

## Data Flow

`HTTP` → Controller binding → Application Service (validation và authorization) → Repository/Redis/FileStorage/SMTP → UnitOfWork → PostgreSQL/Redis/Storage → JSON response hoặc `ProblemDetails`.

## Chat

Chat yêu cầu JWT và kiểm tra membership ở application service. Course chat chỉ dành cho course teacher, Admin hoặc student có enrollment `Accepted`. Direct chat chỉ dành cho user đang `Active`.

### REST endpoints

- `GET /api/chat/rooms`
- `POST /api/chat/courses/{courseId}`
- `POST /api/chat/direct/{userId}`
- `GET /api/chat/rooms/{roomId}/messages?page=1&pageSize=20`
- `POST /api/chat/rooms/{roomId}/messages`
- `PATCH /api/chat/messages/{messageId}`
- `DELETE /api/chat/messages/{messageId}`
- `POST /api/chat/rooms/{roomId}/read/{messageId}`

Message body:

```json
{
  "content": "Hello",
  "replyToMessageId": null
}
```

### Realtime WebSocket

SignalR hub: `/hubs/chat`. Client cần JWT và có thể gọi `JoinRoom`, `LeaveRoom`, `SendMessage`. Server broadcast event `MessageReceived` tới các connection trong room.

## OAuth provider contract

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
