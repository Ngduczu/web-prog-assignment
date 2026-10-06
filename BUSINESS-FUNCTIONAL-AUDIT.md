# Báo cáo rà soát lỗi và khoảng trống nghiệp vụ LMS

Ngày rà soát: `2026-10-06`

## 1. Mục tiêu và phạm vi

Báo cáo này chỉ tập trung vào **tính đúng đắn và tính hoàn chỉnh của nghiệp vụ** để hệ thống có thể chạy được các luồng LMS chính. Chưa xem các hạng mục hardening, tối ưu hiệu năng, UI/UX hoặc triển khai production là mục tiêu sửa ở giai đoạn hiện tại.

Đã rà soát:

- Toàn bộ controller REST, SignalR hub và middleware.
- Application service của authentication, user, course, enrollment, course content, submission, question bank, examination, examination attempt và chat.
- Domain entity, repository, EF Core configuration/migration, Redis, worker auto-submit và file storage.
- `API.md`, `API-AUDIT.md` và toàn bộ test trong `LMS.Domain.Tests`.

Không chỉnh sửa mã nguồn trong lần rà soát này. Hai thay đổi đang có sẵn trong working tree tại `PasswordResetService.cs` và `Program.cs` được giữ nguyên.

## 2. Trạng thái chạy hiện tại

- Build thành công trên `.NET 10`.
- `dotnet test LMS.Domain.Tests/LMS.Domain.Tests.csproj --no-restore`: **133 passed, 0 failed, 0 skipped**.
- Authentication/JWT, authorization cơ bản, refresh token rotation, đăng ký/xác thực email, reset mật khẩu, CRUD course cơ bản, lesson/material, exercise, tạo đề/thi/nộp bài thi và chat đều đã có code thực thi; không còn là stub.
- Các lỗi cũ trong phần `Findings` của `API-AUDIT.md` phần lớn đã được sửa. Không nên dùng phần findings cũ làm danh sách lỗi hiện tại; phần `Follow-up sau khi sửa` của file đó phản ánh trạng thái gần hơn.

Kết luận đường cơ sở: **backend có thể build và chạy các test hiện có, nhưng chưa đủ để hoàn tất một số hành trình nghiệp vụ cốt lõi từ đầu đến cuối**. Test pass không phủ các khoảng trống được liệt kê dưới đây.

## 3. Tóm tắt mức độ ưu tiên

| Mã | Mức | Nhóm | Tóm tắt |
|---|---|---|---|
| BF-01 | Blocker | Thi online | Reload/mất trang không tìm lại được attempt đang thi |
| BF-02 | Blocker | Bài tập | Teacher không thể nhận, tải hoặc chấm bài nộp |
| BF-03 | Blocker | Enrollment | Teacher thấy `studentId` nhưng không xem được danh tính học viên |
| BF-04 | High | Thi online | Worker xử lý attempt `Disconnected` sai hạn và sai lý do |
| BF-05 | High | Thi online | Cấu hình chống gian lận không được enforce khi ghi violation |
| BF-06 | High | Thi online | Điểm bị ẩn không có đường để công bố/lấy lại sau đó |
| BF-07 | High | Course/Enrollment | Có thể accept vào course đã đóng và hạ capacity dưới số đã nhận |
| BF-08 | High | User/Admin | Admin có thể tự khóa tài khoản quản trị cuối cùng |
| BF-09 | High | User/Ownership | Đổi role/status teacher có thể làm course/question bank mất người quản lý hợp lệ |
| BF-10 | High | Exercise | Bài tập không có publish/draft và cho phép deadline vô nghĩa |
| BF-11 | Medium | Enrollment | Vòng đời enrollment bị cụt, không có “của tôi”, leave/revoke/reapply |
| BF-12 | Medium | Course | Student không liệt kê được course `Closed` đã học |
| BF-13 | Medium | Nội dung | Thiếu update/delete question và delete lesson/exercise |
| BF-14 | Medium | Chat | Attachment không tải được và upload REST không phát realtime |
| BF-15 | Medium | File | Material không giới hạn dung lượng; DB và storage có thể lệch trạng thái |
| BF-16 | Medium | Auth | Verification/reset code bị consume trước khi DB commit |
| BF-17 | Medium | Validation | Một số input/race hợp lệ ở HTTP nhưng rơi thành lỗi 500 |
| BF-18 | Low | Chat | Danh sách room có thể hiện room mà teacher hiện tại không còn quyền thao tác |

