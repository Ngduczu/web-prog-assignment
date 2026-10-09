# API – Courses và Enrollments

Cần JWT. Dữ liệu chỉ qua PostgreSQL (`courses`, `enrollments`, `users`); không dùng Redis/Storage.

| Method | Path | Quyền | Chức năng |
| --- | --- | --- | --- |
| POST | `/api/courses` | Teacher, Admin | Tạo khóa học |
| GET | `/api/courses` | Đã đăng nhập | Danh sách, lọc, phân trang |
| GET | `/api/courses/mine` | Student | Khóa học đã được nhận |
| GET | `/api/courses/{courseId}` | Đã đăng nhập | Chi tiết |
| PUT | `/api/courses/{courseId}` | Teacher sở hữu, Admin | Sửa |
| DELETE | `/api/courses/{courseId}` | Teacher sở hữu, Admin | Xóa mềm |
| POST | `/api/courses/{courseId}/restore` | Teacher sở hữu, Admin | Khôi phục |
| POST | `/api/courses/{courseId}/enrollments` | Student `Active` | Đăng ký học |
| GET | `/api/courses/{courseId}/enrollments` | Teacher sở hữu, Admin | Danh sách đăng ký |
| GET | `/api/enrollments/mine` | Student | Đăng ký của tôi |
| POST | `/api/enrollments/{enrollmentId}/accept` | Teacher sở hữu, Admin | Duyệt |
| POST | `/api/enrollments/{enrollmentId}/reject` | Teacher sở hữu, Admin | Từ chối |
| POST | `/api/enrollments/{enrollmentId}/cancel` | Student chủ đăng ký | Hủy/rời khóa |
| POST | `/api/enrollments/{enrollmentId}/revoke` | Teacher sở hữu, Admin | Loại học viên |

Đóng/xóa/khôi phục course còn kéo theo chat room của course (archive/mở lại), xem [chat.md](chat.md).

## Course object

```json
{
  "id": "00000000-0000-0000-0000-000000000003",
  "name": "C# Basics",
  "description": "Introduction",
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "maxStudents": 30,
  "status": "Open",
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z",
  "deletedAt": null
}
```

## `POST /api/courses`

Teacher tự tạo; Admin tạo cho một teacher đang `Active`.

**Request:**

```json
{
  "name": "C# Basics",
  "description": "Introduction",
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "maxStudents": 30,
  "status": "Open"
}
```

**Response `201 Created`:** Course object.

## `GET /api/courses?search=&status=&teacherId=&includeDeleted=false&page=1&pageSize=20`

- Student luôn chỉ nhận course `Open`, kể cả khi gửi `status=Closed`.
- Teacher/Admin dùng được `status`, `teacherId`.
- Course xóa mềm chỉ trả khi Admin truyền `includeDeleted=true`.

**Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000003",
      "name": "C# Basics",
      "description": "Introduction",
      "teacherId": "00000000-0000-0000-0000-000000000002",
      "maxStudents": 30,
      "status": "Open",
      "createdAt": "2026-09-04T10:00:00Z",
      "modifiedAt": "2026-09-04T10:00:00Z",
      "deletedAt": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

## `GET /api/courses/mine`

Student. Trả mảng Course object có enrollment `Accepted` của chính student, gồm cả course `Closed`; bỏ course đã xóa mềm.

## `GET /api/courses/{courseId}?includeDeleted=false`

Student xem được course `Open`; course `Closed` chỉ khi enrollment `Accepted`. `includeDeleted=true` chỉ dành Admin. **Response `200 OK`:** Course object.

## `PUT /api/courses/{courseId}`

`maxStudents` không được nhỏ hơn số enrollment `Accepted`.

**Request:**

```json
{
  "name": "Advanced C#",
  "description": "Updated",
  "maxStudents": 40,
  "status": "Open"
}
```

**Response `200 OK`:** Course object. Chuyển `Closed` sẽ archive chat room của course; chuyển lại `Open` mở lại.

## `DELETE /api/courses/{courseId}`

Xóa mềm (`deletedAt`). **Response `204 No Content`.** Chat room của course bị archive.

## `POST /api/courses/{courseId}/restore`

**Không có body.** **Response `200 OK`:** Course object. Room chỉ mở lại nếu course khôi phục ở trạng thái `Open`.

## Enrollment object

```json
{
  "id": "00000000-0000-0000-0000-000000000004",
  "studentId": "00000000-0000-0000-0000-000000000001",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "status": "Waiting",
  "student": null,
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z",
  "course": {
    "id": "00000000-0000-0000-0000-000000000003",
    "name": "C# Basics",
    "status": "Open"
  }
}
```

- `course` là bản tóm tắt (`id`, `name`, `status`) để client hiển thị tên khóa học mà không phải gọi thêm API. Có trong response của `POST .../enrollments`, `GET /api/enrollments/mine`, `GET .../enrollments` (danh sách theo course), `accept`, `reject`, `revoke`. **Không có** (`null`) ở response của `cancel`.
- `student` chỉ được điền ở danh sách theo course; các response khác là `null`.

Máy trạng thái: `Waiting → Accepted | Rejected | Cancelled`; `Accepted → Cancelled` (student rời hoặc teacher revoke); `Rejected`/`Cancelled → Waiting` khi đăng ký lại (tái dùng record).

## `POST /api/courses/{courseId}/enrollments`

**Không có body.** Student `Active` đăng ký. Nếu record cũ `Rejected`/`Cancelled` thì chuyển lại `Waiting`, không tạo bản sao. **Response `201 Created`:** Enrollment object `status: "Waiting"`.

## `GET /api/enrollments/mine`

Trả mảng Enrollment object (mọi trạng thái, kèm `course` tóm tắt) của student để khôi phục trạng thái sau reload. Course đã xóa mềm không nằm trong kết quả (global query filter). Dữ liệu lấy bằng hai truy vấn: enrollments của student, rồi courses theo danh sách `courseId`.

## `GET /api/courses/{courseId}/enrollments?page=1&pageSize=20`

**Response `200 OK`:** PagedResult, trường `student` được điền để teacher nhận diện người đăng ký, `course` là tóm tắt của course đang xem:

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000004",
      "studentId": "00000000-0000-0000-0000-000000000001",
      "courseId": "00000000-0000-0000-0000-000000000003",
      "status": "Waiting",
      "student": {
        "id": "00000000-0000-0000-0000-000000000001",
        "fullName": "Student",
        "email": "student@example.com"
      },
      "createdAt": "2026-09-04T10:00:00Z",
      "modifiedAt": "2026-09-04T10:00:00Z",
      "course": {
        "id": "00000000-0000-0000-0000-000000000003",
        "name": "C# Basics",
        "status": "Open"
      }
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

## `POST /api/enrollments/{enrollmentId}/accept`

**Không có body.** Chỉ chấp nhận khi enrollment `Waiting`, course `Open` và chưa đủ `maxStudents`. **Response `200 OK`:** Enrollment `Accepted` (có `course`).

## `POST /api/enrollments/{enrollmentId}/reject`

**Không có body.** **Response `200 OK`:** Enrollment `Rejected` (có `course`).

## `POST /api/enrollments/{enrollmentId}/cancel`

**Không có body.** Student hủy khi `Waiting` hoặc rời khóa khi `Accepted`. **Response `200 OK`:** Enrollment `Cancelled`, `course: null`.

## `POST /api/enrollments/{enrollmentId}/revoke`

**Không có body.** Chỉ cho enrollment `Accepted`. **Response `200 OK`:** Enrollment `Cancelled` (có `course`). Student mất quyền đọc nội dung, chat khóa học và làm bài thi.
