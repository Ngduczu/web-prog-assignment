# Thành phần hỗ trợ: worker, logger, middleware, helper và cấu hình

Đối chiếu ngày **2026-10-13**. [Bản đồ file](source-map.md) bao phủ file mã nguồn/cấu hình; tài liệu này giải thích vai trò, nơi gọi, thời điểm và giới hạn của các thành phần phụ. Đường dẫn trong bảng tính từ `backend/LMS.Api/`, trừ khi ghi khác.

## 1. Startup và dependency injection

| File | Làm gì | Khi nào / nơi sử dụng |
| --- | --- | --- |
| `presentation/Program.cs` | Nạp env; JSON enum converter; SignalR/CORS; DI; migrate + seed; middleware; map route/hub | Entry point mỗi lần API khởi động; migrate/seed/HTTPS redirect bỏ qua ở Testing; Swagger/OpenAPI chỉ Development |
| `application/DependencyInjection.cs` | Đăng ký 14 application services theo scoped lifetime | Controller/hub resolve service cho từng scope |
| `infrastructure/DependencyInjection.cs` | DbContext/repository, storage provider, Redis, PasswordHasher, SMTP, OAuth HTTP, JWT, 2 hosted workers | Wiring một lần lúc startup; service thực tế được resolve khi cần |
| `presentation/DependencyInjection.cs` | HttpContextAccessor, ICurrentUser, authorization, fixed-window limiter, exception handler, ProblemDetails | Mọi request; limiter 10/min cho `/api/auth`, 240/min cho nhóm còn lại, theo peer IP và nhóm path |
| `Properties/launchSettings.json` | Profile http/https, port và Development env | `dotnet run --launch-profile ...`, không quyết định cổng container production |
| `appsettings.json`, `appsettings.Development.json` | Giá trị mặc định và mức log | Configuration builder; secret thực tế lấy env |

Repository, DbContext, transaction manager, current user và application service là scoped. `IUnitOfWork` và `ITransactionManager` cùng trỏ về DbContext request. Clock/cache/password hasher/mail/storage/token provider là singleton; Google provider dùng typed HttpClient timeout 10 giây. Hosted worker tạo scope riêng ở mỗi đợt xử lý, không tái dùng DbContext của request. Không có scheduler Hangfire/Quartz trong project.

## 2. ExaminationAutoSubmitWorker

Nguồn: [worker](../../LMS.Api/infrastructure/workers/ExaminationAutoSubmitWorker.cs), [repository](../../LMS.Api/infrastructure/persistence/repositories/ExaminationAttemptRepository.cs), [deadline](../../LMS.Api/application/features/examinationattempts/AttemptDeadline.cs), [grader](../../LMS.Api/application/features/examinationattempts/AttemptGrader.cs).

- Đăng ký `AddHostedService` trong infrastructure DI, chạy cùng tiến trình API, không có endpoint khởi động worker.
- Chờ tick đầu tiên của `PeriodicTimer` **30 giây**; mỗi tick tạo scope, tải tối đa **100** attempt quá hạn, tính reason, chấm và SaveChanges một lần cả batch.
- Case: InProgress hết duration/dueAt; Disconnected quá maxDisconnectMinutes hoặc hết giờ. Nếu API dừng thì worker dừng; khi API chạy lại, lượt quá hạn vẫn còn DB để quét.
- `AttemptDeadline` lấy deadline `min(startedAt + duration, dueAt)`, so disconnect timeout để chọn reason. `AttemptGrader` so đáp án snapshot, thang điểm 10; câu điền khuyết chỉ đúng khi mọi blank khớp đáp án chấp nhận.
- Không dùng Redis, SMTP hoặc SignalR. Không cần trình duyệt còn mở; kết quả được đọc lại bằng `GET /api/examinations/{id}/my-attempt` hoặc `/attempts`.
- Request start/save/disconnect/reconnect/submit/violation dùng cùng helper theo service, nhưng worker không gọi HTTP và không chạy transaction Serializable như service. Row-version attempt phát hiện xung đột; lỗi một batch được log Error, vòng sau tiếp tục. Chu kỳ 30 giây không phải cam kết hoàn tất trong 30 giây khi backlog >100 hoặc DB lỗi.
- `POST /api/examinations/{id}/close` tự hoàn tất attempt trong transaction request với `ExaminationClosed`; không đợi worker. `POST .../auto-submit` là action Admin/System có kiểm tra lý do, khác lịch tự quét.