## 4. Lỗi nghiệp vụ đã xác nhận

### BF-04 — Worker xử lý attempt `Disconnected` sai hạn và sai lý do

**Bằng chứng:**

- `ExaminationAttemptRepository.ListExpiredAsync` chỉ chọn attempt `Disconnected` khi quá `DisconnectedAt + MaxDisconnectMinutes`; nhánh này không kiểm tra hạn duration hoặc `Examination.DueAt`.
- `ExaminationAutoSubmitWorker` gán mọi attempt `Disconnected` thành `DisconnectTimeout`.
- Trong khi đó `ReconnectAsync` lại kiểm tra cả disconnect timeout và duration timeout.

**Kịch bản lỗi:** student disconnect khi bài thi chỉ còn 1 phút nhưng `MaxDisconnectMinutes = 10`. Attempt có thể nằm active thêm gần 9 phút sau khi thời gian thi đã hết; worker chấm với lý do `DisconnectTimeout` thay vì `DurationExpired`.

**Ảnh hưởng:** trạng thái bài thi và audit reason sai; có cửa sổ attempt chưa được khóa đúng hạn.

**Cách sửa:** deadline thực tế của attempt disconnected phải là giá trị nhỏ nhất giữa duration deadline, examination due time và disconnect deadline. Worker chọn submit reason theo deadline nào đến trước. Thêm integration test cho cả ba mốc.

### BF-05 — Các cờ chống gian lận chưa được enforce

**Bằng chứng:** `RecordViolationAsync` chấp nhận mọi `ExaminationViolationType`, tăng `ViolationCount` và có thể auto-submit mà không đối chiếu `RequireFullscreen`, `BlockCopyPaste`, `BlockRightClick`, `DetectTabChange`, `DetectDevTools` hay `FullscreenGraceSeconds`. Các cờ này chỉ được lưu và trả về DTO.

**Kịch bản lỗi:** exam đặt `detectTabChange=false`, nhưng request `violationType=TabChanged` vẫn được tính vi phạm. Tương tự, `CopyAttempt` vẫn được tính khi `blockCopyPaste=false`.

**Ảnh hưởng:** student có thể bị auto-submit trái với cấu hình của teacher; `FullscreenGraceSeconds` hiện không có tác dụng nghiệp vụ ở backend.

**Cách sửa:** ánh xạ từng violation type sang cờ cấu hình tương ứng; reject hoặc ignore event bị tắt. Với fullscreen grace cần truyền/ghi nhận thời điểm bắt đầu vi phạm đủ để server kiểm tra, hoặc xác định rõ đây chỉ là cấu hình frontend và không dùng nó để auto-submit phía server.

### BF-07 — Bất biến capacity và trạng thái course chưa được giữ

**Bằng chứng:**

- `EnrollmentService.TransitionCoreAsync` kiểm tra capacity khi accept nhưng không kiểm tra `course.Status == Open`.
- `CourseService.UpdateAsync` cho phép giảm `MaxStudents` mà không so với số enrollment `Accepted` hiện có.

**Kịch bản lỗi:** course đóng sau khi có request `Waiting` vẫn có thể accept request đó. Course đang có 30 học viên được phép đổi `MaxStudents` xuống 1.

**Ảnh hưởng:** dữ liệu vi phạm ý nghĩa của trạng thái đóng và sức chứa; các quyết định enrollment sau đó không nhất quán.

**Cách sửa:** chỉ accept khi course `Open`; khi hạ capacity, từ chối nếu nhỏ hơn số accepted. Giữ transaction serializable hiện có và thêm test PostgreSQL cạnh tranh thực tế.

### BF-08 — Có thể mất toàn bộ quyền quản trị

