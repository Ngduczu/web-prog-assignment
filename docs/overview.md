# Backend – Tổng quan

Đối chiếu mã nguồn ngày **2026-10-13**. Mô tả implementation trong repository, không phải xác nhận môi trường production.

## 1. Mục tiêu

Backend cung cấp REST API và SignalR cho hệ thống LMS với bốn vai trò `Student`, `Teacher`, `Admin`, `System`:

- Tài khoản: đăng ký + xác thực email, đăng nhập JWT, refresh token, quên/đặt lại mật khẩu, đăng nhập/liên kết Google.
- Khóa học: tạo/sửa/xóa mềm/khôi phục, đăng ký học và duyệt học viên.
- Nội dung: bài giảng, tài liệu đính kèm, bài tập, nộp bài, chấm điểm.
- Ngân hàng câu hỏi và kỳ thi trực tuyến (trắc nghiệm, điền khuyết) có giám sát vi phạm và tự nộp bài.
- Chat theo khóa học, chat trực tiếp, realtime qua SignalR, đính kèm file.

## 2. Công nghệ

| Hạng mục | Công nghệ |
| --- | --- |
| Runtime | .NET 10, ASP.NET Core Web API (controllers) |
| ORM / DB | Entity Framework Core 10 + Npgsql, PostgreSQL 17 |
| Cache / state tạm | Redis 8 (StackExchange.Redis) |
| Xác thực | JWT Bearer (HS256), refresh token opaque lưu Redis, Google OAuth 2.0 + PKCE |
| Realtime | ASP.NET Core SignalR (`/hubs/chat`) |
| Email | SMTP (mặc định Gmail, cổng 587 STARTTLS) |
| File | Local disk (mặc định) hoặc Amazon S3 / S3-compatible (AWSSDK.S3) |
| API docs | Swagger (Swashbuckle) + OpenAPI, chỉ bật ở `Development` |
| Cấu hình | `.env` nạp bằng DotNetEnv + `appsettings*.json` + biến môi trường |
| Test | xUnit, Moq, `Microsoft.AspNetCore.Mvc.Testing` |
| Hạ tầng dev | Docker Compose (PostgreSQL + Redis) |

## 3. Kiến trúc

Dự án một assembly `LMS.Api` chia theo lớp (thư mục viết thường):

```text
backend/LMS.Api
├── presentation/      Controllers, ChatHub, GlobalExceptionHandler, rate limit, Program.cs
├── application/
│   ├── features/      Service + DTO/contract theo module (auth, courses, examinations, chats, ...)
│   ├── abstractions/  Interface: ICache, IFileStorage, IMailSender, ITokenProvider, IClock, IUnitOfWork, ...
│   └── common/        PagedResult
├── domain/
│   ├── entities/      Entity + quy tắc nghiệp vụ (chats, courses, examinations, questions, storage, users)
│   ├── enums/ exceptions/ repositories/
└── infrastructure/
    ├── persistence/   AppDbContext, configurations, migrations, repositories (EF Core)
    ├── caching/Redis  RedisCache
    ├── authentication JWT, OAuth (Google), PasswordHashing
    ├── mail/Gmail     SMTP sender
    ├── storage        Local, S3, FileCleanupQueue + FileCleanupWorker
    └── workers        ExaminationAutoSubmitWorker
```

Luồng xử lý một request:

```text
HTTP
 → Rate limiter (fixed window theo IP)
 → CORS → JwtBearer (validate chữ ký/hạn + đối chiếu user trong PostgreSQL)
 → Controller (binding, [Authorize])
 → Application Service (validate, kiểm tra quyền/sở hữu, nghiệp vụ)
 → Repository / ICache (Redis) / IFileStorage / IMailSender
 → IUnitOfWork.SaveChanges (PostgreSQL, transaction khi cần)
 → JSON response | ProblemDetails (GlobalExceptionHandler)
```

### Xử lý lỗi

`GlobalExceptionHandler` ánh xạ exception sang `ProblemDetails`:

