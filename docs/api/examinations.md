# API – Examinations (soạn kỳ thi)

Đối chiếu mã nguồn ngày **2026-10-13**. Mô tả implementation trong repository, không phải xác nhận môi trường production.

Cần JWT. Dữ liệu chỉ qua PostgreSQL (`examinations`, `examination_questions`, … snapshot); không dùng Redis/Storage/SMTP. Quyền Admin trong bảng bao gồm System.

| Method | Path | Quyền | Chức năng |
| --- | --- | --- | --- |
| POST | `/api/courses/{courseId}/examinations` | Teacher sở hữu, Admin | Tạo kỳ thi `Draft` + snapshot câu hỏi |
| GET | `/api/courses/{courseId}/examinations` | Teacher sở hữu, Admin | Danh sách (mọi trạng thái) |
| GET | `/api/courses/{courseId}/examinations/available` | Student `Accepted`, Teacher sở hữu, Admin | Danh sách cho người thi |
| GET | `/api/examinations/{examinationId}` | Teacher sở hữu, Admin | Chi tiết đầy đủ |
| GET | `/api/examinations/{examinationId}/student` | Student `Accepted`, Teacher sở hữu, Admin/System | Chi tiết dạng học viên; người quản lý xem được cả Draft |
| GET | `/api/examinations/{examinationId}/my-attempt` | Student `Accepted` | Lượt làm bài của tôi |
| GET | `/api/examinations/{examinationId}/attempts` | Teacher sở hữu, Admin | Kết quả cả lớp |
| DELETE | `/api/examinations/{examinationId}/attempts/{attemptId}` | Teacher sở hữu, Admin/System | Xóa lượt hiện hữu và dữ liệu con; xem [examination-attempts.md](examination-attempts.md) |
| PUT | `/api/examinations/{examinationId}` | Teacher sở hữu, Admin | Sửa cấu hình (`Draft`) |
| PUT | `/api/examinations/{examinationId}/question-set` | Teacher sở hữu, Admin | Thay bộ câu hỏi (`Draft`) |
| POST | `/api/examinations/{examinationId}/publish` | Teacher sở hữu, Admin | `Draft` → `Published` |
| POST | `/api/examinations/{examinationId}/close` | Teacher sở hữu, Admin | `Published` → `Closed` |

Vòng đời: `Draft → Published → Closed`. Làm bài xem [examination-attempts.md](examination-attempts.md).

## Examination object