**Bằng chứng:** `UpdateByAdminAsync` bảo vệ admin cuối cùng khi tự hạ role, nhưng `UpdateStatusAsync` cho phép admin tự đổi status sang `Inactive`, `Disabled` hoặc `Banned` mà không kiểm tra còn admin active khác hay không.

**Kịch bản lỗi:** admin active duy nhất tự disable mình. JWT bị vô hiệu hóa ngay và không còn tài khoản nào quản trị hệ thống.

**Ảnh hưởng:** hệ thống bị khóa khỏi các thao tác quản trị dù chương trình vẫn chạy.

**Cách sửa:** không cho admin cuối cùng rời trạng thái `Active`; đếm **active admin** trong cùng transaction. Nên cấm tự ban/disable hoặc yêu cầu một admin active khác thực hiện.

### BF-09 — Thay đổi teacher có thể tạo dữ liệu “mồ côi nghiệp vụ”

**Bằng chứng:** admin có thể đổi role hoặc status của user bất kỳ không phải admin khác. Không có kiểm tra user đang sở hữu `Course` hoặc `QuestionBank`. Trong khi quyền quản lý course/bank yêu cầu role `Teacher` (hoặc Admin), và tạo course/bank yêu cầu teacher active.

**Kịch bản lỗi:** teacher đang sở hữu course bị đổi thành `Student` hoặc `Disabled`. `TeacherId` vẫn trỏ tới user đó nhưng họ không thể quản lý nội dung; chỉ Admin còn xử lý được.

**Ảnh hưởng:** course/question bank mất chủ thể nghiệp vụ hợp lệ, dễ làm gián đoạn lớp đang hoạt động.

**Cách sửa:** trước khi đổi role/status, yêu cầu chuyển quyền sở hữu toàn bộ tài nguyên sang teacher active khác, hoặc chặn thay đổi và trả danh sách dependency cần xử lý.

### BF-16 — Mã xác thực/reset có thể mất dù cập nhật DB thất bại

**Bằng chứng:**

- `RegistrationService.VerifyEmailAsync` consume verification code trước `SaveChangesAsync`.
- `PasswordResetService.ResetAsync` consume reset code trước `SaveChangesAsync`.

**Kịch bản lỗi:** Redis consume thành công nhưng PostgreSQL timeout/deadlock/save thất bại. User chưa active hoặc mật khẩu chưa đổi, nhưng mã đã mất và không thể retry.

**Ảnh hưởng:** luồng tài khoản thất bại không phục hồi được bằng chính request cũ.

**Cách sửa:** dùng trạng thái/idempotency key hoặc transaction/outbox phù hợp giữa Redis và DB; tối thiểu phải khôi phục/reissue code có kiểm soát khi DB save thất bại và có test fault injection.

### BF-17 — Một số lỗi dữ liệu/race trả 500 thay vì lỗi nghiệp vụ

**Bằng chứng:**

- `AdminCreateUserRequest.Role` chưa được `Enum.IsDefined`; `Phone` chưa kiểm tra giới hạn 20 ký tự theo DB.
- Duplicate email/enrollment/direct room/lesson order được “check rồi insert”, nên request đồng thời vẫn có thể chạm unique constraint.
- `GlobalExceptionHandler` xếp mọi `DbUpdateException` vào 500.

**Ảnh hưởng:** input hoặc xung đột nghiệp vụ có thể xuất hiện như lỗi hệ thống; client không biết retry hay sửa dữ liệu.

**Cách sửa:** hoàn thiện validation tại application boundary; bắt unique violation theo constraint name và map sang `409 Conflict`; không map mọi lỗi DB thành 409.

### BF-18 — `ListRooms` nới quyền teacher không đúng với thao tác room

**Bằng chứng:** `ChatService.ListRoomsAsync` coi mọi user có role `Teacher` là hợp lệ với course room đã từng tham gia, không đối chiếu `course.TeacherId`. Các thao tác room khác gọi `EnsureCourseAuthorizationAsync` và yêu cầu đúng course teacher.