| Exception | HTTP |
| --- | --- |
| `BadRequestException` | 400 |
| `UnauthorizeException` | 401 |
| `ForbiddenException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException`, `DbUpdateException` vi phạm unique nghiệp vụ | 409 |
| `ExternalServiceUnavailableException` (Google không kết nối được/timeout) | 503 |
| Còn lại | 500 (`"An unexpected error occurred."`, chi tiết chỉ ghi log) |

Rate limit vượt ngưỡng trả `429`.

## 4. Xác thực và phân quyền

- **Access token**: JWT chứa `NameIdentifier` (userId), `Email`, `Role`, `auth_version`, `jti`. Thời sống mặc định 60 phút (`Jwt:ExpirationMinutes`).
- **Đối chiếu mỗi request xác thực HTTP**: `OnTokenValidated` đọc user từ PostgreSQL, từ chối nếu user không tồn tại, không `Active`, role khác token hoặc `auth_version` khác `users.auth_version`. Khóa tài khoản, đổi role, đổi/đặt lại mật khẩu khiến token cũ bị từ chối ở lần xác thực tiếp theo; không đồng nghĩa ngắt ngay WebSocket đang mở (xem [giới hạn chat](api/chat.md)).
- **Refresh token**: chuỗi opaque, chỉ lưu hash SHA-256 trong Redis (`auth:refresh:*`), TTL 30 ngày, dùng một lần (rotation).
- **Realtime token**: JWT sống 2 phút, claim `token_use=chat_realtime`, chỉ hợp lệ trên `/hubs/chat`. Query `access_token` chỉ được nhận ở path `/hubs/chat`.
- **Phân quyền nghiệp vụ** nằm ở service: Teacher thao tác tài nguyên mình sở hữu, Student truy cập nội dung khóa học có enrollment `Accepted`. Admin/System quản trị khóa học và người dùng theo quy tắc riêng; ngân hàng câu hỏi chỉ được đọc. `POST /api/examination-attempts/{attemptId}/auto-submit` dùng `[Authorize(Roles = "Admin,System")]`. Chỉ System được tạo/cấp vai trò Admin; không API nào cấp vai trò System.
- **Mật khẩu**: hash bằng `PasswordHasher` của ASP.NET Core; độ dài 8–128 ký tự.
- **CORS**: chỉ bật khi `Cors:AllowedOrigins` có giá trị (policy `Frontend`, cho phép credentials). Frontend mặc định gọi qua Next.js server nên không cần CORS, trừ khi trình duyệt kết nối SignalR trực tiếp tới backend.
- **Rate limit** (fixed window 1 phút, partition theo IP): `/api/auth/*` 10 request/phút, các path còn lại 240 request/phút.

## 5. Mô hình dữ liệu chính (PostgreSQL)

| Nhóm | Entity | Ghi chú |
| --- | --- | --- |
| Người dùng | `User`, `ExternalUser` | `AuthVersion` tăng khi đổi mật khẩu/role/status; `ExternalUser` lưu liên kết Google (1 identity thuộc 1 user, 1 user chỉ 1 identity mỗi provider) |
| Khóa học | `Course` (xóa mềm), `Enrollment` | Enrollment: `Waiting → Accepted/Rejected/Cancelled`, record cũ được tái sử dụng khi đăng ký lại |
| Nội dung | `Lesson`, `Material`, `Exercise`, `ExerciseAttachment`, `Submission`, `SubmissionAttachment` | `Submission` có row-version (optimistic concurrency) |
| Câu hỏi | `QuestionBank`, `Question`, `MultipleChoiceOption`, `FillInBlankAnswerKey` | Nguồn để tạo snapshot cho kỳ thi |
| Kỳ thi | `Examination`, `ExaminationQuestionSelectionRule`, `ExaminationQuestion`, `ExaminationMultipleChoiceOption`, `ExaminationFillInBlankAnswerKey` | Câu hỏi được **snapshot** khi tạo/đổi question set, sửa/xóa câu hỏi nguồn không ảnh hưởng kỳ thi |
| Làm bài | `ExaminationAttempt`, `StudentAnswer`, `StudentFillInBlankAnswer`, `ExaminationViolation` | Mỗi student chỉ có **một** attempt cho mỗi kỳ thi (unique `(examination_id, student_id)`) |
| Chat | `ChatRoom`, `ChatParticipant`, `ChatMessage`, `ChatAttachment` | Room loại `Course` hoặc `DirectMessage`; có `ArchivedAt`; participant có mốc ẩn/xóa lịch sử direct conversation |
| Hệ thống | `FileCleanupTask` | Hàng đợi xóa file bền vững |

