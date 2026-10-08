# Tiến độ sửa lỗi nghiệp vụ LMS

Cập nhật gần nhất: `2026-10-08`

Nguồn công việc: `BUSINESS-FUNCTIONAL-AUDIT.md`.

> Bảng 18/18 bên dưới phản ánh audit đầu tiên. Toàn bộ 15/15 finding RF của lần tái rà soát và 6/6 finding RR của lần rà soát bổ sung đã hoàn thành.

## Tiến độ rà soát bổ sung

| Trạng thái | Số lượng |
|---|---:|
| Hoàn thành | 6 |
| Đang thực hiện | 0 |
| Chưa thực hiện | 0 |

| Mã | Trạng thái | Ghi chú |
|---|---|---|
| RR-01 | Hoàn thành | Submission dùng PostgreSQL `xmin`; upload/submit/unsubmit/grade chạy trong transaction serializable |
| RR-02 | Hoàn thành | `AuthVersion` lưu trong PostgreSQL và tăng atomic cùng password/status/role |
| RR-03 | Hoàn thành | Pending identity cho phép callback retry mà không redeem lại provider code |
| RR-04 | Hoàn thành | Cleanup best-effort; queue upsert `ON CONFLICT DO NOTHING` trong transaction nghiệp vụ |
| RR-05 | Hoàn thành | Register retry dùng chung cooldown atomic với resend và yêu cầu password khớp |
| RR-06 | Hoàn thành | Transaction callback dựng question/options mới trên từng attempt |

### RR-01 — Bảo vệ vòng đời submission khi request đồng thời

- Thêm PostgreSQL row-version `xmin` cho `Submission`; stale write được `AppDbContext` chuyển thành `409 Conflict`.
- Bao upload, submit, unsubmit và grade trong transaction `Serializable`; mỗi lần transaction retry đều nạp lại submission và kiểm tra trạng thái mới nhất.
- Upload file trước rồi mới transition metadata trong transaction. Nếu submission đã được submit hoặc transaction thất bại, file vừa upload được đưa vào cleanup; object key cũ chỉ được cleanup cùng thay đổi metadata thành công.
- Mọi lần thay attachment đều cập nhật submission parent để hai upload và upload-vs-submit cùng tham gia optimistic concurrency.
- Thêm migration `AddSubmissionConcurrencyToken`, test mô phỏng upload-vs-submit và grade-vs-unsubmit, cùng test metadata xác nhận `Version` ánh xạ vào `xmin`.
- Xác minh: **199 passed, 0 failed, 0 skipped**; migration đã áp dụng lên PostgreSQL local; EF báo **không có model change chưa tạo migration**.

### RR-02 — Đổi mật khẩu và thu hồi session atomic

- Chuyển nguồn sự thật `AuthVersion` từ Redis sang cột bền vững `users.auth_version`, mặc định `1` và là EF concurrency token.
- Reset password thay password hash và tăng `AuthVersion` trong cùng một lần `SaveChanges`; PostgreSQL hoặc commit cả hai hoặc không commit thay đổi nào.
- Nếu DB save thất bại, service phục hồi password/version trong entity và phát lại reset entry để cùng code có thể retry.
- Login phát token theo version của user trong DB; refresh token và JWT validation đều tải user rồi đối chiếu version bền vững, nên access/refresh token cũ bị từ chối ngay sau reset.
- Đổi role/status qua Admin cũng tăng version trong cùng transaction thay vì commit DB xong mới phụ thuộc Redis.
- Cleanup attempts/cooldown sau commit là best-effort và không thể đổi một reset đã commit thành response lỗi.
- Thêm migration `PersistUserAuthVersion` và test cho DB rollback, Redis cleanup failure, refresh-token revocation, access-token revocation cùng metadata concurrency.
- Xác minh: **203 passed, 0 failed, 0 skipped**; migration đã áp dụng lên PostgreSQL local; EF báo **không có model change chưa tạo migration**.

