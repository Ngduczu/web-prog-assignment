# Tái rà soát lỗi nghiệp vụ LMS

Ngày rà soát: `2026-10-06`

> Trạng thái sửa lỗi ngày `2026-10-08`: **15/15 finding RF-01..RF-15 cùng RR-01, RR-02, RR-04, RR-05, RR-06 đã hoàn thành**. Lần rà soát bổ sung còn **1 finding RR-03 chưa sửa**, được ghi ở mục 7 để xử lý về sau. Nội dung RF bên dưới được giữ làm bằng chứng audit ban đầu; lịch sử triển khai, test và quyết định nghiệp vụ nằm trong `BUSINESS-FIX-PROGRESS.md`.

## 1. Phạm vi và phương pháp

Đã đọc lại toàn bộ các lớp controller, application service, domain entity, repository, EF Core configuration/migration, worker, Redis, mail, local/S3 storage và test hiện có.

Lần rà soát này không mặc định 18 lỗi trong báo cáo cũ là toàn bộ phạm vi. Các luồng được đối chiếu lại từ HTTP → authorization → state transition → persistence → response, đặc biệt ở các biên thời gian, soft-delete, retry và request đồng thời.

Xác minh tại thời điểm phát hiện:

- `dotnet test LMS.Domain.Tests/LMS.Domain.Tests.csproj --no-restore`: **163 passed, 0 failed, 0 skipped**.
- Build trong lần chạy test thành công.
- `dotnet ef migrations has-pending-model-changes`: **không có model change chưa tạo migration**.
- PostgreSQL và Redis trong Docker đang healthy, nhưng bộ integration test hiện tại thay phần lớn service/cache bằng fake và chưa chạy các kịch bản cạnh tranh thật trên PostgreSQL.
- Một test chẩn đoán tạm thời bằng `ToQueryString()` đã được dùng để xác nhận lỗi global query filter rồi xóa khỏi workspace.

## 2. Tóm tắt finding mới

| Mã | Mức | Nhóm | Tóm tắt |
|---|---|---|---|
| RF-01 | High | Soft-delete | Global filter bảng con không loại course đã xóa; `courses/mine` có thể trả course soft-delete |
| RF-02 | High | Exercise | Exercise `Closed` biến mất khỏi student dù cần xem lịch sử submission/điểm |
| RF-03 | High | Examination | Đóng examination không kết thúc attempt đang hoạt động |
| RF-04 | High | Examination | Admin có thể auto-submit sớm với reason không đúng trạng thái |
| RF-05 | High | Concurrency | Serializable conflict có thể thoát thành HTTP 500 thay vì retry/409 |
| RF-06 | High | Exercise | Có thể thay attachment sau khi exercise đã publish/closed |
| RF-07 | Medium | Time | Examination không bắt buộc `StartAt`/`DueAt` là UTC |
| RF-08 | Medium | Validation | Một số input vượt schema chỉ lỗi khi ghi DB |
| RF-09 | Medium | Chat | Gửi text qua REST không broadcast SignalR |
| RF-10 | Medium | Auth | Refresh/OAuth one-time credential bị consume trước khi downstream hoàn tất |
| RF-11 | Medium | Mail | Register/resend/reset có thể để lại trạng thái dở dang khi gửi mail lỗi |
| RF-12 | Medium | File | Cleanup queue không bền vững và chat attachment không dùng queue |
| RF-13 | Medium | Examination | Attempt chưa hoàn thành vẫn trả điểm `0` thay vì `null` |
| RF-14 | Low | Chat | Read receipt có thể lùi về message cũ hoặc trỏ tới message đã xóa |
| RF-15 | Low | Conflict mapping | Mọi unique violation vẫn bị map chung thành `409` trong DbContext |

## 3. Phân tích chi tiết

### RF-01 — Global query filter của bảng con không chặn dữ liệu thuộc course đã xóa

Các configuration của `Enrollment`, `Lesson`, `Exercise` và `Examination` dùng dạng:

```csharp
x => x.Course == null || x.Course.DeletedAt == null
```

Trong khi quan hệ được cấu hình optional. SQL thực tế của query enrollment là `LEFT JOIN` tới tập course đã được lọc `deleted_at IS NULL`, sau đó kiểm tra `course.id IS NULL OR course.deleted_at IS NULL`. Course đã soft-delete không join được, trở thành `NULL`, nên nhánh `Course == null` làm điều kiện đúng.

