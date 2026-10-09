# LMS Backend – Tài liệu

ASP.NET Core (.NET 10) + PostgreSQL + Redis + SignalR. Mã nguồn tại [../LMS.Api](../LMS.Api).

## Đọc theo thứ tự

1. [overview.md](overview.md) – Dự án làm gì, công nghệ, kiến trúc phân lớp, Redis, storage, worker.
2. [setup.md](setup.md) – Cài đặt, biến môi trường, chạy, migration, test.
3. Kiến trúc và hành vi:
   - [architecture/data-flow.md](architecture/data-flow.md) – Luồng dữ liệu từng chức năng (sequence diagram, thứ tự thao tác, điều gì xảy ra khi lỗi).
   - [architecture/error-handling.md](architecture/error-handling.md) – Xử lý ngoại lệ, mapping HTTP, retry/phục hồi, danh mục lỗi theo module.
   - [architecture/data-model.md](architecture/data-model.md) – Bảng, ràng buộc, xóa mềm, đồng thời, snapshot câu hỏi.
   - [architecture/business-rules.md](architecture/business-rules.md) – Ma trận quyền và quy tắc nghiệp vụ.
4. Tham chiếu API (request/response JSON, hành vi, dữ liệu đi qua):
   - [api/conventions.md](api/conventions.md) – Quy ước chung, enum, phân trang, lỗi.
   - [api/auth.md](api/auth.md) · [api/users.md](api/users.md) · [api/courses.md](api/courses.md) · [api/question-banks.md](api/question-banks.md)
   - [api/course-content.md](api/course-content.md) · [api/exercises.md](api/exercises.md)
   - [api/examinations.md](api/examinations.md) · [api/examination-attempts.md](api/examination-attempts.md)
   - [api/chat.md](api/chat.md)

Tài liệu mô tả đúng hành vi hiện tại của code. Khi đổi controller, DTO hoặc service, cập nhật file tương ứng.
