# Tiến độ sửa lỗi nghiệp vụ LMS

Cập nhật gần nhất: `2026-10-06`

Nguồn công việc: `BUSINESS-FUNCTIONAL-AUDIT.md`.

> Bảng 18/18 bên dưới chỉ phản ánh phạm vi audit đầu tiên. Lần tái rà soát đã phát hiện thêm các lỗi và khoảng trống mới; xem `BUSINESS-FUNCTIONAL-REAUDIT.md`. Các mục mới chưa được tính là đã sửa trong bảng này.

## Tiến độ tái rà soát

| Trạng thái | Số lượng |
|---|---:|
| Hoàn thành | 13 |
| Đang thực hiện | 0 |
| Chưa thực hiện | 2 |

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
| RF-14..RF-15 | Chưa thực hiện | Xử lý tuần tự theo báo cáo tái rà soát |

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