Ảnh hưởng trực tiếp đã xác nhận:

- `EnrollmentRepository.ListByStudentAsync` vẫn lấy enrollment thuộc course đã xóa.
- `CourseService.ListMineAsync` lấy các ID này rồi gọi `CourseRepository.ListByIdsAsync`, mà method này dùng `IgnoreQueryFilters()`.
- Không có bước `!course.IsDeleted` trước khi map DTO, nên student có thể thấy course đã xóa trong `GET /api/courses/mine`.

Các service nội dung hiện thường lấy lại course bằng `includeDeleted=false`, nên phần lớn endpoint khác được chặn ở tầng service. Tuy vậy filter sai tạo rủi ro cho repository consumer mới và worker.

**Khuyến nghị:** bỏ nhánh `Course == null` nếu FK nghiệp vụ bắt buộc, cấu hình relationship required đúng với schema, hoặc lọc bằng FK/subquery không bị vô hiệu bởi query filter. `ListMineAsync` vẫn nên phòng thủ bằng cách loại `IsDeleted` và repository list theo ID không nên mặc định `IgnoreQueryFilters()`.

### RF-02 — Exercise đóng bị mất khỏi lịch sử của student

`ExerciseService.ListAsync` và `EnsureStudentCanAccessPublishedExercise` chỉ cho student thấy status `Published`. Khi teacher gọi close, exercise chuyển sang `Closed` và lập tức biến mất khỏi list/get của student.

Trong khi `SubmissionService.GetMineAsync` vẫn cho lấy submission của exercise closed nếu client còn giữ `exerciseId`. `API.md` cũng mô tả student vẫn xem submission/điểm cũ sau khi close.

**Ảnh hưởng:** sau reload, frontend không còn đường khám phá exercise ID để mở bài đã nộp hoặc xem điểm; hành vi phụ thuộc state cục bộ của client.

**Khuyến nghị:** student được list/get cả `Published` và `Closed`; chỉ các thao tác upload/submit/unsubmit mới yêu cầu `Published` và còn hạn.

### RF-03 — `CloseAsync` không xử lý attempt đang thi

`ExaminationService.CloseAsync` chỉ đổi status examination sang `Closed`. Nó không tìm hoặc auto-submit attempt `InProgress`/`Disconnected`.

Các API lưu answer/submit attempt cũng không kiểm tra examination còn `Published`; chúng chỉ kiểm tra status attempt và deadline. Vì vậy teacher đóng đề sớm nhưng student đang thi vẫn có thể tiếp tục làm và submit đến duration/due deadline.

**Ảnh hưởng:** thao tác “close” không có hiệu lực kết thúc kỳ thi như trạng thái thể hiện; điểm có thể được phát hành vì examination đã Closed trong khi attempt vẫn đang chạy.

**Khuyến nghị:** chốt semantics. Nếu close là kết thúc ngay, close và auto-submit toàn bộ active attempt trong transaction/batch có reason riêng. Nếu close chỉ ngăn attempt mới, đổi tên/mô tả trạng thái và không dùng Closed làm điều kiện phát hành điểm cho attempt active.

### RF-04 — Endpoint admin auto-submit không kiểm tra reason

`AutoSubmitAsync` chỉ cấm `StudentSubmitted` và yêu cầu role Admin. Method không xác nhận:

- `DurationExpired` chỉ dùng khi duration/due đã hết.
- `DisconnectTimeout` chỉ dùng cho attempt disconnected đã quá timeout.
- `MaxViolationsExceeded` chỉ dùng khi counter đạt ngưỡng.

Admin biết UUID có thể kết thúc bất kỳ active attempt nào ngay lập tức với audit reason sai.

**Khuyến nghị:** bỏ endpoint public và để worker gọi application command nội bộ, hoặc validate điều kiện server theo từng reason. Nếu cần thao tác cưỡng chế thủ công, dùng reason riêng như `ManuallyTerminated` và ghi actor/audit trail.

### RF-05 — Transaction serializable chưa có xử lý serialization failure

Capacity enrollment, course update, bảo vệ admin cuối cùng và nhiều examination transition chạy trong transaction serializable. `AppDbContext.ExecuteWithIsolationAsync` commit một lần, không retry PostgreSQL `40001` và không chuyển lỗi này thành domain conflict.

