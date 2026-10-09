# API – Course Content: Lessons và Materials

Cần JWT. Teacher sở hữu course hoặc Admin được tạo/sửa; student có enrollment `Accepted` được đọc (chỉ nội dung `Published`).

| Method | Path | Quyền | Chức năng |
| --- | --- | --- | --- |
| POST | `/api/courses/{courseId}/lessons` | Teacher sở hữu, Admin | Tạo bài giảng |
| GET | `/api/courses/{courseId}/lessons` | Teacher sở hữu, Admin, Student `Accepted` | Danh sách bài giảng |
| PUT | `/api/courses/{courseId}/lessons/reorder` | Teacher sở hữu, Admin | Sắp xếp lại |
| GET | `/api/lessons/{lessonId}` | như trên | Chi tiết |
| PUT | `/api/lessons/{lessonId}` | Teacher sở hữu, Admin | Sửa |
| DELETE | `/api/lessons/{lessonId}` | Teacher sở hữu, Admin | Xóa |
| GET | `/api/lessons/{lessonId}/materials` | Teacher sở hữu, Admin, Student `Accepted` | Danh sách tài liệu |
| POST | `/api/lessons/{lessonId}/materials` | Teacher sở hữu, Admin | Upload tài liệu |
| GET | `/api/materials/{materialId}/content` | như danh sách | Tải/xem tài liệu |
| DELETE | `/api/materials/{materialId}` | Teacher sở hữu, Admin | Xóa tài liệu |

**Dữ liệu đi qua:** PostgreSQL (`lessons`, `materials`); các endpoint file còn qua Storage và Cleanup queue.

## Lesson object

```json
{
  "id": "00000000-0000-0000-0000-000000000011",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "name": "Variables",
  "content": "...",
  "order": 1,
  "status": "Draft",
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

## `POST /api/courses/{courseId}/lessons`

**Request:**

```json
{
  "name": "Variables",
  "content": "...",
  "order": 1,
  "status": "Draft"
}
```

**Response `201 Created`:** Lesson object. `order` không được trùng trong cùng course.

## `GET /api/courses/{courseId}/lessons`

**Response `200 OK`:** Mảng Lesson sắp theo `order`. Teacher/Admin thấy `Draft` và `Published`; student chỉ thấy `Published`.

## `GET /api/lessons/{lessonId}`

**Response `200 OK`:** Lesson object. Student truy cập lesson `Draft` nhận `404`.

## `PUT /api/lessons/{lessonId}`

**Request:**

```json
{
  "name": "Variables",
  "content": "Updated content",
  "order": 1,
  "status": "Published"
}
```

**Response `200 OK`:** Lesson object. `order` không trùng trong course.

## `PUT /api/courses/{courseId}/lessons/reorder`

Body phải chứa **đầy đủ** ID lesson hiện có của course theo thứ tự mới.

**Request:**

```json
{
  "lessonIds": [
    "00000000-0000-0000-0000-000000000012",
    "00000000-0000-0000-0000-000000000011"
  ]
}
```

**Hành vi:** Trong một transaction, đánh lại `order` liên tục từ `1`. **Response `200 OK`:** Mảng Lesson theo thứ tự mới.

## `DELETE /api/lessons/{lessonId}`

Xóa lesson và metadata material; file vật lý được đưa vào Cleanup queue. **Response `204 No Content`.**

## Material object

```json
{
  "id": "00000000-0000-0000-0000-000000000012",
  "lessonId": "00000000-0000-0000-0000-000000000011",
  "fileName": "lesson.pdf",
  "objectKey": "courses/00000000-0000-0000-0000-000000000003/lesson.pdf",
  "contentType": "application/pdf",
  "fileSize": 12345,
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

`objectKey` là khóa nội bộ; client không dựng URL từ nó mà dùng endpoint `content`.

## `GET /api/lessons/{lessonId}/materials`

Student chỉ đọc material của lesson `Published`. **Response `200 OK`:** Mảng Material object.

## `POST /api/lessons/{lessonId}/materials`

**Request:** `multipart/form-data`, field `file` (bắt buộc, không rỗng, < 20 MB).

```text
file=@lesson.pdf
```

**Hành vi:** Stream file vào Storage, lưu metadata PostgreSQL. **Response `200 OK`:** Material object.

## `GET /api/materials/{materialId}/content?download=false`

**Response `200 OK`:** Binary stream với `Content-Type` của file. `download=true` trả kèm tên gốc trong `Content-Disposition`.

## `DELETE /api/materials/{materialId}`

Xóa metadata, file vào Cleanup queue. **Response `204 No Content`.**
