# API Flow Audit

Ngày audit: `2026-09-04`

## Phạm vi

Audit ở chế độ read-only đối với:

- `API.md` và toàn bộ controller dưới `LMS.Api/presentation/controllers`.
- Application contracts/services của authentication, users, courses, enrollments, question banks, course content, examinations và examination attempts.
- Repository, persistence, Redis, mail, file storage, authentication/authorization và middleware.
- Các test hiện có trong `LMS.Domain.Tests`.

Không có file code hiện tại nào được chỉnh sửa trong quá trình audit.

## Findings

### Critical

#### 1. API chưa có authentication thực sự

- **Flow:** Tất cả endpoint cần user hiện tại.
- **Files:** `LMS.Api/presentation/Program.cs` dòng 8-9 và 41; `LMS.Api/infrastructure/DependencyInjection.cs`.
- **Bằng chứng:** Chưa thấy `AddAuthentication`, authentication scheme/JWT/OAuth implementation hoặc `UseAuthentication()`. Endpoint `login` và `oauth` trong `AuthController` chỉ trả `200 OK` rỗng.
- **Ảnh hưởng:** Không thể tạo authenticated request hợp lệ. Các service đọc `NameIdentifier`/`Role` sẽ trả `401`; `UseAuthorization()` cũng có thể fail runtime nếu authorization services chưa được đăng ký.
- **Đề xuất:** Triển khai authentication scheme và token flow; đăng ký `AddAuthentication`/`AddAuthorization`; thêm `UseAuthentication()` trước `UseAuthorization()`. Nếu chưa triển khai, loại các route stub khỏi contract public.

#### 2. `auto-submit` có thể tác động attempt của student khác

- **Flow:** `POST /api/examination-attempts/{attemptId}/auto-submit`.
- **File:** `LMS.Api/application/features/examinationattempts/ExaminationAttemptService.cs` dòng 286-308; route tại `LMS.Api/presentation/controllers/ExaminationAttemptsController.cs` dòng 24.
- **Bằng chứng:** `AutoSubmitAsync` lấy attempt trực tiếp bằng ID nhưng không gọi `EnsureStudent()` hoặc `GetOwnedAttemptAsync`. Controller cũng không có `[Authorize]`.
- **Ảnh hưởng:** Caller biết UUID có thể đổi status, submit reason và kết quả của attempt không thuộc mình.
- **Đề xuất:** Tách auto-submit thành endpoint nội bộ cho worker, có worker authentication. Nếu vẫn public, bắt buộc kiểm tra ownership/role, giới hạn reason và kiểm tra điều kiện thời gian ở server.

### High

#### 3. List lesson/exercise không enforce authorization theo contract

- **Flow:** `GET /api/courses/{courseId}/lessons` và `GET /api/courses/{courseId}/exercises`.
- **Files:** `LMS.Api/application/features/coursecontents/LessonService.cs` dòng 66-75; `LMS.Api/application/features/coursecontents/ExerciseService.cs` dòng 54-63.
- **Bằng chứng:** Service chỉ lấy course, không gọi `CourseAuthorization.EnsureCanManage`.
- **Ảnh hưởng:** Sau khi authentication hoạt động, student hoặc teacher khác có thể đọc nội dung quản trị/draft dù contract quy định chỉ teacher sở hữu hoặc Admin.
- **Đề xuất:** Enforce quyền trước khi list. Nếu mục tiêu là cho student đọc, cập nhật contract và chỉ trả lesson/exercise đã publish.

#### 4. Global exception handler làm lộ exception message ở lỗi 500

- **File:** `LMS.Api/presentation/middleware/GlobalExceptionHandler.cs` dòng 35-44.
- **Bằng chứng:** `Detail = exception.Message` được trả cả với exception không dự kiến.
- **Ảnh hưởng:** Có thể lộ thông tin database, filesystem, Redis, cấu hình hoặc implementation detail.
- **Đề xuất:** Với lỗi 500 chỉ trả message tổng quát; log exception đầy đủ ở server. Chỉ trả message chi tiết cho các domain exception đã phân loại.

#### 5. HTTP status khi tạo resource không khớp `API.md`

- **Flow:** Tạo examination, lesson và exercise.
- **Files:** `LMS.Api/presentation/controllers/ExaminationsController.cs` dòng 8-10; `LMS.Api/presentation/controllers/CourseContentController.cs` dòng 10-28.
- **Bằng chứng:** Các action dùng `Ok(...)`, trả `200 OK`; contract yêu cầu `201 Created`.
- **Ảnh hưởng:** Client dựa vào contract có thể xử lý sai response khi tạo resource.
- **Đề xuất:** Dùng `Created(...)` hoặc `StatusCode(201, result)` và thêm integration test kiểm tra status code.

### Medium

#### 6. Kiểm tra capacity enrollment có race condition

- **Flow:** Accept nhiều enrollment đồng thời.
- **Files:** `LMS.Api/application/features/enrollments/EnrollmentService.cs` dòng 119-143; `LMS.Api/infrastructure/persistence/configurations/courses/EnrollmentConfiguration.cs` dòng 21-23.
- **Bằng chứng:** Code đọc số enrollment accepted, so sánh với `MaxStudents`, rồi cập nhật. Unique index chỉ chống trùng student/course, không bảo vệ tổng capacity.
- **Ảnh hưởng:** Concurrent accept có thể làm số student accepted vượt `maxStudents`.
- **Đề xuất:** Dùng row lock/counter update atomic/transaction strategy phù hợp và thêm test concurrency thực tế.