`Database.CreateExecutionStrategy()` hiện không được cấu hình `EnableRetryOnFailure`, nên không có cơ sở đảm bảo serialization failure được tự chạy lại.

**Kịch bản:** hai request accept enrollment cuối cùng cùng đọc một accepted count; PostgreSQL hủy một transaction ở save/commit. Request bị hủy đi qua global handler thành `500` thay vì retry hoặc `409` có thể xử lý.

**Khuyến nghị:** thêm retry có giới hạn cho `40001`/deadlock ở toàn transaction, hoặc map lần thất bại cuối thành `409`. Bổ sung integration test cạnh tranh trên PostgreSQL thật.

### RF-06 — Attachment bài tập có thể bị thay sau khi phát hành

`ExerciseService.UpdateAsync` chỉ cho sửa exercise `Draft`, nhưng `UploadAttachmentAsync` không kiểm tra status. Teacher có thể thay đề/file hướng dẫn khi exercise đã `Published` hoặc `Closed`, kể cả sau khi student đã nộp.

**Ảnh hưởng:** nội dung bài giao không còn ổn định và không có version/audit để chứng minh student đã làm theo file nào.

**Khuyến nghị:** chỉ cho upload/replace attachment ở `Draft`, hoặc tạo revision mới có timestamp và giữ file cũ gắn với các submission đã phát sinh.

### RF-07 — Schedule examination chưa enforce UTC

`ValidateDefinition` kiểm tra thứ tự và duration nhưng không kiểm tra `DateTimeKind.Utc`, trong khi cột PostgreSQL là `timestamp with time zone` và contract yêu cầu UTC. Exercise đã có kiểm tra này nhưng examination chưa có.

**Ảnh hưởng:** timestamp `Local`/`Unspecified` có thể bị diễn giải lệch hoặc bị Npgsql từ chối lúc save, biến input client thành lỗi 500.

**Khuyến nghị:** yêu cầu cả `StartAt` và `DueAt` có `Kind == Utc`, hoặc chuyển contract sang `DateTimeOffset` và chuẩn hóa tại boundary.

### RF-08 — Validation chưa khớp schema/domain

Các trường hợp rõ ràng:

- Exercise description được DB giới hạn 4000 nhưng `ValidateExercise` không kiểm tra.
- Chat content được DB giới hạn 4000 nhưng `ChatMessage.NormalizeContent` không giới hạn; text quá dài tới DB mới lỗi.
- Local registration dùng phần trước `@` làm `FullName`; email hợp lệ có local part dài hơn 100 sẽ vượt cột `full_name`.
- OAuth dùng `externalInfo.FullName.Trim()` mà không cắt/từ chối quá 100 ký tự.
- Admin create user chưa giới hạn email 255 ký tự trong `UserService.NormalizeEmail`.

**Ảnh hưởng:** request hợp lệ ở application boundary có thể rơi thành database exception/500.

**Khuyến nghị:** gom validation theo giới hạn domain/schema và dùng chung cho mọi đường tạo user/message/exercise.

### RF-09 — REST text message không phát realtime

Upload attachment qua REST đã broadcast `MessageReceived`, và gửi qua `ChatHub.SendMessage` cũng broadcast. Riêng `POST /api/chat/rooms/{roomId}/messages` chỉ gọi service rồi trả `201`, không dùng `IHubContext`.

**Ảnh hưởng:** cùng một loại message nhưng client khác nhận realtime hay không phụ thuộc endpoint sender đã dùng.

**Khuyến nghị:** controller REST text broadcast cùng event sau khi save thành công, hoặc đưa publish event vào một application notification dùng chung để tránh bỏ sót đường ghi mới.

### RF-10 — One-time credential bị mất khi bước sau thất bại

- Refresh token được `ConsumeAsync` trước khi đọc auth version/user và trước khi lưu refresh token mới.
- OAuth login code được consume trước khi phát token pair mới.
- OAuth state được consume trước khi exchange code/call Google/link account.

Nếu Redis, DB hoặc provider lỗi sau consume, credential cũ không còn và response mới chưa được tạo. Client phải login/bắt đầu OAuth lại dù request chưa hoàn tất.

**Khuyến nghị:** dùng redeem record có trạng thái `pending/completed`, idempotency result ngắn hạn, hoặc khôi phục credential có kiểm soát khi lỗi transient. Không khôi phục cho lỗi xác thực/quyền thực sự.

