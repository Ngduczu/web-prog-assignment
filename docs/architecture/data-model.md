# Backend – Mô hình dữ liệu (PostgreSQL)

Đối chiếu mã nguồn ngày **2026-10-13**. Mô tả implementation trong repository, không phải xác nhận môi trường production.

Nguồn: `LMS.Api/domain/entities` và `LMS.Api/infrastructure/persistence/configurations`. Tên bảng/cột là `snake_case`. Khóa chính là `uuid`.

## 1. Sơ đồ quan hệ

```mermaid
erDiagram
    users ||--o{ external_users : "liên kết OAuth"
    users ||--o{ courses : "teacher sở hữu"
    users ||--o{ enrollments : "student"
    courses ||--o{ enrollments : ""
    courses ||--o{ lessons : ""
    lessons ||--o{ materials : ""
    courses ||--o{ exercises : ""
    exercises ||--o| exercise_attachments : ""
    exercises ||--o{ submissions : ""
    submissions ||--o| submission_attachments : ""
    users ||--o{ question_banks : "teacher sở hữu"
    question_banks ||--o{ questions : ""
    questions ||--o{ multiple_choice_options : ""
    questions ||--o{ fill_in_blank_answer_keys : ""
    courses ||--o{ examinations : ""
    examinations ||--o{ examination_question_selection_rules : ""
    examinations ||--o{ examination_questions : "snapshot"
    examination_questions ||--o{ examination_multiple_choice_options : ""
    examination_questions ||--o{ examination_fill_in_blank_answer_keys : ""
    examinations ||--o{ examination_attempts : ""
    examination_attempts ||--o{ student_answers : ""
    student_answers ||--o{ student_fill_in_blank_answers : ""
    examination_attempts ||--o{ examination_violations : ""
    courses ||--o| chat_rooms : "course room"
    chat_rooms ||--o{ chat_participants : ""
    chat_rooms ||--o{ chat_messages : ""
    chat_messages ||--o{ chat_attachments : ""
```

## 2. Bảng, ràng buộc và index

Luồng xử lý và dữ liệu:

```mermaid
flowchart TD
    N0["Service kiểm nghiệp vụ trước ghi"]
    N1["EF Core SaveChanges → PostgreSQL"]
    N2["UNIQUE, FK, CHECK và NOT NULL chặn dữ liệu sai"]
    N3["Constraint nghiệp vụ đã map → 409; lỗi khác theo error-handling"]
    N0 --> N1
    N1 --> N2
    N2 --> N3
```

Index `UNIQUE` là chốt chặn cuối cùng cho các quy tắc nghiệp vụ; service thường kiểm tra trước để trả lỗi `409` rõ ràng.