**Kịch bản lỗi:** một participant trước đây được đổi role thành Teacher nhưng không phải teacher của course. Room xuất hiện trong danh sách nhưng mở/gửi tin bị `403`.

**Ảnh hưởng:** trạng thái UI và quyền thực tế không nhất quán.

**Cách sửa:** dùng chung một rule authorization khi list và khi thao tác; teacher chỉ bypass enrollment nếu `course.TeacherId == userId`.

## 5. Khoảng trống làm đứt luồng nghiệp vụ chính

### BF-01 — Không thể phục hồi phiên thi sau reload/mất kết nối trang

Hiện chỉ có `start`, `questions`, answer, disconnect/reconnect, violation và submit. Không có endpoint tìm attempt active theo examination/student, cũng không có endpoint lấy lại trạng thái/result của attempt. `StartAsync` chỉ trả `409` nếu attempt active đã tồn tại.

**Hậu quả:** nếu frontend mất `attemptId` do reload, đóng tab, crash hoặc đổi thiết bị, student không thể tiếp tục bài thi dù attempt vẫn chạy và worker vẫn đếm giờ.

**Cần có tối thiểu:** `GET /api/examinations/{examId}/my-attempt` hoặc `GET /api/examination-attempts/{attemptId}`, trả active/completed attempt của chính student; quy định rõ một hay nhiều lần thi và cách chọn attempt hiện hành.

### BF-02 — Luồng bài tập dừng ở thao tác student upload/submit

Không có API để:

- Student lấy submission hiện tại sau reload.
- Teacher liệt kê submission theo exercise.
- Teacher tải attachment bài nộp.
- Teacher chấm điểm/nhận xét/trả bài.
- Student xem kết quả chấm.

**Hậu quả:** chức năng assignment chưa hoàn thành mục tiêu LMS; teacher không thể thực hiện phần chính của quy trình.

### BF-03 — Teacher không biết enrollment thuộc học viên nào

Danh sách enrollment chỉ trả `studentId`. `GET /api/users/{userId}/profile` chỉ cho chính user hoặc Admin, nên course teacher không thể lấy tên/email của student trong lớp. Danh sách user tổng quát cũng chỉ dành cho Admin.

**Hậu quả:** teacher không thể duyệt enrollment hoặc quản lý lớp theo danh tính con người.

**Cần sửa:** trả một student summary an toàn trong `EnrollmentDto`, hoặc cho course teacher đọc profile tối thiểu của student thuộc course; không lộ phone/email nếu không cần.

### BF-06 — Điểm ẩn không có thời điểm/đường công bố

Khi `ShowScoreImmediately=false`, `Map` luôn ẩn điểm của attempt đã hoàn thành. Không có endpoint result và cũng không có rule “hiện sau `DueAt`/khi exam `Closed`”. Vì vậy điểm đã chấm và lưu trong DB nhưng student không bao giờ lấy được.

**Cần quyết định nghiệp vụ:** thêm `ScoreReleaseAt`/teacher publish results, hoặc tự hiện sau `DueAt`/`Closed`; endpoint đọc attempt phải áp dụng cùng rule.

### BF-10 — Bài tập không có vòng đời phát hành và deadline chưa được kiểm tra

Exercise không có `Draft/Published/Closed`, nên accepted student thấy bài ngay khi teacher tạo. `CreateAsync`/`UpdateAsync` không ngăn `DueAt` ở quá khứ hoặc timestamp không phải UTC.

**Hậu quả:** teacher không soạn trước được; bài có thể vừa tạo đã hết hạn và student không nộp được.

### BF-11 — Vòng đời enrollment chưa hoàn chỉnh

- Không có “enrollments của tôi”, nên student không biết trạng thái `Waiting/Accepted` sau reload.
- `Rejected`/`Cancelled` tồn tại vĩnh viễn dưới unique `(StudentId, CourseId)` và không có reapply.
- Student chỉ cancel được `Waiting`; không có leave course sau khi accepted.
- Teacher/Admin không có revoke/remove accepted enrollment.