### RF-11 — Gửi mail có thể để lại account/code/cooldown dở dang

- Register commit user `Inactive`, sau đó mới tạo code và gửi mail. Nếu Redis/mail lỗi, request trả 500 nhưng email đã bị chiếm; retry register trả conflict.
- Resend verification và forgot password lấy cooldown trước khi gửi mail. Mail lỗi vẫn giữ cooldown.
- Forgot password có thể lưu reset code nhưng người dùng không nhận được, trong khi API che giấu trạng thái theo thiết kế.

**Khuyến nghị:** dùng mail outbox/delivery status; register response chỉ thành công khi job gửi đã được ghi bền vững. Khi gửi trực tiếp thất bại, giải phóng cooldown hoặc cho retry idempotent.

### RF-12 — File cleanup mới chỉ đảm bảo trong vòng đời process

`FileCleanupWorker` dùng `Channel<string>` in-memory. Khi process restart/crash, các object key chưa xử lý bị mất; sau 5 lần retry thất bại record cũng chỉ được log và bỏ.

Ngoài ra `ChatService.SendMessageWithAttachmentAsync` vẫn gọi storage delete trực tiếp trong `catch`, không dùng queue. Lỗi cleanup có thể che lỗi gốc và file orphan không có retry.

**Khuyến nghị:** persistent outbox/table cleanup trong cùng transaction DB, worker đánh dấu hoàn tất, retry/backoff và job reconciliation. Chat attachment phải dùng cùng cơ chế.

### RF-13 — Attempt đang hoạt động trả score giả bằng 0

`ShouldRevealScore` trả `true` khi attempt chưa `Submitted/AutoSubmitted`. Do entity khởi tạo `CorrectAnswers=0`, `TotalQuestions` có giá trị và `Score=0`, API `my-attempt` trả các trường kết quả này ngay khi student đang làm bài.

**Ảnh hưởng:** frontend có thể hiển thị điểm 0 hoặc hiểu nhầm đã được chấm. Đây không làm lộ đáp án nhưng sai trạng thái nghiệp vụ.

**Khuyến nghị:** mọi result field phải `null` khi attempt còn active; sau hoàn thành mới áp dụng `ShowScoreImmediately`/Closed/DueAt.

### RF-14 — Read receipt có thể đi lùi

`MarkAsReadAsync` chỉ kiểm tra message thuộc room rồi gán `LastReadMessageId`. Không kiểm tra message bị xóa và không so sánh thứ tự với receipt hiện tại.

**Ảnh hưởng:** request đến sai thứ tự có thể đưa con trỏ đọc về message cũ, làm số unread tăng trở lại; receipt cũng có thể trỏ tới message đã xóa.

**Khuyến nghị:** lưu thêm `LastReadAt` hoặc so sánh `(CreatedAt, Id)` và chỉ cập nhật tiến; reject/ignore deleted message.

### RF-15 — Unique violation vẫn bị phân loại quá rộng

`GlobalExceptionHandler` có allowlist constraint, nhưng `AppDbContext.SaveChangesAsync` đã bắt **mọi** PostgreSQL unique violation và đổi thành `ConflictException` trước đó. Do vậy allowlist ở middleware không có tác dụng đối với lỗi qua DbContext.

**Ảnh hưởng:** lỗi unique ngoài các bất biến nghiệp vụ đã biết cũng bị trả `409`, có thể che lỗi lập trình/model. Mặt khác các lỗi race hợp lệ đều nhận message chung “record already exists”, không chỉ ra resource nào xung đột.

**Khuyến nghị:** chuyển allowlist xuống `AppDbContext` hoặc bỏ catch chung và để exception handler phân loại theo constraint name.

## 4. Điểm cần chốt nghiệp vụ, chưa kết luận là bug

