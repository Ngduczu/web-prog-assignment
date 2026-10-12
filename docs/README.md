# LMS Backend – Tài liệu

ASP.NET Core (.NET 10) + PostgreSQL + Redis + SignalR. Mã nguồn tại [../LMS.Api](../LMS.Api).

Đối chiếu **2026-10-13**, backend base commit `eef952f`, frontend base `0e772fb`. Tài liệu mô tả code hiện hành, chưa xác nhận production/test runtime. Phiên bản package lấy từ repository, không nâng dependency trong đợt này.

## Dùng để viết báo cáo

- [project-report.md](project-report.md) — Bài toán, phạm vi, tác nhân, 26 use case, kiến trúc, bảo mật, đánh đổi, kiểm thử và bố cục báo cáo.
- [tools-and-integrations.md](tools-and-integrations.md) — JWT, Redis, Mail, OAuth, EF/PostgreSQL, SignalR, Local/S3, Docker/CI: dùng ở đâu, khi nào, API nào, dữ liệu/TTL/cấu hình và lỗi.
- [architecture/supporting-components.md](architecture/supporting-components.md) — Worker, logger, DI, middleware, seeder, clock, auth/upload/pagination helpers, persistence/test/deployment files.
- [architecture/source-map.md](architecture/source-map.md) — Chỉ mục file nguồn/cấu hình theo thư mục và ký hiệu truy vết.
- [api/endpoint-index.md](api/endpoint-index.md) — 105 action trong 9 controller; method, route, action, auth attribute.
- [api/contracts.md](api/contracts.md) — 90 record DTO/request/support contract và enums (bao gồm 3 contract storage nội bộ).
- [architecture/data-dictionary.md](architecture/data-dictionary.md) — 28 bảng, 229 property mapping, kiểu/nullable/index/FK/CHECK từ EF snapshot.
- [testing.md](testing.md) — 246 phương thức Fact/Theory trong 26 file; phân biệt fake/provider thật và danh mục với kết quả chạy.
- [operations.md](operations.md) — Runbook, chẩn đoán, backup/restore, tình huống kiểm tra thực tế.
- [review-findings.md](review-findings.md) — Backend proposals chưa duyệt, không triển khai trong đợt docs.
- [Frontend docs](../../frontend/docs/README.md) — Routes/BFF, UI modules, Excel, theme/i18n, guard thi và findings.

## Đọc theo thứ tự

1. [overview.md](overview.md) – Dự án làm gì, công nghệ, kiến trúc phân lớp, Redis, storage, worker.
2. [setup.md](setup.md) – Cài đặt, biến môi trường, chạy, migration, test.
   [deploy-vps.md](deploy-vps.md) – Compose production, GHCR/Actions, TLS, biến môi trường và phụ thuộc VPS.
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

Khi đổi controller, DTO, service, cấu hình, worker hoặc migration, cập nhật tài liệu module và chỉ mục/phụ lục tương ứng. Không ghi test/benchmark hoặc tính năng dự kiến thành đã hoàn thành nếu chưa có bằng chứng. Link frontend giả định hai repo đặt cạnh nhau.