Các rule này cần được chốt rồi bổ sung endpoint/transition tương ứng; nếu cố ý không cho đăng ký lại thì API phải trả lý do/trạng thái hiện tại rõ ràng.

### BF-12 — Course đã học biến mất khỏi danh sách student

`CourseService.ListAsync` ép filter của Student thành `Open`. Accepted student vẫn xem được course `Closed` nếu biết ID, nhưng không thể tìm lại nó qua danh sách.

**Hậu quả:** mất luồng xem lịch sử lớp/nội dung cũ. Nên có `my-courses` hoặc list kết hợp course open để khám phá và course accepted để học/lịch sử.

### BF-13 — Quản trị nội dung thiếu các thao tác tối thiểu

Chưa có update/delete question, delete question bank, delete lesson và delete exercise. Dữ liệu tạo nhầm hoặc nội dung hết hiệu lực không thể quản lý qua API.

Lưu ý: question đã được snapshot vào examination nên có thể cho sửa/xóa source question mà không làm đổi đề đã tạo; vẫn cần chặn/xử lý question bank đang được selection rule tham chiếu.

### BF-14 — Chat attachment chưa hoàn tất luồng sử dụng

- DTO trả `objectKey` nội bộ nhưng không có endpoint có authorization để tải/xem attachment.
- Upload attachment qua REST không broadcast `MessageReceived`; user khác chỉ thấy sau khi tải lại lịch sử.
- Không có endpoint tải file submission, nhưng đây đã được tính trong BF-02.

### BF-15 — Luồng file còn rủi ro nghiệp vụ

- Lesson material không truyền giới hạn dung lượng, khác exercise/submission/chat.
- Khi thay file, DB được commit trước rồi mới xóa file cũ. Nếu xóa lỗi, request có thể báo lỗi sau khi metadata mới đã lưu.
- Khi xóa material, DB xóa trước storage; storage lỗi để lại orphan file.

**Cần sửa:** thống nhất quota/size policy; việc dọn file nên là eventual consistency có retry/outbox và reconciliation, không làm đảo nghĩa kết quả request đã commit.

## 6. Các điểm cần chốt nghiệp vụ trước khi code

Những điểm sau chưa đủ căn cứ để gọi là bug, nhưng phải quyết định để tránh sửa đi sửa lại:

1. Một student được thi bao nhiêu attempt cho mỗi exam? Hiện code cho phép thi lại vô hạn sau khi attempt trước hoàn thành, chỉ cấm đồng thời hai attempt active.
2. Thời gian thi có tiếp tục chạy khi disconnect không? Code hiện tiếp tục tính từ `StartedAt`; trường `RemainingSeconds` chỉ là snapshot. Nếu muốn pause thì toàn bộ công thức deadline/worker phải đổi.
3. Điểm được phát hành khi nào nếu `ShowScoreImmediately=false`?
4. Student có được xem lesson/material của course `Closed` đã accepted không? Hiện có.
5. Rejected/cancelled enrollment có được đăng ký lại, và accepted enrollment có được rời/revoke không?
6. Teacher bị disable/demote thì tài nguyên được chuyển tự động, đóng lại, hay bắt buộc chọn teacher thay thế?
7. Violation do client tự báo có đủ tin cậy để auto-submit hay cần session/event signing và rule chống gửi lặp?

## 7. Kế hoạch sửa theo giai đoạn

### Giai đoạn 0 — Khép kín các hành trình bắt buộc để dùng được

Mục tiêu: người dùng không bị mắc kẹt trong các luồng chính.

1. BF-01: thêm API lấy `my attempt`/result và phục hồi phiên thi sau reload.
2. BF-02: thêm student get current submission; teacher list/download/grade; student xem kết quả.
3. BF-03: bổ sung student summary có kiểm soát cho danh sách enrollment.
4. BF-11: thêm `my enrollments` để frontend khôi phục trạng thái sau reload.
5. Thêm integration test đi xuyên HTTP cho ba hành trình: enroll → học; upload → submit → grade; start exam → reload → resume → submit.