## 3. FileCleanupQueue và FileCleanupWorker

Nguồn: [queue](../../LMS.Api/infrastructure/storage/FileCleanupQueue.cs), [worker](../../LMS.Api/infrastructure/storage/FileCleanupWorker.cs), [entity](../../LMS.Api/domain/entities/storage/FileCleanupTask.cs), [DbContext](../../LMS.Api/infrastructure/persistence/AppDbContext.cs).

| Bước | Khi nào | Hành vi |
| --- | --- | --- |
| `Enqueue` | Xóa material/lesson/exercise hoặc thay attachment | Add task vào DbContext đang dùng; SaveChanges upsert bằng ON CONFLICT cùng transaction nghiệp vụ |
| `EnqueueImmediatelyAsync` | Upload file thành công nhưng lưu metadata thất bại | Dùng DbContext factory để ghi queue độc lập; không phụ thuộc transaction đang rollback |
| Fallback delete | Không ghi được cleanup queue | Service thử `DeleteIfExistsAsync`; nếu vẫn lỗi log Error, giữ lỗi nghiệp vụ ban đầu |
| Quét | Ngay khi worker bắt đầu, rồi mỗi 10 giây | Tối đa 100 task `NextAttemptAt <= now`, sắp theo NextAttemptAt/Id |
| Thành công | File bị xóa hoặc không còn tồn tại | Remove task DB, save cuối batch |
| Thất bại | Storage/network/quyền truy cập lỗi | Tăng AttemptCount, lưu LastError và lịch retry 1/2/4/8/16/32/60 phút, Warning |

Task bền vững qua restart, không có max-attempt/dead-letter queue hoặc API quản trị task. Task chỉ lưu object key, không lưu provider; đổi Local/S3 mà chưa chuyển file có thể làm cleanup chạy sai nơi. Unique object_key chống enqueue trùng, không phải distributed claim/lock cho nhiều worker. Xóa message mềm và ẩn direct conversation không enqueue attachment; đây không phải garbage collector xóa mọi file cũ.

## 4. Logger: ai ghi, ghi gì, đọc ở đâu

Project dùng `Microsoft.Extensions.Logging.ILogger<T>` do ASP.NET host cung cấp; chưa cấu hình Serilog, NLog, sink file, OpenTelemetry hay dịch vụ log tập trung. Khi chạy terminal xem stdout/stderr; container xem `docker compose ... logs`. Không có thư mục log nghiệp vụ được code tự tạo.

| Nguồn logger | Thời điểm / mức | Nội dung và mục đích |
| --- | --- | --- |
| `presentation/middleware/GlobalExceptionHandler.cs` | Exception HTTP → Error cho lỗi 500 bất ngờ; Warning cho lỗi nghiệp vụ và ExternalServiceUnavailableException 503 | Error kèm exception/stack; Warning chỉ status + exception type, không message chi tiết |
| `infrastructure/workers/ExaminationAutoSubmitWorker.cs` | Error khi quét/chấm/save batch lỗi | Biết attempt quá hạn chưa được hoàn tất; retry ở tick sau |
| `infrastructure/storage/FileCleanupWorker.cs` | Warning từng file delete lỗi; Error vòng xử lý lỗi | Attempt, object key và exception; task tiếp tục nằm DB |
| `application/features/coursecontents/{Lesson,Exercise,Submission}Service.cs` và `features/chats/ChatService.cs` | Warning khi enqueue cleanup lỗi; Error khi fallback delete cũng lỗi | Theo dõi file mồ côi sau upload lỗi |
| `infrastructure/persistence/SystemAccountSeeder.cs` | Information khi bỏ qua vì thiếu cấu hình, tạo/promote; Warning nếu password không đạt 8–128 | Log có email tài khoản; không log password |
| EF Core framework | `Microsoft.EntityFrameworkCore.Database.Command` mặc định Warning | SQL mức Information bị ẩn; warning/error vẫn hiện |
| ASP.NET framework | `Microsoft.AspNetCore` mặc định Warning; Default Information | Chẩn đoán framework theo category |
| Frontend `lib/api/chat-realtime.ts` | SignalR `LogLevel.Warning` | Chẩn đoán lỗi kết nối ở browser console |