### RR-03 — Tiếp tục OAuth callback sau provider exchange

- Sau khi provider xác thực thành công, lưu pending OAuth identity trong Redis với TTL 10 phút trước khi truy cập hoặc thay đổi dữ liệu nội bộ.
- Recovery key là SHA-256 của provider, authorization code, redirect URI và PKCE verifier; credential không xuất hiện trong Redis key và identity không thể bị dùng chéo request OAuth khác.
- Khi callback được retry sau lỗi DB hoặc lỗi phát login code, service đọc pending identity và bỏ qua bước exchange authorization code vốn chỉ dùng được một lần.
- Áp dụng chung cho cả OAuth login và link; pending entry được xóa best-effort sau khi flow thành công, hoặc tự hết hạn nếu Redis cleanup lỗi.
- Thêm test cho login và link: cố ý làm DB lookup lỗi sau provider exchange, provider từ chối lần exchange thứ hai, rồi xác nhận retry vẫn thành công với đúng một lần redeem code.
- Xác minh: **211 passed, 0 failed, 0 skipped**.

### RR-04 — Cleanup bảo toàn lỗi gốc và enqueue idempotent

- `AppDbContext.SaveChangesAsync` tách cleanup task mới khỏi EF insert thông thường và ghi bằng PostgreSQL `INSERT ... ON CONFLICT (object_key) DO NOTHING`.
- Upsert cleanup chạy trong chính transaction của thay đổi nghiệp vụ; task trùng không còn làm rollback metadata/course/chat hợp lệ.
- `EnqueueImmediatelyAsync` bỏ chuỗi `AnyAsync` rồi insert dễ race và dùng chung đường upsert atomic.
- Cleanup sau upload lỗi ở lesson, exercise, submission và chat trở thành best-effort: ưu tiên persistent queue, queue hỏng thì fallback xóa storage trực tiếp, cả hai hỏng vẫn `throw;` đúng exception ban đầu.
- Cleanup file cũ sau khi DB đã commit cũng không còn biến một thao tác thành công thành response lỗi giả.
- Thêm fault-injection tests cho queue/storage cùng lỗi và integration test hai DbContext enqueue đồng thời cùng object key trên PostgreSQL thật.
- Xác minh: **206 passed, 0 failed, 0 skipped**; test PostgreSQL xác nhận cả hai save thành công và chỉ có một task; EF báo **không có model change cần migration mới**.

### RR-05 — Thống nhất cooldown register retry và resend

- Register trên local account `Inactive` chỉ được retry khi password gửi lại khớp password hash đã lưu; payload khác trả `409` và không đổi verification state.
- Register mới, register retry và resend dùng chung thao tác acquire cooldown atomic bằng `SetIfNotExists`.
- Request register lặp trong cooldown không phát email, không thay verification code hiện hành và code cũ vẫn verify được.
- SMTP failure tiếp tục xóa code/attempt/cooldown, nên retry hợp lệ ngay sau lỗi vẫn hoạt động mà không tạo account trùng.
- Thêm test cho register lặp trong một phút và register retry bằng password khác.
- Xác minh: **208 passed, 0 failed, 0 skipped**.

### RR-06 — Làm transaction tạo multiple-choice retry-safe

- Di chuyển toàn bộ việc dựng `Question`, options và lựa chọn correct option vào bên trong transaction callback.
- Mỗi lần retry tạo aggregate mới với ID mới và `CorrectOptionId = null`, rồi thực hiện lại đúng trình tự insert graph trước, cập nhật correct option sau.
- Kết quả chỉ được gán sau khi cả hai lần save của transaction attempt thành công; attempt thất bại không rò entity đã mutate sang response.
- Thêm fault-injection test gây lỗi ở lần save thứ hai, chạy lại callback và xác nhận aggregate mới sạch, khác ID và hoàn tất đúng đáp án.
- Xác minh: **209 passed, 0 failed, 0 skipped**.