1. **Số lần thi:** hiện student được tạo attempt mới không giới hạn sau khi attempt trước hoàn thành. Nếu chỉ cho một lần hoặc giới hạn N lần, model cần `MaxAttempts` và rule rõ ràng.
2. **Course `Closed`:** hiện đóng course chặn enrollment mới và archive chat, nhưng accepted student vẫn học, nộp bài và bắt đầu examination; teacher vẫn có thể tạo/publish nội dung. Cần xác định Closed là “ngừng tuyển” hay “kết thúc lớp”.
3. **Close examination:** cần xác định close kết thúc ngay hay chỉ ngăn attempt mới.
4. **Sửa bài đã phát hành:** nếu cho thay attachment, cần versioning; nếu không, khóa ở Draft.
5. **Anti-cheat:** event do client tự báo và có thể gửi lặp. Cần quyết định mức tin cậy trước khi dùng để auto-submit có hậu quả cao.
6. **Rời/revoke course:** hiện mất enrollment Accepted cũng làm student mất quyền đọc submission/điểm cũ. Cần chính sách lưu quyền lịch sử hay xóa quyền hoàn toàn.

## 5. Giai đoạn sửa đề xuất

### Giai đoạn A — Sửa trạng thái sai nhìn thấy ngay

1. RF-01: sửa query filter và loại course soft-delete khỏi `mine`.
2. RF-02: cho student xem exercise Closed và lịch sử submission.
3. RF-13: ẩn result field khi attempt chưa hoàn thành.
4. RF-09, RF-14: đồng nhất realtime và read receipt.

### Giai đoạn B — Khóa bất biến thi và nội dung

1. Chốt semantics close examination/course và giới hạn số attempt.
2. RF-03, RF-04: kết thúc attempt đúng điều kiện, reason đúng audit.
3. RF-06: khóa attachment sau publish hoặc triển khai revision.
4. RF-07, RF-08: hoàn thiện UTC và schema-bound validation.

### Giai đoạn C — Concurrency và khả năng phục hồi

1. RF-05: retry/map serialization failure và thêm PostgreSQL concurrency tests.
2. RF-10, RF-11: idempotency/outbox cho token, OAuth và mail.
3. RF-12: persistent file-cleanup outbox/reconciliation.
4. RF-15: phân loại DB constraint chính xác.

## 6. Kết luận

Hệ thống hiện build và vượt toàn bộ test sẵn có, nhưng vẫn có lỗi nghiệp vụ ở các nhánh chưa được test: soft-delete, lịch sử exercise, đóng kỳ thi, admin auto-submit, lỗi cạnh tranh PostgreSQL và các workflow nhiều hệ thống Redis/DB/mail/storage.

Ưu tiên sửa trước là **RF-01 → RF-02 → RF-03/RF-04 → RF-05**, vì đây là các lỗi làm trạng thái người dùng nhìn thấy sai hoặc làm sai kết quả/audit của kỳ thi. Sau đó mới xử lý reliability và cleanup dài hạn.

## 7. Backlog từ lần rà soát bổ sung

Ngày rà soát bổ sung: `2026-10-06`

Trạng thái xác minh tại thời điểm ghi nhận:

- `dotnet test LMS.Domain.Tests/LMS.Domain.Tests.csproj --no-restore`: **195 passed, 0 failed, 0 skipped**.
- Working tree sạch trước khi cập nhật tài liệu.
- Các finding dưới đây được xác nhận bằng việc đối chiếu code path; bộ test hiện tại chưa có integration test PostgreSQL cho các race condition tương ứng.

| Mã | Mức | Nhóm | Trạng thái | Tóm tắt |
|---|---|---|---|---|
| RR-01 | High | Submission | Hoàn thành | Dùng `xmin` và transaction serializable; upload lỗi cạnh tranh dọn file mới |
| RR-02 | High | Auth | Hoàn thành | Password và auth version cùng commit trong PostgreSQL; token cũ bị từ chối |
| RR-03 | Medium | OAuth | Chưa sửa | Khôi phục state không làm authorization code đã redeem dùng lại được |
| RR-04 | Medium | File | Hoàn thành | Cleanup best-effort bảo toàn lỗi gốc; enqueue dùng atomic upsert |
| RR-05 | Medium | Registration | Hoàn thành | Register retry dùng chung cooldown atomic và xác minh password |
| RR-06 | Medium | Concurrency | Hoàn thành | Mỗi transaction attempt dựng aggregate multiple-choice mới hoàn toàn |

### RR-01 — Submission không bảo vệ transition đồng thời

**Trạng thái: Đã sửa ngày `2026-10-08`.** Chi tiết triển khai và xác minh nằm trong `BUSINESS-FIX-PROGRESS.md`.