```json
{
  "id": "00000000-0000-0000-0000-000000000017",
  "courseId": "00000000-0000-0000-0000-000000000003",
  "title": "Midterm",
  "description": "...",
  "status": "Published",
  "startAt": "2026-09-10T08:00:00Z",
  "dueAt": "2026-09-10T10:00:00Z",
  "durationMinutes": 90,
  "shuffleQuestions": true,
  "showScoreImmediately": true,
  "security": {
    "requireFullscreen": true,
    "fullscreenGraceSeconds": 10,
    "maxViolations": 3,
    "blockCopyPaste": true,
    "blockRightClick": true,
    "detectTabChange": true,
    "detectDevTools": false,
    "maxDisconnectMinutes": 5
  },
  "questionSelectionRules": [
    {
      "id": "00000000-0000-0000-0000-000000000018",
      "questionBankId": "00000000-0000-0000-0000-000000000005",
      "difficulty": "Medium",
      "questionCount": 20
    }
  ],
  "selectionMode": "Random",
  "selectedQuestionIds": [
    "00000000-0000-0000-0000-000000000021"
  ],
  "questionCount": 20,
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

**Student examination object** giống trên nhưng không có `questionSelectionRules`, `selectionMode`, `selectedQuestionIds`; vẫn có `questionCount` và `security`.

## `POST /api/courses/{courseId}/examinations`

**Request – chọn ngẫu nhiên theo rule:**

```json
{
  "title": "Midterm",
  "description": "...",
  "startAt": "2026-09-10T08:00:00Z",
  "dueAt": "2026-09-10T10:00:00Z",
  "durationMinutes": 90,
  "shuffleQuestions": true,
  "showScoreImmediately": true,
  "security": {
    "requireFullscreen": true,
    "fullscreenGraceSeconds": 10,
    "maxViolations": 3,
    "blockCopyPaste": true,
    "blockRightClick": true,
    "detectTabChange": true,
    "detectDevTools": false,
    "maxDisconnectMinutes": 5
  },
  "selectionMode": "Random",
  "questionSelectionRules": [
    {
      "questionBankId": "00000000-0000-0000-0000-000000000005",
      "difficulty": "Medium",
      "questionCount": 20
    }
  ],
  "questionIds": []
}
```

**Request – chọn thủ công** (ID duy nhất, đúng thứ tự mong muốn):

```json
{
  "selectionMode": "Manual",
  "questionSelectionRules": [],
  "questionIds": [
    "00000000-0000-0000-0000-000000000021",
    "00000000-0000-0000-0000-000000000022"
  ]
}
```

(Các trường còn lại như request ngẫu nhiên.)

**Quy tắc:**

- Hai chế độ loại trừ nhau; không gửi đồng thời rule và `questionIds`.
- Question bank phải thuộc teacher phụ trách course; tối đa 500 câu mỗi kỳ thi; mọi câu hỏi phải tồn tại và có đáp án hợp lệ.
- `startAt`/`dueAt` phải là UTC (`Z`), local/không timezone bị `400`.

**Hành vi:** Chọn câu hỏi (Random: lấy ngẫu nhiên và lưu cố định ngay lúc tạo; Manual: theo danh sách) → sao chép thành snapshot → lưu. **Response `201 Created`:** Examination object.

## `GET /api/courses/{courseId}/examinations`

**Response `200 OK`:** Mảng Examination object (cả `Draft`, `Published`, `Closed`).

## `GET /api/courses/{courseId}/examinations/available`

**Response `200 OK`:** Mảng Student examination object. Với student chỉ loại `Draft`; kỳ thi chưa tới giờ, quá hạn hoặc `Closed` vẫn xuất hiện để hiển thị lịch/lịch sử. Frontend dùng `status`, `startAt`, `dueAt` để hiển thị nhãn; backend kiểm tra lại khi bắt đầu.

## `GET /api/examinations/{examinationId}`

**Response `200 OK`:** Examination object.

## `GET /api/examinations/{examinationId}/student`

Không trả kỳ thi `Draft` cho student. **Response `200 OK`:** Student examination object.

## `GET /api/examinations/{examinationId}/my-attempt`

Ưu tiên attempt `InProgress`/`Disconnected`; vì mỗi student chỉ có tối đa một attempt cho mỗi kỳ thi nên đây chính là attempt duy nhất của họ. Frontend gọi khi reload để lấy lại `attempt.id`; nếu `Disconnected` thì gọi tiếp `reconnect`. `404` nếu chưa từng bắt đầu. **Response `200 OK`:** Examination attempt object (xem [examination-attempts.md](examination-attempts.md)).

## `GET /api/examinations/{examinationId}/attempts?page=1&pageSize=20`

Attempt còn active không lộ `correctAnswers`/`score`. **Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000019",
      "examinationId": "00000000-0000-0000-0000-000000000017",
      "studentId": "00000000-0000-0000-0000-000000000001",
      "studentFullName": "Student",
      "studentEmail": "student@example.com",
      "status": "Submitted",
      "submitReason": "StudentSubmitted",
      "startedAt": "2026-09-10T08:00:00Z",
      "submittedAt": "2026-09-10T08:45:00Z",
      "violationCount": 0,
      "correctAnswers": 8,
      "totalQuestions": 10,
      "score": 8.0
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

## `PUT /api/examinations/{examinationId}`

Chỉ `Draft`. **Request:** giống request tạo nhưng **không** có `selectionMode`, `questionSelectionRules`, `questionIds`; snapshot câu hỏi không đổi. **Response `200 OK`:** Examination object.

## `PUT /api/examinations/{examinationId}/question-set`

Chỉ `Draft`. Thay toàn bộ snapshot + selection rules trong một transaction; dùng `selectionMode`, `questionSelectionRules`, `questionIds` như request tạo. `Random` chọn và lưu bộ mới ngay lúc cập nhật (không chọn lại cho từng attempt).

**Request:**

```json
{
  "selectionMode": "Manual",
  "questionSelectionRules": [],
  "questionIds": [
    "00000000-0000-0000-0000-000000000021"
  ]
}
```

**Response `200 OK`:** Examination object với snapshot hiện hành.

## `POST /api/examinations/{examinationId}/publish`

**Không có body.** Chỉ `Draft`, có ít nhất một câu hỏi và `dueAt` ở tương lai. **Response `200 OK`:** Examination `Published`.

## `POST /api/examinations/{examinationId}/close`

**Không có body.** Chỉ `Published`. Trong cùng transaction, mọi attempt `InProgress`/`Disconnected` được chấm và auto-submit với `submitReason=ExaminationClosed`. **Response `200 OK`:** Examination `Closed`.