### Quét ngang vận hành sau RR-05/RR-06

- Build `LMS.Api` và `LMS.Domain.Tests`: **0 warning, 0 error**.
- Chạy toàn bộ test suite với PostgreSQL thật: **209 passed, 0 failed, 0 skipped**.
- PostgreSQL 17 và Redis 8 đều báo `healthy`; database đã ở migration mới nhất.
- EF model khớp migration snapshot, không có model change chưa tạo migration.
- API khởi động thành công trong môi trường Development; OpenAPI 3.0.4 trả `200` với **71 paths**.
- Request rỗng tới `POST /api/auth/login` đi qua routing và validation pipeline, trả `400` cùng lỗi bắt buộc `Email`/`Password` như dự kiến.

## Tiến độ tái rà soát

| Trạng thái | Số lượng |
|---|---:|
| Hoàn thành | 15 |
| Đang thực hiện | 0 |
| Chưa thực hiện | 0 |

| Mã | Trạng thái | Ghi chú |
|---|---|---|
| RF-01 | Hoàn thành | Global filter dùng quan hệ course bắt buộc; `courses/mine` loại course soft-delete ở cả repository và service |
| RF-02 | Hoàn thành | Student có enrollment accepted xem được exercise `Published` và `Closed`, nhưng không thấy `Draft` |
| RF-03 | Hoàn thành | Close examination auto-submit và chấm mọi attempt active trong cùng transaction |
| RF-04 | Hoàn thành | Admin auto-submit chỉ chấp nhận reason khi điều kiện server tương ứng đã xảy ra |
| RF-05 | Hoàn thành | Transaction retry toàn khối cho PostgreSQL serialization/deadlock, hết retry trả `409` |
| RF-06 | Hoàn thành | Chỉ exercise `Draft` được thêm hoặc thay attachment |
| RF-07 | Hoàn thành | Create/update examination từ chối `StartAt`/`DueAt` không phải UTC |
| RF-08 | Hoàn thành | Validation application/domain khớp giới hạn schema cho exercise, chat và user |
| RF-09 | Hoàn thành | REST text message broadcast `MessageReceived` giống hub và attachment REST |
| RF-10 | Hoàn thành | Refresh token, OAuth login code/state được phục hồi ngắn hạn sau lỗi downstream tạm thời |
| RF-11 | Hoàn thành | Mail lỗi rollback code/cooldown; register account inactive có thể retry idempotent |
| RF-12 | Hoàn thành | File cleanup chuyển sang PostgreSQL outbox bền vững; chat attachment dùng chung queue |
| RF-13 | Hoàn thành | Attempt active luôn trả result field `null` |
| RF-14 | Hoàn thành | Read receipt chỉ tiến, từ chối target đã xóa và được clear khi target bị xóa |
| RF-15 | Hoàn thành | Chỉ unique constraint nghiệp vụ trong allowlist được map thành `409` |

### RF-01 — Loại dữ liệu con của course đã soft-delete

- Sửa global query filter của enrollment, lesson, exercise và examination để bản ghi chỉ hợp lệ khi course liên quan còn tồn tại và chưa bị xóa.
- Khai báo bốn quan hệ course là bắt buộc, đúng với FK `Guid` không nullable và schema hiện có.
- `CourseRepository.ListByIdsAsync` không còn vô hiệu global filter; `CourseService.ListMineAsync` vẫn loại `IsDeleted` phòng thủ cho mọi repository implementation.
- Thêm test hồi quy xác nhận course đã soft-delete không xuất hiện trong danh sách course của student.
- Xác minh: **164 passed, 0 failed, 0 skipped**.

### RF-02 — Giữ exercise đã đóng trong lịch sử student