Không mọi catch đều log: mail chia sẻ bank thất bại thông thường bị bỏ qua không log; phục hồi auth best-effort và lỗi ghi cookie frontend cũng có catch trống. Không dùng “không có log” làm bằng chứng thao tác đã thành công. Log cleanup có object key, seeder có email, exception có thể có dữ liệu hạ tầng; hạn chế chia sẻ log thô.

Không có audit trail tập trung ghi ai gọi từng endpoint. CreatedBy/ModifiedBy ở entity và violations của attempt chỉ bao phủ dữ liệu tương ứng, không tương đương audit log đầy đủ. Hướng dẫn chẩn đoán: [operations](../operations.md).

## 5. Auth và HTTP helper

| File | Công dụng | Nơi gọi / tình huống |
| --- | --- | --- |
| `presentation/security/HttpContextCurrentUser.cs` | Đọc principal thành authenticated/userId/Role | Service không tự parse HTTP header |
| `application/abstractions/auth/CurrentUserExtensions.cs` | Lấy user/role bắt buộc; thiếu → UnauthorizeException | Các service cần danh tính hiện tại |
| `domain/enums/users/RoleExtensions.cs` | `IsAdminOrSystem` | Quyền quản trị; không tự làm mọi action đều được phép (question banks có ngoại lệ chỉ đọc) |
| `application/features/auth/utils/AuthValidation.cs` | Email normalize, password 8–128, mã 6 ký tự, tên Google | Login/register/reset/OAuth |
| `.../AuthSecurity.cs` | RNG, SHA-256, base64url, fixed-time compare | Refresh key, OAuth PKCE/binding/state/code, mã email |
| `.../AuthCacheKeys.cs`, `AuthCacheEntries.cs` | Tập trung tên key và shape Redis | Auth/registration/password reset; bảng đầy đủ ở tools-and-integrations |
| `.../AuthFailurePolicy.cs` | Phân biệt lỗi được phép phục hồi credential đã consume | Refresh, redeem loginCode, OAuth callback |
| `presentation/OAuthHttpResponseExtensions.cs` | Cookie binding + no-store + xóa cookie | AuthController authorize/callback; Secure luôn true |
| `infrastructure/authentication/PasswordHashing/AspNetCorePasswordHasher.cs` | Hash/verify qua PasswordHasher của framework | Mật khẩu local, mã verify/reset, seed System |
| `infrastructure/authentication/OAuth/OAuthResolver.cs` | Chọn IOAuthProvider theo tên | Chỉ có Google; provider không hỗ trợ trả lỗi |
| `presentation/middleware/GlobalExceptionHandler.cs` | Map exception thành HTTP/ProblemDetails và log | Exception request; không phải handler mọi lỗi hub/worker hoặc lỗi binding |

## 6. Helper nghiệp vụ và persistence