| Bảng | Vai trò | Ràng buộc / index đáng chú ý |
| --- | --- | --- |
| `users` | Tài khoản | `UNIQUE(email)`; unique filtered index `IX_users_single_system_role` với `role = 'System'` (tối đa một System); `auth_version` là concurrency token, tăng khi đổi mật khẩu/role/status; `password_hash` null với tài khoản chỉ dùng Google |
| `external_users` | Liên kết OAuth | `UNIQUE(provider, external_user_id)`, `UNIQUE(user_id, provider)` |
| `courses` | Khóa học | Soft delete bằng `deleted_at` (global query filter `deleted_at IS NULL`); index `teacher_id`, `status`, `deleted_at` |
| `enrollments` | Đăng ký học | `UNIQUE(student_id, course_id)`; index `(course_id, status)`; query filter theo course chưa xóa |
| `lessons` | Bài giảng | `UNIQUE(course_id, lesson_order)`; query filter theo course chưa xóa |
| `materials` | Tài liệu bài giảng | `UNIQUE(object_key)` |
| `exercises` | Bài tập | Index `course_id`, `(course_id, status)`; query filter theo course chưa xóa |
| `exercise_attachments` | File đề | `UNIQUE(exercise_id)` (một file/bài), `UNIQUE(object_key)` |
| `submissions` | Bài nộp | `UNIQUE(exercise_id, student_id)`; shadow property `Version` ánh xạ system column PostgreSQL `xmin` (optimistic concurrency) |
| `submission_attachments` | File bài nộp | `UNIQUE(submission_id)`, `UNIQUE(object_key)` |
| `question_banks` | Ngân hàng câu hỏi | index `teacher_id` |
| `questions` | Câu hỏi nguồn | index `(question_bank_id, type, difficulty)` |
| `multiple_choice_options` | Đáp án trắc nghiệm | `UNIQUE(question_id, option_order)` |
| `fill_in_blank_answer_keys` | Đáp án điền khuyết | `UNIQUE(question_id, blank_order, expected_answer)` |
| `examinations` | Kỳ thi | index `(course_id, status)`, `(start_at, due_at)`; query filter theo course chưa xóa |
| `examination_question_selection_rules` | Rule chọn câu (Random) | `UNIQUE(examination_id, question_bank_id, difficulty)` |
| `examination_questions` | **Snapshot** câu hỏi của kỳ thi | `UNIQUE(examination_id, question_order)`; index `source_question_id` (chỉ để truy vết, không ràng buộc khóa ngoại sống còn) |
| `examination_multiple_choice_options` | Snapshot option | `UNIQUE(examination_question_id, option_order)` |
| `examination_fill_in_blank_answer_keys` | Snapshot đáp án điền khuyết | `UNIQUE(examination_question_id, blank_order, expected_answer)` |
| `examination_attempts` | Lượt làm bài | `UNIQUE(examination_id, student_id)` → mỗi student **một lượt hiện hữu**/kỳ thi; shadow `Version` ánh xạ `xmin`; index `(status, last_activity_at)` phục vụ worker |
| `student_answers` | Đáp án đã chọn | `UNIQUE(examination_attempt_id, examination_question_id)` |
| `student_fill_in_blank_answers` | Đáp án điền khuyết của student | `UNIQUE(student_answer_id, blank_order)` |
| `examination_violations` | Nhật ký vi phạm | index `(examination_attempt_id, occurred_at)` |
| `chat_rooms` | Phòng chat | `UNIQUE(course_id)` (một room/course), `UNIQUE(direct_message_key)` (một room/cặp user), index `archived_at` |
| `chat_participants` | Thành viên room | `UNIQUE(chat_room_id, user_id)`; `last_read_message_id`, `conversation_cleared_at` (ẩn lịch sử đến mốc này), `conversation_hidden_at` (ẩn direct room khỏi danh sách) |
| `chat_messages` | Tin nhắn | Index `(chat_room_id, created_at, id)` cho phân trang; xóa mềm bằng `deleted_at` |
| `chat_attachments` | File chat | `UNIQUE(object_key)` |
| `file_cleanup_tasks` | Hàng đợi xóa file | `UNIQUE(object_key)`; index `next_attempt_at`; cột `attempt_count`, `last_error` |

## 3. Xóa mềm và query filter

Luồng xử lý và dữ liệu:

```mermaid
flowchart TD
    D["Course DeletedAt khác null"] --> Q["Global query filter"]
    Q --> H["Ẩn course, enrollments, lessons, exercises, examinations"]
    R["Restore: bỏ DeletedAt"] --> V["Dữ liệu con hiện trở lại"]
    C["ChatMessage DeletedAt khác null"] --> S["Repository/service loại message và chặn tải file"]
    P["Participant ConversationClearedAt"] --> U["Chỉ ẩn lịch sử với user đó; không xóa bytes"]
```

`Course` bị xóa mềm (`deleted_at`). Query filter toàn cục ẩn course đã xóa **và** các bảng con phụ thuộc course (`enrollments`, `lessons`, `exercises`, `examinations`): khi course bị xóa, mọi truy vấn qua EF tự động không thấy dữ liệu con đó. `restore` đưa course (và dữ liệu con) hiện trở lại. Repository có tham số `includeDeleted` để Admin/restore bỏ qua filter.

`ChatMessage` cũng xóa mềm nhưng không có query filter; repository/service loại message đã xóa khỏi danh sách và từ chối tải attachment của message đã xóa (`404`).

## 4. Đồng thời (concurrency)

Luồng xử lý và dữ liệu:

```mermaid
flowchart TD
    A["Transaction action"] --> P["PostgreSQL ReadCommitted hoặc Serializable theo use case"]
    P -->|40001 hoặc 40P01| R["Clear tracker; chờ 25ms rồi 50ms; tối đa 3 lần"]
    R --> P
    R -->|Hết lần thử| C["409 Conflict"]
    P -->|xmin / AuthVersion không còn khớp| C
    P -->|Commit| S["Dữ liệu được lưu"]
```