#### 7. Upload file không có giới hạn kích thước

- **Flow:** Upload lesson material, exercise attachment và submission.
- **Files:** `LMS.Api/application/abstractions/storage/FileUploadPolicy.cs` dòng 7-31; `LessonService.cs` dòng 109-116; `ExerciseService.cs` dòng 89-96.
- **Bằng chứng:** `maximumSizeBytes` luôn là `null`; test hiện có còn xác nhận course upload không có application size limit.
- **Ảnh hưởng:** User có quyền upload có thể tiêu tốn không giới hạn disk và bandwidth.
- **Đề xuất:** Đặt giới hạn theo loại file, cấu hình multipart request limit, kiểm tra quota và cleanup orphan file định kỳ.

#### 8. Clock và UTC không nhất quán ở submission

- **Flow:** Upload/submit submission và kiểm tra deadline.
- **File:** `LMS.Api/application/features/coursecontents/SubmissionService.cs` dòng 46-49 và 139-145.
- **Bằng chứng:** Examination dùng `IClock`, nhưng submission dùng trực tiếp `DateTime.UtcNow`. Input `DateTime` cũng chưa được chuẩn hóa hoặc kiểm tra `DateTimeKind`.
- **Ảnh hưởng:** Khó test chính xác; timestamp có offset/local time có thể tạo hành vi deadline khác với contract UTC.
- **Đề xuất:** Inject `IClock` vào submission service và chuẩn hóa hoặc từ chối timestamp không phải UTC ở boundary.

#### 9. Có thể lệch trạng thái giữa database và file storage

- **Flow:** Upload file mới và cập nhật metadata file.
- **Files:** `LMS.Api/application/features/coursecontents/LessonService.cs` dòng 121-137; `ExerciseService.cs` dòng 116-136.
- **Bằng chứng:** Database save thành công trước khi xóa file cũ. Nếu xóa file cũ thất bại, request có thể báo lỗi dù metadata mới đã commit. Cleanup orphan chỉ xử lý một chiều khi save thất bại.
- **Ảnh hưởng:** Metadata và file thực tế có thể không đồng bộ; có thể phát sinh file orphan hoặc file cũ không được dọn.
- **Đề xuất:** Dùng outbox/cleanup worker hoặc trạng thái pending; coi file deletion là eventual consistency có retry và reconciliation.

### Low

#### 10. Thiếu test cho các boundary quan trọng

- **Files:** `LMS.Domain.Tests/Application/ExaminationAttemptApplicationTests.cs`, `LMS.Domain.Tests/Application/CourseContentApplicationTests.cs` và các test liên quan.
- **Thiếu coverage đáng chú ý:**
  - Authorization của `AutoSubmitAsync` và authentication của worker.
  - Status code và route model binding ở controller.
  - Unauthorized access tới lesson/exercise list.
  - Multipart file bị thiếu hoặc request body sai format.
  - Enrollment capacity khi accept đồng thời.
  - Authentication middleware, login và oauth.
  - Redis/mail failure và consistency sau lỗi storage.
- **Đề xuất:** Thêm integration tests chạy qua ASP.NET pipeline bên cạnh service unit tests.

## Các phần đã kiểm tra, chưa thấy vấn đề rõ ràng

- Course soft-delete dùng global query filter; `includeDeleted` chỉ cho Admin trong `CourseService`.
- Child operations lấy course với `includeDeleted=false`, nên course đã xóa không tiếp tục được dùng để tạo/sửa child.
- Enrollment có unique index theo student/course và kiểm tra transition `Waiting -> Accepted/Rejected/Cancelled`.
- Examination authoring dùng transaction và snapshot question/answer key để tránh question bank thay đổi sau khi publish.
- Answer save kiểm tra ownership, question membership, selected option thuộc đúng question và duplicate fill-in-blank order.
- Verification/password reset dùng Redis namespace riêng, TTL 15 phút và giới hạn 5 lần thử.
- Local storage chống path traversal bằng object-key normalization và cleanup file khi database save thất bại.

## Follow-up sau khi sửa

Đã xử lý trong lượt sửa này:

- Đăng ký JWT authentication và authorization; thêm `[Authorize]` cho các controller cần đăng nhập và mở anonymous có chọn lọc cho route public.
- Login/refresh response không còn trả `userId`, `email` hoặc `role`; các thông tin chọn lọc nằm trong JWT claims và JWT được ký bằng HMAC.
- Refresh token được consume atomically ở Redis; auth version vô hiệu hóa token khi reset password, disable user hoặc đổi role.
- Chặn auto-submit ở controller và service bằng quyền Admin.
- Bổ sung authorization cho lesson/exercise/examination list.
- Không trả exception message chi tiết cho lỗi 500.
- Sửa status tạo lesson, exercise và examination thành `201 Created`.
- Giới hạn course-content upload ở dưới 20 MiB và trả `400` khi thiếu multipart file.
- Submission dùng `IClock`; enrollment accept chạy trong serializable transaction.
- OAuth stub trả `501 Not Implemented` thay vì giả báo thành công.

## Validation

- Diagnostics compile qua VS Code không báo lỗi.
- `dotnet test LMS.sln --no-restore`: **45 passed, 0 failed**.
- Build thành công; đã xử lý warning chưa dùng của `OAuthResolver`.
- Còn rủi ro cần cân nhắc: file deletion vẫn là eventual consistency; serializable transaction cần integration test cạnh tranh thực tế; JWT validation hiện đọc user và Redis ở mỗi request.