- Danh sách exercise của student trả cả `Published` và `Closed`, đồng thời tiếp tục ẩn `Draft`.
- Endpoint chi tiết và tải attachment áp dụng cùng quy tắc để student có thể mở lại bài đã đóng sau reload.
- Cập nhật contract `API.md` và thêm test cho cả list lẫn get exercise `Closed`.
- Xác minh: **165 passed, 0 failed, 0 skipped**.

### RF-03 — Đóng examination kết thúc attempt đang hoạt động

- Chốt semantics: đóng examination có hiệu lực kết thúc ngay, không chỉ ngăn attempt mới.
- Thêm submit reason `ExaminationClosed` để audit không bị gán nhầm thành hết giờ hoặc mất kết nối.
- Khi đóng, tải mọi attempt `InProgress`/`Disconnected`, chấm và auto-submit trước khi chuyển examination sang `Closed`, tất cả trong cùng transaction serializable.
- Cập nhật `API.md` và thêm test bao phủ cả attempt đang làm lẫn disconnected.
- Xác minh: **166 passed, 0 failed, 0 skipped**.

### RF-04 — Xác thực reason của admin auto-submit

- Giữ endpoint vận hành cho Admin nhưng không còn tin reason do caller truyền lên.
- `DurationExpired`, `DisconnectTimeout`, `MaxViolationsExceeded` và `ExaminationClosed` đều được đối chiếu với trạng thái/deadline server; điều kiện chưa đạt trả `409` và attempt không đổi.
- Với hai deadline trùng nhau hoặc examination hết trước, reason chuẩn là `DurationExpired`, không phải `DisconnectTimeout`.
- Cập nhật enum/contract và thêm test cho mọi reason gửi sớm cùng trường hợp duration thực sự đã hết.
- Xác minh: **171 passed, 0 failed, 0 skipped**.

### RF-05 — Xử lý serialization conflict và deadlock

- Transaction manager retry tối đa ba lần cho PostgreSQL `40001` (serialization failure) và `40P01` (deadlock), bao phủ lại toàn bộ callback nghiệp vụ chứ không chỉ câu lệnh commit.
- Xóa change tracker trước mỗi lần chạy lại để không tái sử dụng entity đã bị mutate trong transaction thất bại.
- Sau lần thử cuối, chuyển lỗi thành domain conflict `409` với thông báo có thể retry thay vì rơi thành `500`.
- Thêm test phân loại SQL state để chỉ retry đúng hai lỗi concurrency, không nuốt unique violation.
- Xác minh: **174 passed, 0 failed, 0 skipped**.

### RF-06 — Khóa attachment sau khi phát hành exercise

- Chọn quy tắc bất biến thay vì versioning: attachment chỉ được thêm/thay khi exercise còn `Draft`.
- Kiểm tra trạng thái được thực hiện trước khi upload, nên request sai không tạo file orphan.
- Cập nhật `API.md` và thêm test cho cả `Published` lẫn `Closed`.
- Xác minh: **176 passed, 0 failed, 0 skipped**.

### RF-07 — Bắt buộc lịch examination dùng UTC

- Validation dùng chung cho create/update từ chối `StartAt` hoặc `DueAt` có `DateTimeKind.Local`/`Unspecified` trước khi truy cập persistence.
- Cập nhật contract yêu cầu timestamp hậu tố `Z` và thêm test cho cả hai kind không hợp lệ.
- Xác minh: **178 passed, 0 failed, 0 skipped**.

### RF-08 — Đồng bộ validation với schema

- Exercise description và chat content được chặn ở 4000 ký tự trước khi ghi DB; chat domain bảo vệ cả create/edit.
- Full name tự sinh từ local email và full name từ OAuth được giới hạn 100 ký tự thay vì gây lỗi cột `full_name`.
- Admin create user từ chối email dài hơn 255 ký tự giống các luồng registration/OAuth.
- Cập nhật contract và thêm test hồi quy cho từng đường input nêu trong finding.
- Xác minh: **183 passed, 0 failed, 0 skipped**.

