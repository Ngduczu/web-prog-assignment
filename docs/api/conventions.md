# API – Quy ước chung

## Kết nối

- Base URL REST: `{API_ORIGIN}/api`. SignalR hub: `{API_ORIGIN}/hubs/chat`.
- Local: `http://localhost:5258` hoặc `https://localhost:7086`. Swagger tại `/swagger` (chỉ `Development`).
- Trừ các endpoint ghi `Public`, mọi endpoint cần header `Authorization: Bearer <accessToken>`.

## Định dạng dữ liệu

- JSON `camelCase`.
- UUID là chuỗi, ví dụ `"00000000-0000-0000-0000-000000000001"`.
- Thời gian là UTC ISO-8601, ví dụ `"2026-09-04T10:00:00Z"`. Trường nhập thời gian (`dueAt`, `startAt`) bắt buộc có hậu tố `Z`.
- Enum trong JSON dùng **tên chuỗi** đúng chữ hoa/thường (`"Student"`, `"Open"`).
- Request không có body được ghi là `Không có body`.
- Upload dùng `multipart/form-data`, giới hạn mỗi file nhỏ hơn 20 MB (xem [../overview.md](../overview.md#7-lưu-trữ-file)).
- Binary (`.../content`, `.../attachment`): trả stream với `Content-Type` gốc; `download=true` thêm `Content-Disposition` với tên file gốc, mặc định mở inline.

## Phân trang

Query `page` (mặc định `1`) và `pageSize` (mặc định `20`, tối đa `100`). Response:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0
}
```

Ngoại lệ: `GET /api/chat/rooms/{roomId}/messages` trả **mảng** (mới nhất trước), không phải `PagedResult`.

## Lỗi

Chi tiết cơ chế xử lý, danh mục thông báo theo module và các lỗi do framework sinh ra: [../architecture/error-handling.md](../architecture/error-handling.md).

Lỗi trả `application/problem+json`:

```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "Only waiting enrollments can change status."
}
```

| Status | Khi nào |
| --- | --- |
| `400` | Dữ liệu không hợp lệ, vi phạm quy tắc nhập liệu |
| `401` | Thiếu/sai/hết hạn token, token bị thu hồi (`auth_version`, tài khoản không còn `Active`) |
| `403` | Đã đăng nhập nhưng không có quyền |
| `404` | Không tìm thấy hoặc không được phép biết tài nguyên tồn tại |
| `409` | Xung đột trạng thái/dữ liệu (trùng, concurrency, sai trạng thái) |
| `429` | Vượt rate limit: `/api/auth/*` 10 request/phút, còn lại 240 request/phút mỗi IP |
| `500` | Lỗi không mong đợi, `detail` luôn là `"An unexpected error occurred."` |

## Enum

| Enum | Giá trị |
| --- | --- |
| `Role` | `Student`, `Teacher`, `Admin` |
| `AccountStatus` | `Active`, `Inactive`, `Disabled`, `Banned` |
| `CourseStatus` | `Open`, `Closed` |
| `EnrollmentStatus` | `Waiting`, `Accepted`, `Rejected`, `Cancelled` |
| `LessonStatus` | `Draft`, `Published` |
| `ExerciseStatus` | `Draft`, `Published`, `Closed` |
| `SubmissionStatus` | `Draft`, `Submitted`, `Graded` |
| `QuestionType` | `MultipleChoice`, `FillInBlank` |
| `QuestionDifficulty` | `Easy`, `Medium`, `Hard` |
| `ExaminationStatus` | `Draft`, `Published`, `Closed` |
| `ExaminationAttemptStatus` | `InProgress`, `Disconnected`, `Submitted`, `AutoSubmitted` |
| `ExaminationSubmitReason` | `StudentSubmitted`, `DurationExpired`, `DisconnectTimeout`, `MaxViolationsExceeded`, `ExaminationClosed` |
| `ExaminationViolationType` | `ExitFullscreen`, `TabChanged`, `WindowBlurred`, `CopyAttempt`, `PasteAttempt`, `RightClickAttempt`, `DevToolsSuspected` |

## Luồng khởi tạo phía client

1. `POST /api/auth/login`, lưu cặp token.
2. Gửi `Authorization: Bearer <accessToken>`. Khi nhận `401`, gọi `POST /api/auth/refresh` một lần rồi gửi lại request.
3. Chưa có `/me`: đọc claim `NameIdentifier` trong JWT rồi gọi `GET /api/users/{userId}/profile`.
4. Dùng `role` để chọn giao diện; backend luôn kiểm tra quyền lần cuối.

## Chú thích “Lưu trữ” trong tài liệu endpoint

Mỗi endpoint có dòng **Dữ liệu đi qua** cho biết các thành phần được chạm tới:

- **PostgreSQL**: đọc/ghi qua EF Core (repository + `IUnitOfWork`).
- **Redis**: key `auth:*` (xem [../overview.md](../overview.md#6-redis)).
- **Storage**: `IFileStorage` (local/S3); **Cleanup queue** là bảng `FileCleanupTask`.
- **SMTP**: gửi email.
- **SignalR**: broadcast tới group `chat-room:{roomId}`.