| Cơ chế | Dùng cho |
| --- | --- |
| Transaction `Serializable` + retry 3 lần | accept enrollment, update course, chỉnh/publish/close examination, mọi thao tác attempt, upload/submit/grade bài nộp, mark chat read |
| Row-version (`Version`, xmin của Postgres) | `submissions`, `examination_attempts` |
| Concurrency token | `users` (qua `auth_version`) |
| Unique index | Chốt cuối cho trùng lặp (email, enrollment, lesson order, room, attachment key...) |
| `GetByIdForUpdateAsync` | Khóa dòng `examinations` khi sửa/publish/close |

Xem cách lỗi được dịch ra HTTP ở [error-handling.md](error-handling.md).

## 5. Snapshot câu hỏi

Luồng xử lý và dữ liệu:

```mermaid
flowchart TD
    A["Teacher chủ course hoặc Admin/System → ExaminationService"] --> B["Serializable; validate cấu hình và quyền"]
    B --> C{"Chọn câu"}
    C -->|Random| R["Bank của teacher course → lọc difficulty → đủ số lượng → shuffle"]
    C -->|Manual| M["Question IDs theo thứ tự; kiểm bank của teacher course"]
    R --> S["Tạo examination_questions và snapshot đáp án"]
    M --> S
    S --> P["PostgreSQL: lưu exam, rules, snapshot và options"]
    P --> K["Ghi correct_option_id sau khi lưu options; commit"]
    K --> Q["Attempt dùng snapshot độc lập với câu hỏi nguồn"]
```

Câu hỏi trong kỳ thi **không tham chiếu** trực tiếp `questions`; chúng được sao chép vào `examination_questions` (+ option/answer key) khi tạo hoặc thay question set. Hệ quả:

- Sửa hoặc xóa câu hỏi nguồn không đổi đề thi đã tạo hay điểm đã chấm.
- `source_question_id` chỉ giữ để truy vết.
- Question bank chỉ bị chặn xóa khi còn được selection rule của một examination tham chiếu.
- Với trắc nghiệm, `examination_questions.correct_option_id` được ghi ở lần `SaveChanges` thứ hai sau khi các option có Id.

## 6. Migration

Luồng xử lý và dữ liệu:

```mermaid
flowchart TD
    N0["Entity + EF configuration → migration và model snapshot"]
    N1["API startup ngoài Testing hoặc dotnet ef database update"]
    N2["PostgreSQL cập nhật schema và __EFMigrationsHistory"]
    N3["Đối chiếu lịch sử DB thực tế với migration trong repository"]
    N0 --> N1
    N1 --> N2
    N2 --> N3
```

Thư mục `infrastructure/persistence/migrations`. Thứ tự migration hiện có:

| ID | Thay đổi |
| --- | --- |
| `20260904073847_InitialCreate` | Schema ban đầu |
| `20260904103655_FixSoftDeleteRelationshipMappings` | Quan hệ/query filter xóa mềm |
| `20260904123000_UseDecimalMegabyteFileLimit` | Giới hạn file theo MB thập phân |
| `20261006111627_AddSubmissionGrading` | Điểm và phản hồi bài nộp |
| `20261006113153_AddExerciseLifecycle` | Trạng thái bài tập |
| `20261006124414_AddPersistentFileCleanupQueue` | Hàng đợi xóa file bền vững |
| `20261008020625_AddSubmissionConcurrencyToken` | Concurrency bài nộp |
| `20261008022024_PersistUserAuthVersion` | Thu hồi phiên bằng version trong DB |
| `20261008034151_EnforceSingleExaminationAttempt` | Unique kỳ thi/student |
| `20261010090000_AddSystemRoleSingletonConstraint` | Tối đa một System |
| `20261012040018_AddDirectConversationVisibility` | Hai mốc ẩn/xóa lịch sử theo participant |

API tự `Migrate()` khi khởi động ngoài `Testing`. Danh sách trên phản ánh mã nguồn, không chứng minh DB triển khai đã áp dụng đủ; kiểm tra `__EFMigrationsHistory` trong môi trường tương ứng. Không chạy downgrade hoặc drop DB để đồng bộ tài liệu.

### Phạm vi transaction khi lưu đáp án

Bổ sung cho bảng đồng thời: hai method lưu đáp án trắc nghiệm/điền khuyết dùng `SaveChangesAsync` và optimistic concurrency, không gọi `ExecuteSerializableAsync`. Xem [đối chiếu trong data-flow](data-flow.md#bổ-sung-đối-chiếu-transaction-của-thao-tác-lưu-đáp-án).
