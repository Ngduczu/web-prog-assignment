# API – Users

Base path: `/api/users`. Cần JWT.

| Method | Path | Quyền | Chức năng |
| --- | --- | --- | --- |
| GET | `/api/users/{userId}/profile` | Chính chủ hoặc Admin | Xem hồ sơ |
| GET | `/api/users/search-for-chat?email=` | Đã đăng nhập | Tìm user theo email để tạo chat trực tiếp |
| PUT | `/api/users/me` | Đã đăng nhập | Sửa họ tên, số điện thoại |
| PUT | `/api/users/me/password` | Đã đăng nhập | Đổi/tạo mật khẩu |
| GET | `/api/users` | Admin | Danh sách, tìm kiếm, phân trang |
| POST | `/api/users` | Admin | Tạo user |
| PUT | `/api/users/{userId}` | Admin | Sửa họ tên, phone, role |
| PATCH | `/api/users/{userId}/status` | Admin | Đổi trạng thái tài khoản |

Tất cả chỉ dùng PostgreSQL (`users`, `external_users`); không dùng Redis.

## User profile object

```json
{
  "id": "00000000-0000-0000-0000-000000000002",
  "email": "teacher@example.com",
  "fullName": "Teacher",
  "phone": "0900000000",
  "role": "Teacher",
  "status": "Active",
  "avatarUrl": null,
  "createdAt": "2026-09-04T10:00:00Z",
  "hasPassword": true,
  "isGoogleLinked": null
}
```

`isGoogleLinked` là `true`/`false` chỉ ở `GET /api/users/{userId}/profile`; các response khác trả `null`.

## `GET /api/users/{userId}/profile`

User xem chính mình; Admin xem mọi user. **Response `200 OK`:** User profile object.

## `GET /api/users/search-for-chat?email=student@example.com`

Tìm **chính xác** theo email chuẩn hóa, chỉ trả tài khoản `Active`, không trả chính mình. Chỉ trả dữ liệu tối thiểu.

**Response `200 OK`:**

```json
{
  "id": "00000000-0000-0000-0000-000000000001",
  "email": "student@example.com",
  "fullName": "Student",
  "avatarUrl": null
}
```

## `PUT /api/users/me`

Email, role, status, avatar không đổi được ở đây.

**Request:**

```json
{
  "fullName": "Updated Name",
  "phone": "0900000000"
}
```

**Response `200 OK`:** User profile object.

## `PUT /api/users/me/password`

Tài khoản local phải gửi đúng `currentPassword`. Tài khoản OAuth chưa có mật khẩu gửi `currentPassword: null` để tạo mật khẩu lần đầu.

**Request:**

```json
{
  "currentPassword": "current-password",
  "newPassword": "new-password",
  "passwordConfirm": "new-password"
}
```

Mật khẩu mới dài 8–128 ký tự, khớp xác nhận và khác mật khẩu hiện tại. Đổi mật khẩu và tăng `auth_version` cùng một commit → mọi access/refresh token đã phát bị thu hồi.

**Response `204 No Content`.** Client phải xóa token/cookie và yêu cầu đăng nhập lại.

## `GET /api/users?search=&page=1&pageSize=20`

**Response `200 OK`:**

```json
{
  "items": [
    {
      "id": "00000000-0000-0000-0000-000000000002",
      "email": "teacher@example.com",
      "fullName": "Teacher",
      "phone": null,
      "role": "Teacher",
      "status": "Active",
      "avatarUrl": null,
      "createdAt": "2026-09-04T10:00:00Z",
      "hasPassword": true
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

## `POST /api/users`

Admin tạo user role `Student`/`Teacher`/`Admin`, status `Active`.

**Request:**

```json
{
  "email": "teacher@example.com",
  "fullName": "Teacher",
  "phone": "0900000000",
  "password": "password123",
  "role": "Teacher"
}
```

**Response `201 Created`:** User profile object.

## `PUT /api/users/{userId}`

**Request:**

```json
{
  "fullName": "Updated Name",
  "phone": "0900000000",
  "role": "Teacher"
}
```

**Response `200 OK`:** User profile object.

Quy tắc:

- Admin không sửa được admin khác.
- Admin cuối cùng không tự đổi role khỏi `Admin` (còn admin khác thì được).
- Teacher đang sở hữu course hoặc question bank không thể đổi sang role khác cho tới khi chuyển quyền sở hữu.
- Đổi role tăng `AuthVersion` → thu hồi token cũ.

## `PATCH /api/users/{userId}/status`

**Request:**

```json
{
  "status": "Disabled"
}
```

**Response `200 OK`:** User profile object.

Quy tắc:

- Admin `Active` cuối cùng không tự chuyển sang `Inactive`/`Disabled`/`Banned`; kiểm tra trong transaction serializable.
- Teacher đang sở hữu course/question bank không thể rời `Active`.
- Đổi status tăng `AuthVersion` trong cùng transaction → thu hồi token cũ.