### RF-09 — Broadcast realtime cho REST text message

- `POST /api/chat/rooms/{roomId}/messages` phát `MessageReceived` tới đúng SignalR group sau khi service lưu message thành công.
- Payload và event name đồng nhất với gửi qua `ChatHub` và upload attachment REST.
- Sửa mô tả realtime đã cũ trong `API.md` và thêm controller test xác nhận response `201` cùng broadcast.
- Xác minh: **184 passed, 0 failed, 0 skipped**.

### RF-10 — Phục hồi one-time credential sau lỗi tạm thời

- Refresh token và OAuth login code được phục hồi bằng `SetIfNotExists` trong một phút nếu DB/Redis/token issuance lỗi sau consume.
- Không phục hồi với lỗi xác thực, quyền, not-found hoặc conflict thực sự để giữ semantics one-time và không mở lại credential đã bị từ chối hợp lệ.
- OAuth state cũng được phục hồi ngắn hạn khi provider/application lỗi; binding cookie chỉ bị xóa sau khi callback hoàn tất thành công.
- Nếu chính Redis phục hồi lỗi, hệ thống giữ exception gốc thay vì che khuất nguyên nhân.
- Thêm fault-injection tests xác nhận refresh token và OAuth login code cũ retry thành công sau lỗi DB tạm thời.
- Xác minh: **186 passed, 0 failed, 0 skipped**.

### RF-11 — Khôi phục workflow khi gửi mail lỗi

- Register retry trên local account `Inactive` sẽ phát lại verification code cho cùng user thay vì trả conflict hoặc tạo account mới.
- Mọi lỗi gửi verification mail xóa verification code, attempts và cooldown vừa tạo để lần sau retry sạch.
- Forgot password xóa reset code và cooldown khi SMTP lỗi, vẫn giữ cơ chế không tiết lộ email tồn tại.
- Cập nhật `API.md` và thêm fault-injection tests cho register/forgot-password retry sau SMTP failure.
- Xác minh: **188 passed, 0 failed, 0 skipped**.

### RF-12 — Hàng đợi file cleanup bền vững

- Thay `Channel<string>` in-memory bằng bảng `file_cleanup_tasks` có unique object key, attempt count, next-attempt và last error.
- Worker đọc task từ PostgreSQL, xóa task khi storage cleanup thành công; lỗi được giữ lại vô thời hạn với exponential backoff thay vì bỏ sau năm lần.
- Thao tác xóa/thay file lesson, exercise và submission ghi task trong cùng unit of work với thay đổi metadata; cleanup file upload mới sau DB failure dùng một DbContext độc lập.
- Chat attachment dùng cùng persistent queue khi save message thất bại, không còn gọi storage delete trực tiếp làm che lỗi gốc.
- Thêm migration `AddPersistentFileCleanupQueue`, model/domain test và cập nhật contract vận hành file.
- Xác minh: **189 passed, 0 failed, 0 skipped**; EF Core báo **không có model change chưa tạo migration**.

### RF-13 — Không trả điểm giả cho attempt active

- `InProgress` và `Disconnected` luôn ẩn `correctAnswers`, `totalQuestions`, `score`, kể cả khi `ShowScoreImmediately=true`.
- Chỉ attempt `Submitted`/`AutoSubmitted` mới xét rule công bố ngay, examination `Closed` hoặc qua `DueAt`.
- Cập nhật example/semantics trong `API.md` và thêm test cho cả cấu hình phát hành điểm bật/tắt.
- Xác minh: **190 passed, 0 failed, 0 skipped**.

### RF-14 — Read receipt đơn điệu và không trỏ message đã xóa

- Mark-as-read so sánh `(CreatedAt, Id)` với target hiện tại; request cũ đến trễ trở thành no-op.
- Target đã soft-delete bị từ chối; khi xóa message, mọi participant đang trỏ tới message đó được clear receipt trong cùng save.
- Production chạy transition mark-as-read trong transaction serializable để hai request đồng thời không ghi lùi con trỏ.
- Cập nhật contract và thêm test cho request đảo thứ tự cùng target đã xóa.
- Xác minh: **192 passed, 0 failed, 0 skipped**.

