# API – Question Banks và Questions

Cần JWT. Dữ liệu chỉ qua PostgreSQL (`question_banks`, `questions`, `multiple_choice_options`, `fill_in_blank_answer_keys`).

| Method | Path | Quyền | Chức năng |
| --- | --- | --- | --- |
| POST | `/api/question-banks` | Teacher, Admin | Tạo ngân hàng câu hỏi |
| GET | `/api/question-banks?teacherId=` | Teacher, Admin | Danh sách ngân hàng |
| PUT | `/api/question-banks/{questionBankId}` | Chủ bank, Admin | Sửa tên/mô tả |
| DELETE | `/api/question-banks/{questionBankId}` | Chủ bank, Admin | Xóa bank và câu hỏi |
| POST | `/api/question-banks/{questionBankId}/share` | Chủ bank (Teacher) | Sao chép bank cho teacher khác theo email |
| POST | `/api/question-banks/{questionBankId}/multiple-choice` | Chủ bank, Admin | Thêm câu trắc nghiệm |
| POST | `/api/question-banks/{questionBankId}/fill-in-blank` | Chủ bank, Admin | Thêm câu điền khuyết |
| GET | `/api/question-banks/{questionBankId}/questions` | Chủ bank, Admin | Danh sách câu hỏi |
| PUT | `/api/questions/{questionId}` | Chủ bank, Admin | Sửa nội dung/độ khó |
| DELETE | `/api/questions/{questionId}` | Chủ bank, Admin | Xóa câu hỏi nguồn |

Câu hỏi trong bank là **nguồn**; kỳ thi tạo snapshot nên sửa/xóa câu hỏi nguồn không ảnh hưởng kỳ thi đã tạo.

## Question bank object

```json
{
  "id": "00000000-0000-0000-0000-000000000005",
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "name": "C# Questions",
  "description": "Basics",
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

## `POST /api/question-banks`

**Request:**

```json
{
  "teacherId": "00000000-0000-0000-0000-000000000002",
  "name": "C# Questions",
  "description": "Basics"
}
```

**Response `200 OK`:** Question bank object. Teacher tạo cho chính mình; Admin chỉ định `teacherId`.

## `GET /api/question-banks?teacherId={teacherId}`

Teacher bỏ `teacherId` để lấy bank của mình, không xem được của teacher khác. Admin nên truyền `teacherId` (nếu bỏ, tìm theo ID của admin nên thường rỗng).

**Response `200 OK`:**

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000005",
    "teacherId": "00000000-0000-0000-0000-000000000002",
    "name": "C# Questions",
    "description": "Basics",
    "createdAt": "2026-09-04T10:00:00Z",
    "modifiedAt": "2026-09-04T10:00:00Z"
  }
]
```

## `POST /api/question-banks/{questionBankId}/share`

**Request:** `{ "recipientEmail": "teacher2@example.com" }`

Tạo bản sao bank (kèm toàn bộ câu hỏi, đáp án) vào tài khoản Teacher đang Active có email tương ứng, rồi gửi email thông báo (lỗi gửi mail không làm hỏng việc chia sẻ). Tên giữ nguyên, mô tả ghi nguồn. Lỗi: `400` email sai/tự chia sẻ, `403` không phải chủ bank, `404` bank hoặc người nhận không tồn tại.

**Response `200 OK`:** Question bank object của bản sao (`teacherId` = người nhận).

## `PUT /api/question-banks/{questionBankId}`

**Request:**

```json
{
  "name": "Updated",
  "description": "Updated"
}
```

**Response `200 OK`:** Question bank object.

## `DELETE /api/question-banks/{questionBankId}`

Xóa bank cùng toàn bộ câu hỏi nguồn. `409` nếu bank đang được selection rule của examination tham chiếu. **Response `204 No Content`.**

## Question object – multiple choice

```json
{
  "id": "00000000-0000-0000-0000-000000000006",
  "questionBankId": "00000000-0000-0000-0000-000000000005",
  "content": "2 + 2 = ?",
  "type": "MultipleChoice",
  "difficulty": "Easy",
  "shuffleOptions": true,
  "options": [
    {
      "id": "00000000-0000-0000-0000-000000000007",
      "content": "3",
      "order": 1,
      "isCorrect": false
    },
    {
      "id": "00000000-0000-0000-0000-000000000008",
      "content": "4",
      "order": 2,
      "isCorrect": true
    }
  ],
  "answers": [],
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

## `POST /api/question-banks/{questionBankId}/multiple-choice`

Phải có **chính xác một** option `isCorrect: true`.

**Request:**

```json
{
  "content": "2 + 2 = ?",
  "difficulty": "Easy",
  "shuffleOptions": true,
  "options": [
    { "content": "3", "isCorrect": false },
    { "content": "4", "isCorrect": true }
  ]
}
```

**Response `200 OK`:** Question object dạng multiple choice.

## Question object – fill in blank

```json
{
  "id": "00000000-0000-0000-0000-000000000009",
  "questionBankId": "00000000-0000-0000-0000-000000000005",
  "content": "Capital of France: ___",
  "type": "FillInBlank",
  "difficulty": "Easy",
  "shuffleOptions": false,
  "options": [],
  "answers": [
    {
      "id": "00000000-0000-0000-0000-000000000010",
      "blankOrder": 1,
      "expectedAnswer": "Paris"
    }
  ],
  "createdAt": "2026-09-04T10:00:00Z",
  "modifiedAt": "2026-09-04T10:00:00Z"
}
```

## `POST /api/question-banks/{questionBankId}/fill-in-blank`

**Request:**

```json
{
  "content": "Capital of France: ___",
  "difficulty": "Easy",
  "answers": [
    { "blankOrder": 1, "expectedAnswer": "Paris" }
  ]
}
```

**Response `200 OK`:** Question object dạng fill in blank.

## `GET /api/question-banks/{questionBankId}/questions`

**Response `200 OK`:** Mảng question object (một trong hai dạng trên).

## `PUT /api/questions/{questionId}`

Chỉ sửa `content` và `difficulty`; loại câu hỏi, options và đáp án không đổi.

**Request:**

```json
{
  "content": "Updated question",
  "difficulty": "Medium"
}
```

**Response `200 OK`:** Question object.

## `DELETE /api/questions/{questionId}`

Xóa câu hỏi nguồn; snapshot trong examination giữ nguyên. **Response `204 No Content`.**