**Điều kiện hoàn thành:** frontend chỉ cần access token và ID resource ổn định là có thể hoàn thành ba hành trình trên, không cần giữ state chỉ tồn tại trong memory/local component.

### Giai đoạn 1 — Sửa bất biến dữ liệu và độ đúng của bài thi

1. BF-04: hợp nhất deadline của duration/due/disconnect trong service và worker.
2. BF-05: enforce cờ anti-cheat và grace period.
3. BF-06: thiết kế/cài rule phát hành điểm.
4. BF-07: bảo vệ `Open` và capacity khi accept/update.
5. BF-08, BF-09: bảo vệ admin cuối cùng và ownership của teacher.
6. Chốt số attempt tối đa và semantics của disconnect.
7. Bổ sung PostgreSQL integration/concurrency tests cho worker, enrollment capacity và role/status transition.

**Điều kiện hoàn thành:** mọi state transition quan trọng đều có guard tại service, constraint/transaction phù hợp ở DB và test cho trường hợp cạnh tranh/thời gian biên.

### Giai đoạn 2 — Hoàn thiện vòng đời nội dung học tập

1. BF-10: thêm draft/publish/close cho exercise và validate deadline UTC.
2. BF-11, BF-12: hoàn thiện reapply/leave/revoke và danh sách `my courses` gồm lịch sử.
3. BF-13: thêm update/delete/archive cho question bank/question/lesson/exercise.
4. Quy định cách xử lý nội dung đã được tham chiếu bởi exam/submission.

**Điều kiện hoàn thành:** teacher có thể tạo nháp, phát hành, sửa hợp lệ, đóng/archive và dọn nội dung mà không cần thao tác DB trực tiếp.

### Giai đoạn 3 — Hoàn thiện chat, file và khả năng phục hồi lỗi

1. BF-14: endpoint tải attachment có authorization và broadcast realtime cho upload REST.
2. BF-15: giới hạn file thống nhất; outbox/cleanup retry/reconciliation cho storage.
3. BF-16: làm verification/reset idempotent trước lỗi DB/Redis/mail.
4. BF-17: chuẩn hóa validation và map unique conflict chính xác.
5. BF-18: dùng chung authorization rule khi list và thao tác chat room.

**Điều kiện hoàn thành:** lỗi hạ tầng tạm thời không làm mất mã, mất file hoặc trả trạng thái trái với dữ liệu đã commit; xung đột nghiệp vụ trả 4xx có thể xử lý được.

## 8. Thứ tự triển khai khuyến nghị trong từng PR

Mỗi PR nên đi theo lát cắt dọc nhỏ:

1. Chốt rule và cập nhật contract/DTO.
2. Viết test thất bại cho application rule và HTTP contract.
3. Sửa domain/application.
4. Thêm migration/repository nếu cần.
5. Chạy unit + integration test và cập nhật `API.md` ngay trong cùng PR.

Không nên gom toàn bộ giai đoạn vào một PR vì examination, enrollment và user ownership đều có transaction/migration riêng, khó review và rollback.

## 9. Những hạng mục tạm thời không đưa vào kế hoạch lỗi nghiệp vụ

- CORS, logging chi tiết, secrets/config production, rate-limit tuning, monitoring và tối ưu query.
- Đổi HTTP `200` thành `201` cho các resource mà `API.md` hiện đang chủ động mô tả `200`.
- Thiết kế UI, notification, search nâng cao và báo cáo thống kê.

Các hạng mục này có thể cần trước khi deploy production, nhưng không phải trọng tâm “sửa nghiệp vụ để chương trình chạy được trước” của lần rà soát này.

## 10. Kết luận

Nên bắt đầu từ **Giai đoạn 0**, không mở rộng thêm tính năng phụ. Ba blocker lớn nhất hiện tại là phục hồi attempt khi reload, hoàn tất workflow bài nộp/chấm bài và cung cấp đủ danh tính học viên cho teacher duyệt enrollment. Sau đó sửa ngay deadline/anti-cheat của examination và các bất biến admin/course để tránh phát sinh dữ liệu sai khó phục hồi.