### RF-15 — Phân loại unique violation đúng allowlist

- Di chuyển allowlist constraint vào `DatabaseConstraintPolicy` dùng chung tại persistence và exception middleware.
- `AppDbContext.SaveChangesAsync` chỉ đổi unique violation đã biết thành `ConflictException`; constraint lạ giữ nguyên `DbUpdateException` để được log/trả `500`.
- Middleware dùng cùng policy nên không còn hai rule phân loại mâu thuẫn.
- Thêm test cho constraint nghiệp vụ hợp lệ và unique constraint ngoài allowlist.
- Xác minh: **195 passed, 0 failed, 0 skipped**.

### Xác minh cuối tái rà soát

- Toàn bộ test: **195 passed, 0 failed, 0 skipped**; build **0 warning, 0 error**.
- Migration `AddPersistentFileCleanupQueue` đã áp dụng thành công lên PostgreSQL local cùng các migration còn thiếu trước đó.
- `dotnet ef migrations has-pending-model-changes`: **không có model change chưa tạo migration**.
- Chuỗi sửa lỗi được tách thành **15 commit**, đúng một commit cho mỗi finding `RF-01..RF-15`.

## Tổng quan

| Trạng thái | Số lượng |
|---|---:|
| Hoàn thành | 18 |
| Đang thực hiện | 0 |
| Chưa thực hiện | 0 |

## Trạng thái từng lỗi

| Mã | Trạng thái | Ghi chú |
|---|---|---|
| BF-01 | Hoàn thành | Đã thêm API phục hồi attempt của student sau reload |
| BF-02 | Hoàn thành | Student khôi phục; teacher liệt kê, tải và chấm submission |
| BF-03 | Hoàn thành | Enrollment list trả student summary an toàn |
| BF-04 | Hoàn thành | Worker/service dùng deadline đến trước và đúng submit reason |
| BF-05 | Hoàn thành | Violation đối chiếu cấu hình và fullscreen grace |
| BF-06 | Hoàn thành | Điểm phát hành khi Closed/qua dueAt và đọc qua my-attempt |
| BF-07 | Hoàn thành | Chặn accept course đóng và capacity thấp hơn accepted count |
| BF-08 | Hoàn thành | Chặn disable/demote admin active cuối cùng |
| BF-09 | Hoàn thành | Chặn đổi role/status khi teacher còn sở hữu tài nguyên |
| BF-10 | Hoàn thành | Exercise Draft/Published/Closed và deadline UTC |
| BF-11 | Hoàn thành | My enrollments, reapply, leave và revoke |
| BF-12 | Hoàn thành | Thêm my courses gồm course Closed đã accepted |
| BF-13 | Hoàn thành | CRUD nội dung và quy tắc an toàn dữ liệu tham chiếu |
| BF-14 | Hoàn thành | Download và realtime chat attachment |
| BF-15 | Hoàn thành | File limit và cleanup retry sau commit |
| BF-16 | Hoàn thành | Khôi phục verification/reset code khi DB lỗi |
| BF-17 | Hoàn thành | Validation và unique conflict |
| BF-18 | Hoàn thành | Đồng nhất quyền khi list chat room |

## Nhật ký

### BF-01 — Phục hồi examination attempt

- Thêm `GET /api/examinations/{examinationId}/my-attempt`.
- Chỉ Student có enrollment `Accepted` được lấy attempt của chính mình.
- Ưu tiên attempt `InProgress`/`Disconnected`; nếu không có thì trả attempt hoàn thành gần nhất.
- Trả `404` nếu student chưa từng bắt đầu examination.
- Cập nhật application contract, repository EF Core, controller và `API.md`.
- Thêm test phục hồi attempt active và trường hợp chưa có attempt.
- Xác minh: **135 passed, 0 failed, 0 skipped**.