`SubmissionService.UploadAsync` kiểm tra submission còn `Draft` trước khi upload file, nhưng không khóa record hoặc kiểm tra lại trạng thái khi ghi metadata. `SubmitAsync`, `UnsubmitAsync` và `GradeAsync` cũng là các read-check-write độc lập. Entity `Submission` không có row-version/concurrency token và các transition không chạy trong cùng transaction serializable.

Các lịch thực thi có thể xảy ra:

- Upload đọc `Draft`, một request khác submit thành công, sau đó upload vẫn thay attachment của bài đã nộp.
- Grade và unsubmit cùng đọc `Submitted`; request đến sau có thể ghi đè status của request trước, tạo submission `Draft` nhưng vẫn còn grade hoặc đưa bài đã unsubmit trở lại `Graded`.
- Hai upload cùng đọc object key cũ có thể lost update; tùy thời điểm worker xử lý cleanup task, file của request thắng trước đó có thể trở thành orphan.

**Ảnh hưởng:** tính bất biến “chỉ draft mới được thay file” và “bài graded không thể unsubmit” không được đảm bảo dưới request đồng thời; nội dung được chấm có thể không phải nội dung student đã submit.

**Hướng sửa:** thêm PostgreSQL row-version (`xmin`) cho `Submission`, bao toàn bộ transition bằng transaction phù hợp và chuyển concurrency conflict thành `409`. Với upload, sau khi lưu object phải kiểm tra/ghi metadata có điều kiện theo version; nếu conflict thì enqueue object mới để cleanup. Thêm integration test thật cho upload-vs-submit và grade-vs-unsubmit.

### RR-02 — Reset password và thu hồi session không atomic

**Trạng thái: Đã sửa ngày `2026-10-08`.** Chi tiết triển khai và xác minh nằm trong `BUSINESS-FIX-PROGRESS.md`.

`PasswordResetService.ResetAsync` consume reset code, lưu password hash mới vào PostgreSQL, rồi mới khởi tạo/tăng `auth:version` trong Redis. Khối phục hồi hiện chỉ bao quanh `SaveChangesAsync`; lỗi Redis xảy ra sau khi DB commit không khôi phục code và không thể rollback password.

**Ảnh hưởng:** API có thể trả lỗi trong khi mật khẩu thực tế đã đổi; người dùng không biết mật khẩu nào đang có hiệu lực. Nếu auth-version chưa tăng, access/refresh token cũ có thể tiếp tục hợp lệ khi Redis hoạt động lại với version cũ hoặc fallback version `1`.

**Hướng sửa:** lưu security version bền vững trong PostgreSQL hoặc dùng transactional outbox để bắt buộc hoàn tất việc thu hồi session. Reset cần có trạng thái/idempotency result để retry trả cùng kết quả thay vì consume code lần nữa. Bổ sung fault-injection test tại từng bước sau DB commit.

### RR-03 — OAuth state recovery không phục hồi authorization code

Callback consume OAuth state trước khi gọi provider. Khi downstream lỗi, controller ghi state trở lại Redis trong một phút. Tuy nhiên `LoginWithOAuthAsync`/`LinkOAuthAsync` exchange authorization code với Google trước khi đọc hoặc ghi dữ liệu nội bộ. Nếu exchange đã thành công rồi DB/Redis lỗi, authorization code đã bị provider consume và retry callback với state được phục hồi vẫn thất bại.

**Ảnh hưởng:** cơ chế recovery hiện chỉ hữu ích với lỗi xảy ra trước khi provider chấp nhận code; nó không đảm bảo retry cho lỗi DB, tạo user/link hoặc phát login code sau exchange.

**Hướng sửa:** sau khi exchange thành công, ghi một pending OAuth identity nội bộ có TTL/idempotency key và cho callback retry tiếp tục từ kết quả đó. Không quảng bá việc khôi phục state như bảo đảm toàn bộ callback có thể chạy lại.

### RR-04 — Persistent file cleanup vẫn có thể che lỗi gốc

**Trạng thái: Đã sửa ngày `2026-10-08`.** Chi tiết triển khai và xác minh nằm trong `BUSINESS-FIX-PROGRESS.md`.

Các catch sau upload gọi `CleanupFileAsync`/`EnqueueImmediatelyAsync` rồi mới `throw`. Nếu cleanup queue cũng không ghi được — đặc biệt khi PostgreSQL chính là thành phần đang lỗi — exception từ cleanup thay thế exception nghiệp vụ/persistence ban đầu. `EnqueueImmediatelyAsync` còn dùng chuỗi `AnyAsync` rồi `Insert`, nên hai request có thể cùng vượt qua bước kiểm tra và va unique constraint.