Migration nằm ở `infrastructure/persistence/migrations`, tự động chạy (`db.Database.Migrate()`) khi API khởi động, trừ môi trường `Testing`. Sau migrate, `SystemAccountSeeder` seed/nâng cấp tài khoản System từ `SYSTEMACCOUNT__EMAIL`/`SYSTEMACCOUNT__PASSWORD`.

## 6. Redis

Redis dùng cho state ngắn hạn của xác thực, truy cập qua `ICache` (`GetAsync`, `SetAsync`, `SetIfNotExistsAsync`, `ConsumeAsync` = GET+DEL atomic bằng Lua, `IncrementAsync` = INCR + PEXPIRE atomic, `RemoveAsync`). Giá trị là JSON camelCase. Chuỗi cấu hình: `ConnectionStrings:Redis` (kết nối lazy).

| Key | Nội dung | TTL | Dùng ở |
| --- | --- | --- | --- |
| `auth:refresh:{sha256(refreshToken)}` | Session refresh (userId, authVersion) | 30 ngày | login, refresh |
| `auth:email-verification:{userId:N}` | userId, email, hash mã 6 ký tự | 15 phút | register, resend, verify-email |
| `auth:email-verification-attempts:{userId:N}` | Số lần kiểm tra mã sau khi tìm thấy entry; lần thứ 6 bị chặn | 15 phút từ lần tăng đầu tiên | verify-email |
| `auth:email-verification-cooldown:{userId}` | Khóa gửi lại mã | 1 phút | register, resend |
| `auth:password-reset:{userId}` | Mã đặt lại mật khẩu | 15 phút | forgot/reset-password |
| `auth:password-reset-attempts:{userId:N}` | Số lần kiểm tra mã sau khi tìm thấy entry; lần thứ 6 bị chặn | 15 phút từ lần tăng đầu tiên | reset-password |
| `auth:password-reset-cooldown:{userId}` | Khóa gửi lại | 1 phút | forgot-password |
| `auth:oauth-state:{state}` | State, PKCE verifier, mục đích (Login/Link), userId (khi Link) | 10 phút | OAuth authorize/callback |
| `auth:oauth-identity:{sha256(binding)}` | Identity Google chờ xử lý | 10 phút | OAuth callback |
| `auth:oauth-login-code:{sha256(loginCode)}` | Login code dùng một lần | 2 phút | OAuth callback, `login-code` |

Mã/token được consume atomic. Khi bước sau (SMTP, ghi PostgreSQL) lỗi tạm thời, backend khôi phục hoặc dọn key để client có thể thử lại (xem [architecture/error-handling.md](architecture/error-handling.md) và [api/auth.md](api/auth.md)). Mã xác thực email/đặt lại mật khẩu được lưu dạng hash, không lưu thô.

Nếu Redis không khả dụng, các endpoint xác thực dùng Redis (login, refresh, đăng ký, quên mật khẩu, OAuth) sẽ lỗi `500`; các endpoint còn lại không phụ thuộc Redis.

## 7. Lưu trữ file

`IFileStorage` có hai cài đặt, chọn bằng `S3Storage:Enabled`:

- **Local** (mặc định): lưu dưới `FileStorage:RootPath` (mặc định `storage`, tương đối so với content root của API). Object key dạng `{thư mục}/{guid}{đuôi file}`.
- **S3**: bucket/region/endpoint cấu hình qua `S3Storage:*`.

Quy tắc upload: `multipart/form-data`, field `file`, kích thước `0 < size < 20 MB` (20.000.000 byte, `RequestSizeLimit(20_000_000)`), tên file và content type tối đa 255 ký tự. Không có whitelist loại file.

Metadata (tên file, content type, size, `objectKey`) nằm trong PostgreSQL. Client không dùng `objectKey` để dựng URL; phải tải qua endpoint `content`/`attachment` (backend stream, kiểm tra quyền).

**Dọn file**: khi thay/xóa file, `objectKey` cũ được ghi vào bảng `FileCleanupTask` (upsert idempotent trong cùng transaction nghiệp vụ). `FileCleanupWorker` quét mỗi 10 giây, tối đa 100 task, xóa file; lỗi thì retry với backoff mũ (1, 2, 4, … tối đa 60 phút). Task không mất khi API restart.

## 8. Background worker

| Worker | Chu kỳ | Công việc |
| --- | --- | --- |
| `ExaminationAutoSubmitWorker` | 30 giây, batch 100 | Tìm attempt hết giờ làm bài / quá thời gian disconnect / kỳ thi đã quá hạn, chấm và tự nộp với `submitReason` tương ứng |
| `FileCleanupWorker` | 10 giây, batch 100 | Xử lý `FileCleanupTask` như mục 7 |

## 9. Email

`IMailSender` → `GmailService` dùng `System.Net.Mail.SmtpClient`, gửi trực tiếp trong request: mã xác thực email, mã đặt lại mật khẩu và thông báo chia sẻ ngân hàng câu hỏi. Hai luồng mã xác thực/reset dọn mã/cooldown nếu gửi lỗi. Chia sẻ bank commit bản sao trước rồi gửi mail; lỗi mail thông thường không làm thất bại việc chia sẻ. Không có mail queue/worker hoặc retry mail tự động. Xem [tools-and-integrations.md](tools-and-integrations.md) để tra API, TTL và tình huống lỗi.

## 10. Google OAuth

Authorization Code + PKCE, `state` lưu Redis, gắn với cookie `__Host-LMS.OAuth.{state}` của trình duyệt khởi tạo. Hai mục đích: đăng nhập (`Login`) và liên kết tài khoản (`Link`). Callback đăng nhập trả/redirect một `loginCode` dùng một lần; frontend đổi lấy token qua `POST /api/auth/login-code`. Đăng nhập Google tự tạo tài khoản `Student` nếu email chưa có, và tự liên kết nếu email trùng tài khoản local `Active` chưa có Google. Microsoft OAuth chưa hỗ trợ. Chi tiết: [architecture/data-flow.md](architecture/data-flow.md#5-google-oauth-authorization-code--pkce) và [api/auth.md](api/auth.md).

## 11. Cấu trúc test

`backend/LMS.Domain.Tests` (xUnit) gồm test domain (`Chats`, `Courses`, `Examinations`), test application service (`Application`), test persistence/storage, test controller và test tích hợp (`Integration`: xác thực API, SignalR, JSON contract, hàng đợi cleanup).

## 12. Hạn chế hiện tại

- Chưa có `GET /api/users/me`; client đọc `userId` từ JWT rồi gọi `GET /api/users/{userId}/profile`.
- Mỗi student có tối đa một lượt hiện hữu cho mỗi kỳ thi; Teacher sở hữu/Admin/System có thể xóa lượt qua API reset để cho bắt đầu lại, vẫn phải còn trong thời gian thi.
- Course `Closed` nghĩa là ngừng tuyển: student đã `Accepted` vẫn xem nội dung/lịch sử.
- Khi enrollment bị cancel/revoke, student mất quyền đọc nội dung khóa học.
- Chưa có OAuth Microsoft.