### BF-02 — Hoàn thiện workflow submission

- Thêm `GET /api/exercises/{exerciseId}/my-submission` cho student khôi phục bài nộp sau reload.
- Thêm danh sách phân trang submission đã nộp cho course teacher/Admin; draft của student không bị lộ.
- Thêm endpoint tải attachment với kiểm tra owner/course manager.
- Thêm chấm và cập nhật điểm `0..10`, feedback, người chấm và thời điểm chấm.
- Thêm trạng thái `Graded`; bài đã chấm không thể unsubmit hoặc thay file.
- Thêm migration `AddSubmissionGrading` và cập nhật `API.md`.
- Thêm 4 application tests cho recover, list/grade, khóa unsubmit và download.
- Xác minh: **139 passed, 0 failed, 0 skipped**.

### BF-03 — Danh tính student trong enrollment

- Danh sách enrollment của course trả thêm `student.id`, `student.fullName` và `student.email`.
- Không mở rộng quyền đọc toàn bộ user profile và không lộ phone/avatar.
- Repository eager-load student chỉ cho danh sách teacher/Admin; response tạo/cập nhật enrollment vẫn có thể trả `student: null`.
- Thêm test xác nhận course teacher nhận đúng student summary.

### BF-04 — Deadline của attempt disconnected

- Dùng chung công thức examination deadline là `min(startedAt + duration, dueAt)`.
- Repository worker nhận attempt `Disconnected` khi quá examination deadline hoặc disconnect deadline.
- Worker và `ReconnectAsync` chọn `DurationExpired`/`DisconnectTimeout` theo deadline đến trước.
- Thêm test cho trường hợp duration hết trước disconnect timeout.

### BF-05 — Enforce cấu hình anti-cheat

- Ánh xạ mọi `ExaminationViolationType` sang cờ security tương ứng; event bị tắt trả `400` và không tăng counter.
- `ExitFullscreen` yêu cầu `durationSeconds`; khoảng rời fullscreen trong grace không bị ghi violation.
- Giữ auto-submit khi số violation hợp lệ đạt `MaxViolations`.
- Thêm test cho violation bị tắt và boundary fullscreen grace.

### BF-06 — Phát hành điểm examination

- Giữ ẩn điểm ngay sau submit khi `ShowScoreImmediately=false`.
- Tự phát hành điểm khi examination `Closed` hoặc đạt `DueAt`.
- Student lấy kết quả bằng endpoint `my-attempt` của BF-01.
- Thêm test chuyển thời gian qua due time và xác nhận điểm được trả.

### BF-07 — Bất biến course capacity/enrollment

- Chỉ accept enrollment khi course còn `Open`.
- Không cho giảm `MaxStudents` thấp hơn số enrollment `Accepted`.
- Course update dùng transaction serializable khi có transaction manager.
- Thêm test cho course đóng và hạ capacity dưới số học viên hiện hữu.

### BF-08 — Bảo vệ admin active cuối cùng

- Đếm `Active Admin` thay vì tổng account có role Admin.
- Chặn admin active cuối cùng tự disable/ban/inactivate hoặc tự hạ role.
- Status/role transition chạy trong transaction serializable khi có transaction manager.
- Thêm test cho trường hợp admin active cuối cùng tự disable.

### BF-09 — Bảo vệ ownership của teacher

- Trước khi demote hoặc chuyển teacher khỏi `Active`, kiểm tra cả course (kể cả soft-deleted) và question bank đang sở hữu.
- Nếu còn dependency, trả `409` và yêu cầu chuyển quyền trước.
- Kiểm tra nằm trong cùng transaction với user transition.
- Thêm test teacher có course không thể bị disable.

### BF-10 — Vòng đời exercise