| File / thư mục | Làm gì | Khi nào cần đọc |
| --- | --- | --- |
| `application/features/courses/CourseAuthorization.cs` | Quyền quản lý/read content theo Admin/System, teacher chủ, Accepted student | Debug 403 ở lessons/exercises/exams; không sửa UI để vượt quyền |
| `application/abstractions/storage/FileUploadPolicy.cs` | Validate metadata/stream/file limit thống nhất | Mọi module upload; xem FileUpload/FileContent/StoredFile trong IFileStorage |
| `application/common/PagedResult.cs` | Items/Page/PageSize/TotalCount và TotalPages tính toán | Users/courses/enrollments/submissions/attempt results; chat history trả mảng riêng |
| `application/abstractions/time/IClock.cs`, `infrastructure/time/SystemClock.cs` | Cấp UtcNow, có thể fake trong test | Auth expiry, deadline thi/bài tập; một số entity/token/worker còn dùng DateTime.UtcNow trực tiếp |
| `infrastructure/persistence/AppDbContext.cs` | Unit of Work, transaction retry, mapping, cleanup upsert | Tất cả repo; tối đa 3 lần thử serialization/deadlock, delay 25ms rồi 50ms |
| `.../DatabaseConstraintPolicy.cs` | Danh sách unique constraint map thành 409 | Không phải mọi unique violation tự thành 409 |
| `.../AppDbContextFactory.cs` | Tạo context design-time cho dotnet ef | Migration CLI; không phải context request |
| `.../SystemAccountSeeder.cs` | Tạo/promote System theo env sau migration | Startup ngoài Testing; không đổi password/status của user được promote |
| `.../configurations/**` | EF mapping, FK/cascade/restrict, max length, query filters/indexes | Điều tra dữ liệu ẩn, xóa quan hệ, uniqueness |
| `.../repositories/**` | Query EF, Include/AsNoTracking/pagination/locks | Kiểm tra số query, lọc/sắp xếp thực tế |
| `.../migrations/**`, `AppDbContextModelSnapshot.cs` | Schema evolution và trạng thái model EF | Deploy/migrate; không chỉnh snapshot đơn lẻ để sửa DB |
| `Properties/AssemblyInfo.cs` | Cho test assembly truy cập internal | Unit tests helper nội bộ |

## 7. File hỗ trợ build, test và deploy

| File (từ backend root) | Vai trò / khi dùng |
| --- | --- |
| `LMS.sln`, `LMS.Api/LMS.Api.csproj`, `LMS.Domain.Tests/LMS.Domain.Tests.csproj` | Tập project và dependency; restore/build/test |
| `LMS.Domain.Tests/**` | Domain/service/JSON/HTTP/hub/storage/persistence tests; fixture/fake giúp cô lập dependency |
| `example.env` | Mẫu cấu hình local; không phải cấu hình production Compose; không đưa `.env` thật vào docs |
| `docker-compose.yml` | Chỉ PostgreSQL/Redis dev; không khởi chạy Next/API |
| `Dockerfile`, `.dockerignore` | Build/publish .NET, runtime user không root, volume `/app/storage`; lọc build context |
| `docker-compose.prod.yml` | PostgreSQL/Redis/API/Next/nginx, volumes, service network và healthcheck DB/Redis |
| `nginx.conf.template` | TLS, reverse proxy hai domain, WebSocket timeout; còn default host trỏ TiNo |
| `deploy.sh` | Nhận component + full SHA 40 hex; flock 300 giây chống 2 repo deploy đồng thời; cập nhật tag, pull/up/ps |
| `.github/workflows/ci-deploy.yml` | PR chạy checks/build image; main push hoặc workflow_dispatch thành công mới publish GHCR/deploy SSH |
| `.gitignore` | Loại artifact/secret khỏi Git; không phải công cụ xóa secret đã lỡ commit |
| `LMS.sln.DotSettings.user` / IDE metadata nếu có | Cấu hình cá nhân IDE, không phải contract/runtime |

`deploy.sh` cập nhật tag trong `.env` trước pull/up, không tự rollback hoặc chờ readiness API. Compose có healthcheck DB/Redis nhưng API chưa map `/health`. Không dùng container “running” thay kiểm tra đăng nhập/upload/chat. Xem [deploy-vps](../deploy-vps.md) và [operations](../operations.md).