`Enqueue` trong cùng unit of work chỉ kiểm tra `DbSet.Local`, không kiểm tra task đã tồn tại trong DB. Unique constraint của `file_cleanup_tasks.object_key` không thuộc business-conflict allowlist, nên duplicate queue task có thể làm rollback cả thay đổi nghiệp vụ và trả `500`.

**Ảnh hưởng:** mất nguyên nhân lỗi gốc, cleanup không chắc được ghi lại khi DB unavailable và request hợp lệ có thể thất bại vì duplicate outbox task.

**Hướng sửa:** cleanup trong catch phải là best-effort và luôn bảo toàn exception gốc; dùng upsert/`ON CONFLICT DO NOTHING` cho enqueue. Cân nhắc fallback xóa trực tiếp có log khi persistent queue không khả dụng và thêm test queue failure/race.

### RR-05 — Register retry bỏ qua cooldown verification

**Trạng thái: Đã sửa ngày `2026-10-08`.** Chi tiết triển khai và xác minh nằm trong `BUSINESS-FIX-PROGRESS.md`.

Khi email đã thuộc local account `Inactive`, `RegisterAsync` gọi thẳng `CreateAndSendVerificationCodeAsync`. Đường này dùng `SetAsync` để ghi cooldown, trong khi endpoint `ResendVerificationAsync` dùng `SetIfNotExistsAsync` và từ chối khi cooldown còn hiệu lực. Register retry cũng không xác nhận password gửi lại khớp với password hash đã lưu.

**Ảnh hưởng:** caller có thể gọi lại `/register` để thay verification code liên tục, làm link/code trong email trước mất hiệu lực và gửi mail vượt qua rule cooldown của resend. Global auth rate limit chỉ giới hạn số request theo IP, không giữ bất biến theo account.

**Hướng sửa:** chỉ coi register là idempotent khi payload tương thích, hoặc hướng mọi account `Inactive` qua cùng một hàm resend có thao tác acquire cooldown atomic. Không thay code hiện hành nếu cooldown chưa hết. Thêm test gọi register lặp trong một phút và với password khác.

### RR-06 — Transaction retry dùng lại aggregate multiple-choice đã mutate

**Trạng thái: Đã sửa ngày `2026-10-08`.** Chi tiết triển khai và xác minh nằm trong `BUSINESS-FIX-PROGRESS.md`.

`QuestionService.CreateMultipleChoiceAsync` tạo `Question` và options bên ngoài callback transaction. Callback save graph lần đầu, sau đó gán `CorrectOptionId` và save lần hai. `AppDbContext` retry deadlock/serialization bằng cách clear change tracker rồi gọi lại callback.

Nếu lỗi retryable xảy ra ở lần save thứ hai hoặc lúc commit, object được capture đã có `CorrectOptionId`. Lần retry re-add question và correct option cùng lúc, tạo vòng phụ thuộc insert: option cần question tồn tại, đồng thời question trỏ tới correct option chưa tồn tại.

**Ảnh hưởng:** transaction retry có thể thoát bằng lỗi FK/circular dependency khác thay vì phục hồi deadlock; cam kết RF-05 không đúng cho đường tạo câu hỏi multiple-choice.

**Hướng sửa:** dựng aggregate mới hoàn toàn bên trong mỗi lần callback chạy, hoặc thiết kế schema/write path không cần hai lần save và không capture mutable entity qua các attempt. Thêm fault-injection test gây lỗi retryable sau lần save thứ hai.

### Thứ tự xử lý đề xuất

1. `RR-01`: bảo vệ submission lifecycle và file correctness.
2. `RR-02`: bảo đảm đổi mật khẩu luôn thu hồi session hoặc có khả năng hoàn tất lại.
3. `RR-04`: không để cleanup che lỗi gốc và làm enqueue idempotent.
4. `RR-05`: thống nhất cooldown register/resend.
5. `RR-03`: làm OAuth callback resumable sau provider exchange.
6. `RR-06`: làm transaction callback retry-safe.

Sau mỗi finding cần cập nhật trạng thái tại bảng trên, ghi test/migration liên quan vào `BUSINESS-FIX-PROGRESS.md`, và tạo một commit riêng cho đúng một lỗi.