- Thêm `ExerciseStatus`: `Draft`, `Published`, `Closed`; exercise mới luôn là draft.
- Student chỉ thấy exercise published; update chỉ cho draft.
- Thêm endpoint publish/close và chặn publish khi deadline đã qua.
- `DueAt` phải dùng UTC; submission chỉ thay đổi khi exercise published.
- Thêm migration `AddExerciseLifecycle` và test lọc draft khỏi danh sách student.

### BF-11 — Vòng đời enrollment

- Thêm `GET /api/enrollments/mine` cho student khôi phục trạng thái.
- `Rejected`/`Cancelled` có thể request lại trên cùng record, không vi phạm unique index.
- Student có thể cancel request hoặc rời enrollment `Accepted`.
- Course teacher/Admin có thể revoke enrollment `Accepted`.
- Cập nhật domain tests và application test cho reapply.

### BF-12 — Danh sách course của student

- Thêm `GET /api/courses/mine` dựa trên enrollment `Accepted`.
- Kết quả gồm cả course `Open` và `Closed`, loại course soft-deleted.
- Giữ endpoint `GET /api/courses` làm danh sách course open để khám phá.
- Thêm test accepted student nhìn thấy course closed trong `mine`.

### BF-13 — Hoàn thiện quản trị nội dung

- Thêm update/delete source question; nội dung đáp án không bị sửa ngầm và snapshot examination không bị ảnh hưởng.
- Thêm xóa question bank, trả `409` nếu đang được examination selection rule tham chiếu.
- Thêm xóa lesson kèm cleanup material và xóa exercise chỉ khi còn `Draft`.
- Thêm các REST endpoint tương ứng và test cho update/delete, cleanup và chặn xóa exercise đã publish.

### BF-14 — Hoàn thiện chat attachment

- Thêm endpoint tải attachment theo `messageId`/`attachmentId`, kiểm tra lại quyền room trước khi mở storage.
- Message hoặc attachment đã xóa/không thuộc message trả `404`.
- Upload attachment qua REST phát `MessageReceived` tới cùng SignalR group.
- Thêm test participant hợp lệ tải attachment.

### BF-15 — Giới hạn và nhất quán file

- Material, exercise attachment và submission cùng giới hạn dưới 20 MB tại HTTP, application và storage boundary.
- Thêm `FileCleanupWorker`: thao tác xóa sau DB commit được đưa vào queue, retry tối đa 5 lần và không làm response báo thất bại sau khi DB đã lưu thành công.
- File mới tạo khi DB save thất bại cũng được cleanup qua queue trong runtime production.
- Hạn chế in-memory đã được thay bằng persistent cleanup outbox ở RF-12; task còn lại tiếp tục được xử lý sau restart.

### BF-16 — Khôi phục mã khi DB thất bại

- Email verification hoàn tác status về `Inactive` và cấp lại cache entry nếu lưu DB thất bại.
- Password reset hoàn tác password hash và cấp lại reset entry nếu lưu DB thất bại.
- Thêm fault-injection tests xác nhận cùng mã có thể retry.

### BF-17 — Validation và unique conflict

- Admin create/update user kiểm tra role thuộc enum, phone tối đa 20 ký tự và password null/độ dài hợp lệ.
- Global exception handler chỉ map PostgreSQL unique violation của allowlist constraint nghiệp vụ sang `409`; lỗi DB khác vẫn là `500`.
- Thêm test role ngoài enum và phone vượt schema.

### BF-18 — Đồng nhất quyền danh sách chat room

- Teacher chỉ thấy course room khi `course.TeacherId` đúng với user hiện tại; lịch sử participant không tự cấp quyền.
- Admin và student có enrollment `Accepted` giữ nguyên quyền đúng theo các thao tác room khác.
- Thêm test loại room của teacher không sở hữu course.

### Xác minh cuối

- Build: **0 warning, 0 error**.
- Test: **163 passed, 0 failed, 0 skipped**.
