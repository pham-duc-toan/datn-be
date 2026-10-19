# Tài liệu tích hợp Frontend

Tài liệu cho người viết frontend (web) gọi vào backend của hệ thống gán nhãn cộng đồng. Mọi request đi qua **API gateway**; frontend không gọi thẳng từng service.

- Chạy backend và dữ liệu mẫu: [huong-dan-chay-va-test.md](huong-dan-chay-va-test.md).
- Kiến trúc và lý do thiết kế: [kien-truc-backend.md](kien-truc-backend.md).
- JSON Schema của tập nhãn và từng loại nhãn (dùng được trực tiếp ở frontend): [contracts/labeling](../contracts/labeling).

Mục lục:

1. [Quy ước chung](#1-quy-ước-chung)
2. [Xác thực](#2-xác-thực)
3. [Bản đồ màn hình → API theo vai trò](#3-bản-đồ-màn-hình--api-theo-vai-trò)
4. [Doanh nghiệp: tạo và vận hành dự án](#4-doanh-nghiệp-tạo-và-vận-hành-dự-án)
5. [Labeler: nhận việc, gán nhãn, nhận tiền](#5-labeler-nhận-việc-gán-nhãn-nhận-tiền)
6. [Workspace gán nhãn: hợp đồng chi tiết](#6-workspace-gán-nhãn-hợp-đồng-chi-tiết) — ví dụ trọn vẹn từng bài toán ở [6.6](#66-ví-dụ-trọn-vẹn-theo-từng-bài-toán)
7. [Admin](#7-admin)
8. [Dữ liệu cập nhật trễ: khi nào phải hỏi lại](#8-dữ-liệu-cập-nhật-trễ-khi-nào-phải-hỏi-lại)
9. [Mã lỗi](#9-mã-lỗi)
10. [Giới hạn](#10-giới-hạn)
11. [Backend chưa có / cần lưu ý](#11-backend-chưa-có--cần-lưu-ý)
12. [Cổng link: người chia sẻ và trang vượt link](#12-cổng-link-người-chia-sẻ-và-trang-vượt-link)
13. [Xác minh doanh nghiệp (KYB)](#13-xác-minh-doanh-nghiệp-kyb)
14. [Thông báo trong ứng dụng và email](#14-thông-báo-trong-ứng-dụng-và-email)
15. [Hồ sơ, kỹ năng, cấp độ, bảng xếp hạng](#15-hồ-sơ-kỹ-năng-cấp-độ-bảng-xếp-hạng)
16. [Bảo vệ dữ liệu: link xem file, watermark](#16-bảo-vệ-dữ-liệu-link-xem-file-watermark)
17. [Bổ sung đợt hoàn thiện 10/2026](#17-bổ-sung-đợt-hoàn-thiện-102026) — 2FA, xác minh danh tính, VNPay / MoMo, hoá đơn, bảo vệ dự án, lời mời, công cụ / định dạng xuất mới, chỉ số chất lượng, watermark âm thanh / video, thông báo realtime + ngôn ngữ, hàng đợi duyệt chung, mẫu bài toán

---

## 1. Quy ước chung

| Mục | Quy ước |
|---|---|
| Base URL | Gateway: `http://localhost:8080` (dev) hoặc `http://localhost:18080` (stack Docker `docker-compose.demo.yml`). Mọi đường dẫn trong tài liệu tính từ đây |
| CORS | Gateway cho phép origin trong `Cors:AllowedOrigins` — mặc định `http://localhost:3000` và `http://localhost:5173`. Origin khác thì thêm vào cấu hình (biến môi trường `Cors__AllowedOrigins__2=...`) |
| Định dạng | JSON, UTF-8. Tên trường **camelCase** |
| Enum | Chuỗi camelCase: `"running"`, `"pendingReview"`, `"qualityCheck"`… Gửi lên cũng dùng dạng này |
| ID | UUID dạng chuỗi |
| Thời gian | ISO 8601 có múi giờ, server trả UTC: `"2026-10-06T08:59:13.364+00:00"` |
| Tiền | **Số nguyên đồng (VND)**, tên trường kết thúc bằng `Vnd`. Không có số lẻ |
| Phân trang | Query `page` (từ 1) và `pageSize` (1–100, mặc định 20). Response: `{ "items": [...], "page", "pageSize", "total" }` |
| Thứ tự | Mọi danh sách mặc định **mới nhất trước** (theo thời điểm tạo / nộp / mở đơn — kể cả hàng đợi duyệt của admin). Ngoại lệ có chủ đích: danh mục sắp theo tên / khoá (setting, mẫu bài toán, chiến dịch, tên miền chặn), bảng xếp hạng (theo điểm), dòng thời gian **bên trong** một đối tượng (lịch sử nhãn, sự kiện của một đơn duyệt) đọc từ trên xuống theo thời gian, chuỗi `daily[]` của thống kê admin (để vẽ biểu đồ). Frontend giữ `page` và bộ lọc trên URL (`?page=2&status=open`) qua `lib/use-url-state.ts` |
| Xác thực | Header `Authorization: Bearer <accessToken>` cho mọi API trừ `/auth/*` |
| Idempotency | Hai API tiền bắt buộc header `Idempotency-Key` (mục 4.1, 5.7) |
| File mẫu (ảnh / âm thanh / video) | `fileUrl` là link qua gateway dạng `{gateway}/media/v/{token}`, **gắn thẳng** vào `<img>` / `<audio>` / `<video>` (không cần header, token chính là quyền xem). Sống 5 phút; hết hạn → 410 `link_het_han`, gọi lại API đã trả link để lấy link mới. Chi tiết mục 16 |
| Mã thiết bị | Workspace gán nhãn nên gửi header `X-Device-Id: <uuid>` khi nộp nhãn — sinh một lần, lưu `localStorage`. Dùng cho chống gian lận (nhiều tài khoản một máy); không gửi vẫn nộp được |
| Giới hạn tần suất | Gateway đếm theo phút: mọi API 600 lần / người dùng (chưa đăng nhập: theo IP); đăng nhập / đăng ký / quên – đặt lại mật khẩu / xác thực email 20 lần / IP; tạo lệnh nạp / rút 30 lần. Vượt → **429** (mục bảng mã HTTP) |

**Lỗi** trả dạng ProblemDetails, luôn có trường `code` ổn định để frontend dịch sang câu thông báo:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "nhan_khong_hop_le",
  "status": 400,
  "detail": "Nhan khong hop le: cong cu 'vat' muc 0: khung vuot ra ngoai anh (300x200).",
  "code": "nhan_khong_hop_le",
  "traceId": "00-73831b8dbacbe82cd07645929867fd3d-..."
}
```

- Hiển thị cho người dùng: dịch theo `code`. `detail` là tiếng Việt không dấu, dành cho dev và log. Riêng lỗi nhãn (`nhan_sai_dinh_dang`, `nhan_khong_hop_le`) thì `detail` chỉ đúng công cụ, đúng mục bị sai, có thể hiện gần nguyên văn.
- Gửi `traceId` khi báo lỗi cho backend.
- Lỗi kiểm dữ liệu đăng ký (`/auth/register`) dùng dạng `ValidationProblemDetails`: `{ "errors": { "password": ["..."] } }`.

**Ý nghĩa mã HTTP:**

| Mã | Khi nào | Frontend nên |
|---|---|---|
| 200 / 201 / 202 / 204 | Thành công. 202 = đã nhận, đang xử lý nền. 204 = không có nội dung (ví dụ hết task) | |
| 400 | Dữ liệu gửi lên sai | Hiện lỗi tại form, không thử lại |
| 401 | Thiếu / hết hạn token | Làm mới token một lần (mục 2.3) rồi gọi lại |
| 403 | **Sai vai trò** (ví dụ labeler gọi API của admin) hoặc chưa đủ điều kiện (mục 5.2) | Hiện lý do theo `code` |
| 404 | Không tồn tại **hoặc không có quyền với tài nguyên đó** | Hiện "không tìm thấy". Backend cố ý không phân biệt hai trường hợp để không lộ tài nguyên của người khác |
| 409 | Trạng thái không cho phép (ví dụ publish dự án đang chạy, duyệt nhãn đã duyệt) hoặc hai người sửa cùng lúc (`xung_dot_dong_thoi`) | Tải lại dữ liệu rồi cho người dùng thao tác lại |
| 401 `phien_da_bi_thu_hoi` | Phiên đã bị thu hồi: đổi / đặt lại mật khẩu, "đăng xuất mọi nơi", hoặc bị khoá (mục 2.6) | **Không** refresh (refresh token cũng đã bị thu hồi) — xoá token, về trang đăng nhập |
| 429 `qua_nhieu_yeu_cau` | Vượt giới hạn tần suất | Đọc header `Retry-After` (giây), khoá nút và thử lại sau từng ấy giây. Không thử lại liên tục |

---

## 2. Xác thực

### 2.1 Đăng ký, đăng nhập

```http
POST /auth/register
{ "email": "a@b.com", "password": "toi-thieu-8-ky-tu", "displayName": "Nguyen Van A", "roles": ["labeler"] }
→ 201 { "userId": "..." }
→ 409 email_da_ton_tai
→ 400 ValidationProblemDetails (code du_lieu_khong_hop_le)
```

Đăng ký xong hệ thống gửi **email xác thực** (mục 2.4). Chưa xác thực vẫn đăng nhập và dùng bình thường, chỉ **chưa rút tiền và chưa publish dự án được**.

- `roles`: một hoặc nhiều trong `business`, `labeler`. Không tự đăng ký được `admin`. **Chia sẻ link kiếm tiền không phải vai trò riêng** mà là chức năng của labeler (mục 12), nên `sharer` không còn chọn được (→ 400). Tài khoản cũ đã đăng ký với `sharer` vẫn đăng nhập bình thường; frontend bỏ `sharer` khỏi danh sách vai trò để chuyển qua lại.
- Mật khẩu 8–128 ký tự. Tên hiển thị tối đa 100 ký tự.

```http
POST /auth/login
{ "email": "a@b.com", "password": "..." }
→ 200 { "accessToken": "...", "refreshToken": "...", "tokenType": "Bearer", "expiresIn": 900 }
→ 401 sai_thong_tin (sai email hoặc mật khẩu — thông báo chung, không nói sai cái nào)
→ 403 tai_khoan_bi_khoa (admin khoá, mục 7.4) — hiện "Tài khoản đã bị khoá, liên hệ quản trị viên"
```

### 2.2 Người dùng hiện tại

```http
GET /me
→ { "userId": "...", "email": "...", "roles": ["business"], "name": "..." }
```

Dùng `roles` để chọn giao diện. Một tài khoản có thể có nhiều vai trò (vừa `business` vừa `labeler`). "Reviewer" **không** phải vai trò hệ thống mà là vai trò *trong một dự án* (mục 4.9).

### 2.3 Làm mới và đăng xuất

| Token | Sống | Ghi chú |
|---|---|---|
| `accessToken` | 15 phút (`expiresIn` giây) | JWT, gửi trong header |
| `refreshToken` | 14 ngày **kể từ lần làm mới gần nhất** | **Dùng một lần**: mỗi lần làm mới trả cặp token mới, token cũ hết hiệu lực. Chuỗi mờ dạng `<mã phiên>.<bí mật>` — frontend không cần đọc bên trong |

```http
POST /auth/refresh   { "refreshToken": "..." }   → 200 cặp token mới | 401 phien_khong_hop_le (kể cả khi tài khoản vừa bị khoá)
POST /auth/logout    { "refreshToken": "..." }   → 204 (luôn 204)
```

Cách làm khuyến nghị:

- Gặp 401 thì gọi `/auth/refresh` **một lần duy nhất** (khóa lại để nhiều request song song không cùng làm mới), lưu cặp mới, rồi gọi lại request cũ. Refresh cũng 401 thì đưa về trang đăng nhập.
- **Không dùng lại refresh token cũ.** Mỗi lần đăng nhập mở một **phiên** (một thiết bị / trình duyệt); backend chỉ nhớ token hiện tại của phiên. Dùng lại một token đã xoay bị coi là dấu hiệu bị đánh cắp và **thu hồi cả phiên đó**: mọi tab dùng chung phiên phải đăng nhập lại (thiết bị khác — phiên khác — không ảnh hưởng). Hai tab cùng làm mới bằng một token cũng gây ra chuyện này, nên đồng bộ token giữa các tab (ví dụ BroadcastChannel).
- **Sau khi triển khai bản backend này, mọi người dùng phải đăng nhập lại một lần** (refresh token cấp trước đó trả 401).
- 401 có `code: "phien_da_bi_thu_hoi"` thì **bỏ qua bước refresh**, đưa thẳng về trang đăng nhập (mục 2.6).

**Claim trong access token.** Ngoài `sub`, `role`, token mang hai cờ mà backend dùng để chặn rút tiền / publish:

| Claim | Ý nghĩa | Đổi khi nào |
|---|---|---|
| `email_verified` | Đã xác thực email | Sau `POST /auth/verify-email` |
| `kyb_verified` | Doanh nghiệp đã được admin duyệt KYB | Sau khi admin duyệt (mục 13) |

Hai cờ được ghi **lúc cấp token**, nên token đang cầm vẫn mang giá trị cũ. **Ngay sau khi xác thực email hoặc nhận thông báo "đã xác minh doanh nghiệp", gọi `POST /auth/refresh` một lần** rồi mới cho rút tiền / publish. Không làm thì người dùng vẫn bị 409 `chua_xac_thuc_email` / `chua_xac_minh_doanh_nghiep` cho tới khi token tự hết hạn (tối đa 15 phút). Để hiển thị giao diện vẫn dùng `GET /me/profile` (`emailVerified`) và `GET /kyb/me` (`status`) — không cần giải mã token.

### 2.4 Xác thực email

Email chứa link `{frontend}/xac-thuc-email?token=...` (sống 1 ngày). Trang này đọc `token` trên URL rồi gọi:

```http
POST /auth/verify-email   { "token": "..." }      (không cần đăng nhập)
→ 200 { "emailVerified": true }
→ 400 ma_khong_hop_le   (sai / đã dùng / hết hạn — cho nút "Gửi lại email")

POST /me/verify-email/resend                     (đã đăng nhập)
→ 202 | 409 da_xac_thuc | 409 gui_qua_nhanh (cách lần trước < 60 giây)
```

- `GET /me/profile` có `emailVerified` — hiện thanh nhắc "Xác thực email để rút tiền / publish dự án" khi `false`.
- Sau khi xác thực, **gọi `POST /auth/refresh`** để lấy access token mới mang `email_verified = true` (mục 2.3); token cũ vẫn bị rút tiền / publish trả 409 `chua_xac_thuc_email`. Nếu người dùng bấm link xác thực trên thiết bị khác, thiết bị đang mở chỉ cần làm mới token khi gặp 409 này.
- Domain trang frontend lấy từ setting `identity.frontend_url` (mặc định `http://localhost:5173`).

### 2.5 Quên, đặt lại, đổi mật khẩu

```http
POST /auth/forgot-password   { "email": "a@b.com" }                      → 202 (LUÔN 202, kể cả email không tồn tại)
POST /auth/reset-password    { "token": "...", "newPassword": "..." }    → 204 | 400 ma_khong_hop_le | 400 mat_khau_khong_hop_le
POST /me/password            { "currentPassword": "...", "newPassword": "..." }
→ 200 cặp token mới (giống đăng nhập) | 400 mat_khau_hien_tai_sai | 400 mat_khau_khong_hop_le
```

- Link trong email: `{frontend}/dat-lai-mat-khau?token=...`, sống 30 phút, dùng một lần. Đặt lại xong → về trang đăng nhập. Đặt lại qua email cũng coi như đã xác thực email.
- Quên mật khẩu luôn 202: hiện "Nếu email có tài khoản, bạn sẽ nhận được link" — không báo email có tồn tại hay không.
- Đặt lại / đổi mật khẩu **đăng xuất mọi thiết bị khác** (mục 2.6). Đổi mật khẩu trả cặp token mới cho chính thiết bị đang dùng: **thay token ngay**, token cũ bị chặn.

### 2.6 Đăng xuất mọi nơi và thu hồi phiên

```http
POST /me/logout-all   → 204
```

Sau các sự kiện dưới đây, **mọi access token cấp trước đó** bị gateway trả 401 `phien_da_bi_thu_hoi` (chậm nhất vài trăm ms), và mọi refresh token bị thu hồi:

| Sự kiện | Thiết bị đang dùng |
|---|---|
| `POST /me/logout-all` | Cũng bị đăng xuất |
| `POST /me/password` | Dùng cặp token mới trong response |
| `POST /auth/reset-password` | Đăng nhập lại |
| Admin khoá tài khoản (mục 7.4) | Đăng nhập trả 403 `tai_khoan_bi_khoa` |

Lưu ý: mốc thu hồi tính theo **giây** — token cấp trong cùng giây với mốc cũng bị chặn; gặp trường hợp này thì đăng nhập lại là xong.

---

## 3. Bản đồ màn hình → API theo vai trò

| Vai trò | Màn hình | API chính |
|---|---|---|
| Mọi vai trò | Xác thực email, quên / đổi mật khẩu, đăng xuất mọi nơi | `POST /auth/verify-email`, `POST /me/verify-email/resend`, `POST /auth/forgot-password`, `POST /auth/reset-password`, `POST /me/password`, `POST /me/logout-all` (mục 2.4–2.6) |
| | Hồ sơ, ảnh đại diện, kỹ năng | `GET/PUT /me/profile`, `POST /me/avatar/upload-url`, `PUT /me/avatar`, `PUT /me/skills`, `GET /users/public?ids=` (mục 15) |
| | Chuông thông báo, tuỳ chọn email | `GET /notifications/me/unread-count`, `GET /notifications/me`, `POST /notifications/me/{id}/read`, `POST /notifications/me/read-all`, `GET/PUT /notifications/me/preferences` (mục 14) |
| Doanh nghiệp | Hồ sơ doanh nghiệp (KYB) — **phải được duyệt mới publish được** | `GET/PUT /kyb/me`, `POST /kyb/me/license`, `POST /kyb/me/submit` (mục 13) |
| | Ví, nạp tiền | `GET /ledger/me/balance`, `POST /payments/deposits`, `GET /payments/deposits/{id}` |
| | Danh sách dự án của tôi | `GET /projects?mine=true` |
| | Wizard tạo dự án | `POST /projects`, `PUT /projects/{id}/label-schema`, `/guideline`, `/pricing`, `/channels`, `/eligibility` |
| | Nạp dữ liệu | `POST /projects/{id}/datasets` (ZIP), `POST /projects/{id}/uploads` + `POST /projects/{id}/datasets/manifest`, `GET /projects/{id}/datasets`, `GET /projects/{id}/samples` |
| | Câu hỏi vàng | `GET/POST /projects/{id}/gold-items`, `DELETE /projects/{id}/gold-items/{goldItemId}` |
| | Checklist + publish | `GET /projects/{id}/readiness`, `POST /projects/{id}/publish` |
| | Kiểm soát chất lượng | `PUT /projects/{id}/quality-control`, `GET /quality/projects/{id}/summary`, `GET /quality/projects/{id}/labelers` |
| | Theo dõi dự án, chi phí, ETA | `GET /projects/{id}`, `GET /tasks/projects/{id}/progress`, `GET /ledger/projects/{id}/cost`, `POST /projects/{id}/pause` / `resume` / `complete` / `cancel` |
| | Task bị labeler báo lỗi | `GET /tasks/projects/{id}/reports`, `POST /tasks/reports/{id}/resolve` (mục 4.13) |
| | Tranh chấp nhãn đã duyệt | `POST /ledger/disputes`, `GET /ledger/disputes/mine` (mục 4.14) |
| | Thành viên, mời, mã mời, đơn xin tham gia | `GET/POST /projects/{id}/members`, `POST /projects/{id}/members/batch`, `.../block`, `.../unblock`, `DELETE .../members/{userId}`, `GET/POST /projects/{id}/invite-codes`, `DELETE .../invite-codes/{codeId}`, `GET /projects/{id}/join-requests`, `POST .../join-requests/{requestId}/approve` / `reject` (mục 4.9) |
| | Duyệt nhãn | `GET /annotations/projects/{id}?status=pendingReview`, `POST /annotations/{id}/approve` / `reject`, `POST /annotations/projects/{id}/approve-agreed`, `GET /annotations/{id}/history` |
| | Kết quả, phân bố nhãn, tải về | `GET /annotations/projects/{id}/results`, `GET /annotations/projects/{id}/distribution`, `GET /annotations/projects/{id}/export?format=json\|csv\|coco\|yolo\|voc` |
| Labeler | Chợ dự án | `GET /projects`, `GET /projects/{id}` |
| | Tham gia, mã mời, đơn xin, bài test | `POST /projects/{id}/join`, `POST /projects/join-by-code`, `POST /projects/{id}/join-requests`, `DELETE /projects/{id}/join-requests/mine`, `GET /projects/join-requests/mine`, `POST /projects/{id}/entrance-test/attempts`, `.../attempts/{attemptId}/submit`, `GET .../attempts` (mục 5.1) |
| | Workspace gán nhãn | `POST /tasks/projects/{id}/next`, `POST /tasks/assignments/{id}/submit`, `PUT /tasks/assignments/{id}/draft`, `POST /tasks/assignments/{id}/report`, `POST /tasks/assignments/{id}/release`, `GET /tasks/assignments/mine` |
| | Lịch sử, khiếu nại, uy tín | `GET /annotations/mine`, `POST /annotations/{id}/appeal`, `GET /quality/me` |
| | Ví, rút tiền, thống kê thu nhập | `GET /ledger/me/balance`, `GET /ledger/me/transactions`, `GET /ledger/me/earnings`, `POST /ledger/withdrawals`, `GET /ledger/withdrawals/mine` |
| | Cấp độ, huy hiệu, bảng xếp hạng | `GET /me/level`, `GET /users/leaderboard` (mục 15) |
| Reviewer (trong dự án) | Duyệt nhãn của dự án được giao | Giống phần duyệt nhãn của doanh nghiệp |
| Labeler (chia sẻ link) | Quản lý link, chiến dịch | `POST /links`, `POST /links/bulk`, `GET /links`, `GET/PUT/DELETE /links/{id}`, `GET/POST /links/campaigns` |
| | API token, API rút gọn, Quick Link | `POST/GET /links/api-key`, `GET /links/api-shorten?api=&url=`, `GET /links/quick?api=&url=` |
| | Thống kê, giới thiệu, ví | `GET /gate/stats/me/daily` / `top-links` / `sources` / `outcomes`, `GET /links/referrals/me`, `POST /links/referrals/claim`, ví như labeler (mục 5.7) |
| Khách vãng lai | Trang vượt link (không đăng nhập) | `GET /g/{code}`, `POST /g/{code}/sessions`, `POST /g/sessions/{id}/submit`, `GET /go/{code}?t=`, `POST /links/r/{code}/report` |
| Admin | Duyệt hồ sơ doanh nghiệp | `GET /kyb/admin`, `GET /kyb/admin/{businessId}`, `POST /kyb/admin/{businessId}/approve` / `reject` (mục 13.2) |
| | Quản lý tài khoản, khoá / mở khoá | `GET /users/admin`, `POST /users/admin/{userId}/block` / `unblock` (mục 7.4) |
| | Duyệt dự án | `GET /projects/pending-approval`, `POST /projects/{id}/approve` / `reject` |
| | Khiếu nại | `GET /annotations/appeals`, `POST /annotations/{id}/appeal/resolve` |
| | Đối soát sổ cái | `GET /ledger/admin/reconciliation` |
| | Chạy Dawid–Skene ngay | `POST /quality/admin/dawid-skene/run` |
| | Setting hệ thống (phí, tự duyệt, hạn mức…) | `GET /admin/settings`, `PUT /admin/settings/{key}`, `GET /admin/settings/{key}/history` |
| | Duyệt rút tiền | `GET /ledger/admin/withdrawals`, `POST /ledger/admin/withdrawals/{id}/approve` / `reject` |
| | Đối chiếu nạp chuyển khoản | `GET /payments/admin/deposits`, `POST /payments/admin/deposits/{id}/approve` / `reject` |
| | Kiểm duyệt link | `GET /links/admin/review-queue`, `POST /links/admin/{id}/disable` / `dismiss-reports`, `GET/POST/DELETE /links/admin/blocked-domains` |
| | Phân xử tranh chấp | `GET /ledger/admin/disputes`, `POST /ledger/admin/disputes/{id}/resolve` (mục 7.5) |
| | Hồ sơ nghi gian lận | `GET /quality/admin/fraud-cases`, `GET /quality/admin/fraud-cases/{id}`, `POST /quality/admin/fraud-cases/{id}/resolve` (mục 7.6) |
| | Nhật ký hệ thống, báo cáo tổng quan | `GET /admin/audit`, `GET /admin/stats/overview` (mục 7.7) |
| | Thông báo toàn hệ thống | `POST /admin/announcements`, `GET /admin/announcements` (mục 7.8) |
| | Truy người làm lộ ảnh | `POST /media/admin/watermark/decode` (mục 16.2) |

---

## 4. Doanh nghiệp: tạo và vận hành dự án

### 4.1 Nạp tiền

Dự án cần **ký quỹ** trước khi chạy: tiền được khóa từ số dư khả dụng của doanh nghiệp (giải thích chi tiết ở mục "Tiền" của hướng dẫn chạy).

```http
POST /payments/deposits
Idempotency-Key: <uuid sinh ở client, giữ nguyên khi bấm lại>
{ "amountVnd": 1000000 }
→ 200 { "intentId": "...", "businessId": "...", "amountVnd": 1000000, "provider": "sandbox", "status": "pending",
        "checkoutUrl": "http://...", "transferCode": null, "bankInfo": null, "transferredAt": null,
        "rejectReason": null, "createdAt": "...", "completedAt": null }
```

1. Sinh `Idempotency-Key` **một lần cho mỗi lần người dùng định nạp**. Bấm lại, mạng chập chờn gửi lại thì vẫn dùng key cũ: backend trả lại đúng lệnh cũ chứ không tạo lệnh thứ hai.
2. Chuyển người dùng tới `checkoutUrl` (trang của cổng thanh toán; dev là cổng giả lập).
3. Sau khi quay về: gọi `GET /payments/deposits/{intentId}` mỗi 2–3 giây đến khi `status` là `succeeded` hoặc `failed`. Rồi gọi `GET /ledger/me/balance` (tiền vào ví sau thêm khoảng 1–2 giây, xem mục 8).

```http
GET /ledger/me/balance
→ { "businessAvailableVnd": 950000, "escrowVnd": 931700, "pendingVnd": 0, "availableVnd": 0 }
```

`businessAvailableVnd` / `escrowVnd` là phần doanh nghiệp; `pendingVnd` / `availableVnd` là phần labeler (một tài khoản có thể có cả hai).

Số tiền nạp phải nằm trong hạn mức admin đặt (mặc định 10.000 – 500.000.000đ); ngoài hạn mức → **400** `so_tien_nap_khong_hop_le`, thông báo có sẵn hạn mức hiện tại.

**Nạp bằng chuyển khoản ngân hàng thủ công** (khi admin bật — mặc định bật):

```http
POST /payments/deposits/manual
Idempotency-Key: <uuid>
{ "amountVnd": 500000 }
→ 200 { "intentId": "...", "provider": "manual_transfer", "status": "pending", "checkoutUrl": null,
        "transferCode": "CROWD3F9A1B2C4D5E", "bankInfo": "Ngan hang ... STK ... Chu TK ...", ... }

POST /payments/deposits/{intentId}/transferred        ← người dùng bấm "Tôi đã chuyển khoản"
→ 200 { ..., "status": "awaitingApproval", "transferredAt": "..." }     (hoặc "succeeded" nếu dưới ngưỡng tự duyệt)
```

1. Hiện `bankInfo` và **`transferCode` (nội dung chuyển khoản bắt buộc)** kèm nút sao chép; nhắc người dùng ghi đúng nội dung để admin tìm được giao dịch.
2. Sau khi chuyển, gọi `/transferred`. Bấm lại không sao (trả về trạng thái hiện tại).
3. `awaitingApproval`: admin đang đối chiếu sao kê, có thể mất vài giờ. Hỏi lại `GET /payments/deposits/{intentId}` khi người dùng mở lại trang (không cần polling dày). Kết cục: `succeeded` (tiền vào `businessAvailableVnd` sau vài giây) hoặc `rejected` (hiện `rejectReason`).
4. Admin tắt kênh này → `POST /manual` trả **409** `chuyen_khoan_tat`: ẩn lựa chọn chuyển khoản.

| `status` | Ý nghĩa |
|---|---|
| `pending` | Cổng: chờ thanh toán. Chuyển khoản: chờ người dùng chuyển và bấm "đã chuyển" |
| `awaitingApproval` | Chuyển khoản: đã báo chuyển, chờ admin đối chiếu |
| `succeeded` | Tiền đã vào ví |
| `failed` | Cổng báo thất bại |
| `rejected` | Chuyển khoản: admin không thấy tiền về / sai nội dung (`rejectReason`) |

### 4.2 Vòng đời dự án

```
draft ──publish──► pendingEscrow ──ledger giữ tiền──► pendingApproval ──admin duyệt──► running ◄──► paused
  ▲                     │ thiếu tiền                         │ admin từ chối / quá 72 giờ      │
  └─────────────────────┘ (statusReason ghi lý do)           ▼                                 ▼
                                                        cancelled                    completed / cancelled
```

- Chỉ `draft` mới sửa được cấu hình (tập nhãn, giá, dữ liệu…). Câu vàng sửa được khi `draft` hoặc `paused`.
- Admin bật `project.auto_approve` (mục 7.1) thì dự án đi thẳng `pendingEscrow → running`, không dừng ở `pendingApproval`. Frontend cứ hỏi lại `GET /projects/{id}` đến khi khác `pendingEscrow`, rồi hiện đúng trạng thái nhận được. "Quá 72 giờ" cũng là setting (`project.approval_timeout`).
- `pendingEscrow` thường chỉ kéo dài vài giây. Thiếu tiền thì dự án **quay về `draft`** và `statusReason` ghi rõ, ví dụ `"So du kha dung 0d, can ky quy 100000d. Hay nap them tien."`.
- Kết thúc (`completed` / `cancelled`): phần ký quỹ chưa dùng tự trả về ví doanh nghiệp.

### 4.3 Tạo dự án

```http
POST /projects
{ "name": "Phan loai xe", "description": "...", "modality": "image", "visibility": "public" }
→ 201 ProjectResponse
```

- `modality`: `image` | `text` | `audio` | `video` | `pair` | `list`. **Không đổi được sau khi tạo**: nó quyết định loại file được nạp và công cụ được dùng.
- `visibility` (chỉ đổi khi còn Nháp):
  - `public`: labeler thấy dự án và tự tham gia (`join`, hoặc làm đạt bài test).
  - `approval`: labeler thấy dự án nhưng phải gửi **đơn**, chủ dự án duyệt. Có bài test thì làm đạt mới thành đơn, kèm điểm.
  - `private`: labeler không thấy; chỉ vào khi được chủ dự án mời hoặc có mã mời.
  Cả ba chế độ đều mời trực tiếp và dùng mã mời được (mục 4.9).

`ProjectResponse` (cũng là response của `GET /projects/{id}` và của mọi lệnh `PUT` cấu hình):

```json
{
  "id": "...", "name": "...", "description": "...", "modality": "image",
  "status": "draft", "visibility": "public",
  "labelSchema": null, "guideline": null,
  "unitPriceVnd": 0, "redundancy": 0, "maxRedundancy": 0, "goldCheckPercent": 10, "deadline": null,
  "allowProfessional": true, "allowLinkGateway": false, "allowCollaborative": false,
  "minLevel": null, "minReputation": null,
  "requireEntranceTest": false, "entranceQuestionCount": 10, "entrancePassPercent": 80,
  "publishedAt": null, "isOwner": true,
  "ownerId": "...", "budgetVnd": 0, "statusReason": null, "createdAt": "...", "updatedAt": "..."
}
```

Các trường `ownerId`, `budgetVnd`, `statusReason`, `createdAt`, `updatedAt` là `null` khi người xem không phải chủ dự án.

Danh sách:

```http
GET /projects?mine=true&status=running&modality=audio&page=1&pageSize=20
→ { "items": [ { "id", "name", "modality", "status", "visibility", "unitPriceVnd", "deadline", "requireEntranceTest", "isOwner" } ], "page", "pageSize", "total" }
```

Không có `mine`: mọi dự án người gọi xem được, gồm dự án công khai đang chạy **và** các dự án mình là thành viên (chủ, labeler, reviewer). `mine=true`: chỉ các dự án mình là thành viên. Màn hình chợ của labeler nên lọc thêm `status=running`.

### 4.4 Tập nhãn (label schema)

Tập nhãn = loại dữ liệu + danh sách **công cụ**. Mỗi công cụ là một "câu hỏi" trên màn hình gán nhãn. Một dự án có thể có nhiều công cụ (tối đa 20).

```http
PUT /projects/{id}/label-schema
{
  "modality": "image",
  "tools": [
    { "name": "loai", "kind": "classification", "classes": ["ngoai_troi", "trong_nha"] },
    { "name": "vat",  "kind": "bbox", "classes": ["xe", "nguoi"], "required": false },
    { "name": "vung", "kind": "polygon", "classes": ["duong"], "required": false }
  ]
}
→ 200 ProjectResponse (labelSchema đã được điền đủ giá trị mặc định)
```

| Trường | Ý nghĩa |
|---|---|
| `modality` | Phải trùng loại dữ liệu của dự án, nếu không trả 400 `loai_du_lieu_khong_khop` |
| `segmentSeconds` | Chỉ audio/video, 5–3600. File dài hơn sẽ bị **cắt thành nhiều mẫu**, mỗi đoạn N giây là một task |
| `tools[].name` | Khóa của công cụ trong nhãn nộp lên. `^[a-z][a-z0-9_]{0,39}$`, không trùng trong một tập nhãn |
| `tools[].kind` | Loại công cụ, xem bảng dưới |
| `classes` | Danh sách lớp (1–50 ký tự mỗi lớp, tối đa 100 lớp, không trùng). Bắt buộc với công cụ **chọn lớp** (`classification` và mọi công cụ dạng danh sách); **không được khai** với `transcription`, `freeText`, `number`, `pairwise`, `ranking`. Phân loại cần ≥ 2 lớp |
| `allowMultiple` | Chỉ `classification`: cho chọn nhiều lớp (mặc định `false`) |
| `required` | Mặc định `true`. `false` = labeler được bỏ trống công cụ này |
| `minItems`, `maxItems` | Công cụ dạng danh sách (`bbox`, `polygon`, `keypoint`, `polyline`, `mask`, `span`, `relation`, `temporalSegment`, `track`): số mục tối thiểu / tối đa (mặc định 0 / 1000) |
| `maxLength` | Chỉ `transcription`, `freeText`: độ dài tối đa (mặc định 5000) |
| `allowTie` | Chỉ `pairwise`: cho chọn "ngang nhau". **Mặc định `true`**; khai `false` để bắt buộc chọn một bên |
| `matchThreshold` | Ngưỡng 0–1 để chấm câu vàng **và** ghép đối tượng khi gộp nhãn nhiều người (mục 6.5, 4.11). Bỏ trống = mặc định của loại công cụ. Không dùng cho `classification`, `pairwise`, `freeText`, `number` |
| `attributes` | Công cụ dạng danh sách (trừ `relation`): **thuộc tính của từng đối tượng**, tối đa 20. Mỗi thuộc tính `{ "name", "type": "choice" \| "boolean" \| "text" \| "number", "required"?: false, "options"?: [...] (bắt buộc với `choice`, 2–50 lựa chọn), "maxLength"?: 500 (chỉ `text`) }`. Xem mục 6.3 |
| `spanTool` | Chỉ `relation`, bắt buộc: `name` của công cụ `span` trong cùng tập nhãn mà hai đầu quan hệ trỏ tới |
| `min`, `max`, `integer`, `tolerance` | Chỉ `number`: giới hạn giá trị (bỏ trống = không giới hạn), chỉ nhận số nguyên (`integer`, mặc định `false`), và mức lệch vẫn tính là khớp khi chấm / gộp (`tolerance`, mặc định 0) |

Công cụ nào dùng được với loại dữ liệu nào:

| kind | image | text | audio | video | pair | list | Dùng cho |
|---|---|---|---|---|---|---|---|
| `classification` | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | Chọn một / nhiều lớp cho cả mẫu |
| `freeText` | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | Câu trả lời **mở**: mô tả ảnh, dịch, tóm tắt, viết lại |
| `number` | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | Thang điểm, số đếm |
| `bbox` | ✔ | | | | | | Khung chữ nhật (kèm thuộc tính: OCR) |
| `polygon` | ✔ | | | | | | Đa giác |
| `keypoint` | ✔ | | | | | | Điểm mốc (khuôn mặt, tư thế) |
| `polyline` | ✔ | | | | | | Đường gấp khúc không khép kín (làn đường) |
| `mask` | ✔ | | | | | | Mặt nạ phân vùng tô bằng cọ |
| `span` | | ✔ | | | | | Đoạn văn bản (NER) |
| `relation` | | ✔ | | | | | Quan hệ có hướng giữa hai đoạn của một công cụ `span` |
| `transcription` | ✔ | | ✔ | ✔ | | | Chép lại nội dung **có sẵn**: lời nói, chữ trong ảnh |
| `temporalSegment` | | | ✔ | ✔ | | | Đoạn thời gian |
| `track` | | | | ✔ | | | Theo dõi đối tượng qua các khung hình |
| `pairwise` | | | | | ✔ | | So sánh hai câu trả lời |
| `ranking` | | | | | | ✔ | Xếp hạng nhiều mục |

`freeText` và `transcription` cùng gửi `{ "text": "..." }` nhưng khác bản chất: `transcription` có **một đáp án đúng** nên được chấm câu vàng và gộp tự động; `freeText` là câu trả lời mở nên **không chấm, không gộp**.

Sai hình dạng → 400 `tap_nhan_sai_dinh_dang` (`detail` liệt kê từng chỗ sai). Sai luật (tên trùng, công cụ không hợp loại dữ liệu…) → 400 `tap_nhan_khong_hop_le`. Frontend có thể kiểm trước bằng [label-schema.schema.json](../contracts/labeling/label-schema.schema.json) (ví dụ thư viện `ajv`, draft 2020-12).

### 4.5 Hướng dẫn, giá, kênh, điều kiện tham gia

> **Phần giá (`pricing`), `quality-control`, `requireReview`, `budgetVnd` đã thay bằng [mục 18.1](#181-doanh-nghiệp-giá-cả-dự-án-kênh-checklist).** Hướng dẫn và điều kiện tham gia giữ nguyên.

```http
PUT /projects/{id}/guideline
{ "markdown": "# Huong dan\n...", "examples": [ { "sampleId": "...", "label": "xe", "isCorrect": true, "explanation": "..." } ] }

PUT /projects/{id}/pricing
{ "unitPriceVnd": 2000, "redundancy": 2, "maxRedundancy": 3, "budgetVnd": 500000, "deadline": "2027-01-31T00:00:00Z" }

PUT /projects/{id}/quality-control
{ "goldCheckPercent": 10, "requireReview": true }

PUT /projects/{id}/channels
{ "allowProfessional": true, "allowLinkGateway": false, "allowCollaborative": false }

PUT /projects/{id}/eligibility
{ "minLevel": null, "minReputation": null, "requireEntranceTest": true, "entranceQuestionCount": 5, "entrancePassPercent": 80 }
```

- `unitPriceVnd`: thù lao **mỗi nhãn được duyệt**. `redundancy`: mỗi mẫu cần mấy người gán độc lập (mỗi người được trả riêng).
- `maxRedundancy` (tùy chọn, mặc định = `redundancy`, tối đa 10): **trần redundancy thích ứng**. Mẫu mà các labeler chọn khác nhau (không lựa chọn nào quá bán) sẽ được hệ thống tự xin thêm người gán, tối đa tới trần này. Bằng `redundancy` là tắt tính năng. Ký quỹ tính theo trần; phần không dùng tới được trả lại khi kết thúc dự án.
- `goldCheckPercent` (0–50, mặc định 10): phần trăm số lần cấp task là **câu vàng kiểm tra** (câu vàng `purpose: "qualityCheck"`, mục 4.7). Labeler không phân biệt được với task thật; câu kiểm tra **không trả tiền**. Trang dự án phía labeler nên công khai con số này.
- `requireReview` (mặc định `true`; bỏ trường = giữ nguyên): **có duyệt từng nhãn không**. `false` = khi quality-svc có kết quả đồng thuận của task:
  - nhãn **khớp** số đông (`consensusAgrees = true`) được hệ thống duyệt và trả tiền ngay (`reviewerId` rỗng, lịch sử `auto_approved`);
  - dự án chỉ có câu trả lời mở (đồng thuận `notApplicable`) → mọi nhãn được tự duyệt;
  - nhãn **lệch** số đông và mẫu **tranh chấp** vẫn `pendingReview`, chờ chủ dự án.

  Với `redundancy = 1` thì mọi nhãn đều khớp, tức là tự duyệt hết: nên bật câu vàng kiểm tra. Chỉ đổi được khi **nháp** (điều khoản với labeler chốt lúc publish). `ProjectResponse.requireReview` trả lại giá trị hiện tại. Setting toàn hệ thống `annotation.auto_approve_agreed` vẫn có: bật thì mọi dự án tự duyệt nhãn khớp như trên.
- `budgetVnd`: số tiền sẽ ký quỹ. Phải ≥ `estimatedCostVnd` của readiness (mục 4.8).
- Markdown của hướng dẫn: frontend tự render, **phải sanitize** (không render HTML thô).

### 4.6 Nạp dữ liệu

Có hai đường. Kết quả đều là các **mẫu** (sample); mỗi mẫu thành một task khi dự án chạy.

**Đường A — ZIP ảnh (chỉ dự án `image`, file ≤ 200 MB):**

```http
POST /projects/{id}/datasets          (multipart/form-data: name, file)
→ 201 DatasetResponse  (xử lý xong ngay trong request)
```

Chỉ nhận jpg / png / webp trong ZIP; ảnh trùng nội dung bị bỏ. Dự án khác `image` → 409 `zip_chi_cho_anh`.

**Đường B — upload thẳng lên kho file + manifest (mọi loại dữ liệu, file lớn):**

```
① POST /projects/{id}/uploads           xin link upload
② PUT  <uploadUrl>  (thân = nội dung file)   trình duyệt đẩy THẲNG lên MinIO, không qua backend
③ POST /projects/{id}/datasets/manifest  khai báo các mẫu → 202, lô ở trạng thái pending
④ GET  /projects/{id}/datasets           hỏi lại đến khi status = ready | failed
```

```http
① POST /projects/{id}/uploads
{ "files": [ { "name": "cuoc-goi.wav", "sizeBytes": 736078 }, { "name": "danh-sach.jsonl", "sizeBytes": 2048 } ] }
→ 200 [ { "name": "cuoc-goi.wav", "key": "<projectId>/raw/<uuid>.wav", "uploadUrl": "http://localhost:9000/...", "expiresAt": "..." }, ... ]
```

- Tối đa 100 file mỗi lần xin, mỗi file ≤ 5 GB, link sống 1 giờ.
- Đuôi file được lọc theo loại dữ liệu (`.jpg .jpeg .png .webp` / `.wav .mp3 .m4a .ogg .flac .aac` / `.mp4 .mov .webm .mkv`), cộng `.jsonl` / `.json` cho file manifest. Sai → 400 `duoi_file_khong_hop_le`.
- `key` do hệ thống đặt; giữ lại để dùng ở bước ③.

```js
// ② Upload, có thanh tiến độ
const xhr = new XMLHttpRequest();
xhr.open("PUT", slot.uploadUrl);
xhr.upload.onprogress = (e) => setProgress(e.loaded / e.total);
xhr.send(file);           // KHÔNG gắn header Authorization — link đã được ký sẵn
```

```http
③ POST /projects/{id}/datasets/manifest
{ "name": "lo-1", "rows": [ { "file": "<key>", "name": "cuoc-goi.wav" } ] }
  hoặc
{ "name": "lo-1", "manifestKey": "<key của file .jsonl đã upload>" }
→ 202 DatasetResponse { "status": "pending", ... }
```

Một dòng manifest theo loại dữ liệu:

| modality | Dòng | Ghi chú |
|---|---|---|
| image, audio, video | `{ "file": "<key>", "name": "tùy chọn" }` | Audio/video: backend tự đo thời lượng; nếu máy chủ không có ffprobe thì khai thêm `"durationSec"` |
| text | `{ "text": "...", "name": "tùy chọn" }` | ≤ 100.000 ký tự |
| pair | `{ "prompt": "tùy chọn", "a": "...", "b": "..." }` | |
| list | `{ "prompt": "tùy chọn", "items": ["...", "...", "..."] }` | 2–50 mục, mỗi mục là chuỗi không rỗng. Thứ tự trong `items` là **chỉ số mục** (từ 0) dùng khi xếp hạng |

`rows` tối đa 1.000 dòng; nhiều hơn thì ghép thành file `.jsonl` (mỗi dòng một object, ≤ 50.000 dòng) rồi gửi `manifestKey`.

```http
④ GET /projects/{id}/datasets
→ [ { "id", "name", "status": "ready", "sampleCount": 3, "skippedCount": 1,
      "errorSummary": "dong 2: file khong phai anh png / jpeg / webp hop le.", "createdAt", "finishedAt" } ]
```

- `status`: `pending` → `ingesting` → `ready` | `failed`. Hỏi lại mỗi 2 giây.
- Dòng sai **không làm hỏng cả lô**: bị bỏ qua, đếm vào `skippedCount`, lý do (tối đa 20 dòng đầu) trong `errorSummary`. Lý do thường gặp: file không tồn tại / chưa upload xong, file thuộc dự án khác, nội dung không đúng loại (ảnh giả, file âm thanh không có hình khi nạp vào dự án video), trùng nội dung với mẫu đã có.
- `failed` = không có mẫu hợp lệ nào, hoặc lỗi hệ thống.
- Đang có lô `pending` / `ingesting` thì **chưa publish được**.

Xem mẫu:

```http
GET /projects/{id}/samples?page=1&pageSize=50
GET /projects/{id}/samples?ids=<id1>,<id2>,...      ← chỉ các mẫu này (tối đa 100; sai định dạng → 400 ma_mau_khong_hop_le, quá 100 → 400 qua_nhieu_mau)
→ { "items": [ { "id", "datasetId", "modality", "originalName", "sizeBytes",
                 "fileUrl": "http://localhost:9000/...",   // null với text / pair / list
                 "content": null,                         // {"text"}, {"prompt","a","b"} hoặc {"prompt","items"} với text / pair / list
                 "metadata": { "width": 300, "height": 200 } } ], ... }
```

`fileUrl` chỉ sống **5 phút**; hết hạn thì gọi lại API để lấy link mới.

**Hiển thị cho doanh nghiệp:** trang kết quả (`GET /annotations/projects/{id}/results`), trang duyệt, câu vàng, báo lỗi task chỉ trả `sampleId`. Frontend gọi `?ids=` cho các mẫu **đang hiện trên trang** để có `originalName` (tên file lúc upload) và ảnh xem trước — không bày mã mẫu hay JSON cho người dùng. JSON chỉ có trong file xuất. Chỉ chủ dự án / admin gọi được (labeler, reviewer → 403: dữ liệu gốc là tài sản của doanh nghiệp).

### 4.7 Câu hỏi vàng

Câu vàng là mẫu có sẵn đáp án, dùng cho bài test đầu vào (`entranceTest`) hoặc kiểm tra chất lượng (`qualityCheck`). Mẫu đã là câu vàng **không thành task trả tiền**.

```http
POST /projects/{id}/gold-items
{ "items": [ { "sampleId": "...", "purpose": "qualityCheck",
               "expectedPayload": { "loai": { "labelIds": ["ngoai_troi"] },
                                    "vat": [ { "labelId": "xe", "x": 10, "y": 10, "w": 50, "h": 40 } ] } } ] }
→ 200 [ { "id", "sampleId", "expectedPayload": { "taskType": "image", "schemaVersion": 1, "data": {...} }, "purpose" } ]
```

`expectedPayload` có **đúng hình dạng nhãn labeler nộp** (mục 6) và được kiểm y như vậy, nên có thể dùng lại chính workspace gán nhãn để doanh nghiệp tạo đáp án. Tối đa 2.000 câu mỗi dự án.

### 4.8 Checklist và publish

```http
GET /projects/{id}/readiness
→ { "ready": false, "missing": ["chua_co_huong_dan", "ngan_sach_khong_du"],
    "sampleCount": 120, "entranceGoldCount": 0,
    "platformFeePercent": 30, "platformFeePerLabelVnd": 600, "estimatedCostVnd": 624000 }
```

- `missing` là danh sách mã; frontend dịch thành checklist: `chua_co_tap_nhan`, `chua_co_huong_dan`, `chua_cau_hinh_gia`, `chua_co_du_lieu`, `du_lieu_dang_xu_ly`, `deadline_da_qua`, `ngan_sach_khong_du`, `thieu_cau_hoi_vang_cho_test`.
- `estimatedCostVnd` = số mẫu × **trần redundancy** × (đơn giá + phí) = ký quỹ tối thiểu. Phí nền tảng cộng thêm, không trừ vào thù lao labeler.

```http
POST /projects/{id}/publish
→ 200 ProjectResponse { "status": "pendingEscrow" }
→ 409 chua_xac_minh_doanh_nghiep   (hồ sơ KYB chưa được admin duyệt — mục 13; đã duyệt mà vẫn 409 → làm mới token, mục 2.3)
→ 409 chua_xac_thuc_email            (chưa xác thực email, hoặc token chưa được làm mới sau khi xác thực)
```

Nên gọi `GET /kyb/me` trước khi mở checklist: `status` khác `verified` thì hiện thẻ "Xác minh doanh nghiệp" đứng đầu checklist và khoá nút publish. Các bước còn lại (tạo dự án, tập nhãn, nạp dữ liệu, nạp tiền) **không** cần KYB.

Sau đó hỏi lại `GET /projects/{id}` mỗi 1–2 giây trong tối đa ~30 giây:

- `pendingApproval`: đã giữ tiền, chờ admin duyệt (quá 72 giờ không ai duyệt thì tự hủy, hoàn tiền).
- `draft` kèm `statusReason`: thiếu tiền. Hiện lý do + nút nạp tiền.

### 4.9 Vận hành

```http
GET /tasks/projects/{id}/progress
→ { "projectId", "totalTasks": 120, "openTasks": 80, "completedTasks": 35, "excludedTasks": 5,
    "activeLeases": 3, "submissions": 82, "requiredSubmissions": 230,
    "cancelledTasks": 2, "submissionsLast24h": 40, "estimatedCompletionAt": "2026-10-16T09:00:00+00:00",
    "frozenAt": null }
```

- `excludedTasks`: câu hỏi vàng (không tính vào việc phải làm). `cancelledTasks`: task bị huỷ (dự án huỷ, hoặc mẫu bị loại vì báo lỗi — mục 4.13).
- `estimatedCompletionAt` (FB-20): ngoại suy từ tốc độ nộp 24 giờ qua; `null` khi 24 giờ qua không ai nộp ("chưa ước tính được").
- `frozenAt` có giá trị: dự án đã đóng lâu (mặc định 90 ngày) và dữ liệu vận hành đã được dọn — các con số là **bản chụp lúc dọn**, không đổi nữa; `submissionsLast24h` = 0, `estimatedCompletionAt` = `null`. Hiện ghi chú "số liệu chốt ngày …". Khi đó danh sách báo lỗi task (mục 4.13) của dự án trả rỗng.

**Chi phí dự án** (FB-20) — số liệu từ sổ cái:

```http
GET /ledger/projects/{id}/cost      (chủ dự án / admin)
→ { "projectId", "escrowState": "active", "reservedVnd": 1000000, "balanceVnd": 640000,
    "spentAnnotationsVnd": 336000, "spentGateVnd": 24000, "disputeRefundsVnd": 0, "returnedVnd": 0,
    "paidAnnotations": 12, "heldVnd": 140000, "disputedVnd": 0 }
```

| Trường | Ý nghĩa |
|---|---|
| `reservedVnd` / `balanceVnd` | Đã ký quỹ lúc publish / còn lại trong ký quỹ |
| `spentAnnotationsVnd` | Đã chi cho nhãn được duyệt (thù lao labeler + phí nền tảng) |
| `spentGateVnd` | Đã chi cho cổng vượt link |
| `disputeRefundsVnd` | Tiền thắng tranh chấp đã về lại (mục 4.14) |
| `returnedVnd` | Ký quỹ còn dư đã trả về ví khi kết thúc |
| `heldVnd` / `disputedVnd` | Thù lao labeler đang treo (còn tranh chấp được) / đang bị đóng băng do tranh chấp |

`escrowState`: `active` / `closing` / `closed`. Dự án chưa publish → 404.

```http
POST /projects/{id}/pause | /resume | /complete
POST /projects/{id}/cancel   { "reason": "..." }
```

**Hoàn thành / hủy dự án đã chạy** (ký quỹ còn lại về ví doanh nghiệp) chỉ được khi không còn việc nào có thể sinh tiền cho labeler. Gợi ý màn hình theo từng bước:

| Phản hồi | Ý nghĩa | FE làm gì |
|---|---|---|
| 409 `can_tam_dung_truoc` | Dự án đang chạy | Nút "Tạm dừng" trước; tạm dừng thì ngừng cấp task mới, người đang làm vẫn nộp được |
| 409 `chua_the_dong`, `closeCheck.reasons` chứa `chua_dong_bo_tam_dung` | Lệnh tạm dừng chưa tới task-svc | Thử lại sau vài giây |
| … `con_luot_dang_lam` (`activeLeases`) | Labeler đang làm dở | "Còn N người đang làm — chờ họ nộp (tối đa 15 phút)" |
| … `con_nhan_cho_duyet` (`pendingReview`) | Còn nhãn chưa duyệt | Link sang hàng duyệt / nút duyệt hàng loạt |
| … `con_khieu_nai` (`openAppeals`) | Có khiếu nại chờ admin | "Chờ admin phân xử" |
| … `con_han_khieu_nai` (`rejectedInAppealWindow`, `appealWindowEndsAt`) | Nhãn bị từ chối còn trong hạn khiếu nại | "Đóng được sau {appealWindowEndsAt}" |
| … `nhan_chua_dong_bo` (`annotationCount` ≠ `submittedCount`) | Nhãn vừa nộp chưa tới annotation-svc | Thử lại sau vài giây |
| 503 `dich_vu_khong_san_sang` | Không kiểm được | Thử lại sau |

```json
{ "code": "chua_the_dong", "detail": "Chua dong duoc du an: con 2 nhan cho duyet.",
  "closeCheck": { "reasons": ["con_nhan_cho_duyet"], "activeLeases": 0, "submittedCount": 2, "annotationCount": 2,
                  "pendingReview": 2, "openAppeals": 0, "rejectedInAppealWindow": 0, "appealWindowEndsAt": null } }
```

Sau khi đóng: duyệt, từ chối, khiếu nại, phân xử nhãn của dự án → 409 `du_an_da_ket_thuc`.

Thành viên. Reviewer luôn do chủ dự án thêm; labeler vào bằng một trong bốn đường: chủ dự án mời, mã mời, tự `join` / bài test (`public`), đơn được duyệt (`approval`).

```http
GET    /projects/{id}/members        → [ { "userId", "role": "labeler", "state": "active", "joinedAt" } ]
POST   /projects/{id}/members        { "userId": "...", "role": "labeler" }   → 200 thành viên
                                     { "email": "a@b.vn", "role": "labeler" } → 202 lời mời (luôn vậy, xem dưới)
POST   /projects/{id}/members/batch  { "role": "labeler", "userIds": ["..."], "emails": ["a@b.vn", "c@d.vn"] }
→ { "added": 1, "invited": 2, "results": [
      { "input": "<userId>", "userId": "...", "displayName": "Nguyen Van A", "status": "added" },
      { "input": "a@b.vn", "userId": null, "displayName": null, "status": "invited", "invitationId": "...", "message": "Da gui loi moi toi email nay." },
      { "input": "c@d.vn", "userId": null, "displayName": null, "status": "invited", "invitationId": "...", "message": "Da gui loi moi toi email nay." } ] }
POST   /projects/{id}/members/{userId}/block | /unblock
DELETE /projects/{id}/members/{userId}
```

- `batch`: tối đa `project.member_batch_max` (mặc định 100) đầu vào; mỗi đầu vào một dòng kết quả, **đúng thứ tự gửi** (userIds trước, emails sau).
- **Theo userId** — kết quả chi tiết, thêm thẳng: `added`, `khong_tim_thay`, `trung_lap`, `da_la_thanh_vien`, `bi_chan` (đang bị chặn — dùng `unblock`), `bi_chan_boi_doanh_nghiep`, `tai_khoan_bi_khoa`, `khong_phai_labeler`, `chua_du_cap_do`, `chua_du_uy_tin`, `pending_consent` (dự án cần tự đồng ý điều khoản → đã tạo lời mời).
- **(đổi, VD-S-16) Theo email** — mọi email đúng dạng đều trả **cùng** `invited` + `invitationId`, **không** kèm `userId` / `displayName`, dù email có tài khoản hay không, đã là thành viên hay đang bị chặn. Ô mời không còn là cách dò email nào có tài khoản. Người có tài khoản nhận lời mời trong ứng dụng và **tự chấp nhận** (`POST /projects/invitations/{id}/accept`, điều kiện cấp độ / uy tín kiểm lúc đó) — không còn được thêm thẳng; email chưa có tài khoản nhận thư mời đăng ký. Chỉ còn hai mã khác: `email_khong_hop_le`, `trung_lap` (cùng một email hai lần, so theo chuỗi). Frontend: hiện "Đã gửi lời mời", người đó xuất hiện trong danh sách thành viên khi họ chấp nhận.
- Hạn mức riêng: một người mời tối đa `project.email_invite_daily_max` (mặc định 200) email / 24 giờ → vượt thì **429** `vuot_han_muc_moi_email` kèm `Retry-After` và `retryAfterSeconds`, không tạo lời mời nào. Mời theo userId không tính vào hạn mức này.
- Email không phân biệt hoa thường, bỏ khoảng trắng hai đầu.
- `POST /members` một người: theo userId lỗi trả đúng mã như trên (404 `khong_tim_thay`, 409 cho các mã còn lại); theo email luôn **202** + lời mời (400 nếu email sai dạng, 429 khi vượt hạn mức). Người được thêm nhận thông báo `thanh_vien`; người được mời nhận `loi_moi_du_an`.
- Labeler phải đủ `minLevel` / `minReputation` của dự án; reviewer chỉ cần tài khoản không bị khoá.

Mã mời — chia sẻ một mã thay vì gõ từng người (vào được cả dự án riêng tư, kể cả khi còn Nháp):

```http
POST   /projects/{id}/invite-codes   { "maxUses": 50, "expiresInHours": 72, "note": "Lop K66" }   (mọi trường tuỳ chọn)
→ 201 { "id", "projectId", "code": "K7QX3MWPA9TRHC2N", "note", "maxUses": 50, "usedCount": 0,
        "expiresAt", "createdAt", "revokedAt": null, "status": "active" }
GET    /projects/{id}/invite-codes   → [ ... ]  (mới nhất trước; status: active | expired | exhausted | revoked)
DELETE /projects/{id}/invite-codes/{codeId}  → mã đã thu hồi (giữ lại để xem lịch sử)
```

- Không gửi `expiresInHours` thì 7 ngày; tối đa `project.invite_code_max_ttl` (mặc định 30 ngày). Không gửi `maxUses` thì không giới hạn lượt.
- Hiển thị mã dạng `K7QX-3MWP-A9TR-HC2N` cho dễ đọc; server chấp nhận cả có gạch, khoảng trắng, chữ thường. Gợi ý: tạo link `https://<frontend>/join?code=...` trỏ tới màn nhập mã.
- Mã mời bỏ qua chế độ hiển thị và bài test, **không** bỏ qua cấp độ / uy tín.

Đơn xin tham gia (dự án `approval`):

```http
GET  /projects/{id}/join-requests?status=pending      (pending | approved | rejected | cancelled; mặc định pending, cũ nhất trước)
→ [ { "id", "projectId", "projectName", "userId", "displayName", "level": 2, "reputation": 81,
      "message": "Em da gan nhan anh 2 nam", "status": "pending",
      "entranceAttemptId": null, "scorePercent": null, "createdAt", "decidedAt": null, "rejectReason": null } ]
POST /projects/{id}/join-requests/{requestId}/approve                  → đơn, status "approved" (labeler thành thành viên)
POST /projects/{id}/join-requests/{requestId}/reject  { "reason": "Du an da du nguoi" }   → đơn, status "rejected"
```

- `displayName` / `level` / `reputation` là số **hiện tại** (null nếu identity-svc tạm không trả lời; danh sách vẫn hiện).
- Duyệt kiểm lại điều kiện: uy tín tụt dưới ngưỡng từ lúc gửi đơn → 409 `chua_du_uy_tin`; khi đó từ chối đơn.
- Đơn sinh từ bài test có `scorePercent` + `entranceAttemptId`.
- Có đơn mới → chủ dự án nhận thông báo `don_tham_gia` (mặc định không gửi email).

Chặn labeler thì lượt task họ đang giữ bị thu hồi ngay.

### 4.10 Duyệt nhãn

> **Đã thay bằng [mục 18](#18-dự-án-làm-trung-tâm-kiến-trúc-mục-318)** (dự án làm trung tâm). Phần dưới mô tả API cũ, giữ lại để đối chiếu.

Chủ dự án và reviewer của dự án đều duyệt được. Không ai tự duyệt nhãn của chính mình.

```http
GET /annotations/projects/{id}?status=pendingReview&page=1&pageSize=20
→ { "items": [ AnnotationResponse ], ... }
```

```json
{
  "id": "...", "projectId": "...", "taskId": "...", "sampleId": "...", "source": "professional",
  "fileUrl": "http://...", "sampleContent": null, "sampleMetadata": { "width": 300, "height": 200 },
  "labelerId": "...",
  "payload": { "taskType": "image", "schemaVersion": 1,
               "data": { "loai": { "labelIds": ["ngoai_troi"] }, "vat": [ { "labelId": "xe", "x": 20, "y": 30, "w": 100, "h": 50 } ] } },
  "status": "pendingReview", "submittedAt": "...", "reviewedAt": null,
  "rejectReason": null, "appealMessage": null, "canAppeal": false,
  "consensusAgrees": true
}
```

`source`: `professional` (labeler) hoặc `linkGateway` (khách vãng lai qua trang vượt link — mục 12). Nhãn `linkGateway` có `taskId: null`, `labelerId: null`; duyệt / loại để chọn lọc dữ liệu, **không** sinh tiền theo nhãn. Nên cho lọc theo nguồn trên màn hình duyệt.

Màn hình duyệt = **workspace gán nhãn ở chế độ chỉ xem**: vẽ lại mẫu (từ `fileUrl` / `sampleContent` / `sampleMetadata`) và nhãn (`payload.data`) theo đúng quy ước ở mục 6. Tập nhãn lấy từ `GET /projects/{id}` (`labelSchema`).

```http
POST /annotations/{id}/approve                              → 200 AnnotationResponse   (ledger trả tiền labeler)
POST /annotations/{id}/reject   { "reason": "Sai lop" }     → 200 AnnotationResponse
GET  /annotations/{id}/history  → [ { "action": "submitted|approved|rejected|appealed|appeal_accepted|appeal_denied", "actorId", "note", "at" } ]
```

Duyệt lại nhãn đã duyệt → 409 `da_duyet`.

**Gợi ý từ đồng thuận.** Khi một mẫu đủ người gán, quality-svc gộp các nhãn theo từng công cụ (cùng thuật toán với mục 4.11) và đặt `consensusAgrees` trên từng nhãn: `true` (khớp kết quả gộp ở **mọi** công cụ đã có kết quả), `false` (lệch ở ít nhất một công cụ), `null` (chưa đủ người, mẫu tranh chấp ở mọi công cụ người đó làm, hoặc tập nhãn chỉ có `freeText`). Với công cụ dạng danh sách, "khớp" nghĩa là F1 giữa các đối tượng của người đó và các đối tượng đã gộp ≥ `quality.consensus_min_f1` (0,8): vẽ thiếu / thừa một trong nhiều vật vẫn khớp.

```http
GET  /annotations/projects/{id}?status=pendingReview&consensusAgrees=false     lọc nhãn lệch
POST /annotations/projects/{id}/approve-agreed
→ 200 { "approvedCount": 14, "skippedOwnCount": 0 }
```

`approve-agreed` duyệt **mọi** nhãn đang chờ duyệt có `consensusAgrees: true` (tối đa 500 mỗi lần; còn nữa thì gọi lại), người duyệt là người bấm. Nhãn của chính người bấm bị bỏ qua (`skippedOwnCount`).

### 4.11 Kết quả và tải về

```http
GET /annotations/projects/{id}/results
```

```json
{
  "projectId": "...", "modality": "pair", "labelSchema": { ... },
  "sampleCount": 2, "disputedCount": 0,
  "labelDistribution": { "tot_hon": { "a": 1 }, "an_toan": { "an_toan": 1 } },
  "samples": [
    {
      "sampleId": "...", "storageKey": null,
      "sampleContent": { "prompt": "...", "a": "...", "b": "..." }, "sampleMetadata": {},
      "approvedCount": 2, "disputed": false,
      "tools": {
        "tot_hon": { "kind": "pairwise", "method": "majority", "final": { "choice": "a" }, "votes": { "a": 2 }, "agrees": [true, true], "disputed": false },
        "an_toan": { "kind": "classification", "method": "majority", "final": { "labelIds": ["an_toan"] }, "votes": { "an_toan": 2 }, "agrees": [true, true], "disputed": false }
      },
      "labels": [ { "tot_hon": { "choice": "a" }, "an_toan": { "labelIds": ["an_toan"] } }, { ... } ]
    }
  ]
}
```

- Kết quả **gộp theo từng công cụ** từ các nhãn đã duyệt. Mọi công cụ trả cùng một khung:

  | Trường | Ý nghĩa |
  |---|---|
  | `kind` | Loại công cụ |
  | `method` | Cách gộp, bảng dưới. `"none"` = không gộp (công cụ `freeText`, hoặc chưa ai làm công cụ tùy chọn này): xem `labels` |
  | `final` | Kết quả gộp, **cùng hình dạng với nhãn nộp lên** của công cụ đó. `null` = tranh chấp |
  | `disputed` | `true` = không có kết quả được quá nửa số người ủng hộ. Nên làm nổi bật cho doanh nghiệp xem lại |
  | `agrees` | Mảng `true` / `false`, **theo thứ tự `labels`** (bỏ qua người không làm công cụ này): nhãn của người đó có khớp `final` không |
  | `supports` | Chỉ công cụ dạng danh sách: số người vẽ từng đối tượng trong `final` (cùng thứ tự) |
  | `votes` / `scores` | Phân loại, cặp: số phiếu từng lựa chọn. Xếp hạng: điểm Borda từng mục |

  | Công cụ | `method` | Gộp thế nào | Một người "khớp" khi |
  |---|---|---|---|
  | `classification`, `pairwise` | `majority` | Lựa chọn được **hơn một nửa** số người chọn (nhiều lớp: xét từng lớp) | Chọn đúng kết quả gộp |
  | `bbox`, `keypoint`, `temporalSegment` | `cluster` | Ghép các đối tượng "cùng một vật" của những người khác nhau thành cụm (cùng lớp, độ giống ≥ ngưỡng); giữ cụm có hơn một nửa số người; đối tượng gộp = **trung vị** từng toạ độ | F1 với các đối tượng đã gộp ≥ 0,8 |
  | `polygon`, `polyline`, `track` | `cluster` | Như trên; đối tượng gộp = bản **"ở giữa"** của cụm (giống các bản khác nhất) | như trên |
  | `mask` | `cluster` | Như trên; mặt nạ gộp = **bỏ phiếu từng pixel** trong cụm | như trên |
  | `span`, `relation` | `majority` | Giữ đoạn / quan hệ được hơn một nửa số người ghi **y hệt** | như trên |
  | `transcription` | `medoid` | Bản chép "ở giữa" (tổng độ lệch tới các bản khác nhỏ nhất) | CER so với bản đó ≤ 0,1 |
  | `number` | `median` | Trung vị | Lệch trung vị ≤ `tolerance` |
  | `ranking` | `borda` | Mục đứng thứ k (từ 0) trong m mục được m−1−k điểm; cộng điểm rồi xếp giảm dần | Tỉ lệ cặp mục xếp cùng chiều (Kendall) ≥ 0,8 |

  Với mọi công cụ trừ phân loại / cặp, **không quá một nửa số người khớp** thì công cụ đó tranh chấp (`final: null`). Thuộc tính của đối tượng được gộp theo từng thuộc tính: `choice` / `boolean` lấy giá trị quá bán (không có thì bỏ), `number` lấy trung vị, `text` lấy bản ở giữa; một đối tượng chỉ được tính là khớp khi khớp cả các thuộc tính **có trong kết quả gộp**.

  Ví dụ một mẫu ảnh có khung (3 người vẽ, người thứ ba bỏ sót một vật):

  ```json
  "tools": {
    "vat": { "kind": "bbox", "method": "cluster",
             "final": [ { "labelId": "nguoi", "x": 121, "y": 21, "w": 20, "h": 59 }, { "labelId": "xe", "x": 10, "y": 10, "w": 50, "h": 30 } ],
             "supports": [2, 3], "agrees": [true, true, false], "disputed": false } }
  ```
- Ngưỡng ghép đối tượng: `matchThreshold` của công cụ, không có thì setting `labeling.threshold.<loại>` (mục 6.5). Mức F1 để tính là khớp: setting `quality.consensus_min_f1`.
- `labelDistribution`: số lần mỗi lớp xuất hiện, theo từng công cụ **có lớp** (phân loại, cặp, và các công cụ dạng danh sách — đếm trên nhãn đã duyệt). Dùng cho biểu đồ cảnh báo lệch lớp.
- `imbalanceWarnings` (FB-24): cảnh báo lệch lớp, cùng hình dạng với API `distribution` dưới đây.
- `samples[].consensusStatus` (`agreed` | `disputed` | `notApplicable` | `null`) và `consensusFinal` (`{ "<tên công cụ>": <final> \| null }`): kết quả đồng thuận của quality-svc tính trên **mọi** nhãn đã nộp (kể cả chưa duyệt, bằng đúng thuật toán trên), khác với `tools` chỉ tính trên nhãn đã duyệt. `notApplicable` = tập nhãn chỉ có `freeText`.

Phân bố nhãn (nhẹ hơn `results`, dùng cho biểu đồ trên trang dự án):

```http
GET /annotations/projects/{id}/distribution
→ { "projectId", "sampleCount": 40,
    "labelDistribution": { "loai": { "meo": 35, "cho": 5 } },
    "imbalanceWarnings": [
      { "code": "lop_ap_dao", "tool": "loai", "label": "meo", "count": 35, "total": 40, "percent": 88 },
      { "code": "lop_chua_co_nhan", "tool": "loai", "label": "chim", "count": 0, "total": 40, "percent": 0 } ],
    "thresholdPercent": 80 }
```

- Chỉ xét công cụ **phân loại** có ≥ 2 lớp và ≥ 20 nhãn đã duyệt (`annotation.imbalance_min_labels`). `lop_ap_dao`: một lớp chiếm **hơn** `thresholdPercent`; `lop_chua_co_nhan`: lớp chưa ai chọn.
- Chỉ là **cảnh báo** — dữ liệu có thể lệch thật. Gợi ý hiển thị: dải vàng "Lớp *meo* chiếm 88 % — kiểm tra dữ liệu hoặc hướng dẫn".

```http
GET /annotations/projects/{id}/export?format=json|csv|coco|yolo|voc     → file tải về (Content-Disposition)
```

- `json`: giống `results`.
- `csv`: một dòng một mẫu, một cột một công cụ (UTF-8 có BOM, mở bằng Excel được). Ô ghi **kết quả gộp**: lớp (`a;b`), lựa chọn, bản chép, số, thứ tự xếp hạng (`2;0;1`); công cụ dạng danh sách ghi JSON các đối tượng đã gộp; tranh chấp để trống; `freeText` ghi JSON câu trả lời của từng người.
- `coco`: chỉ dự án ảnh có công cụ `bbox` / `polygon` / `mask`; dự án khác → 400 `coco_chi_cho_khung_anh`. Mỗi đối tượng **đã gộp** là một annotation (thêm `support` = số người vẽ, `attributes` nếu có); mặt nạ xuất `segmentation` dạng RLE không nén của COCO. Ảnh tranh chấp có `"disputed": true` trong `images` và không có annotation của công cụ tranh chấp.
- `yolo` / `voc` (FB-25): file **zip**, chỉ dự án ảnh có công cụ `bbox` (đa giác không xuất); dự án khác → 400 `khung_chi_cho_anh`. YOLO: `labels/{sampleId}.txt` (mỗi dòng `lớp tâm_x tâm_y rộng cao`, chuẩn hoá 0–1), `classes.txt`, `data.yaml`. VOC: `Annotations/{sampleId}.xml` (toạ độ pixel), `ImageSets/Main/default.txt`. Zip **không** chứa ảnh (ảnh đã có ở doanh nghiệp): `images.txt` ánh xạ `sampleId` → khoá file lưu trữ. Giống COCO, mỗi khung **đã gộp** là một dòng; mẫu tranh chấp có file nhãn rỗng. Ảnh thiếu kích thước bị bỏ qua và được đếm trong `README.txt`.

Gọi bằng `fetch` có header Authorization rồi tạo blob để tải (thẻ `<a href>` không gửi được token).

### 4.12 Chỉ số chất lượng (quality-svc)

Chủ dự án và admin xem được; người khác nhận 404.

```http
GET /quality/projects/{id}/summary
→ { "projectId", "labelCount": 15, "tasksEvaluated": 7,
    "agreed": 7, "disputed": 0, "notApplicable": 0, "waitingMoreLabels": 0,
    "redundancyIncreases": 1,
    "goldAnswers": 2, "goldAccuracyPercent": 50,
    "tools": [ { "tool": "cam_xuc", "krippendorffAlpha": 0.81, "itemCount": 7, "labelCount": 14, "computedAt": "..." } ],
    "archivedAt": null }
```

| Trường | Ý nghĩa |
|---|---|
| `tasksEvaluated` | Số task đã đủ người và được tính đồng thuận (trạng thái cuối của mỗi task) |
| `agreed` / `disputed` / `notApplicable` | Đồng thuận / vẫn tranh chấp khi đã hết trần / tập nhãn chỉ có công cụ không gộp (`freeText`) |
| `waitingMoreLabels` | Task đang tranh chấp, đã xin thêm người, chờ người gán tiếp |
| `redundancyIncreases` | Tổng số lần đã xin thêm người |
| `goldAccuracyPercent` | Tỉ lệ trả lời đúng câu vàng kiểm tra của cả dự án; `null` khi chưa có câu nào |
| `archivedAt` | `null` = số liệu sống. Có giá trị = dự án đã đóng lâu, nhãn thô đã được dọn; cả `summary` và `labelers` là **bản chụp lúc lưu trữ** (riêng `reputation` của labeler luôn là điểm hiện tại). Hiện ghi chú "số liệu chốt ngày …" |
| `tools[].krippendorffAlpha` | Mức đồng thuận đã trừ phần trùng hợp ngẫu nhiên (1 = hoàn toàn nhất trí, ≤ 0 = như đoán bừa). Chỉ có cho công cụ phân loại một lớp và so sánh cặp; cập nhật theo lô (mặc định 15 phút) |

```http
GET /quality/projects/{id}/labelers
→ [ { "labelerId", "labelCount": 7, "agreementPercent": 100, "agreementSampleCount": 7,
      "goldAnswers": 1, "goldAccuracyPercent": 100,
      "dawidSkeneSkill": { "cam_xuc": 0.97 }, "reputation": 90 } ]
```

`dawidSkeneSkill` theo từng công cụ (0–1); rỗng khi labeler có dưới 5 nhãn trong dự án. Admin chạy lô Dawid–Skene ngay bằng `POST /quality/admin/dawid-skene/run` → `{ "computed": 3, "full": false }`: chỉ tính lại dự án **có nhãn mới** từ lần trước (nhanh). `?full=true` tính lại mọi dự án — chậm (~1 giây / dự án), chỉ dùng sau khi đổi `quality.ds_min_labels`.

### 4.13 Task bị labeler báo lỗi (FL-07)

Labeler báo một mẫu hỏng / không phù hợp / khó hiểu (mục 5.4). Đủ 3 người báo (`task.report_auto_exclude`) thì task **tự huỷ** nếu không ai đang làm; còn lại chủ dự án tự quyết.

```http
GET  /tasks/projects/{id}/reports?status=open|dismissed|excluded|all       (mặc định open)
→ [ { "id", "taskId", "sampleId", "labelerId", "reason": "broken", "note": "Anh den thui",
      "state": "open", "createdAt", "resolvedAt": null, "reportsOnTask": 2 } ]

POST /tasks/reports/{reportId}/resolve   { "action": "dismiss" | "exclude" }   → báo cáo sau khi xử lý
```

- `dismiss`: mẫu không sao (chỉ đóng báo cáo này; người báo vẫn không được giao lại mẫu đó).
- `exclude`: loại mẫu — task bị huỷ, **mọi** báo cáo của task thành `excluded`, task tính vào `cancelledTasks`. Đang có người giữ task → 409 `con_nguoi_dang_lam`, thử lại sau (tối đa 15 phút).
- `reason`: `broken` (hỏng, không mở được), `inappropriate` (nội dung không phù hợp), `unclear` (không rõ cách gán), `other` (bắt buộc kèm `note`).

### 4.14 Tranh chấp nhãn đã duyệt (FM-06)

> **Đã thay bằng [mục 18](#18-dự-án-làm-trung-tâm-kiến-trúc-mục-318)** (dự án làm trung tâm). Phần dưới mô tả API cũ, giữ lại để đối chiếu.

Doanh nghiệp phát hiện một nhãn **đã duyệt** là sai (ví dụ sau khi kiểm tra lại) và đòi lại tiền. Chỉ mở được khi thù lao của nhãn **còn đang treo** (chưa về ví khả dụng của labeler — mặc định 3 ngày sau khi duyệt). Mỗi nhãn một tranh chấp.

```http
POST /ledger/disputes   { "annotationId": "...", "reason": "Khung lech han doi tuong" }
→ 200 { "id", "annotationId", "projectId", "labelerId", "businessId", "amountVnd": 20000,
        "reason", "state": "open", "openedAt", "resolvedAt": null, "resolutionNote": null }
→ 409 khong_con_treo (tiền đã về ví labeler) | 409 da_tranh_chap | 400 thieu_ly_do
→ 404 (không phải nhãn đã duyệt thuộc dự án của mình)

GET /ledger/disputes/mine?status=open|refundedBusiness|paidLabeler&page=1&pageSize=20
```

- Mở tranh chấp thì khoản treo **đóng băng** (labeler nhận thông báo; tiền không về khả dụng tới khi admin xử).
- Admin phân xử (mục 7.5): `refundedBusiness` → thù lao **và phí nền tảng** của nhãn đó về lại ký quỹ (dự án đã đóng thì về ví), nhãn bị đánh dấu loại khỏi kết quả; `paidLabeler` → tiền chạy tiếp cho labeler.
- Nút "Tranh chấp" nên hiện trên nhãn `approved` trong màn hình duyệt / kết quả; gặp 409 `khong_con_treo` thì ẩn nút.

---

## 5. Labeler: nhận việc, gán nhãn, nhận tiền

### 5.1 Chợ dự án và tham gia

```http
GET  /projects?status=running&page=1&pageSize=20   dự án công khai đang chạy + dự án mình đã tham gia
GET  /projects/{id}                        chi tiết, có labelSchema + guideline để xem trước
POST /projects/{id}/join                   → 200 { "userId", "role": "labeler", "state": "active", "joinedAt" }
```

`join` trả 409 `can_lam_test` nếu dự án yêu cầu bài test đầu vào, 409 `can_xin_duyet` nếu dự án ở chế độ `approval`, 404 nếu dự án riêng tư (labeler không thấy nó). Không đủ cấp độ / uy tín → 409 `chua_du_cap_do` / `chua_du_uy_tin`, `detail` ghi rõ "yeu cau toi thieu 3, hien tai la 1" — kiểm ngay ở đây chứ không đợi tới lúc nhận task. Chợ dự án nên hiện `minLevel` / `minReputation` và làm mờ nút khi labeler chưa đủ (`GET /me/level`).

Nhập mã mời (màn "Tham gia bằng mã"):

```http
POST /projects/join-by-code   { "code": "k7qx-3mwp-a9tr-hc2n" }
→ 200 { "projectId", "projectName", "member": { "userId", "role": "labeler", "state": "active", "joinedAt" } }
```

404 mã không tồn tại; 409 `ma_moi_het_han` / `ma_moi_het_luot` / `ma_moi_da_thu_hoi` / `da_la_thanh_vien` / `bi_chan` / `chua_du_cap_do` / `chua_du_uy_tin`. Chỉ tài khoản labeler (doanh nghiệp → 403).

Dự án `approval` (nút "Xin tham gia" thay cho "Tham gia"):

```http
POST   /projects/{id}/join-requests   { "message": "Em da gan nhan anh 2 nam" }   (tuỳ chọn, ≤ 500 ký tự)
→ 201 { "id", "projectId", "status": "pending", "message", "createdAt", ... }
GET    /projects/join-requests/mine   → mọi đơn của tôi, mới nhất trước, kèm projectName; đơn bị từ chối có rejectReason + canRequestAgainAt
DELETE /projects/{id}/join-requests/mine   → rút đơn đang chờ (404 nếu không có)
```

- 409 `da_co_don_dang_cho` (mỗi dự án một đơn chờ), `chua_duoc_xin_lai` (bị từ chối chưa quá `project.join_request_cooldown`, mặc định 24 giờ — hiện `canRequestAgainAt`), `can_lam_test` (dự án có bài test: làm bài, đạt thì đơn tự tạo), `khong_can_duyet` (dự án `public`: dùng `join`).
- Kết quả đơn tới qua thông báo `thanh_vien` ("Đơn xin tham gia được duyệt / bị từ chối"). Được duyệt thì `POST /tasks/projects/{id}/next` dùng được sau vài giây (task-svc nhận `member.added`).

### 5.2 Bài test đầu vào

```http
POST /projects/{id}/entrance-test/attempts
→ { "attemptId", "expiresAt",                // 30 phút
    "labelSchema": { ... }, "schemaVersion": 1,
    "questions": [ { "sampleId", "fileUrl", "content", "metadata" } ] }
```

Mỗi câu hiển thị giống một task (mục 6). Nộp:

```http
POST /projects/{id}/entrance-test/attempts/{attemptId}/submit
{ "answers": [ { "sampleId": "...", "payload": { "label": { "labelIds": ["do"] } } } ] }
→ { "attemptId", "scorePercent": 100, "passPercent": 80, "passed": true, "joinedProject": true, "attemptsLeft": 2 }

GET /projects/{id}/entrance-test/attempts   → lịch sử (tối đa 3 lần làm)
```

- Đậu thì tự thành thành viên (`joinedProject: true`). Dự án `approval`: đậu thì thành **đơn** chờ chủ dự án duyệt — `joinedProject: false`, `joinRequestId` có giá trị; đang có đơn chờ thì không làm lại bài được (409 `da_co_don_dang_cho`).
- Bắt đầu bài kiểm cấp độ / uy tín **trước** (409 `chua_du_cap_do`...), không để labeler làm xong mới biết không đủ điều kiện.
- Câu trả lời sai định dạng → 400, bài **chưa** bị tính là đã nộp; sửa rồi nộp lại.
- Server không bao giờ trả đáp án.

### 5.3 Nhận task

> **Đã thay bằng [mục 18](#18-dự-án-làm-trung-tâm-kiến-trúc-mục-318)** (dự án làm trung tâm). Phần dưới mô tả API cũ, giữ lại để đối chiếu.

```http
POST /tasks/projects/{id}/next
→ 200 LeaseResponse   |   204 hết task cho bạn   |   403 chưa đủ điều kiện (code cho biết lý do)
```

- Đang giữ task của dự án này thì gọi lại vẫn nhận **chính task đó** (không tốn thêm lượt). Mỗi người giữ tối đa một task mỗi dự án.
- Lượt giữ (lease) sống **15 phút** (`expiresAt`). Hết hạn thì task về lại hàng đợi cho người khác; nộp sau đó bị 409 `lease_het_han`.
- 204: hiển thị "hết việc", có thể cho thử lại sau.
- 403 thường gặp: `khong_phai_thanh_vien`, `du_an_khong_chay`, `bi_chan_khoi_du_an`, `da_qua_deadline`, `chua_du_cap_do`, `chua_du_uy_tin`. Ngay sau khi `join` hoặc ngay sau khi dự án được duyệt có thể nhận 403 trong vài giây (mục 8): thử lại sau 1–2 giây.

- Một số task có thể là **câu vàng kiểm tra** (theo `goldCheckPercent` của dự án). Response giống hệt task thường và FE **không được** tìm cách phân biệt. Nộp như bình thường; `taskCompleted` luôn `false` với câu kiểm tra.

Chi tiết `LeaseResponse` và cách dựng màn hình: **mục 6**.

### 5.4 Nộp, bỏ qua, xem task đang giữ

> **Đã thay bằng [mục 18](#18-dự-án-làm-trung-tâm-kiến-trúc-mục-318)** (dự án làm trung tâm). Phần dưới mô tả API cũ, giữ lại để đối chiếu.

```http
POST /tasks/assignments/{assignmentId}/submit   { "payload": { ... } }
→ 200 { "assignmentId", "taskCompleted": false }
→ 400 nhan_sai_dinh_dang | nhan_khong_hop_le | thieu_nhan   (lease VẪN giữ — sửa rồi nộp lại)
→ 409 lease_het_han | lease_khong_con

POST /tasks/assignments/{assignmentId}/release   → 204 (trả task về hàng đợi ngay)
GET  /tasks/assignments/mine                     → [ LeaseResponse ] các task đang giữ (để khôi phục sau khi tải lại trang)
```

Nộp xong thì gọi `next` để lấy task tiếp theo. Khi nộp, gửi kèm header `X-Device-Id` (mục 1).

**Lưu nháp (FL-05):**

```http
PUT /tasks/assignments/{assignmentId}/draft   { "payload": { ...dạng giống khi nộp, CHƯA cần hợp lệ... } }
→ 200 { "assignmentId", "draftSavedAt" }
→ 400 nhap_khong_hop_le (không phải JSON object) | 400 nhap_qua_lon (> 256 KB) | 409 lease_het_han | lease_khong_con
```

- Gợi ý: tự lưu sau mỗi thay đổi (debounce 2–3 giây) và khi rời trang. Hiện "Đã lưu nháp lúc …".
- `next` / `assignments/mine` trả lại task đang giữ kèm `draft` và `draftSavedAt` → dựng lại màn hình từ `draft` nếu có.
- Nháp gắn với **lượt giữ**: hết 15 phút hoặc bỏ qua task thì nháp mất theo.

**Báo lỗi task (FL-07):**

```http
POST /tasks/assignments/{assignmentId}/report   { "reason": "broken|inappropriate|unclear|other", "note": "..." }
→ 200 báo cáo (giống mục 4.13) — task được trả lại, bạn sẽ không được giao lại mẫu này
→ 400 ly_do_khong_hop_le | thieu_ghi_chu (reason other) | ghi_chu_qua_dai   |   409 lease_khong_con
```

Báo lỗi xong gọi `next` để lấy task khác. Không bị trừ uy tín.

### 5.5 Lịch sử và khiếu nại

> **Đã thay bằng [mục 18](#18-dự-án-làm-trung-tâm-kiến-trúc-mục-318)** (dự án làm trung tâm). Phần dưới mô tả API cũ, giữ lại để đối chiếu.

```http
GET /annotations/mine?status=rejected&page=1&pageSize=20
→ { "annotations": { "items": [ AnnotationResponse ], ... },
    "pendingCount", "approvedCount", "rejectedCount", "appealedCount", "approvalRatePercent" }

POST /annotations/{id}/appeal   { "message": "Anh nay co sac vang, de nghi xem lai" }
```

Chỉ hiện nút khiếu nại khi `canAppeal: true` (bị từ chối, chưa khiếu nại lần nào, còn trong hạn 7 ngày).

### 5.6 Điểm uy tín

```http
GET /quality/me     → { "reputation": 78, "agreementPercent": 92, "updatedAt": "..." }
```

`reputation` 0–100 (`null` khi chưa có bằng chứng) tính từ câu vàng kiểm tra + mức khớp đồng thuận. Dự án có thể đặt `minReputation` (mục 4.5): dưới mức đó thì `next` trả 403 `chua_du_uy_tin`. API **không** trả số câu vàng đúng/sai — đếm thay đổi sau từng lần nộp là đoán được task nào là câu kiểm tra.

### 5.7 Ví và rút tiền

Doanh thu chia sẻ link của labeler vào **cùng ví** này: `pendingVnd` rồi `availableVnd`, như thù lao nhãn (mục 12).

```http
GET /ledger/me/balance        → { "pendingVnd": 20000, "availableVnd": 63000, ... }
GET /ledger/me/transactions?page=1&pageSize=20
→ { "items": [ { "seq", "type": "annotationPayout", "reference", "description", "accountCode", "amountVnd": 20000, "at" } ], ... }

POST /ledger/withdrawals
Idempotency-Key: <uuid, giữ nguyên khi bấm lại>
{ "amountVnd": 50000, "bankAccount": "..." }
→ { "id", "amountVnd", "taxVnd", "netVnd", "state": "requested", "createdAt", "failureReason" }

GET /ledger/withdrawals/mine
```

- `pendingVnd`: nhãn đã duyệt nhưng tiền còn **treo** (mặc định 3 ngày; dev 2 phút) để xử lý khiếu nại / gian lận. Hết hạn treo thì chuyển sang `availableVnd`.
- Thuế: mặc định rút ≥ 2.000.000đ bị khấu trừ 10% (`taxVnd`); mức rút tối thiểu mặc định 50.000đ. Các con số này admin đổi được, nên **đừng hard-code**: hiện `taxVnd` / `netVnd` backend trả về, và hiện nguyên thông báo của lỗi `duoi_muc_toi_thieu`.
- `state`:

| `state` | Ý nghĩa | Hiển thị |
|---|---|---|
| `pendingApproval` | Lệnh vượt ngưỡng tự duyệt (mặc định > 2.000.000đ) hoặc admin đang tắt tự duyệt: **tiền đã bị giữ** (khả dụng giảm), chờ admin | "Đang chờ duyệt" |
| `requested` | Đã duyệt, đang chuyển khoản | "Đang chuyển" |
| `completed` | Đã chuyển | |
| `failed` | Cổng chuyển khoản lỗi — tiền tự hoàn lại ví, lý do ở `failureReason` | |
| `rejected` | Admin từ chối — tiền hoàn lại ví, lý do ở `failureReason` | |

Response có thêm `reviewedAt` (lúc duyệt / từ chối). Hỏi lại `GET /ledger/withdrawals/mine` để cập nhật.

Rút tiền khi **chưa xác thực email** → 409 `chua_xac_thuc_email` (mục 2.4).

**Thống kê thu nhập (FL-11):**

```http
GET /ledger/me/earnings?from=2026-10-01&to=2026-10-31&groupBy=day|project
→ { "from": "2026-10-01", "to": "2026-10-31", "groupBy": "day", "totalVnd": 340000,
    "byKindVnd": { "annotation": 300000, "gateClick": 30000, "referral": 10000 },
    "items": [ { "key": "2026-10-05", "amountVnd": 60000, "count": 3 } ] }
```

- Ngày tính theo **giờ Việt Nam**. Mặc định 30 ngày gần nhất; tối đa 366 ngày (`khoang_ngay_khong_hop_le`).
- `groupBy=project` thì `key` là `projectId`. Ngày không có thu nhập thì **không có** trong `items` — biểu đồ tự điền 0.
- Tính mọi khoản thu kể cả đang treo; **không** tính khoản bị giữ lại (link vi phạm) và khoản đã hoàn cho doanh nghiệp do thua tranh chấp.

---

## 6. Workspace gán nhãn: hợp đồng chi tiết

### 6.1 Frontend nhận gì

```json
{
  "assignmentId": "...", "projectId": "...", "taskId": "...", "sampleId": "...",
  "modality": "audio",
  "fileUrl": "http://localhost:8080/media/v/ZGVmZ2hp...",
  "content": null,
  "metadata": { "durationSec": 10, "segmentStart": 10, "segmentEnd": 20, "sourceDurationSec": 25 },
  "labelSchema": {
    "modality": "audio", "segmentSeconds": 10,
    "tools": [
      { "name": "loi_noi", "kind": "transcription", "required": true, "maxLength": 5000 },
      { "name": "doan", "kind": "temporalSegment", "required": true, "classes": ["giong_noi", "nhac", "im_lang"], "minItems": 0, "maxItems": 1000 }
    ]
  },
  "schemaVersion": 1,
  "expiresAt": "2026-10-06T09:15:00+00:00",
  "unitPriceVnd": 8000
}
```

| Trường | Ý nghĩa |
|---|---|
| `modality` | Chọn khung hiển thị mẫu: ảnh, văn bản, player âm thanh, player video, hai cột A/B, danh sách mục |
| `fileUrl` | Link xem / nghe file qua media-svc, sống 5 phút. `null` với text / pair. Ảnh labeler nhận có **watermark chìm** mang mã của chính họ (không nhìn thấy, không đổi kích thước — mục 16). Hết hạn khi đang làm thì gọi lại `next` (trả lại đúng task, kèm link mới) |
| `content` | Nội dung text (`{"text"}`), pair (`{"prompt"?, "a", "b"}`) hoặc list (`{"prompt"?, "items": [...]}`). `null` với dữ liệu file |
| `metadata` | Thông tin mẫu, chỉ có những trường liên quan: `width`, `height` (ảnh, video), `durationSec`, `segmentStart`, `segmentEnd`, `sourceDurationSec` (audio, video), `length` (text), `itemCount` (list) |
| `labelSchema` | Danh sách công cụ, **đã điền đủ giá trị mặc định**. Mỗi công cụ là một khối trên màn hình, theo đúng thứ tự trong mảng |
| `expiresAt` | Hiện đồng hồ đếm ngược; cảnh báo khi còn ~2 phút |

### 6.2 Frontend gửi gì

```json
{ "payload": { "<name của công cụ>": <kết quả theo kind>, ... } }
```

- **Khóa = `name`** của công cụ, giá trị có hình dạng theo `kind` (bảng 6.3).
- Công cụ `required: true` phải có mặt. Công cụ `required: false` được **bỏ hẳn khóa** khi người dùng không làm.
- Không gửi khóa thừa (công cụ không có trong tập nhãn) → 400.
- Không gửi `taskType` / `schemaVersion`: server tự lấy theo dự án.
- Server **chuẩn hóa** nhãn (ví dụ sắp xếp `labelIds`), nên nhãn trả về ở màn hình duyệt có thể khác thứ tự so với lúc gửi.

Ví dụ với task âm thanh ở trên:

```json
{ "payload": {
    "loi_noi": { "text": "xin chao cac ban" },
    "doan": [ { "labelId": "giong_noi", "start": 0.5, "end": 4.2 }, { "labelId": "nhac", "start": 4.2, "end": 10 } ] } }
```

### 6.3 Từng loại công cụ

#### `classification` — chọn lớp (mọi loại dữ liệu)

- UI: radio (`allowMultiple: false`) hoặc checkbox (`true`), nhãn hiển thị là `classes`.
- Gửi: `{ "labelIds": ["ngoai_troi"] }`.
- Server kiểm: lớp có trong `classes`; không trùng; single-label thì đúng 1 lớp, multi-label thì ≥ 1 lớp.

#### `bbox` — khung chữ nhật (image)

- UI: canvas vẽ khung trên ảnh, mỗi khung chọn một lớp trong `classes`.
- Gửi: `[ { "labelId": "xe", "x": 20, "y": 30, "w": 100, "h": 50 } ]`.
- **Tọa độ theo pixel của ảnh gốc** (`metadata.width × metadata.height`), gốc (0,0) ở góc **trên-trái**, `x`/`y` là góc trên-trái của khung, `w`/`h` > 0. Ảnh hiển thị thu nhỏ thì phải nhân ngược tỉ lệ trước khi gửi. Số thực được chấp nhận.
- Server kiểm: khung nằm trong ảnh (`x + w ≤ width`, `y + h ≤ height`); số khung trong `[minItems, maxItems]`.

#### `polygon` — đa giác (image)

- UI: click từng đỉnh, đóng vòng.
- Gửi: `[ { "labelId": "duong", "points": [[0, 200], [300, 200], [300, 150], [0, 150]] } ]`.
- Cùng hệ tọa độ với bbox. 3–500 đỉnh, mọi đỉnh nằm trong ảnh. Đa giác tự đóng (đỉnh cuối nối về đỉnh đầu), không cần gửi lặp đỉnh đầu.

#### `span` — đánh dấu đoạn chữ (text)

- UI: bôi đen đoạn văn bản trong `content.text`, chọn lớp (gắn thực thể / NER).
- Gửi: `[ { "labelId": "TEN_NGUOI", "start": 4, "end": 8 } ]`.
- `start`, `end` là **chỉ số ký tự UTF-16**, `end` **không tính**: đoạn được đánh dấu chính là `text.slice(start, end)` trong JavaScript. Lấy từ `Selection` / `Range` phải quy về chỉ số trong chuỗi gốc (không tính HTML).
- Server kiểm: `0 ≤ start < end ≤ metadata.length`.

#### `transcription` — chép lại nội dung có sẵn (audio, video, image)

- UI: ô nhập nhiều dòng, đếm ký tự theo `maxLength`. Audio / video: chép lời nói; ảnh: chép chữ trong ảnh (OCR cả ảnh — cần vị trí từng dòng chữ thì dùng `bbox` kèm thuộc tính `text`).
- Gửi: `{ "text": "xin chao cac ban" }`.

#### `freeText` — câu trả lời mở (mọi loại dữ liệu)

- UI: ô nhập nhiều dòng như trên. Dùng cho mô tả ảnh, dịch, tóm tắt, trả lời câu hỏi, viết lại câu trả lời của LLM.
- Gửi: `{ "text": "Một tấm biển báo màu đỏ đặt bên đường." }`.
- Server kiểm: `required: true` thì không được rỗng; không quá `maxLength`. **Không chấm câu vàng, không gộp tự động** (mỗi người viết một kiểu): màn hình kết quả hiện câu trả lời của từng người.

#### `number` — một số (mọi loại dữ liệu)

- UI: thanh trượt / ô sao / ô nhập số theo `min`, `max`, `integer` của công cụ (ví dụ `"integer": true, "min": 1, "max": 5` → 5 ngôi sao).
- Gửi: `{ "value": 4 }`.
- Server kiểm: là số; nguyên nếu `integer`; trong `[min, max]`.

#### `keypoint` — điểm mốc (image)

- UI: click đặt điểm, mỗi điểm chọn một lớp (`"mat_trai"`, `"mui"`...). Một lớp có thể có nhiều điểm.
- Gửi: `[ { "labelId": "mui", "x": 100.5, "y": 85 } ]`, cùng hệ toạ độ pixel với bbox.
- Server kiểm: điểm nằm trong ảnh.

#### `polyline` — đường gấp khúc (image)

- UI: click từng đỉnh, **không** đóng vòng (làn đường, dây điện, đường viền).
- Gửi: `[ { "labelId": "vach_lien", "points": [[0, 140], [90, 80], [200, 60]] } ]`. 2–500 đỉnh, mọi đỉnh trong ảnh, đường phải có độ dài > 0.
- Vẽ từ đầu nào cũng được: backend coi hai chiều vẽ là một đường.

#### `mask` — mặt nạ phân vùng (image)

- UI: tô bằng cọ / tẩy trên canvas cùng kích thước ảnh gốc; mỗi vùng chọn một lớp. Các vùng được phép chồng nhau.
- Gửi: `[ { "labelId": "bau_troi", "rle": { "size": [150, 200], "counts": [0, 15000, 15000] } } ]`.
- `rle` là **RLE không nén kiểu COCO**: `size` = `[cao, rộng]` (đúng bằng `metadata.height`, `metadata.width`); `counts` = độ dài các đoạn pixel **0 / 1 xen kẽ, bắt đầu bằng đoạn 0**, duyệt pixel **theo cột** (từ trên xuống, hết cột thì sang cột bên phải). Tổng `counts` phải bằng cao × rộng; chỉ đoạn đầu được dài 0 (khi pixel trên-trái thuộc vùng); mặt nạ phải có ít nhất một pixel.

```js
// mask: Uint8Array dài width * height theo HÀNG (index = y * width + x), khác 0 = thuộc vùng
function maHoaRle(mask, width, height) {
  const counts = [];
  let dangBat = 0, dai = 0;                      // luôn bắt đầu bằng đoạn 0
  for (let x = 0; x < width; x++) {              // duyệt THEO CỘT
    for (let y = 0; y < height; y++) {
      const v = mask[y * width + x] ? 1 : 0;
      if (v === dangBat) dai++;
      else { counts.push(dai); dangBat = v; dai = 1; }
    }
  }
  counts.push(dai);
  return { size: [height, width], counts };      // counts.length === 1 nghĩa là mặt nạ rỗng — đừng gửi
}

function giaiMaRle(rle) {                         // để vẽ lại mặt nạ (màn hình duyệt, kết quả gộp)
  const [height, width] = rle.size;
  const mask = new Uint8Array(width * height);
  let p = 0, bat = 0;
  for (const dai of rle.counts) {
    if (bat) for (let i = p; i < p + dai; i++) mask[(i % height) * width + Math.floor(i / height)] = 1;
    p += dai; bat ^= 1;
  }
  return mask;
}
```

#### `track` — theo dõi đối tượng (video)

- UI: tại một số thời điểm (keyframe) người dùng vẽ khung quanh đối tượng; giữa hai keyframe frontend **nội suy tuyến tính** vị trí và kích thước để hiển thị — backend chấm và gộp theo đúng phép nội suy đó.
- Gửi: `[ { "labelId": "xe", "keyframes": [ { "t": 0, "x": 10, "y": 20, "w": 60, "h": 60 }, { "t": 3.5, "x": 110, "y": 30, "w": 60, "h": 60 } ] } ]`.
- `t` tính bằng **giây từ đầu mẫu** (như `temporalSegment`, xem 6.4), tăng dần, ít nhất **hai** keyframe; khung theo pixel của video (`metadata.width × height`). Đối tượng tồn tại từ keyframe đầu tới keyframe cuối; biến mất rồi xuất hiện lại thì tạo một chuỗi mới.

#### `relation` — quan hệ giữa hai đoạn văn bản (text)

- UI: sau khi đánh dấu các đoạn bằng công cụ `span` (công cụ có `name` = `spanTool`), kéo mũi tên từ đoạn này sang đoạn kia và chọn loại quan hệ.
- Gửi: `[ { "labelId": "lam_viec_tai", "from": { "start": 0, "end": 3 }, "to": { "start": 17, "end": 20 } } ]`.
- Hai đầu là **vị trí `[start, end)` của hai đoạn đã có** trong kết quả công cụ span của cùng nhãn (không dùng số thứ tự: backend sắp xếp lại danh sách đoạn). Quan hệ **có hướng** (`from` → `to`), không nối một đoạn với chính nó, không ghi trùng. Xoá một đoạn thì phải xoá các quan hệ gắn với nó trước khi gửi.

#### `ranking` — xếp hạng nhiều mục (list)

- UI: danh sách `content.items` kéo-thả để sắp xếp.
- Gửi: `{ "order": [2, 0, 3, 1] }` — **chỉ số** mục trong `content.items` (từ 0) theo thứ tự **tốt nhất → kém nhất**. Phải đủ mọi mục (`metadata.itemCount`), mỗi mục đúng một lần.
- Nên xáo thứ tự hiển thị ban đầu để giảm thiên vị, nhưng gửi lên theo chỉ số gốc.

#### Thuộc tính của từng đối tượng (`attributes`)

Công cụ dạng danh sách (trừ `relation`) có thể khai `attributes`; khi đó mỗi đối tượng mang thêm khoá `attributes`:

```json
{ "labelId": "dong_chu", "x": 10, "y": 10, "w": 100, "h": 20,
  "attributes": { "noi_dung": "CẤM ĐỖ XE", "ngon_ngu": "vi", "viet_tay": false, "co_chu": 24 } }
```

- UI: chọn một đối tượng → bảng thuộc tính bên cạnh: `choice` → dropdown `options`; `boolean` → checkbox; `text` → ô nhập (`maxLength`); `number` → ô số.
- Thuộc tính `required: true` phải có trên **mọi** đối tượng (text thì không được rỗng); thuộc tính tùy chọn chưa điền thì **bỏ hẳn khoá**. Không gửi thuộc tính ngoài khai báo.

#### `temporalSegment` — đánh dấu đoạn thời gian (audio, video)

- UI: timeline / waveform, kéo chọn khoảng, chọn lớp.
- Gửi: `[ { "labelId": "nhac", "start": 0, "end": 2.5 } ]`, đơn vị **giây**.
- **Thời gian tính từ đầu ĐOẠN, không phải đầu file** (xem 6.4). Nằm trong `[0, metadata.durationSec]`, `start < end`.

#### `pairwise` — so sánh cặp (pair)

- UI: hai cột A / B (kèm `prompt` nếu có), nút chọn A, B, và "ngang nhau" khi `allowTie: true` (mặc định).
- Gửi: `{ "choice": "a" }` (`"a"` | `"b"` | `"tie"`).

### 6.4 Lưu ý theo loại dữ liệu

| modality | Hiển thị mẫu | Lưu ý |
|---|---|---|
| `image` | `<img src=fileUrl>` hoặc canvas | Dùng `metadata.width/height` làm hệ tọa độ, không dùng kích thước đang hiển thị |
| `text` | `content.text` | Hiển thị đúng chuỗi gốc (giữ khoảng trắng, xuống dòng) để chỉ số `span` khớp |
| `audio` | `<audio src=fileUrl>` + waveform | Nếu `metadata.segmentStart` có giá trị, mẫu là **một đoạn cắt từ file dài** và `fileUrl` là **cả file**. Player phải tua tới `segmentStart` và dừng ở `segmentEnd`; mốc 0 trên timeline = `segmentStart`. Thời gian gửi lên = thời gian player − `segmentStart` |
| `video` | `<video src=fileUrl>` | Giống audio. Công cụ `track`: vẽ khung lên lớp canvas phủ trên video, toạ độ theo `metadata.width × height` |
| `pair` | `content.prompt`, `content.a`, `content.b` | Nên đảo ngẫu nhiên vị trí trái/phải trên màn hình để giảm thiên vị, nhưng **gửi lên theo tên gốc** `a` / `b` |
| `list` | `content.prompt`, `content.items[]` | Mỗi mục là một chuỗi; chỉ số trong mảng là định danh mục. Hiển thị xáo trộn được, gửi lên theo chỉ số gốc |

### 6.5 Kiểm trước ở frontend và chấm câu vàng

- Hình dạng từng loại kết quả có JSON Schema ở [contracts/labeling/tools](../contracts/labeling/tools) (ví dụ `bbox.result.schema.json`). Frontend kiểm trước để báo lỗi sớm; server vẫn kiểm lại tất cả, kèm các luật phụ thuộc mẫu (khung trong ảnh, đoạn trong thời lượng).
- Câu vàng (bài test, kiểm tra chất lượng) chấm **theo từng công cụ**, mọi công cụ đều phải khớp:

| kind | Khớp khi |
|---|---|
| `classification` | Tập lớp giống hệt |
| `bbox` | Mỗi khung đáp án có một khung cùng lớp với IoU ≥ 0,5 |
| `polygon` | IoU vùng ≥ 0,5 |
| `span` | F1 ≥ 1,0 (khớp chính xác) |
| `transcription` | Tỉ lệ lỗi ký tự (CER) ≤ 0,1 |
| `temporalSegment` | IoU thời gian ≥ 0,5 |
| `pairwise` | Cùng lựa chọn |
| `keypoint` | Mỗi điểm đáp án có một điểm cùng lớp lệch không quá **2% đường chéo ảnh** (độ giống = 1 − lệch / đường chéo ≥ 0,98) |
| `polyline` | Độ lệch trung bình dọc đường ≤ 3% đường chéo ảnh (độ giống ≥ 0,97), không phụ thuộc chiều vẽ và số đỉnh |
| `mask` | IoU vùng ≥ 0,5 |
| `track` | IoU không-thời gian ≥ 0,5: lấy mẫu đều 50 thời điểm, tổng diện tích giao / tổng diện tích hợp — lệch vị trí **và** lệch thời điểm bắt đầu / kết thúc đều bị trừ |
| `relation` | F1 ≥ 1,0 trên các quan hệ trùng hết (cùng loại, cùng hai đầu, cùng hướng) |
| `number` | Lệch đáp án ≤ `tolerance` của công cụ |
| `ranking` | Tỉ lệ cặp mục xếp cùng chiều với đáp án (Kendall) ≥ 0,8 — 3 mục thì phải đúng hết, 4 mục được lệch một cặp |
| `freeText` | **Không chấm** (đáp án có ghi cũng bỏ qua) |

Công cụ dạng danh sách: số đối tượng phải **bằng** đáp án, và mỗi đối tượng khớp phải khớp cả các thuộc tính **đáp án có ghi** (`choice` / `boolean` / `number` bằng nhau, `text` có CER ≤ 0,1).

Ngưỡng đổi được bằng `matchThreshold` của công cụ; không khai thì dùng setting `labeling.threshold.<bbox | polygon | span | transcription | temporal_segment | keypoint | polyline | mask | track | relation | ranking>`.

### 6.6 Ví dụ trọn vẹn theo từng bài toán

Mỗi ví dụ đi đủ bốn bước: **(a)** doanh nghiệp đặt tập nhãn (`PUT /projects/{id}/label-schema`), **(b)** một dòng manifest khi nạp dữ liệu, **(c)** phần đáng chú ý của task FE nhận từ `POST /tasks/projects/{id}/next`, **(d)** body FE gửi lên `POST /tasks/assignments/{assignmentId}/submit`, kèm các lỗi hay gặp. Mọi ví dụ (cả các ca sai) đã được chạy qua thư viện kiểm nhãn của backend.

Câu vàng (`expectedPayload`) và câu trả lời bài test đầu vào (`payload`) dùng **đúng hình dạng ở bước (d)**.

#### Ví dụ 1 — Phân loại ảnh (một lớp)

(a) Tập nhãn:

```json
{ "modality": "image",
  "tools": [ { "name": "loai_xe", "kind": "classification", "classes": ["o_to", "xe_may", "xe_tai"] } ] }
```

(b) Manifest: `{ "file": "<key>", "name": "anh-001.jpg" }` (hoặc upload ZIP).

(c) Task nhận được:

```json
{ "modality": "image", "fileUrl": "http://...jpg?X-Amz-...", "content": null,
  "metadata": { "width": 1280, "height": 720 },
  "labelSchema": { "modality": "image", "tools": [ { "name": "loai_xe", "kind": "classification", "required": true,
                   "classes": ["o_to", "xe_may", "xe_tai"], "allowMultiple": false } ] } }
```

Màn hình: ảnh + nhóm radio 3 lựa chọn.

(d) Gửi:

```json
{ "payload": { "loai_xe": { "labelIds": ["xe_may"] } } }
```

| Gửi sai | Kết quả |
|---|---|
| `{"loai_xe":{"labelIds":["o_to","xe_may"]}}` | 400 `nhan_khong_hop_le`: "Cong cu 'loai_xe': phai chon DUNG MOT lop." |
| `{"loai_xe":{"labelIds":["xe_dap"]}}` | 400 `nhan_khong_hop_le`: "lop 'xe_dap' khong co trong tap nhan." |
| `{"labelIds":["xe_may"]}` (quên bọc tên công cụ) | 400 `nhan_sai_dinh_dang` |

**Biến thể — gắn nhiều thẻ** (`"allowMultiple": true`): màn hình dùng checkbox, gửi `{ "the": { "labelIds": ["co_nguoi", "ban_dem"] } }`. Phải chọn ít nhất một thẻ; muốn cho phép "không có thẻ nào" thì thêm một lớp như `"khong_co"`.

#### Ví dụ 2 — Phát hiện đối tượng (khung chữ nhật)

(a)

```json
{ "modality": "image",
  "tools": [ { "name": "doi_tuong", "kind": "bbox", "classes": ["nguoi", "xe"] } ] }
```

(c) Như ví dụ 1, `metadata: { "width": 1280, "height": 720 }`. Màn hình: canvas, chọn lớp rồi kéo khung.

(d) Gửi, tọa độ theo **pixel ảnh gốc**:

```json
{ "payload": { "doi_tuong": [
    { "labelId": "nguoi", "x": 100,   "y": 50,  "w": 80,  "h": 200 },
    { "labelId": "xe",    "x": 400.5, "y": 300, "w": 250, "h": 150 } ] } }
```

Ảnh không có đối tượng nào: gửi `{ "payload": { "doi_tuong": [] } }`. Công cụ bắt buộc nên vẫn phải có khóa, nhưng danh sách được rỗng vì mặc định `minItems: 0`. Muốn bắt có ít nhất một khung thì đặt `"minItems": 1`.

Quy đổi từ tọa độ trên màn hình (ảnh hiển thị thu nhỏ) sang tọa độ gốc:

```js
// rect: khung người dùng vẽ, tính theo pixel của <img> đang hiển thị
const tiLe = task.metadata.width / imgEl.clientWidth;      // ảnh giữ tỉ lệ nên dùng chung một hệ số
const khung = {
  labelId: lopDangChon,
  x: Math.max(0, rect.left * tiLe),
  y: Math.max(0, rect.top * tiLe),
  w: Math.min(rect.width * tiLe, task.metadata.width - rect.left * tiLe),
  h: Math.min(rect.height * tiLe, task.metadata.height - rect.top * tiLe),
};
```

| Gửi sai | Kết quả |
|---|---|
| `{}` (thiếu khóa `doi_tuong`) | 400 `nhan_sai_dinh_dang`: "Required properties [\"doi_tuong\"] are not present" |
| Khung `x: 1200, w: 200` trên ảnh rộng 1280 | 400 `nhan_khong_hop_le`: "khung vuot qua chieu rong anh (1280px)." |
| `w: 0` hoặc số âm | 400 `nhan_sai_dinh_dang` |

#### Ví dụ 3 — Phân vùng (đa giác)

(a)

```json
{ "modality": "image",
  "tools": [ { "name": "vung", "kind": "polygon", "classes": ["mat_duong", "via_he"], "minItems": 1 } ] }
```

(d) Gửi; đa giác tự đóng, không cần lặp lại đỉnh đầu:

```json
{ "payload": { "vung": [
    { "labelId": "mat_duong", "points": [[0, 720], [1280, 720], [1280, 500], [0, 450]] } ] } }
```

| Gửi sai | Kết quả |
|---|---|
| Đa giác chỉ có 2 đỉnh | 400 `nhan_sai_dinh_dang` |
| `{"vung":[]}` khi `minItems: 1` | 400 `nhan_khong_hop_le`: "so doi tuong phai tu 1 den 1000 (dang co 0)." |

#### Ví dụ 4 — Phân loại văn bản (cảm xúc, chủ đề, ý định…)

(a)

```json
{ "modality": "text",
  "tools": [ { "name": "cam_xuc", "kind": "classification", "classes": ["tich_cuc", "tieu_cuc", "trung_tinh"] } ] }
```

(b) Manifest: `{ "text": "Giao hàng chậm, nhân viên thiếu lịch sự.", "name": "review-123" }`.

(c)

```json
{ "modality": "text", "fileUrl": null,
  "content": { "text": "Giao hàng chậm, nhân viên thiếu lịch sự." },
  "metadata": { "length": 40 } }
```

(d) `{ "payload": { "cam_xuc": { "labelIds": ["tieu_cuc"] } } }`

#### Ví dụ 5 — Gắn thực thể trong văn bản (NER)

(a)

```json
{ "modality": "text",
  "tools": [ { "name": "thuc_the", "kind": "span", "classes": ["TEN_NGUOI", "TO_CHUC", "DIA_DIEM"] } ] }
```

(c) `content.text` = `"Chị Lan làm việc tại FPT ở Đà Nẵng."`, `metadata.length` = 35.

(d) Gửi; `end` không tính, `text.slice(start, end)` ra đúng cụm được đánh dấu:

```json
{ "payload": { "thuc_the": [
    { "labelId": "TEN_NGUOI", "start": 0,  "end": 7  },
    { "labelId": "TO_CHUC",   "start": 21, "end": 24 },
    { "labelId": "DIA_DIEM",  "start": 27, "end": 34 } ] } }
```

| start–end | `text.slice(start, end)` |
|---|---|
| 0–7 | `Chị Lan` |
| 21–24 | `FPT` |
| 27–34 | `Đà Nẵng` |

Lấy chỉ số từ vùng người dùng bôi đen: hiển thị văn bản trong **một** phần tử chỉ chứa text (không chèn thẻ con trước khi lấy vị trí), rồi:

```js
const sel = window.getSelection();
const r = sel.getRangeAt(0);
// Đếm số ký tự từ đầu phần tử tới điểm bắt đầu / kết thúc vùng chọn
const truoc = document.createRange();
truoc.selectNodeContents(vanBanEl);
truoc.setEnd(r.startContainer, r.startOffset);
const start = truoc.toString().length;
const end = start + r.toString().length;
```

Dùng **đúng chuỗi server trả về**. Không `trim()`, không chuẩn hóa Unicode (`normalize()`), không đổi `\r\n`. Mọi thay đổi làm lệch chỉ số.

| Gửi sai | Kết quả |
|---|---|
| `"start": 27, "end": 40` (vượt độ dài 35) | 400 `nhan_khong_hop_le`: "doan [27, 40) vuot qua do dai van ban (35)." |
| `"start": 10, "end": 10` | 400 `nhan_khong_hop_le`: "doan [10, 10) rong hoac nguoc." |

#### Ví dụ 6 — Chép lời âm thanh (speech-to-text)

(a)

```json
{ "modality": "audio",
  "tools": [ { "name": "loi_noi", "kind": "transcription", "maxLength": 2000 } ] }
```

(b) Manifest: `{ "file": "<key file .wav/.mp3>", "name": "cuoc-goi-01.wav" }`.

(c) `{ "modality": "audio", "fileUrl": "http://...wav?...", "metadata": { "durationSec": 8 } }`. Màn hình: player + ô nhập nhiều dòng có bộ đếm ký tự.

(d) `{ "payload": { "loi_noi": { "text": "Xin chào, tôi muốn đặt lịch hẹn vào thứ hai." } } }`

Quá `maxLength` → 400 `nhan_khong_hop_le`: "toi da 2000 ky tu."

#### Ví dụ 7 — Đánh dấu đoạn âm thanh, file dài được cắt đoạn

(a) File dài được cắt thành các đoạn 10 giây, mỗi đoạn một task:

```json
{ "modality": "audio", "segmentSeconds": 10,
  "tools": [ { "name": "doan", "kind": "temporalSegment", "classes": ["giong_noi", "nhac", "im_lang"] } ] }
```

(b) Một dòng manifest cho file 25 giây → backend tạo **3 mẫu**: 0–10, 10–20, 20–25.

(c) Task của đoạn thứ hai:

```json
{ "modality": "audio", "fileUrl": "http://...cuoc-goi.wav?...",
  "metadata": { "durationSec": 10, "segmentStart": 10, "segmentEnd": 20, "sourceDurationSec": 25 } }
```

`fileUrl` là **cả file 25 giây**. Player phải phát từ giây 10 đến giây 20:

```js
const bd = task.metadata.segmentStart ?? 0;
const kt = task.metadata.segmentEnd ?? task.metadata.durationSec;
audio.currentTime = bd;
audio.ontimeupdate = () => { if (audio.currentTime >= kt) audio.pause(); };
// Thời gian hiển thị trên timeline và gửi lên = thời gian của file − bd
const thoiGianTrongDoan = audio.currentTime - bd;
```

(d) Người dùng đánh dấu giọng nói từ giây 13,2 đến 17,5 **của file** → gửi `3.2`–`7.5`:

```json
{ "payload": { "doan": [
    { "labelId": "giong_noi", "start": 3.2, "end": 7.5 },
    { "labelId": "nhac",      "start": 7.5, "end": 10  } ] } }
```

Gửi nhầm thời gian của file (`13.2`–`17.5`) → 400 `nhan_khong_hop_le`: "doan ket thuc o 17.5s, vuot qua do dai mau 10s."

Task **không** bị cắt (file ngắn hơn một đoạn, hoặc dự án không khai `segmentSeconds`) thì không có `segmentStart`; mốc 0 là đầu file.

#### Ví dụ 8 — Video: phân loại cảnh + sự kiện theo thời gian

(a) Công cụ sự kiện là tùy chọn:

```json
{ "modality": "video",
  "tools": [
    { "name": "canh", "kind": "classification", "classes": ["ban_ngay", "ban_dem"] },
    { "name": "su_kien", "kind": "temporalSegment", "classes": ["xe_vuot_den"], "required": false } ] }
```

(c) `{ "modality": "video", "fileUrl": "http://...mp4?...", "metadata": { "width": 1920, "height": 1080, "durationSec": 12 } }`. Màn hình: `<video>` + timeline; quy tắc cắt đoạn giống ví dụ 7.

(d) Có sự kiện:

```json
{ "payload": { "canh": { "labelIds": ["ban_ngay"] },
               "su_kien": [ { "labelId": "xe_vuot_den", "start": 2, "end": 4.5 } ] } }
```

Không có sự kiện nào: **bỏ hẳn khóa** công cụ tùy chọn.

```json
{ "payload": { "canh": { "labelIds": ["ban_dem"] } } }
```

#### Ví dụ 9 — So sánh cặp câu trả lời (RLHF)

(a) Chọn câu tốt hơn + đánh giá an toàn:

```json
{ "modality": "pair",
  "tools": [
    { "name": "tot_hon", "kind": "pairwise" },
    { "name": "an_toan", "kind": "classification", "classes": ["an_toan", "khong_an_toan"] } ] }
```

`allowTie` mặc định là `true` (cho chọn "ngang nhau"). Muốn bắt buộc chọn một bên thì khai `"allowTie": false`.

(b) Manifest: `{ "prompt": "Thu do cua Viet Nam la gi?", "a": "Ha Noi.", "b": "Viet Nam co nhieu thanh pho dep." }`.

(c) `{ "modality": "pair", "fileUrl": null, "content": { "prompt": "...", "a": "...", "b": "..." }, "metadata": {} }`

(d) `{ "payload": { "tot_hon": { "choice": "a" }, "an_toan": { "labelIds": ["an_toan"] } } }`

Hai câu ngang nhau: `"choice": "tie"`. Nếu FE đảo vị trí trái/phải để giảm thiên vị, vẫn gửi theo tên gốc `a` / `b` của `content`.

| Gửi sai | Kết quả |
|---|---|
| `"choice": "tie"` khi `allowTie: false` | 400 `nhan_khong_hop_le`: "Cong cu 'tot_hon' khong cho chon hoa." |
| `"choice": "c"` hoặc `"A"` | 400 `nhan_sai_dinh_dang` |

#### Ví dụ 10 — Nhiều công cụ trên một màn hình

Một tập nhãn có thể trộn công cụ, ví dụ phân loại ảnh + khung tùy chọn:

```json
{ "modality": "image",
  "tools": [
    { "name": "loai", "kind": "classification", "classes": ["ngoai_troi", "trong_nha"] },
    { "name": "vat", "kind": "bbox", "classes": ["xe", "nguoi"], "required": false } ] }
```

Gửi chỉ phần đã làm: `{ "payload": { "loai": { "labelIds": ["ngoai_troi"] } } }`. Gửi thêm khóa không có trong tập nhãn (ví dụ `"mau_sac"`) → 400 `nhan_sai_dinh_dang`.

Gợi ý dựng payload chung cho mọi tập nhãn:

```js
function dungPayload(labelSchema, ketQuaTheoCongCu) {
  const payload = {};
  for (const tool of labelSchema.tools) {
    const kq = ketQuaTheoCongCu[tool.name];          // undefined = người dùng chưa làm
    if (kq === undefined) {
      if (tool.required) throw new Error(`Chưa làm: ${tool.name}`);
      continue;                                       // công cụ tùy chọn: bỏ khóa
    }
    payload[tool.name] = kq;
  }
  return { payload };
}
```

#### Ví dụ 11 — OCR: khung + thuộc tính của từng khung

(a)

```json
{ "modality": "image",
  "tools": [ { "name": "chu", "kind": "bbox", "classes": ["dong_chu"],
               "attributes": [
                 { "name": "noi_dung", "type": "text", "required": true, "maxLength": 200 },
                 { "name": "ngon_ngu", "type": "choice", "options": ["vi", "en"] },
                 { "name": "viet_tay", "type": "boolean" },
                 { "name": "co_chu", "type": "number" } ] } ] }
```

(c) Trong `labelSchema` trả về, mỗi thuộc tính đã điền đủ mặc định: `{ "name": "noi_dung", "type": "text", "required": true, "maxLength": 200 }`, `{ "name": "ngon_ngu", "type": "choice", "required": false, "options": ["vi", "en"] }`…

(d) Ảnh 200 × 150:

```json
{ "payload": { "chu": [
    { "labelId": "dong_chu", "x": 10, "y": 10, "w": 100, "h": 20,
      "attributes": { "noi_dung": "CAM DO XE", "ngon_ngu": "vi", "viet_tay": false, "co_chu": 24 } } ] } }
```

Chỉ gửi thuộc tính bắt buộc cũng hợp lệ: `"attributes": { "noi_dung": "CAM DO XE" }`.

| Gửi sai | Kết quả |
|---|---|
| Khung không có `attributes`, hoặc thiếu `noi_dung` | 400 `nhan_khong_hop_le`: "Cong cu 'chu': doi tuong thieu thuoc tinh bat buoc 'noi_dung'." |
| `"ngon_ngu": "fr"` | 400 `nhan_khong_hop_le`: "thuoc tinh 'ngon_ngu': 'fr' khong nam trong cac lua chon." |
| `"viet_tay": "co"` | 400 `nhan_khong_hop_le`: "thuoc tinh 'viet_tay' phai la true / false." |
| `"mau": "do"` (không khai trong tập nhãn) | 400 `nhan_khong_hop_le`: "thuoc tinh 'mau' khong co trong tap nhan." |
| `"noi_dung": ["a"]` (không phải chuỗi / số / true-false) | 400 `nhan_sai_dinh_dang` |

Kết quả gộp của khung này (mục 4.11): toạ độ lấy trung vị; `noi_dung` lấy bản chép "ở giữa"; `ngon_ngu` / `viet_tay` lấy giá trị quá bán, không có thì bỏ; `co_chu` lấy trung vị.

#### Ví dụ 12 — Điểm mốc khuôn mặt

(a)

```json
{ "modality": "image",
  "tools": [ { "name": "khuon_mat", "kind": "keypoint", "classes": ["mat_trai", "mat_phai", "mui"], "minItems": 1 } ] }
```

(d) Ảnh 200 × 150:

```json
{ "payload": { "khuon_mat": [
    { "labelId": "mat_trai", "x": 80, "y": 60 }, { "labelId": "mat_phai", "x": 120, "y": 60 }, { "labelId": "mui", "x": 100.5, "y": 85 } ] } }
```

| Gửi sai | Kết quả |
|---|---|
| `{"labelId":"mui","x":250,"y":60}` | 400 `nhan_khong_hop_le`: "diem (250, 60) nam ngoai chieu rong anh." |
| `{"labelId":"tai","x":10,"y":60}` | 400 `nhan_khong_hop_le`: "lop 'tai' khong co trong tap nhan." |
| `"khuon_mat": []` khi `minItems: 1` | 400 `nhan_khong_hop_le`: "so doi tuong phai tu 1 den 1000 (dang co 0)." |
| Điểm thiếu `y` | 400 `nhan_sai_dinh_dang` |

Hai người chấm cùng một điểm được coi là trùng khi lệch nhau không quá 2% đường chéo ảnh (ảnh 200 × 150: 5 pixel; ảnh 1920 × 1080: ~44 pixel). Cần chặt / lỏng hơn thì đặt `matchThreshold` (0,99 = 1% đường chéo).

#### Ví dụ 13 — Làn đường (đường gấp khúc)

(a)

```json
{ "modality": "image",
  "tools": [ { "name": "lan_duong", "kind": "polyline", "classes": ["vach_lien", "vach_dut"] } ] }
```

(d)

```json
{ "payload": { "lan_duong": [
    { "labelId": "vach_lien", "points": [[0, 140], [90, 80], [200, 60]] },
    { "labelId": "vach_dut", "points": [[40, 149], [120, 90]] } ] } }
```

| Gửi sai | Kết quả |
|---|---|
| Đường một đỉnh | 400 `nhan_sai_dinh_dang` |
| `"points": [[5,5],[5,5]]` | 400 `nhan_khong_hop_le`: "duong co do dai bang 0 (cac diem trung nhau)." |
| Đỉnh `[90, 200]` trên ảnh cao 150 | 400 `nhan_khong_hop_le`: "diem (90, 200) nam ngoai chieu cao anh." |

#### Ví dụ 14 — Phân vùng bằng mặt nạ

(a)

```json
{ "modality": "image",
  "tools": [ { "name": "vung", "kind": "mask", "classes": ["bau_troi", "mat_duong"] } ] }
```

(d) Ảnh 200 × 150 (30.000 pixel): nửa trái là bầu trời (cột 0–99 = 15.000 pixel đầu khi duyệt theo cột), nửa phải là mặt đường:

```json
{ "payload": { "vung": [
    { "labelId": "bau_troi",  "rle": { "size": [150, 200], "counts": [0, 15000, 15000] } },
    { "labelId": "mat_duong", "rle": { "size": [150, 200], "counts": [15000, 15000] } } ] } }
```

Mã hoá / giải mã: hàm `maHoaRle` / `giaiMaRle` ở mục 6.3.

| Gửi sai | Kết quả |
|---|---|
| `"size": [200, 150]` (đảo rộng / cao) | 400 `nhan_khong_hop_le`: "mat na 150x200 khac kich thuoc anh 200x150." |
| `"counts": [0, 15000, 14000]` | 400 `nhan_khong_hop_le`: "tong counts phai bang so pixel cua anh (30000)." |
| `"counts": [100, 0, 29900]` | 400 `nhan_khong_hop_le`: "counts khong o dang chuan: chi doan dau tien duoc dai 0." |
| `"counts": [30000]` (mặt nạ rỗng) | 400 `nhan_sai_dinh_dang` |

#### Ví dụ 15 — Theo dõi đối tượng trong video

(a)

```json
{ "modality": "video",
  "tools": [ { "name": "doi_tuong", "kind": "track", "classes": ["xe", "nguoi"],
               "attributes": [ { "name": "bi_che", "type": "boolean" } ] } ] }
```

(c) `"metadata": { "width": 320, "height": 240, "durationSec": 12 }`.

(d) Ba keyframe; giữa các keyframe khung được nội suy tuyến tính:

```json
{ "payload": { "doi_tuong": [
    { "labelId": "xe",
      "keyframes": [ { "t": 0, "x": 10, "y": 20, "w": 60, "h": 60 },
                     { "t": 3.5, "x": 110, "y": 30, "w": 60, "h": 60 },
                     { "t": 6, "x": 200, "y": 40, "w": 60, "h": 60 } ],
      "attributes": { "bi_che": false } } ] } }
```

| Gửi sai | Kết quả |
|---|---|
| Chuỗi chỉ có một keyframe | 400 `nhan_khong_hop_le`: "chuoi khung can it nhat hai moc thoi gian." |
| Keyframe `t: 1` đứng sau `t: 3` | 400 `nhan_khong_hop_le`: "moc thoi gian phai tang dan (moc 1s dung sau 3s)." |
| `t: 20` trên video 12 giây | 400 `nhan_khong_hop_le`: "moc 20s vuot qua do dai mau 12s." |
| `x: 300, w: 60` trên video rộng 320 | 400 `nhan_khong_hop_le`: "khung o moc 6s vuot qua chieu rong video (320px)." |

#### Ví dụ 16 — Trích xuất quan hệ giữa các thực thể

(a)

```json
{ "modality": "text",
  "tools": [
    { "name": "thuc_the", "kind": "span", "classes": ["NGUOI", "TO_CHUC", "DIA_DIEM"] },
    { "name": "quan_he", "kind": "relation", "classes": ["lam_viec_tai", "dat_tai"], "spanTool": "thuc_the", "required": false } ] }
```

`spanTool` trỏ tới công cụ không tồn tại hoặc không phải `span` → 400 `tap_nhan_khong_hop_le`: "spanTool 'khong_co' phai la mot cong cu span trong tap nhan."

(c) `"content": { "text": "Lan lam viec tai FPT o Ha Noi" }, "metadata": { "length": 29 }`.

(d)

```json
{ "payload": {
    "thuc_the": [ { "labelId": "NGUOI", "start": 0, "end": 3 }, { "labelId": "TO_CHUC", "start": 17, "end": 20 }, { "labelId": "DIA_DIEM", "start": 23, "end": 29 } ],
    "quan_he":  [ { "labelId": "lam_viec_tai", "from": { "start": 0, "end": 3 }, "to": { "start": 17, "end": 20 } },
                  { "labelId": "dat_tai", "from": { "start": 17, "end": 20 }, "to": { "start": 23, "end": 29 } } ] } }
```

Không có quan hệ nào thì bỏ khoá `quan_he` (công cụ tùy chọn) hoặc gửi `[]`.

| Gửi sai | Kết quả |
|---|---|
| `"to": {"start":17,"end":21}` (không phải một đoạn đã đánh dấu) | 400 `nhan_khong_hop_le`: "dau 'to' [17, 21) khong phai mot doan trong cong cu 'thuc_the'." |
| `from` và `to` cùng một đoạn | 400 `nhan_khong_hop_le`: "quan he phai noi HAI doan khac nhau." |
| Ghi hai lần cùng một quan hệ | 400 `nhan_khong_hop_le`: "quan he bi ghi trung." |

#### Ví dụ 17 — Xếp hạng nhiều câu trả lời + chấm điểm

(a)

```json
{ "modality": "list",
  "tools": [
    { "name": "xep_hang", "kind": "ranking" },
    { "name": "diem_tot_nhat", "kind": "number", "integer": true, "min": 1, "max": 5, "tolerance": 1 } ] }
```

(b) Manifest: `{ "prompt": "Cau tra loi nao tot nhat?", "items": ["Tra loi A", "Tra loi B", "Tra loi C", "Tra loi D"] }`.

(c) `"modality": "list", "fileUrl": null, "content": { "prompt": "...", "items": ["Tra loi A", "Tra loi B", "Tra loi C", "Tra loi D"] }, "metadata": { "itemCount": 4 }`.

(d) "C tốt nhất, rồi A, D, B; câu tốt nhất được 4 điểm":

```json
{ "payload": { "xep_hang": { "order": [2, 0, 3, 1] }, "diem_tot_nhat": { "value": 4 } } }
```

| Gửi sai | Kết quả |
|---|---|
| `"order": [2, 0, 3]` | 400 `nhan_khong_hop_le`: "phai xep du 4 muc (dang co 3)." |
| `"order": [2, 0, 3, 4]` | 400 `nhan_khong_hop_le`: "muc 4 khong ton tai (mau co 4 muc, danh so tu 0)." |
| `"order": [2, 0, 2, 1]` (lặp mục) | 400 `nhan_sai_dinh_dang` |
| `"value": 4.5` | 400 `nhan_khong_hop_le`: "Cong cu 'diem_tot_nhat': phai la so nguyen." |
| `"value": 6` | 400 `nhan_khong_hop_le`: "gia tri 6 lon hon muc toi da 5." |
| `"value": "4"` | 400 `nhan_sai_dinh_dang` |

Với `"tolerance": 1`, ba người chấm 4, 5, 4 cho kết quả gộp 4 (trung vị) và cả ba đều tính là khớp.

#### Ví dụ 18 — Mô tả ảnh (câu trả lời mở) + chép chữ trong ảnh

(a)

```json
{ "modality": "image",
  "tools": [
    { "name": "mo_ta", "kind": "freeText", "maxLength": 300 },
    { "name": "chu_trong_anh", "kind": "transcription", "required": false } ] }
```

(d)

```json
{ "payload": { "mo_ta": { "text": "Mot tam bien bao mau do dat ben duong." }, "chu_trong_anh": { "text": "CAM DO XE" } } }
```

| Gửi sai | Kết quả |
|---|---|
| `"mo_ta": {"text": "   "}` | 400 `nhan_khong_hop_le`: "Cong cu 'mo_ta': van ban khong duoc rong." |
| `mo_ta` dài 301 ký tự | 400 `nhan_khong_hop_le`: "Cong cu 'mo_ta': toi da 300 ky tu." |

Ở màn hình kết quả, `chu_trong_anh` có `final` (bản chép "ở giữa"), còn `mo_ta` có `method: "none"` — hiển thị mô tả của từng người từ `labels`.

**Biến thể — đánh giá câu trả lời của LLM** (dữ liệu `pair`): trộn so sánh cặp, thang điểm và ô viết lại:

```json
{ "modality": "pair",
  "tools": [ { "name": "tot_hon", "kind": "pairwise" },
             { "name": "diem_a", "kind": "number", "integer": true, "min": 1, "max": 5 },
             { "name": "viet_lai", "kind": "freeText", "required": false } ] }
```

Gửi: `{ "payload": { "tot_hon": { "choice": "a" }, "diem_a": { "value": 5 }, "viet_lai": { "text": "Thu do cua Phap la Paris." } } }`.

---

## 7. Admin

```http
GET  /projects/pending-approval                 → [ ProjectResponse ]
POST /projects/{id}/approve                     → running
POST /projects/{id}/reject   { "reason": "..." } → cancelled, ledger hoàn ký quỹ

GET  /annotations/appeals?page=1&pageSize=20     → hàng đợi khiếu nại (AnnotationResponse)
POST /annotations/{id}/appeal/resolve  { "accept": true, "note": "..." }   → chấp nhận = duyệt và trả tiền; bác = từ chối vĩnh viễn

GET  /ledger/admin/reconciliation
→ { "healthy": true, "entryCount", "grandTotal": 0, "unbalancedEntries": [], "balanceMismatches": [], "negativeAccounts": [], "brokenChain": [] }
```

`healthy: false` là sự cố nghiêm trọng về tiền: hiện cảnh báo đỏ trên dashboard admin.

### 7.1 Setting hệ thống

Mọi con số nghiệp vụ (phí nền tảng, tự duyệt, hạn mức nạp / rút, thời hạn, giới hạn file…) là **setting** admin sửa được lúc đang chạy. Đổi có hiệu lực cho **thao tác mới** (dự án đã publish giữ phí cũ…).

```http
GET /admin/settings
→ [ { "key": "fee.platform_percent", "group": "Phi va tien", "type": "int", "unit": "%",
      "min": 0, "max": 90, "effect": "newOperations", "description": "...",
      "defaultValue": 30, "value": 30, "version": 1, "updatedAt": "...", "updatedBy": null, "choices": null }, ... ]   (115 mục)

GET /admin/settings/{key}
PUT /admin/settings/{key}   { "value": 20, "reason": "Khuyen mai thang 10" }   → SettingResponse (version + 1)
GET /admin/settings/{key}/history
→ [ { "version": 2, "oldValue": 30, "newValue": 20, "changedBy": "...", "changedAt": "...", "reason": "..." } ]
```

Gợi ý màn hình: nhóm theo `group`, mỗi dòng một ô nhập theo `type`:

| `type` | Ô nhập | Gửi `value` |
|---|---|---|
| `bool` | công tắc | `true` / `false` |
| `int`, `long` | số nguyên, giới hạn `min`–`max`, hiện `unit` | số |
| `double` | số thực | số |
| `durationSeconds` | số giây (nên hiển thị đổi sang phút / giờ / ngày) | số giây |
| `text` | ô văn bản nhiều dòng | chuỗi |
| `text` có `choices` (mảng, khác `null`) | **danh sách chọn** từ `choices` (vd `quality.redundancy_policy`: `majority` / `posterior` / `voi`; `ledger.gate_payout_mode`: `perClick` / `batched`) | một phần tử của `choices`; ngoài danh sách → 400 |

- `effect: "restart"`: hiện ghi chú "có tác dụng khi service khởi động lại".
- Hiện `defaultValue` cạnh giá trị đang dùng, có nút "về mặc định" (PUT lại `defaultValue`).
- Lỗi: **400** `gia_tri_khong_hop_le` (sai kiểu / ngoài `min`–`max`, thông báo nói rõ), **400** `thieu_gia_tri`, **409** `xung_dot_setting` (vd hạn mức nạp tối thiểu lớn hơn tối đa), **404** `khong_tim_thay`.

Các setting tự duyệt đáng làm nổi bật trên màn hình:

| Key | Mặc định | Ý nghĩa |
|---|---|---|
| `project.auto_approve` | `false` | Dự án ký quỹ xong là chạy luôn, không vào hàng đợi duyệt |
| `annotation.auto_approve_agreed` | `false` | Nhãn khớp đồng thuận được hệ thống duyệt và trả tiền |
| `ledger.withdraw_auto_approve` / `ledger.withdraw_auto_approve_max_vnd` | `true` / 2.000.000 | Lệnh rút ≤ ngưỡng tự gửi đi; lớn hơn thì vào hàng đợi 7.2 |
| `payment.manual_transfer_enabled` / `payment.manual_transfer_auto_approve_max_vnd` | `true` / 0 | Bật kênh chuyển khoản; ngưỡng tự xác nhận (0 = luôn chờ admin) |
| `fee.platform_percent` | 30 | Phí nền tảng cộng trên đơn giá |

### 7.2 Hàng đợi duyệt rút tiền

```http
GET  /ledger/admin/withdrawals?state=pendingApproval&page=1&pageSize=20     (mặc định state = pendingApproval, cũ nhất trước)
→ { "items": [ { "id", "labelerId", "amountVnd", "taxVnd", "netVnd", "bankAccount", "state",
                 "createdAt", "reviewedBy", "reviewedAt", "failureReason" } ], "page", "pageSize", "total" }
POST /ledger/admin/withdrawals/{id}/approve                         → state requested (gửi chuyển khoản)
POST /ledger/admin/withdrawals/{id}/reject  { "reason": "..." }     → state rejected, tiền về ví labeler
```

Duyệt / từ chối lệnh không còn chờ → **409** `lenh_rut_khong_cho_duyet` (admin khác vừa xử lý — tải lại danh sách). Từ chối thiếu lý do → **400** `thieu_ly_do`.

### 7.3 Hàng đợi đối chiếu nạp tiền chuyển khoản

```http
GET  /payments/admin/deposits?status=awaitingApproval&page=1&pageSize=20     (mặc định awaitingApproval)
→ { "items": [ DepositResponse ], "page", "pageSize", "total" }
POST /payments/admin/deposits/{intentId}/approve  { "bankTxnRef": "FT26100812345" }   → succeeded, ledger cộng ví
POST /payments/admin/deposits/{intentId}/reject   { "reason": "..." }                 → rejected
```

- Admin tìm giao dịch trong sao kê theo `transferCode` và `amountVnd`, nhập **mã giao dịch ngân hàng** vào `bankTxnRef` (tùy chọn nhưng nên có). Một mã chỉ dùng cho một lệnh: dùng lại → **409** `ma_giao_dich_trung`.
- Duyệt được cả lệnh `pending` (tiền đã về nhưng doanh nghiệp chưa bấm "đã chuyển").

### 7.4 Quản lý tài khoản, khoá / mở khoá

```http
GET  /users/admin?search=&role=&status=&page=1&pageSize=20      (mới tạo trước)
→ { "items": [ { "userId", "email", "displayName", "roles": ["business"], "status": "active",
                 "blockedAt": null, "blockedReason": null, "kybStatus": "verified", "createdAt" } ],
    "page", "pageSize", "total" }
POST /users/admin/{userId}/block    { "reason": "Gian lận nhãn" }   → AdminUserResponse (status blocked)
POST /users/admin/{userId}/unblock                                 → AdminUserResponse (status active)
```

- `search`: tìm trong email và tên hiển thị (không phân biệt hoa thường). `role`: `business` / `labeler` / `sharer` / `admin`. `status`: `active` / `blocked`.
- `kybStatus` chỉ có ở tài khoản doanh nghiệp: `draft` / `submitted` / `verified` / `rejected`, `null` nếu chưa mở hồ sơ.
- Khoá có hiệu lực **ngay**: đăng nhập trả 403, refresh token bị thu hồi, access token đang dùng bị gateway trả 401 `phien_da_bi_thu_hoi` (mục 2.6); nhận task và rút tiền bị chặn. Người bị khoá nhận email có lý do (thông báo trong ứng dụng thấy khi được mở khoá).
- Khoá / mở khoá lại tài khoản đã ở trạng thái đó: trả 200, không làm gì. Không ghi lý do → **400** `thieu_ly_do`. Khoá admin → **409** `khong_khoa_admin`.
- Tài khoản cũng có thể bị khoá khi admin xác nhận hồ sơ gian lận kèm khoá (mục 7.6).

### 7.5 Phân xử tranh chấp (FM-06)

> **Đã thay bằng [mục 18](#18-dự-án-làm-trung-tâm-kiến-trúc-mục-318)** (dự án làm trung tâm). Phần dưới mô tả API cũ, giữ lại để đối chiếu.

```http
GET  /ledger/admin/disputes?status=open&page=1&pageSize=20     (cũ nhất trước)
→ { "items": [ DisputeResponse (mục 4.14) ], "page", "pageSize", "total" }
POST /ledger/admin/disputes/{id}/resolve   { "outcome": "refundBusiness" | "payLabeler", "note": "Khung sai ro rang" }
→ DisputeResponse (state refundedBusiness | paidLabeler)
→ 400 ket_cuc_khong_hop_le | thieu_ghi_chu   |   409 tranh_chap_da_xu
```

Màn hình phân xử nên hiện nhãn (`GET /annotations/{annotationId}` + lịch sử `.../history`), lý do của doanh nghiệp, số tiền. Cả hai bên nhận thông báo kết quả (`tranh_chap`, email bắt buộc).

### 7.6 Hồ sơ nghi gian lận (FM-07)

Hệ thống **không tự khoá ai** — chỉ mở hồ sơ để admin xem.

| `type` | Khi nào mở | `evidence` (ví dụ) |
|---|---|---|
| `qua_nhanh` | Trong 20 lượt gần nhất ở một dự án, ≥ 50 % làm nhanh hơn max(2 giây, 25 % trung vị thời gian làm của dự án) | `fastCount`, `windowSize`, `thresholdSeconds`, `projectMedianSeconds`, `recentWorkSeconds` |
| `mot_nhan_lien_tuc` | 30 câu phân loại liên tiếp cùng một lớp, trong khi người khác ở dự án chọn đa dạng | `answer`, `streak`, `otherLabelersDistinctAnswers` |
| `chung_ip` / `chung_thiet_bi` | ≥ 3 tài khoản nộp từ cùng IP công khai / cùng `X-Device-Id` trong 1 ngày | `client_ip` / `device_id`, `accounts`, `windowSeconds` |

Ngưỡng là setting `quality.fraud_*` (mục 7.1).

```http
GET  /quality/admin/fraud-cases?status=open|dismissed|confirmed&type=&page=1&pageSize=20   (mới cập nhật trước)
→ { "items": [ { "id", "type", "projectId", "userIds": ["..."], "evidence": { ... },
                 "status": "open", "createdAt", "updatedAt", "resolvedAt": null, "note": null, "blocked": false } ], ... }
GET  /quality/admin/fraud-cases/{id}
→ như trên + "users": [ { "userId", "submissions": 120, "medianWorkSeconds": 1.4, "reputation": 35 } ]
POST /quality/admin/fraud-cases/{id}/resolve   { "action": "dismiss" | "confirm", "note": "...", "blockAccounts": true }
→ hồ sơ sau khi xử lý   |   400 hanh_dong_khong_hop_le | thieu_ghi_chu   |   409 ho_so_da_xu_ly
```

- `confirm` + `blockAccounts: true` → mọi tài khoản trong `userIds` bị khoá (như mục 7.4, lý do = `note`).
- Hồ sơ cùng đối tượng (cùng người + dự án, cùng IP…) **cộng dồn** vào hồ sơ đang mở, không mở trùng. Hồ sơ vừa xử lý thì 7 ngày sau mới mở lại (`quality.fraud_reopen_cooldown`).

### 7.7 Nhật ký hệ thống và báo cáo (FP-06, FM-09)

admin-svc ghi lại **các thao tác nghiệp vụ quan trọng** (ai làm gì, lúc nào: tài khoản, KYB, dự án, nạp / rút, tranh chấp, gian lận, setting…) — giữ 365 ngày (`admin.audit_retention_days`). Sự kiện phát sinh theo **từng lượt nộp / từng nhãn / từng click** (`annotation.submitted`, `annotation.approved`, `consensus.reached`, `funds.held`…) **không lưu chi tiết** ở đây (danh sách: setting `admin.audit_excluded_types`) nhưng vẫn được **đếm** cho báo cáo bên dưới; muốn xem lịch sử một nhãn thì dùng `GET /annotations/{id}/history`.

```http
GET /admin/audit?actorId=&eventType=project.published&correlationId=&from=2026-10-01T00:00:00Z&to=&page=1&pageSize=50
→ { "items": [ { "eventId", "eventType", "producer": "project-svc", "occurredAt", "correlationId", "causationId",
                 "actorId", "actorRole": "business", "payload": { ... } } ], "page", "pageSize", "total" }
```

- Mới nhất trước. `correlationId`: mọi sự kiện sinh ra từ cùng một thao tác của người dùng (ví dụ publish → ký quỹ → sinh task) — dùng để xem "thao tác này kéo theo những gì".
- Payload lớn hơn 64 KB bị cắt (`payload` = `{ "_truncated": true, "bytes": 81234 }`).
- Lọc `eventType=annotation.submitted` (hoặc loại khác trong `admin.audit_excluded_types`) chỉ ra các bản ghi **cũ** từ trước khi loại đó bị loại trừ — không phải lỗi.

```http
GET /admin/stats/overview?from=2026-10-01&to=2026-10-31          (ngày giờ Việt Nam, mặc định 30 ngày)
→ { "from", "to", "auditSince": "2026-10-12T...",
    "newUsers", "businessesVerified", "projectsPublished",
    "annotationsSubmitted", "annotationsApproved", "annotationsRejected",
    "depositsVnd", "labelerPayoutsVnd", "platformFeeVnd", "withdrawalsVnd",
    "disputesOpened", "accountsBlocked", "fraudCasesConfirmed",
    "daily": [ { "date": "2026-10-05", "newUsers", "annotationsSubmitted", "annotationsApproved", "depositsVnd" } ] }
```

Số liệu lấy từ bảng đếm theo ngày (mọi sự kiện đều được đếm, kể cả loại không lưu chi tiết) nên **chỉ có từ `auditSince`** (lúc bắt đầu ghi) — hiện ghi chú "số liệu từ ngày …" khi `from` sớm hơn.

### 7.8 Thông báo toàn hệ thống (FM-09)

```http
POST /admin/announcements   { "title": "Bảo trì 22h", "body": "Hệ thống bảo trì 22h–23h tối nay.", "audience": "all", "sendEmail": false }
→ 200 { "id", "title", "body", "audience", "sendEmail", "createdBy", "createdAt" }
→ 400 tieu_de_khong_hop_le | noi_dung_khong_hop_le | doi_tuong_khong_hop_le
GET  /admin/announcements?limit=20      (mới nhất trước)
```

- `audience`: `all` / `business` / `labeler`. Người nhận thấy thông báo loại `he_thong` (mục 14) sau vài giây.
- `sendEmail: true` thì gửi thêm email cho người **không tắt** email loại `he_thong`. Dùng tiết kiệm.

---

## 8. Dữ liệu cập nhật trễ: khi nào phải hỏi lại

Các service trao đổi với nhau qua hàng đợi sự kiện, nên một số thay đổi **xuất hiện ở service khác sau vài trăm ms đến vài giây**. Backend chỉ đẩy realtime **thông báo** (WebSocket, mục 17.9); trạng thái của từng màn hình (dự án, dataset, task, số dư…) không được đẩy, nên frontend hỏi lại (polling) có giới hạn số lần. Kết quả của các việc chạy nền (dự án được duyệt, nhãn bị từ chối, rút tiền xong…) cũng được gửi thành **thông báo** (mục 14) — màn hình đang mở thì polling, còn người dùng ở trang khác thấy ngay qua chuông thông báo (đẩy qua WebSocket). Chưa có push lên thiết bị (Firebase / Web Push): đóng tab thì chỉ còn email:

| Sau khi | Chờ thấy ở | Cách làm |
|---|---|---|
| `POST /publish` | `GET /projects/{id}` chuyển `pendingApproval` hoặc về `draft` | Hỏi lại mỗi 1–2 giây, tối đa ~30 giây |
| `POST /datasets/manifest` | `GET /datasets` có `status` `ready` / `failed` | Mỗi 2 giây; lô lớn có thể vài phút, cho người dùng rời trang |
| Admin duyệt dự án, labeler `join` | `POST /tasks/.../next` hết trả 403 | Gặp 403 `khong_phai_thanh_vien` / `du_an_chua_san_sang` / `du_an_khong_chay` ngay sau hành động đó thì thử lại 2–3 lần, cách 1 giây |
| Nộp nhãn | Nhãn xuất hiện trong `GET /annotations/...` | Thường < 1 giây |
| Mẫu đủ người gán | `consensusAgrees` trên nhãn; mẫu tranh chấp được mở lại cho thêm người | Vài giây (đi qua quality-svc) |
| Chạy Dawid–Skene | `krippendorffAlpha`, `dawidSkeneSkill`, uy tín cập nhật | Theo lô 15 phút, hoặc ngay khi admin chạy tay |
| Duyệt nhãn | `GET /ledger/me/balance` của labeler tăng `pendingVnd` | Vài giây |
| Thanh toán nạp tiền | `deposits/{id}` → `succeeded`, rồi số dư tăng | Mỗi 2–3 giây |
| Rút tiền | `withdrawals/mine` → `completed` / `failed` | Mỗi vài giây. Lệnh `pendingApproval` chờ người duyệt: không polling, hỏi lại khi mở trang |
| Admin duyệt rút / nạp chuyển khoản | Số dư người dùng đổi | Vài giây |
| Admin đổi setting | Mọi service áp dụng | Thường < 1 giây; service đang tắt sẽ nhận khi bật lại |
| Admin duyệt KYB | `POST /projects/{id}/publish` hết trả 409 `chua_xac_minh_doanh_nghiep` | **Làm mới token** (`POST /auth/refresh`) khi nhận thông báo "đã xác minh" hoặc khi `GET /kyb/me` chuyển `verified` — có hiệu lực ngay với token mới |
| Xác thực email | Rút tiền / publish hết trả 409 `chua_xac_thuc_email` | **Làm mới token** ngay sau `POST /auth/verify-email` — có hiệu lực ngay với token mới |
| Nhãn được duyệt | `GET /me/level` tăng `approvedCount`, có thể lên cấp / có huy hiệu | Vài giây |
| Đổi mật khẩu, đăng xuất mọi nơi, bị khoá | Token cũ bị 401 `phien_da_bi_thu_hoi` | Vài trăm ms |
| Bất kỳ sự kiện nào ở trên | Thông báo mới | Đẩy qua WebSocket (mục 17.9); `unread-count` hỏi thưa làm dự phòng (mục 14) |

---

## 9. Mã lỗi

Danh sách các `code` thường gặp để frontend dịch. Mã không có trong bảng: hiện thông báo chung kèm `traceId`.

| code | HTTP | Ý nghĩa |
|---|---|---|
| `khong_tim_thay` | 404 | Không tồn tại hoặc không có quyền |
| `khong_co_quyen` | 403 | Sai vai trò |
| `xung_dot_dong_thoi` | 409 | Người khác vừa sửa cùng dữ liệu, tải lại rồi thử lại |
| `thieu_idempotency_key` | 400 | Thiếu header `Idempotency-Key` |
| `loai_du_lieu_khong_hop_le` | 400 | `modality` không thuộc 5 loại |
| `tap_nhan_sai_dinh_dang` / `tap_nhan_khong_hop_le` | 400 | Tập nhãn sai hình dạng / sai luật |
| `loai_du_lieu_khong_khop` | 400 | `modality` của tập nhãn khác của dự án |
| `ngan_sach_khong_hop_le`, `don_gia_khong_hop_le`, `thieu_deadline`, `deadline_da_qua`, `tran_redundancy_khong_hop_le` | 400 | Cấu hình giá sai |
| `ti_le_cau_vang_khong_hop_le` | 400 | `goldCheckPercent` ngoài 0–50 |
| `zip_chi_cho_anh`, `khong_phai_zip`, `zip_qua_lon` | 409 / 400 | Upload ZIP sai |
| `duoi_file_khong_hop_le`, `qua_nhieu_file`, `dung_luong_khong_hop_le` | 400 | Xin link upload sai |
| `manifest_khong_hop_le`, `manifest_qua_dai` | 400 | Manifest sai |
| `mau_khong_thuoc_du_an`, `da_la_cau_vang`, `qua_nhieu_cau_vang` | 400 / 409 | Câu vàng sai |
| `thieu_nhan` | 400 | Thiếu trường `payload` |
| `nhan_sai_dinh_dang` | 400 | Nhãn sai hình dạng (thiếu công cụ bắt buộc, thừa khóa, sai kiểu) |
| `nhan_khong_hop_le` | 400 | Nhãn sai nghĩa (lớp không có, khung ra ngoài ảnh, đoạn vượt thời lượng…) |
| `lease_het_han`, `lease_khong_con` | 409 | Lượt giữ task đã hết hạn / đã nộp / đã bỏ qua |
| `khong_phai_thanh_vien`, `bi_chan_khoi_du_an`, `du_an_khong_chay`, `du_an_chua_san_sang`, `da_qua_deadline`, `chua_du_cap_do`, `chua_du_uy_tin`, `khong_mo_kenh_chuyen_nghiep`, `tai_khoan_bi_khoa` | 403 | Không được nhận task |
| `can_lam_test`, `du_an_rieng_tu`, `da_la_thanh_vien`, `bi_chan`, `can_xin_duyet` | 409 | Không tham gia được |
| `chua_du_cap_do`, `chua_du_uy_tin`, `khong_phai_labeler`, `tai_khoan_bi_khoa` | 409 | (project-svc) Không đủ điều kiện vào dự án — `join`, mã mời, đơn, bài test, chủ dự án mời / duyệt |
| `ma_moi_het_han`, `ma_moi_het_luot`, `ma_moi_da_thu_hoi` | 409 | Mã mời không dùng được nữa (thu hồi lần hai cũng `ma_moi_da_thu_hoi`) |
| `han_ma_moi_khong_hop_le`, `so_luot_khong_hop_le`, `ghi_chu_qua_dai` | 400 | Tạo mã mời sai (hạn 1 giờ … `project.invite_code_max_ttl`, lượt 1–100.000, ghi chú ≤ 200) |
| `da_co_don_dang_cho`, `chua_duoc_xin_lai`, `khong_can_duyet`, `don_da_xu_ly` | 409 | Đơn xin tham gia: đã có đơn chờ / chưa hết thời gian chờ / dự án public / đơn đã duyệt, từ chối hoặc rút |
| `loi_nhan_qua_dai`, `ly_do_qua_dai` | 400 | Lời nhắn / lý do từ chối > 500 ký tự |
| `thieu_thong_tin`, `qua_nhieu_nguoi`, `khong_the_them_owner` | 400 | Mời thành viên: thiếu role, gửi cả userId lẫn email, vượt `project.member_batch_max`, role owner |
| `da_duyet`, `tu_duyet` | 409 | Duyệt nhãn đã duyệt / tự duyệt nhãn của mình |
| `khong_the_khieu_nai`, `da_khieu_nai`, `qua_han_khieu_nai` | 409 | Không khiếu nại được |
| `khong_du_so_du` | 409 | Rút quá số dư khả dụng |
| `duoi_muc_toi_thieu` | 400 | Rút dưới mức tối thiểu (thông báo có mức hiện tại) |
| `so_tien_nap_khong_hop_le` | 400 | Nạp ngoài hạn mức |
| `chuyen_khoan_tat` | 409 | Admin đang tắt nạp chuyển khoản thủ công |
| `lenh_nap_da_chot`, `khong_phai_chuyen_khoan` | 409 | Lệnh nạp đã xong / không phải lệnh chuyển khoản |
| `ma_giao_dich_trung` | 409 | (admin) Mã sao kê đã dùng cho lệnh nạp khác |
| `lenh_rut_khong_cho_duyet` | 409 | (admin) Lệnh rút đã được xử lý |
| `thieu_ly_do` | 400 | Từ chối (nhãn / lệnh rút / lệnh nạp) phải có lý do |
| `gia_tri_khong_hop_le`, `thieu_gia_tri` | 400 | (admin) Giá trị setting sai kiểu / ngoài giới hạn |
| `url_khong_hop_le`, `ten_mien_bi_chan`, `alias_khong_hop_le`, `mat_khau_khong_hop_le`, `het_han_trong_qua_khu`, `qua_nhieu_url` | 400 | Tạo link sai |
| `alias_da_ton_tai`, `chien_dich_da_ton_tai`, `link_da_ngung` | 409 | Tạo / sửa link xung đột |
| `khong_duoc_chia_se_link`, `chua_xac_thuc` | 403 / 401 | Tài khoản không phải labeler (doanh nghiệp / admin không chia sẻ link) / không có token, API key sai hoặc đã bị thay |
| `ma_gioi_thieu_sai`, `tu_gioi_thieu`, `qua_han_nhap_ma`, `da_co_nguoi_gioi_thieu`, `gioi_thieu_vong` | 404 / 409 | Nhập mã giới thiệu không được |
| `link_khong_ton_tai` | 404 | Trang vượt link: link không tồn tại / chưa duyệt xong / hết hạn / bị vô hiệu hoá |
| `sai_mat_khau`, `turnstile_that_bai` | 403 | Trang vượt link: mật khẩu sai / xác minh chống bot thất bại |
| `chua_het_dem_nguoc`, `da_nop` | 409 | Nộp trước khi hết đếm ngược / nộp trùng |
| `thieu_cau_tra_loi`, `nhan_sai_dinh_dang`, `nhan_khong_hop_le` | 400 | Thiếu câu trả lời / nhãn sai (phiên vẫn còn, sửa rồi nộp lại) |
| `phien_het_han`, `token_da_dung` | 410 | Bộ câu hết hạn / token mở link đã dùng: tải lại trang |
| `token_khong_hop_le` | 403 | Token mở link sai hoặc hết hạn (3 phút) |
| `xung_dot_setting` | 409 | (admin) Setting mâu thuẫn với setting khác (min > max) |
| `dinh_dang_chua_ho_tro`, `coco_chi_cho_khung_anh` | 400 | Định dạng xuất không hợp lệ |
| `chua_xac_minh_doanh_nghiep` | 409 | Publish khi hồ sơ KYB chưa được duyệt |
| `ten_doanh_nghiep_khong_hop_le`, `ma_so_thue_khong_hop_le`, `dia_chi_khong_hop_le` | 400 | Khai hồ sơ KYB sai (mã số thuế: 10 chữ số, chi nhánh `0123456789-001`) |
| `file_khong_hop_le`, `file_qua_lon` | 400 | Giấy phép không phải pdf/png/jpg hoặc quá dung lượng |
| `ho_so_chua_du`, `thieu_giay_phep` | 409 | Nộp KYB khi chưa khai đủ / chưa tải giấy phép lên |
| `ho_so_dang_cho_duyet`, `da_xac_minh` | 409 | Sửa / nộp lại hồ sơ đang chờ duyệt hoặc đã xác minh |
| `khong_cho_duyet` | 409 | (admin) Duyệt / từ chối hồ sơ không ở trạng thái chờ |
| `khong_khoa_admin` | 409 | (admin) Khoá tài khoản admin |
| `trang_thai_khong_hop_le` | 400 | (admin) Tham số `status` sai |
| `loai_bat_buoc`, `loai_khong_hop_le`, `thieu_tuy_chon` | 400 | Tắt email loại bắt buộc / loại không tồn tại / thiếu `items` |
| `email_da_ton_tai` | 409 | Đăng ký email đã có |
| `sai_thong_tin` | 401 | Đăng nhập sai email hoặc mật khẩu |
| `tai_khoan_bi_khoa` | 403 | Đăng nhập / nhận task khi tài khoản bị khoá |
| `phien_khong_hop_le` | 401 | Refresh token sai / hết hạn / đã thu hồi — về trang đăng nhập |
| `phien_da_bi_thu_hoi` | 401 | Access token bị thu hồi (mục 2.6) — về trang đăng nhập, không refresh |
| `qua_nhieu_yeu_cau` | 429 | Vượt giới hạn tần suất, chờ `Retry-After` giây |
| `du_lieu_khong_hop_le` | 400 | Đăng ký sai dữ liệu (`errors` theo trường) |
| `ma_khong_hop_le` | 400 | Link xác thực email / đặt lại mật khẩu sai, đã dùng hoặc hết hạn |
| `da_xac_thuc`, `gui_qua_nhanh` | 409 | Gửi lại email xác thực khi đã xác thực / chưa đủ 60 giây |
| `mat_khau_hien_tai_sai`, `mat_khau_khong_hop_le` | 400 | Đổi mật khẩu sai mật khẩu cũ / mật khẩu mới sai độ dài |
| `chua_xac_thuc_email` | 409 | Rút tiền / publish khi chưa xác thực email |
| `ten_khong_hop_le`, `gioi_thieu_qua_dai` | 400 | Sửa hồ sơ sai (tên 1–100 ký tự, giới thiệu ≤ 500) |
| `file_khong_hop_le`, `file_qua_lon`, `key_khong_hop_le`, `chua_tai_anh` | 400 / 409 | Ảnh đại diện sai loại / quá lớn / key không phải của mình / chưa tải lên |
| `linh_vuc_khong_hop_le`, `ngon_ngu_khong_hop_le`, `loai_du_lieu_khong_hop_le` | 400 | Kỹ năng sai |
| `ky_khong_hop_le`, `id_khong_hop_le`, `qua_nhieu_id` | 400 | Bảng xếp hạng / hồ sơ công khai sai tham số |
| `nhap_khong_hop_le`, `nhap_qua_lon` | 400 | Lưu nháp sai |
| `ly_do_khong_hop_le`, `thieu_ghi_chu`, `ghi_chu_qua_dai` | 400 | Báo lỗi task / phân xử thiếu ghi chú |
| `hanh_dong_khong_hop_le`, `con_nguoi_dang_lam` | 400 / 409 | Xử lý báo lỗi task / hồ sơ gian lận sai hành động; loại task đang có người làm |
| `khung_chi_cho_anh` | 400 | Xuất YOLO / VOC cho dự án không phải ảnh có khung |
| `khong_con_treo`, `da_tranh_chap` | 409 | Tranh chấp khi tiền đã về ví labeler / nhãn đã có tranh chấp |
| `ket_cuc_khong_hop_le`, `tranh_chap_da_xu` | 400 / 409 | (admin) Phân xử sai kết cục / đã phân xử |
| `nhom_khong_hop_le`, `khoang_ngay_khong_hop_le` | 400 | Thống kê sai `groupBy` / khoảng ngày (tối đa 366 ngày) |
| `ho_so_da_xu_ly` | 409 | (admin) Hồ sơ gian lận đã xử lý |
| `tieu_de_khong_hop_le`, `noi_dung_khong_hop_le`, `doi_tuong_khong_hop_le` | 400 | (admin) Thông báo toàn hệ thống sai |
| `link_khong_hop_le` | 403 | Link xem file sai / bị sửa (mục 16) |
| `link_het_han` | 410 | Link xem file quá 5 phút — lấy link mới từ API đã trả nó |
| `khong_co_file`, `anh_hong` | 404 / 409 | File mẫu không còn trong kho / ảnh hỏng không gắn được watermark |
| `anh_khong_hop_le`, `kich_thuoc_khong_hop_le`, `file_qua_lon` | 400 / 413 | (admin) Giải mã watermark: file không phải ảnh / width–height sai / ảnh > 20 MB |

---

## 10. Giới hạn

Các con số dưới đây là **giá trị mặc định** — admin đổi được lúc đang chạy (mục 7.1, key ghi trong ngoặc). Frontend chỉ nên dùng chúng để gợi ý trước; luật cuối cùng là lỗi 400 / 413 backend trả về (thông báo luôn kèm giới hạn hiện tại).

| Mục | Giới hạn mặc định |
|---|---|
| Access token / refresh token | 15 phút / 14 ngày (`identity.access_token_lifetime`, `identity.refresh_token_lifetime`) — dùng `expiresIn` trong response đăng nhập, đừng hard-code |
| Mật khẩu | 8–128 ký tự (`identity.password_min_length`, `identity.password_max_length`) |
| Link xem file (`fileUrl`) | 5 phút (`storage.view_link_ttl`) |
| Link upload (`uploadUrl`) | 1 giờ (`upload.link_ttl`) — dùng `expiresAt` trong response |
| Lượt giữ task | 15 phút (`task.lease_duration`) — dùng `expiresAt`; một task mỗi dự án mỗi người |
| Bài test đầu vào | 30 phút mỗi lần, tối đa 3 lần (`entrance.duration`, `entrance.max_attempts`) |
| Khiếu nại | 1 lần mỗi nhãn, trong 7 ngày sau khi bị từ chối (`annotation.appeal_window`) — dùng cờ `canAppeal` |
| Tên dự án / mô tả | 200 / 5.000 ký tự (`project.name_max_length`, `project.description_max_length`) |
| File ZIP | 200 MB (`dataset.zip_max_bytes`) |
| Xin link upload | 100 file mỗi lần, mỗi file ≤ 5 GB (`upload.max_files`, `upload.max_file_bytes`) |
| Manifest | `rows` ≤ 1.000 dòng; file `.jsonl` ≤ 50.000 dòng; ảnh qua manifest ≤ 50 MB (`dataset.manifest_inline_max_rows`, `dataset.manifest_file_max_rows`, `dataset.image_max_bytes`) |
| Văn bản một mẫu | 100.000 ký tự (`dataset.text_max_chars`) |
| Nạp tiền | 10.000 – 500.000.000đ mỗi lần (`payment.deposit_min_vnd`, `payment.deposit_max_vnd`) |
| Rút tiền | tối thiểu 50.000đ; thuế 10% từ 2.000.000đ (`ledger.withdraw_*`) |
| Duyệt hàng loạt nhãn khớp | 500 nhãn mỗi lần bấm (`annotation.bulk_approve_max`) |
| Trang vượt link | Đếm ngược 8 giây (`gate.countdown`); 1 câu vàng + 2 câu thật (`gate.gold_per_session`, `gate.real_per_session`); bộ câu sống 10 phút (`gate.session_ttl`); token mở link 3 phút, dùng một lần (`gate.token_ttl`) |
| Link rút gọn | Mã 7 ký tự (`link.code_length`); alias 3–32 ký tự `[A-Za-z0-9_-]`; mật khẩu 4–100 ký tự; hàng loạt 100 URL (`link.bulk_max`) |
| Tập nhãn | ≤ 20 công cụ; ≤ 100 lớp mỗi công cụ; tên lớp ≤ 50 ký tự |
| Mỗi công cụ dạng danh sách | ≤ 1.000 mục (mặc định); polygon 3–500 đỉnh; polyline 2–500 đỉnh; ≤ 20 thuộc tính mỗi công cụ, thuộc tính `text` ≤ 5.000 ký tự |
| Mặt nạ (`mask`) | ≤ 200 mặt nạ mỗi nhãn; mỗi mặt nạ ≤ 100.000 đoạn; tổng ≤ 200.000 đoạn mỗi công cụ; ảnh ≤ 20.000 pixel mỗi chiều |
| Theo dõi (`track`) | ≤ 500 chuỗi mỗi nhãn; mỗi chuỗi 2–2.000 keyframe |
| Xếp hạng (`ranking`) | Mẫu danh sách 2–50 mục |
| Câu vàng | ≤ 2.000 mỗi dự án (`project.gold_items_max`) |
| Giấy phép kinh doanh (KYB) | `.pdf` / `.png` / `.jpg`, ≤ 10 MB (`identity.kyb_file_max_bytes`) |
| Phân trang | `pageSize` ≤ 100 |
| Giới hạn tần suất (gateway, mỗi phút) | Mọi API 600 / người dùng; xác thực 20 / IP; tạo lệnh nạp / rút 30 (`gateway.rate_limit_per_minute`, `..._auth_per_minute`, `..._money_per_minute`) |
| Link xác thực email / đặt lại mật khẩu | 1 ngày / 30 phút, dùng một lần (`identity.email_verify_ttl`, `identity.password_reset_ttl`); gửi lại email xác thực cách nhau ≥ 60 giây |
| Ảnh đại diện | `.png` / `.jpg` / `.webp`, ≤ 2 MB (`identity.avatar_max_bytes`) |
| Hồ sơ | Tên hiển thị ≤ 100 ký tự; giới thiệu ≤ 500 ký tự; mỗi nhóm kỹ năng ≤ 20 mục, mỗi mục ≤ 50 ký tự |
| Bản nháp | ≤ 256 KB mỗi lượt giữ task |
| Báo lỗi task | 3 người báo → task tự huỷ (`task.report_auto_exclude`) |
| Bảng xếp hạng / hồ sơ công khai | ≤ 100 người mỗi lần |
| Link xem file mẫu | 5 phút (`storage.view_link_ttl`) |

---

## 11. Backend chưa có / cần lưu ý

| Mục | Ảnh hưởng tới frontend |
|---|---|
| Upload thẳng lên MinIO | Chỉ **tải lên** còn đi thẳng MinIO (xem file thì qua media-svc, mục 16). Link upload trỏ tới `http://localhost:9000` ở dev. Trình duyệt báo lỗi CORS khi `PUT` thì cấu hình MinIO cho phép origin của frontend (biến môi trường `MINIO_API_CORS_ALLOW_ORIGIN`) |
| Thông báo đẩy chỉ cho thông báo | WebSocket chỉ đẩy **thông báo** (mục 17.9); trạng thái từng màn hình (tiến độ dự án, số dư…) vẫn hỏi lại theo mục 8 |
| Chưa xác minh tự động với cơ quan thuế | Admin tự đối chiếu giấy phép và mã số thuế (mục 13.2). Hồ sơ đã xác minh không sửa được qua API |
| `freeText` không gộp tự động | Câu trả lời mở không có một đáp án đúng: màn hình kết quả hiện câu trả lời của từng người (`labels`), không có `final` |
| Chưa có: hình khối 3D / point cloud, quan hệ giữa các khung trên ảnh, xếp hạng một phần (top-k) | Không làm UI cho các dạng này. Khung xoay, track ẩn / hiện lại, xuất MOT / COCO keypoints đã có (mục 17.6) |
| Cổng vượt link chỉ cho công cụ làm nhanh | Danh sách là setting `gate.allowed_tools` (mục 17.5) |
| Hồ sơ người gán trên màn hình duyệt | Gom các `labelerId` trên trang rồi gọi **một lần** `GET /users/public?ids=...` (mục 15) để hiện tên, ảnh, cấp độ |
| Rate limit đếm theo từng instance gateway | Chạy nhiều gateway thì hạn mức thực tế cao hơn — không ảnh hưởng FE |

---

## 12. Cổng link: người chia sẻ và trang vượt link

Kênh thu thập thứ hai (đặc tả 2.4), giống các dịch vụ rút gọn link kiếm tiền kiểu link4m. **Người chia sẻ** là **labeler** (không cần vai trò riêng; tài khoản cũ có vai trò `sharer` vẫn dùng được): rút gọn link và chia sẻ. Doanh nghiệp và admin **không** chia sẻ link (`403 khong_duoc_chia_se_link`): doanh nghiệp không được nhận tiền từ chính ngân sách mình chi, admin là bên kiểm duyệt link. Tài khoản vừa doanh nghiệp vừa labeler vẫn chia sẻ được, nhưng trang vượt link của họ **không bao giờ phát câu của dự án do chính họ sở hữu**. **Khách vãng lai** mở link, giải 1–3 câu gán nhãn nhỏ (có câu vàng) thì được chuyển tới link đích. Người chia sẻ được trả tiền theo lượt vượt hợp lệ, vào **cùng ví** với thu nhập labeler (mục 5.7, rút tiền như labeler).

### 12.1 Người chia sẻ: tạo và quản lý link

```http
POST /links
{ "url": "https://example.com/bai-viet", "alias": "bai-viet-1", "password": "tuy-chon", "expiresAt": "2027-01-01T00:00:00Z", "campaignId": null }
→ 201 { "id", "code": "bai-viet-1", "shortUrl": "http://localhost:5173/g/bai-viet-1", "destinationUrl", "domain",
        "status": "pendingScan", "statusReason": null, "hasPassword": true, "expiresAt", "campaignId", "createdAt" }
```

- Mọi trường trừ `url` đều tuỳ chọn. Không có `alias` thì backend sinh mã ngẫu nhiên.
- `shortUrl` = `Links:ShortBaseUrl` + `code`, trỏ vào **trang vượt link của frontend** (`/g/{code}`, stack demo `http://localhost:5173/g/`). Trang đó gọi `GET /g/{code}` của gateway. Sao chép thẳng `shortUrl`, không tự ghép.
- `status`:

| `status` | Ý nghĩa | Hiển thị |
|---|---|---|
| `pendingScan` | Đang kiểm duyệt link đích (thường vài giây). Chưa chia sẻ được | "Đang kiểm tra", hỏi lại `GET /links/{id}` mỗi 2 giây |
| `active` | Đang chạy | Nút sao chép `shortUrl` |
| `blocked` | Link đích độc hại / tên miền bị chặn — vĩnh viễn (`statusReason`) | Báo lỗi, gợi ý tạo link khác |
| `disabled` | Người dùng xoá, hoặc admin vô hiệu hoá vì vi phạm (`statusReason`) | |

- Link đích **không sửa được** (muốn đổi thì tạo link mới). `PUT /links/{id}` chỉ đổi `{ "changePassword": true, "password": null|"...", "expiresAt", "campaignId" }`.
- `DELETE /links/{id}` → 204, link ngừng chạy.
- `GET /links?campaignId=&status=&page=&pageSize=` — danh sách của tôi, mới nhất trước.
- Chiến dịch: `POST /links/campaigns {"name"}`, `GET /links/campaigns` → `[ { "id", "name", "linkCount", "createdAt" } ]`.
- Hàng loạt (FS-03): `POST /links/bulk {"urls":[...], "campaignId"?}` → mỗi URL một dòng `{ "url", "link": LinkResponse | null, "errorCode", "error" }`. Dòng lỗi không làm hỏng dòng khác.

**API token (FS-05), API rút gọn và Quick Link (FS-02)** — cho script, website, plugin trình duyệt:

```http
POST /links/api-key            → { "apiKey": "lk_…", "prefix": "lk_3f9a1b", "createdAt" }   ← key đầy đủ CHỈ hiện lần này
GET  /links/api-key            → { "apiKey": null, "prefix", "createdAt" }  |  404 nếu chưa tạo
POST /links           (header X-Api-Key: lk_…)        ← cùng body như trên, không cần token
GET  /links/quick?api=lk_…&url=https://...&alias=...  → text/plain "http://localhost:5173/g/aB3xY9k"
GET  /links/api-shorten?api=lk_…&url=https://...&alias=...
     → 200 {"status":"success","shortenedUrl":"http://localhost:5173/g/aB3xY9k"}
     → 4xx {"status":"error","code":"url_khong_hop_le","message":"..."}   (401 chua_xac_thuc, 400 url_khong_hop_le /
           ten_mien_bi_chan, 409 alias_da_ton_tai; vượt hạn mức gateway → 429 ProblemDetails qua_nhieu_yeu_cau)
```

`api-shorten` có cùng hình dạng với API của các dịch vụ link kiếm tiền (link4m...), nên code PHP / script có sẵn chỉ cần đổi URL. Lỗi vẫn mang mã HTTP thật: PHP `file_get_contents` cần `ignore_errors` để đọc được JSON lỗi. Trang "API rút gọn" của frontend có ví dụ PHP / cURL / JS / Python.

Tạo lại key thì key cũ hết hiệu lực ngay. Hiện key một lần kèm nút sao chép và cảnh báo "lưu lại, sẽ không hiện lần nữa".

**Giới thiệu (FS-08):**

```http
GET  /links/referrals/me       → { "code": "Ab3dE9xY", "referredCount": 2, "referredBy": null }
POST /links/referrals/claim    { "code": "Ab3dE9xY" }   → 204
```

- Link mời có thể là `https://<frontend>/dang-ky?ref=<code>`. Sau khi đăng ký và đăng nhập, frontend gọi `claim`.
- Chỉ tài khoản **mới** (mặc định trong 7 ngày kể từ đăng ký) nhập được mã. Lỗi: `tu_gioi_thieu`, `da_co_nguoi_gioi_thieu`, `qua_han_nhap_ma`, `ma_gioi_thieu_sai`.
- Người giới thiệu nhận 10% doanh thu cổng link của người được mời, lấy từ phần nền tảng. Chỉ bắt đầu sau khi người được mời tự kiếm đủ ngưỡng (mặc định 50.000đ).

**Thống kê (FS-07)** — đọc từ ClickHouse; doanh thu là **ước tính**, số chính thức ở ví (`GET /ledger/me/transactions`):

```http
GET /gate/stats/me/daily?from=2026-10-01&to=2026-10-31     (mặc định 30 ngày gần nhất, tối đa 366 ngày)
→ [ { "date": "2026-10-09", "views": 120, "submits": 80, "paidClicks": 55, "estimatedRevenueVnd": 15400 } ]
GET /gate/stats/me/top-links?limit=10   → [ { "key": "<linkId>", "views", "paidClicks", "estimatedRevenueVnd" } ]
GET /gate/stats/me/sources              → key = tên miền referrer ("" = truy cập trực tiếp)
GET /gate/stats/me/outcomes             → key ∈ tinhTien | truotCauVang | trungIp | tuVuot | hetNganSach | khongCoCauHoi
```

Thống kê có độ trễ khoảng 1–2 giây.

### 12.2 Trang vượt link (khách vãng lai, không đăng nhập)

Đường dẫn công khai: `/g/{code}` là trang của frontend. Frontend gọi các API sau qua gateway:

```http
GET /g/{code}
→ { "code", "requiresPassword": false, "countdownSeconds": 8, "turnstileEnabled": true, "turnstileSiteKey": "1x00000000000000000000AA" }

POST /g/{code}/sessions   { "turnstileToken": "<token widget Turnstile>", "password": "<nếu requiresPassword>" }
→ { "sessionId", "answerableAt", "expiresAt",
    "labelSchema": { "modality": "text", "tools": [ { "name": "loai", "kind": "classification", "classes": ["cho","meo"], ... } ] },
    "questions": [ { "sampleId", "modality": "text", "fileUrl": null, "content": { "text": "..." }, "metadata": { "length": 16 } }, ... ] }

POST /g/sessions/{sessionId}/submit   { "answers": { "<sampleId>": { "loai": { "labelIds": ["cho"] } }, ... } }
→ đạt:   { "passed": true,  "redirectUrl": "/go/{code}?t=…", "redirectExpiresAt" }
→ trượt: { "passed": false, "newSession": { …như trên… } }

GET /go/{code}?t=…    → 302 tới link đích
```

Luồng giao diện:

1. Gọi `GET /g/{code}`. 404 `link_khong_ton_tai` → trang "link không tồn tại hoặc đã bị gỡ". `requiresPassword` → ô mật khẩu.
2. Hiện **Cloudflare Turnstile** với `turnstileSiteKey` (thư viện `https://challenges.cloudflare.com/turnstile/v0/api.js`). Dev dùng khoá test, widget luôn đạt. Có token rồi mới gọi `POST /sessions`.
3. Bắt đầu đếm ngược tới `answerableAt` (giờ server). Trong lúc đếm, hiện câu hỏi và banner. Nộp sớm → 409 `chua_het_dem_nguoc`, nên khoá nút cho tới khi hết giờ.
4. Vẽ từng câu bằng **đúng quy ước workspace** (mục 6): `labelSchema` + `content` / `fileUrl` + `metadata`. Cổng chỉ dùng phân loại **chọn một** và so sánh cặp, giao diện nên là nút bấm lớn, làm một tay được. Câu vàng và câu thật **trộn lẫn**, không có cờ nào phân biệt — frontend không cần và không được đoán.
5. Phải trả lời **mọi** câu. 400 (`thieu_cau_tra_loi`, `nhan_sai_dinh_dang`) → phiên còn nguyên, sửa rồi nộp lại.
6. `passed: false` → thay bằng `newSession`, kèm thông báo "Trả lời chưa chính xác, thử lại" (bộ mới có đếm ngược riêng).
7. `passed: true` → nút **"Lấy link"** điều hướng trình duyệt tới `redirectUrl` (gateway trả 302 tới link đích). Token sống 3 phút và **dùng một lần**: mở lại → 410 `token_da_dung`, khi đó cho vượt lại.
8. `questions: []` → không có câu hỏi (hết ngân sách, hoặc không có dự án phù hợp). Chỉ đếm ngược rồi nộp `{"answers":{}}` để lấy link.
9. Nút **"Báo cáo link vi phạm"** (không cần đăng nhập): `POST /links/r/{code}/report {"reason":"..."}` → 202. Mỗi IP chỉ tính một lần.

Lượt vượt chỉ sinh tiền khi đạt câu vàng, không phải chính chủ link (đăng nhập hoặc cùng IP lúc tạo link), không trùng IP trong 24 giờ, và dự án còn ngân sách cổng. Frontend **không** hiển thị điều này cho khách; người chia sẻ xem ở `outcomes`.

### 12.3 Doanh nghiệp: bật cổng link cho dự án

- Wizard: `PUT /projects/{id}/channels { "allowProfessional": true, "allowLinkGateway": true, "allowCollaborative": false }`.
- Dự án chỉ lên cổng khi dữ liệu là **ảnh / văn bản / cặp**, mọi công cụ là **phân loại chọn một** hoặc **so sánh cặp**, và có câu vàng mục đích `qualityCheck`. Nên cảnh báo ở wizard nếu bật cổng mà không thoả.
- Cổng **chỉ tiêu phần ngân sách vượt** mức ký quỹ tối thiểu (`estimatedCostVnd` ở `readiness`). Ví dụ tối thiểu 7.800đ, đặt 20.000đ → cổng được tiêu 12.200đ. Gợi ý ở bước giá: "Ngân sách cho cổng link = ngân sách − ký quỹ tối thiểu".
- Mỗi nhãn từ cổng tốn ký quỹ `đơn giá + phí` (như nhãn chuyên nghiệp). Nhãn hiện ở màn hình duyệt với `source: "linkGateway"` (mục 4.10).

### 12.4 Admin: kiểm duyệt link

```http
GET  /links/admin/review-queue?page=&pageSize=   → { "items": [ { "link": LinkResponse, "ownerId", "reportCount", "recentReasons": [...] } ], ... }
POST /links/admin/{id}/disable  { "reason": "Lừa đảo", "withholdRevenue": true }   → link disabled; doanh thu đang treo bị giữ lại
POST /links/admin/{id}/dismiss-reports                                            → bỏ khỏi hàng đợi
GET  /links/admin/blocked-domains
POST /links/admin/blocked-domains  { "domain": "casino.com", "reason": "Cờ bạc" } → { ..., "disabledLinkCount": 3 }
DELETE /links/admin/blocked-domains/{domain}
```

- `withholdRevenue` (mặc định `true`) giữ lại mọi khoản **đang treo** của link: doanh thu người chia sẻ và hoa hồng giới thiệu. Khoản đã hết thời gian treo thì không thu hồi được.
- Chặn tên miền thì chặn cả tên miền con, và vô hiệu hoá ngay mọi link đang chạy tới đó (có giữ doanh thu).

---

## 13. Xác minh doanh nghiệp (KYB)

Doanh nghiệp phải được admin xác minh mới **publish** được dự án; mọi bước khác (tạo dự án, tập nhãn, nạp dữ liệu, nạp tiền) làm được ngay sau khi đăng ký. Tài khoản seed `doanhnghiep*@crowd.local` đã được xác minh sẵn.

```
draft ──nộp──► submitted ──duyệt──► verified   (khoá, không sửa được nữa)
  ▲               │
  │            từ chối (kèm lý do)
  └── sửa ◄── rejected
```

### 13.1 Doanh nghiệp: khai hồ sơ và nộp

```http
GET /kyb/me
→ { "businessId", "status": "draft", "companyName": "", "taxCode": "", "address": "",
    "hasLicense": false, "licenseFileName": null, "rejectReason": null, "submittedAt": null, "reviewedAt": null }

PUT /kyb/me   { "companyName": "Công ty TNHH ABC", "taxCode": "0312345678", "address": "Hà Nội" }   → KybResponse

POST /kyb/me/license   { "fileName": "giay-phep.pdf", "sizeBytes": 245000 }
→ { "uploadUrl": "http://localhost:9000/kyb-docs/...", "method": "PUT", "expiresAt": "..." }
PUT <uploadUrl>   (body = nội dung file, gửi thẳng lên kho file — không qua gateway, không kèm Authorization)

POST /kyb/me/submit   → KybResponse { "status": "submitted" }
```

- Màn hình: một form (tên, mã số thuế, địa chỉ) + ô chọn file giấy phép + nút "Nộp hồ sơ". Thứ tự gọi: `PUT /kyb/me` → `POST /kyb/me/license` → `PUT <uploadUrl>` → `POST /kyb/me/submit`.
- Tải file mới thì giấy phép cũ được thay. `hasLicense: true` nghĩa là đã xin link; nộp mà file chưa thực sự lên kho → **409** `thieu_giay_phep`.
- `submitted`: hiện "Đang chờ duyệt", khoá form (sửa → **409** `ho_so_dang_cho_duyet`).
- `rejected`: hiện `rejectReason`, mở lại form để sửa và nộp lại.
- `verified`: hiện huy hiệu "Đã xác minh". Kết quả duyệt / từ chối đến qua thông báo + **email bắt buộc** (mục 14).

### 13.2 Admin: hàng đợi duyệt

```http
GET  /kyb/admin?status=submitted&page=1&pageSize=20     (mặc định submitted, nộp sớm nhất trước)
→ { "items": [ { "profile": KybResponse, "email", "displayName", "accountStatus": "active", "licenseUrl": null } ],
    "page", "pageSize", "total" }
GET  /kyb/admin/{businessId}      → như trên, có "licenseUrl" (link xem giấy phép, sống 5 phút)
POST /kyb/admin/{businessId}/approve                          → KybResponse { "status": "verified" }
POST /kyb/admin/{businessId}/reject  { "reason": "Giấy phép bị mờ" }   → KybResponse { "status": "rejected" }
```

- `status` lọc được cả `verified` / `rejected` / `draft` để tra cứu.
- Mở chi tiết mới có `licenseUrl`; link hết hạn thì gọi lại API chi tiết. Ảnh hiện bằng `<img>`, PDF bằng `<iframe>` hoặc mở tab mới.
- Duyệt / từ chối hồ sơ không còn chờ (admin khác vừa xử lý) → **409** `khong_cho_duyet`. Từ chối thiếu lý do → **400** `thieu_ly_do`.
- Sau khi duyệt, doanh nghiệp phải **làm mới token** mới publish được (mục 2.3) — phía doanh nghiệp tự làm khi thấy `GET /kyb/me` là `verified`.

**Dọn dữ liệu dự án đã đóng (admin, hiếm dùng).** Hệ thống tự dọn dữ liệu vận hành của dự án đã hoàn thành / huỷ sau `project.closed_data_retention` (mặc định 90 ngày; đặt 0 để tắt). Muốn dọn ngay một dự án:

```http
POST /tasks/admin/projects/{projectId}/purge
→ 200 { "projectId", "purgedAt", "purgedNow": true, "deletedTasks": 120, "deletedAssignments": 240, "deletedReports": 0, "deletedGoldSamples": 5 }
POST /quality/admin/projects/{projectId}/archive
→ 200 { "projectId", "purgedAt", "purgedNow": true, "deleted": { "labels": 240, "consensus_rounds": 120, "task_targets": 120, "ds_state": 1 } }
→ 409 du_an_chua_dong (dự án chưa hoàn thành / huỷ) | 404 khong_tim_thay
```

Gọi lại lần hai trả `purgedNow: false` (không xoá gì thêm). Nhãn đã duyệt, kết quả, tiền, mẫu và uy tín labeler **không** bị xoá; tiến độ và báo cáo chất lượng vẫn xem được dưới dạng bản chụp (`frozenAt` — mục 4.9, `archivedAt` — mục 4.12).

---

## 14. Thông báo trong ứng dụng và email

notification-svc ghi lại các sự kiện liên quan tới người dùng thành **thông báo trong ứng dụng** (luôn có) và gửi **email** theo tuỳ chọn. Thông báo mới được **đẩy realtime qua WebSocket** khi người dùng đang mở web (mục 17.9); chưa có push lên thiết bị (Firebase / Web Push).

```http
GET  /notifications/me/unread-count                        → { "count": 3 }
GET  /notifications/me?unreadOnly=false&page=1&pageSize=20 (mới nhất trước)
→ { "items": [ { "id", "type": "du_an", "title": "Dự án đã được duyệt",
                 "body": "Dự án đã được duyệt và đang chạy — labeler bắt đầu nhận task.",
                 "link": "/projects/3f2c...", "createdAt", "readAt": null } ],
    "page", "pageSize", "total", "unreadCount": 3 }
POST /notifications/me/{id}/read    → 204   (đọc rồi vẫn 204; thông báo của người khác → 404)
POST /notifications/me/read-all     → { "updated": 3 }
```

Cách làm khuyến nghị:

- Chuông trên thanh điều hướng: gọi `unread-count` khi tải trang, rồi mở WebSocket (mục 17.9) — mỗi khung `notification` thì tăng số chưa đọc / hiện toast. Mất kết nối thì xin vé mới, mở lại và gọi lại `unread-count`. Giữ polling `unread-count` **thưa** (vd 5 phút, dừng khi `document.hidden`) làm dự phòng. Mở danh sách thì gọi `GET /notifications/me`.
- `title` và `body` đã được dịch theo ngôn ngữ người dùng chọn lúc tạo thông báo (`vi` / `en`, mục 17.9), hiện nguyên văn. `link` là **đường dẫn tài nguyên của backend** (`/projects/{id}`, `/kyb/me`, `/ledger/me`, `/ledger/withdrawals`) — frontend ánh xạ sang route màn hình tương ứng; `null` thì không có nút "Xem".
- Bấm vào một thông báo: `POST .../{id}/read` rồi điều hướng theo `link`.

**Loại thông báo** (`type`) và email:

| `type` | Khi nào | Người nhận | Email |
|---|---|---|---|
| `kyb_ket_qua` | Hồ sơ KYB được duyệt / bị từ chối | Doanh nghiệp | **Bắt buộc** |
| `tai_khoan` | Tài khoản bị khoá / mở khoá | Mọi vai trò | **Bắt buộc** |
| `rut_tien` | Rút tiền thành công / thất bại / admin từ chối lệnh rút | Labeler, sharer | **Bắt buộc** |
| `nap_tien` | Nạp tiền thành công | Doanh nghiệp | Bật (tắt được) |
| `du_an` | Dự án được duyệt, publish không được (thiếu tiền, bị từ chối), bị huỷ (có ký quỹ), hoàn ký quỹ | Doanh nghiệp | Bật (tắt được) |
| `nhan_tu_choi` | Nhãn bị từ chối, khiếu nại bị bác | Labeler | Bật (tắt được) |
| `nhan_duyet` | Nhãn được duyệt (chỉ kênh chuyên nghiệp — kênh cổng link không báo từng nhãn). **Gộp** theo dự án | Labeler | Tắt (bật được) |
| `tien_kha_dung` | Tiền treo chuyển sang khả dụng (với sharer: mỗi lượt click / mỗi lô). **Gộp** | Labeler, sharer | Tắt (bật được) |
| `tranh_chap` | Doanh nghiệp mở tranh chấp nhãn của bạn / admin phân xử xong | Labeler, doanh nghiệp | **Bắt buộc** |
| `he_thong` | Thông báo từ quản trị viên (mục 7.8) | Theo đối tượng admin chọn | Bật (tắt được); chỉ gửi khi admin chọn gửi email |
| `cap_do` | Lên / xuống cấp, nhận huy hiệu (mục 15) | Labeler | Tắt (bật được) |
| `thanh_vien` | Được chủ dự án mời vào dự án; đơn xin tham gia được duyệt / bị từ chối (kèm lý do) | Labeler, reviewer | Bật (tắt được) |
| `don_tham_gia` | Có đơn xin tham gia mới (kèm điểm bài test nếu có); `link` = `/projects/{id}/join-requests` | Doanh nghiệp | Tắt (bật được) |

**Gộp:** hai loại phát sinh nhiều (`nhan_duyet`, `tien_kha_dung`) không tạo một dòng cho mỗi sự kiện. Sự kiện mới được **cộng dồn** vào thông báo cùng loại (và cùng dự án) còn **chưa đọc**, chưa gửi email, có sự kiện gần nhất trong 24 giờ. Thông báo đó được viết lại ("3 nhãn được duyệt, tổng +60.000 đ…", "60.000 đ (3 khoản) đã chuyển…") và `createdAt` chuyển thành thời điểm sự kiện mới nhất, nên dòng này nổi lên đầu danh sách (`unread-count` không tăng thêm). Người dùng đọc rồi thì sự kiện sau mở dòng mới.

```http
GET /notifications/me/preferences
→ [ { "type": "rut_tien", "label": "Kết quả rút tiền", "mandatory": true, "emailEnabled": true }, ... ]   (13 mục)
PUT /notifications/me/preferences   { "items": [ { "type": "du_an", "emailEnabled": false } ] }
→ danh sách đầy đủ sau khi lưu
```

- Màn hình cài đặt: mỗi loại một công tắc "Gửi email", dùng `label` làm nhãn. `mandatory: true` thì hiện công tắc bật và **khoá** (gửi tắt → **400** `loai_bat_buoc`).
- Chỉ cần gửi các loại thay đổi. Tắt email không ảnh hưởng thông báo trong ứng dụng.
- Email đi trễ vài giây (gửi nền, tự thử lại khi máy chủ mail lỗi). Ở dev xem email tại Mailpit `http://localhost:8025` (stack demo: `http://localhost:18025`).

---

## 15. Hồ sơ, kỹ năng, cấp độ, bảng xếp hạng

### 15.1 Hồ sơ của tôi (FC-05)

```http
GET /me/profile
→ { "userId", "email", "emailVerified": true, "displayName": "Nguyen Van A", "bio": null,
    "avatarUrl": "http://.../avatars/...?X-Amz-...", "roles": ["labeler"],
    "skills": { "domains": ["Y te"], "languages": ["vi", "en"], "modalities": ["image", "text"] }, "createdAt" }
PUT /me/profile   { "displayName": "...", "bio": "..." }      → hồ sơ sau khi lưu (ghi đè cả hai: luôn gửi displayName; bio null = xoá)
PUT /me/skills    { "domains": [...], "languages": [...], "modalities": [...] }   → hồ sơ sau khi lưu (ghi đè cả ba)
```

- `avatarUrl` là link ký **có hạn** — đừng lưu lại lâu, lấy lại khi tải trang.
- Kỹ năng (FL-01): `modalities` ∈ `image`, `text`, `audio`, `video`, `pair`, `list`; `domains` / `languages` là chữ tự do (trùng nhau không phân biệt hoa thường thì gộp). Đổi tên hiển thị có hiệu lực trong token **từ lần đăng nhập / refresh sau**.

**Ảnh đại diện** — tải thẳng lên kho file như KYB:

```http
POST /me/avatar/upload-url   { "fileName": "a.png", "sizeBytes": 120000 }
→ { "key", "uploadUrl", "method": "PUT", "expiresAt" }
PUT  <uploadUrl>  (body = file)
PUT  /me/avatar   { "key": "..." }     → hồ sơ có avatarUrl mới
```

### 15.2 Hồ sơ công khai

```http
GET /users/public?ids=uuid1,uuid2,...     (đăng nhập bất kỳ vai trò; ≤ 100 id; id không tồn tại bị bỏ qua)
→ [ { "userId", "displayName", "avatarUrl", "level": 3, "badges": [ { "code": "moc_100", "name": "100 nhãn được duyệt", "awardedAt" } ] } ]
```

Không trả email. Dùng cho màn hình duyệt nhãn, bảng thành viên, bảng xếp hạng.

### 15.3 Cấp độ, huy hiệu, bảng xếp hạng (FL-12)

```http
GET /me/level
→ { "level": 2, "approvedCount": 73, "reputation": 68,
    "nextLevel": { "level": 3, "minApproved": 200, "minReputation": 65 },
    "badges": [ { "code": "moc_100", "name": "...", "awardedAt" } ] }
GET /users/leaderboard?period=all|month|week&limit=20
→ [ { "rank": 1, "userId", "displayName", "avatarUrl", "level": 4, "approvedCount": 1250 } ]
```

- Cấp tính từ **số nhãn được duyệt** (chỉ kênh chuyên nghiệp) **và** uy tín: mặc định cấp 2 = 50 nhãn / uy tín 50, cấp 3 = 200 / 65, cấp 4 = 1.000 / 75, cấp 5 = 5.000 / 85 (`identity.level_rules`). Uy tín giảm có thể **xuống cấp**. `nextLevel: null` khi đã cao nhất — hiện thanh tiến độ tới cấp sau.
- Huy hiệu: `moc_100`, `moc_1000`, `moc_10000` (số nhãn được duyệt), `uy_tin_cao`. Nhận rồi thì giữ.
- Bảng xếp hạng theo số nhãn được duyệt trong kỳ: `all` (từ trước tới nay), `month` (30 ngày gần nhất), `week` (7 ngày gần nhất), tính theo ngày giờ Việt Nam. `limit` 1–100, mặc định 20. `period` sai → 400 `ky_khong_hop_le`.
- Dự án có thể đặt `minLevel` (mục 4.5): dưới mức đó `next` trả 403 `chua_du_cap_do`.

---

## 16. Bảo vệ dữ liệu: link xem file, watermark

Mọi file mẫu (ảnh, âm thanh, video) đi qua **media-svc**. Frontend không thấy địa chỉ kho file, chỉ nhận link `/media/v/{token}` sống 5 phút.

| Ai xem | Ở đâu | Nhận bản nào |
|---|---|---|
| Chủ dự án, admin | `GET /projects/{id}/samples`, màn duyệt nhãn | **Bản gốc** |
| Labeler | Task (`next`, `assignments/mine`), bài test đầu vào | Ảnh có **watermark chìm** mang mã của labeler |
| Reviewer (không phải chủ dự án) | Màn duyệt nhãn | Ảnh có watermark của reviewer |
| Khách cổng link | Trang vượt link | Ảnh có watermark của phiên vượt link |

- Watermark **không nhìn thấy** và không đổi kích thước ảnh: vẽ khung / đa giác như bình thường.
- Âm thanh / video: phát qua proxy, hỗ trợ tua (`Range`), không watermark.
- Response có `Cache-Control: no-store` và `Content-Disposition: inline`. Có thể chặn chuột phải / kéo thả ảnh ở UI cho đỡ tiện tay, nhưng **không** chặn được chụp màn hình — watermark là để truy ra người làm lộ.
- Admin tắt được watermark bằng setting `media.watermark_enabled` (khi đó labeler nhận bản gốc).

### 16.1 Hiển thị ảnh / file

```html
<img src="{fileUrl}">    <audio src="{fileUrl}" controls>    <video src="{fileUrl}" controls>
```

| Mã | Khi nào | FE làm gì |
|---|---|---|
| 200 / 206 | Bình thường (206: đoạn khi tua) | |
| 403 `link_khong_hop_le` | Link bị sửa / sai | Lấy lại link từ API |
| 410 `link_het_han` | Quá 5 phút | Gọi lại API đã trả link (`next` trả lại đúng task kèm link mới) |
| 404 `khong_co_file`, 409 `anh_hong` | File mất / hỏng | Hiện "Không hiển thị được", cho labeler báo lỗi task (mục 5.4) |

Thẻ `<img>` không đọc được mã lỗi: dùng sự kiện `onerror` rồi gọi lại API lấy link mới (một lần); vẫn lỗi thì hiện thông báo.

### 16.2 Admin: truy người làm lộ ảnh

```http
POST /media/admin/watermark/decode     (multipart: file = ảnh nghi bị lộ; width, height = tuỳ chọn)
→ { "found": true, "confidence": 0.97, "viewer": { "type": "user", "userId": "...", "guestSessionId": null } }
→ { "found": false, "confidence": 0.52, "viewer": null }      (không có watermark / bị cắt xén / quá biến dạng)
```

- `viewer.userId`: tài khoản đã nhận ảnh (labeler / reviewer) — tra thông tin bằng `GET /users/admin?search=` hoặc `GET /users/public?ids=`. `guestSessionId`: khách vãng lai ở cổng link (không có tài khoản).
- Watermark chứa thẳng người xem (không lưu gì phía server) → API trả về **ai**, không trả về ảnh nào (nhìn ảnh bị lộ là biết).
- Chịu được nén JPEG, lưu lại, đổi độ sáng. Ảnh bị **thu nhỏ**: gửi kèm `width` / `height` là kích thước gốc của mẫu (`metadata` của mẫu) để hệ thống phóng lại trước khi giải. **Không** chịu được cắt xén.
- `confidence` ~0,5 là ngẫu nhiên; `found: true` đã qua kiểm CRC-32 nên tin được. Ảnh lớn không có watermark có thể mất vài giây (thử mọi độ mạnh).

---

## 17. Bổ sung đợt hoàn thiện 10/2026

Các tính năng dưới đây được thêm sau khi các mục 1–16 đã viết. Mục nào thay đổi hành vi cũ có ghi rõ **(đổi)**.

### 17.1 Đăng nhập hai bước và xác thực hai lớp (FC-04) — **(đổi)** mục 2.1

`POST /auth/login` với tài khoản **đã bật 2FA** trả `200` nhưng **không có token**:

```json
{ "twoFactorRequired": true, "challengeToken": "…", "expiresIn": 300 }
```

Frontend hiện ô nhập mã 6 số rồi gọi:

```http
POST /auth/2fa/verify   { "challengeToken": "…", "code": "123456" }        → giống kết quả login thường (accessToken, refreshToken…)
POST /auth/2fa/verify   { "challengeToken": "…", "recoveryCode": "ABCD-EFGH" }   (mất điện thoại: dùng mã dự phòng, mỗi mã một lần)
```

Sai mã → `401 ma_2fa_sai` kèm `attemptsLeft`; sai quá `identity.two_factor_max_attempts` lần hoặc quá `expiresIn` giây → `401 het_luot_2fa` (quay lại bước mật khẩu). Cách phân biệt: response có `twoFactorRequired` hay có `accessToken`.

Quản lý 2FA ở trang bảo mật (đã đăng nhập):

| API | Ghi chú |
|---|---|
| `GET /me/2fa` | `{ enabled, recoveryCodesLeft }` |
| `POST /me/2fa/setup` | `{ secret, otpauthUri }` — vẽ QR từ `otpauthUri`. **Chưa bật** cho tới khi `enable` |
| `POST /me/2fa/enable { code }` | Trả 10 mã dự phòng — **hiện một lần**, nhắc người dùng lưu |
| `POST /me/2fa/disable { password, code \| recoveryCode }` | |
| `POST /me/2fa/recovery-codes { code }` | Bộ mã mới, bộ cũ hết hiệu lực |
| `POST /users/admin/{userId}/2fa/reset` (admin) | Người mất cả điện thoại lẫn mã dự phòng |

Bật / tắt / admin đặt lại 2FA luôn gửi email (không tắt được).

### 17.2 Xác minh danh tính và độ tuổi (FP-05, mục 2.9)

```http
GET  /me/identity-verification
PUT  /me/identity-verification               { fullName, dateOfBirth: "2000-05-31", idNumber }
POST /me/identity-verification/upload-url    { side: "front" | "back", fileName, sizeBytes }  → { uploadUrl, … }  (PUT ảnh thẳng lên uploadUrl)
POST /me/identity-verification/submit
```

Response: `{ status: draft|submitted|verified|rejected, fullName, dateOfBirth, idNumberLast4, hasFront, hasBack, adult, rejectReason, submittedAt, reviewedAt }`. Số giấy tờ chỉ trả 4 số cuối. Một số giấy tờ chỉ xác minh được cho **một** tài khoản.

Admin: `GET /users/admin/identity-verifications?status=submitted`, `GET /users/admin/identity-verifications/{userId}` (kèm link xem ảnh 5 phút — **mỗi lần mở được ghi nhật ký audit**), `POST …/{userId}/approve`, `POST …/{userId}/reject { reason }`.

Sau khi được duyệt, **làm mới token** (`/auth/refresh`) để token mang claim `identity_verified` / `adult` — project-svc đọc claim này khi tham gia dự án yêu cầu xác minh / dự án nội dung nhạy cảm.

### 17.3 Nạp tiền qua VNPay / MoMo (FB-03) — **(đổi)** mục 4.1

```http
GET  /payments/deposits/providers                         → [ "sandbox", "vnpay", "momo" ]  (theo setting payment.enabled_providers)
POST /payments/deposits   { amountVnd, provider: "vnpay" }  (header Idempotency-Key)  → { intentId, checkoutUrl, … }
```

Chuyển trình duyệt tới `checkoutUrl`. Cổng trả người dùng về Return URL của frontend — **frontend không tin tham số trên Return URL**: gọi

```http
POST /payments/deposits/{intentId}/sync     → trạng thái lệnh (backend hỏi ngược cổng bằng API server-to-server)
GET  /payments/deposits/{intentId}
```

Tiền chỉ vào ví khi cổng báo qua IPN (`/payments/webhooks/vnpay`, `/payments/webhooks/momo` — cổng gọi, không phải frontend) hoặc khi `sync` / worker đối soát xác nhận. Lệnh treo quá `payment.deposit_expire_after` thành hết hạn. Dev / demo: `checkoutUrl` trỏ tới **cổng giả lập** trong payment-svc (trang "thanh toán" có nút thành công / huỷ, ký đúng thuật toán của VNPay / MoMo).

### 17.4 Hoá đơn VAT phí nền tảng (FB-05)

```http
GET  /ledger/invoices                          → hoá đơn của tôi (doanh nghiệp)
GET  /ledger/invoices/preview?period=2026-10   → tạm tính (kể cả tháng đang chạy), không lưu
POST /ledger/invoices   { period: "2026-09" }  → xuất hoá đơn tháng đã kết thúc (đã có thì trả lại bản cũ)
GET  /ledger/invoices/{id}
GET  /ledger/invoices/{id}/html                → bản thể hiện in được (mở tab mới / in)
GET  /ledger/invoices/{id}/xml                 → dữ liệu hoá đơn điện tử (cấu trúc Thông tư 78)
GET  /ledger/invoices/admin?period=&page=&pageSize=   (admin)
```

Phí nền tảng đã thu là giá **đã gồm thuế**; hoá đơn tách tiền hàng + VAT (`invoice.vat_percent`). Đầu tháng hệ thống tự xuất hoá đơn tháng trước (`invoice.auto_issue`). Hoá đơn đã xuất không sửa / xoá được.

### 17.5 Dự án: bảo vệ nội dung, lời mời, quan hệ labeler, nhập nguồn ngoài

**Nội dung nhạy cảm / NDA / yêu cầu xác minh (mục 2.9)** — cấu hình khi dự án còn nháp:

```http
PUT  /projects/{id}/protection   { sensitiveContent, contentWarning, ndaRequired, ndaText, requireVerifiedIdentity }
GET  /projects/{id}/terms        → văn bản cảnh báo / NDA + trạng thái đồng ý của tôi
POST /projects/{id}/consent      { acceptContentWarning, acceptNda, fullName }
GET  /projects/{id}/consents     (chủ dự án: ai đã đồng ý / ký NDA)
POST /projects/{id}/leave        (labeler / reviewer tự rời dự án — bất kỳ lúc nào, không bị phạt)
```

Dự án nhạy cảm: lease có `sensitiveContent: true` — frontend hiện cảnh báo trước khi hiện mẫu. Làm liên tục quá `welfare.max_continuous_minutes` phút → `POST /tasks/projects/{id}/next` trả `429 can_nghi_giai_lao` kèm header `Retry-After` và `retryAfterSeconds` / `breakUntil`; frontend hiện màn hình nghỉ có đồng hồ đếm ngược.

**Lời mời** — **(đổi)** mục 4.9: mời theo email luôn trả `202` + lời mời, kể cả email **chưa có tài khoản** (trước là lỗi `khong_tim_thay`) và email đã có tài khoản (trước là thêm thẳng) — xem VD-S-16 ở mục 4.9. Email chưa có tài khoản nhận thư kèm link `…/dang-ky?email=…&invitation=…`, đăng ký đúng email đó thì lời mời tự gắn vào tài khoản.

Mỗi lời mời có `byEmail`. Chủ dự án xem (`GET /projects/{id}/invitations`): lời mời theo email có `userId: null` cho tới khi được chấp nhận; lời mời theo userId có `email: null` (không lộ email của người đó). Người được mời (`/mine`) luôn thấy đủ.

```http
GET  /projects/invitations/mine
POST /projects/invitations/{id}/accept        (dự án cần đồng ý điều khoản: gọi /consent trước)
POST /projects/invitations/{id}/decline
GET  /projects/{id}/invitations               (chủ dự án)
DELETE /projects/{id}/invitations/{invitationId}
```

Đơn xin tham gia chủ dự án không xử lý trong `project.join_request_ttl` tự hết hạn: labeler nhận thông báo "đơn đã hết hạn" (`join_request.decided`, `reason = "het_han"`).

**Danh sách labeler của doanh nghiệp, đánh giá, gợi ý (FB-23, FL-02):**

```http
GET    /projects/labeler-lists?kind=preferred|blocked
PUT    /projects/labeler-lists/{labelerId}   { kind: "preferred" | "blocked", note }
DELETE /projects/labeler-lists/{labelerId}
POST   /projects/{id}/members/invite-preferred          → mời cả danh sách ưu tiên
PUT    /projects/{id}/ratings/{labelerId}  { stars: 1-5, comment }
GET    /projects/{id}/ratings
GET    /projects/labelers/{labelerId}/rating-summary
PUT    /projects/{id}/tags   { domains: [...], languages: [...] }
GET    /projects/recommended?limit=10                   → dự án hợp hồ sơ kỹ năng của labeler
PUT    /projects/{id}/retention   { deleteFilesAfterClose }   → đồng ý xoá file gốc khi dự án đóng quá hạn giữ
```

Labeler trong danh sách chặn của doanh nghiệp không tham gia được mọi dự án của doanh nghiệp đó; danh sách ưu tiên được duyệt đơn tự động.

**Đơn giá tối thiểu (FM-04):** `PUT /projects/{id}/pricing` với đơn giá < `project.min_unit_price_vnd` → `400 don_gia_duoi_toi_thieu`; checklist publish cũng báo mã này khi admin nâng mức sau.

**Nhập dữ liệu từ nguồn ngoài (FB-11):**

```http
POST /projects/{id}/datasets/import
{ "name": "lo s3", "source": "s3",  "s3": { "endpoint": null, "region": "ap-southeast-1", "bucket": "…", "prefix": "anh/", "accessKey": "…", "secretKey": "…" } }
{ "name": "lo url", "source": "urls", "urls": [ "https://…/a.jpg", … ] }
{ "name": "lo drive", "source": "googleDrive", "googleDrive": { "links": [ "https://drive.google.com/file/d/…" ] } }
→ 202 { id, status: "pending" }   (theo dõi như lô manifest — mục 4.6)
```

Chỉ tải từ địa chỉ công khai (chặn IP nội bộ — SSRF); tối đa `dataset.import_max_files` file mỗi lô. Khoá bí mật S3 chỉ lưu dạng mã hoá tới khi nhập xong rồi xoá.

**Cổng link chỉ cho công cụ làm nhanh — (đổi) mục 12.3:** dự án bật cổng link mà tập nhãn có công cụ ngoài `gate.allowed_tools` (mặc định `classification, pairwise, number, bbox, keypoint, span`) → checklist có `cong_cu_khong_hop_cong_link`, publish trả `409` kèm tên công cụ. Frontend: khi bật kênh cổng link, cảnh báo ngay nếu tập nhãn có công cụ khác.

### 17.6 Công cụ gán nhãn và xuất file mới — **(đổi)** mục 6.3, 4.11

- `bbox` có `"rotatable": true` (chỉ bbox): mỗi khung thêm `angle` (độ, chiều kim đồng hồ quanh tâm, -180..180). Kiểm "nằm trong ảnh" theo 4 đỉnh sau khi xoay. Gộp: góc = trung vị.
- `track`: mỗi keyframe có thể có `"visible": false` — đối tượng **khuất** từ mốc đó tới mốc `visible` kế tiếp (không nội suy qua đoạn khuất).
- Định dạng xuất mới (`GET /annotations/projects/{id}/export?format=`): `yolo-seg` (đa giác), `yolo-obb` (khung xoay), `coco-keypoints`, `conll` (span → BIO, dự án văn bản), `mot` (track, dự án video; khung hình theo `fps` của mẫu). `yolo` ghi khung xoay thành hộp bao; `voc` có thêm `<polygon>` và `<angle>`. Định dạng không hợp loại dữ liệu → `400`.
- `GET /annotations/projects/{id}/results` dùng bộ đệm kết quả gộp theo mẫu: header `X-Results-Cache: hits=…; misses=…` (chỉ để đo).

### 17.7 Chỉ số chất lượng mở rộng (FQ-02) — **(đổi)** mục 4.12

`GET /quality/projects/{id}/summary` — mỗi phần tử `tools[]` thêm:

```json
{ "tool": "khung", "krippendorffAlpha": 0.87, "fleissKappa": null, "alphaMetric": "set_f1", "itemCount": 120, "labelCount": 360, "computedAt": "…" }
```

`alphaMetric`: `nominal` (phân loại, cặp — có thêm `fleissKappa`), `interval` (số), `cer` (chép lời), `kendall` (xếp hạng), `set_f1` (khung, đa giác, mặt nạ, span, điểm mốc, đường, quan hệ, đoạn thời gian, chuỗi khung). Trước đây chỉ công cụ phân loại / cặp có alpha. `freeText` không có chỉ số.

`GET /quality/projects/{id}/labelers` — mỗi labeler thêm `stapleMask: { "<tên công cụ mask>": { sensitivity, specificity, sampleCount } }` (STAPLE: độ nhạy = tô được bao nhiêu phần vật thật, độ đặc hiệu = không tô lấn ra nền). Chỉ để chủ dự án xem, **không** vào điểm uy tín.

### 17.8 Âm thanh / video có watermark — **(đổi)** mục 16

Link xem của labeler / reviewer / khách cổng link với mẫu **âm thanh** trả `audio/flac`, **video** trả `video/mp4` (H.264) — đã nhúng watermark người xem. Lần mở đầu tiên mất thời gian mã hoá lại (tỉ lệ với độ dài file, video ngắn vài giây); các lần sau và khi tua (`Range`) lấy từ bộ đệm. Frontend dùng `<audio>` / `<video>` như cũ, nên hiện trạng thái "đang chuẩn bị" khi chờ.

`POST /media/admin/watermark/decode` nhận cả file âm thanh / video (tối đa 500 MB); response thêm `mediaType: image | audio | video`. Không chịu được: cắt đầu file âm thanh, đổi tốc độ / tần số lấy mẫu, thu âm lại qua loa, cắt xén / đổi kích thước video.

### 17.9 Thông báo realtime và ngôn ngữ (FC-06, FC-08) — **(đổi)** mục 14

```http
POST /notifications/me/realtime-ticket        → { ticket, expiresAt }   (vé dùng MỘT lần, sống 30 giây)
GET  ws://<gateway>/notifications/realtime?ticket=…
```

Khung đầu `{ "kind": "connected" }`; mỗi thông báo mới là một khung `{ "kind": "notification", "notification": { id, type, title, body, link, createdAt } }`. Vé sai / hết hạn / đã dùng: kết nối mở rồi **đóng ngay với mã 4401** — xin vé mới. Mất kết nối: xin vé mới rồi mở lại, và tải lại `unread-count` (thông báo trong lúc rớt vẫn nằm trong danh sách). Giữ polling `unread-count` thưa (vd 5 phút) làm dự phòng.

```http
GET /notifications/me/language          → { "language": "vi" }
PUT /notifications/me/language          { "language": "en" }     (vi | en; khác → 400)
```

Ngôn ngữ áp cho **thông báo và email tạo sau đó** (thông báo cũ giữ ngôn ngữ lúc tạo). Giao diện frontend tự dịch; mã lỗi `code` của API không phụ thuộc ngôn ngữ.

Loại thông báo mới (mục 14): `bao_mat` (bật / tắt 2FA — email bắt buộc), `xac_minh_danh_tinh`, `loi_moi_du_an`. Thông báo cũ hơn `notification.retention_days` (180 ngày) bị xoá.

### 17.10 Admin: hàng đợi duyệt chung, mẫu bài toán, vận hành

**Hàng đợi duyệt chung** — một màn hình cho mọi loại đơn chờ duyệt (đọc từ event, cập nhật trễ vài giây):

```http
GET  /admin/approvals?kind=&status=pending&page=&pageSize=
GET  /admin/approvals/summary            → { totalPending, kinds: [ { kind, pending, oldestOpenedAt, oldestWaitingHours } ] }
POST /admin/approvals/rebuild            → dựng lại từ nhật ký audit
```

Mỗi mục `{ id, kind, subjectId, status, title, requesterId, amountVnd, note, openedAt, decidedAt, decidedBy }`; `status`: `pending`, `waiting` (dự án chờ ký quỹ), `approved`, `rejected`, `cancelled`, `resolved`. Bấm vào mục thì gọi API duyệt của service chủ theo `kind`:

| kind | subjectId | Xem / duyệt |
|---|---|---|
| `kyb` | businessId | `GET /kyb/admin/{id}`, `POST …/approve`, `POST …/reject` |
| `identity` | userId | `GET /users/admin/identity-verifications/{id}`, `…/approve`, `…/reject` |
| `project` | projectId | `GET /projects/{id}`, `POST /projects/{id}/approve`, `POST /projects/{id}/reject` |
| `withdrawal` | withdrawalId | `POST /ledger/admin/withdrawals/{id}/approve`, `…/reject` |
| `appeal` | annotationId | `POST /annotations/{id}/appeal/resolve { accept, note }` |
| `dispute` | disputeId | `POST /ledger/admin/disputes/{id}/resolve` |
| `link` | linkId | `POST /links/admin/{id}/disable`, `POST /links/admin/{id}/dismiss-reports` |
| `referral` | referredId | `POST /links/admin/referrals/{id}/approve`, `…/reject { reason }` |

**Giới thiệu nghi lạm dụng (FS-08):** `GET /links/admin/referrals/flagged` → `[ { referrerId, referredId, reasons, createdAt } ]`; `reasons` gồm `trung_ip_nguoi_gioi_thieu`, `nhieu_tai_khoan_cung_ip`, `dot_bien`. Người dùng nhập mã vẫn nhận `204` (không chặn), nhưng hoa hồng chỉ tính sau khi admin chấp nhận.

**Mẫu bài toán / template workspace (FM-03):**

```http
GET  /templates?modality=image          (mọi người đăng nhập — chỉ mẫu đang bật)
GET  /templates/{id}
GET  /admin/templates                   (admin — cả mẫu đã tắt)
POST /admin/templates   { code, name, description, labelSchema, guidelineMarkdown, suggestedUnitPriceVnd, suggestedRedundancy }
PUT  /admin/templates/{id}
POST /admin/templates/{id}/deactivate | /activate
```

Tạo dự án từ mẫu: frontend tạo dự án với `modality` của mẫu rồi gọi `PUT /projects/{id}/label-schema` với `labelSchema` của mẫu và `PUT /projects/{id}/guideline` với `guidelineMarkdown` — dự án **không** tham chiếu mẫu (sửa mẫu không đổi dự án đã tạo).

**Vận hành:** `GET /admin/ops/queues` → `{ reachable, checkedAt, threshold, deadLetterQueues: [ { queue, messages, overThreshold } ] }`. DLQ vượt `ops.dlq_alert_threshold` thì gửi email tới `ops.alert_email`.

### 17.11 Mã lỗi mới

| HTTP | code | Khi nào |
|---|---|---|
| 401 | `ma_2fa_sai` / `het_luot_2fa` | Bước 2 đăng nhập |
| 429 | `can_nghi_giai_lao` | Dự án nhạy cảm, làm quá lâu liên tục |
| 400 | `don_gia_duoi_toi_thieu` | Đơn giá dưới mức tối thiểu |
| 409 | `cong_cu_khong_hop_cong_link` | Bật cổng link với công cụ không cho phép |
| 400 | `tap_nhan_khong_hop_le` / `ma_khong_hop_le` | Tạo mẫu bài toán |
| 409 | `ma_da_ton_tai` | Trùng `code` mẫu bài toán |
| 409 | `da_xet` | Xét lại giới thiệu đã xét |
| 400 | `ngon_ngu_khong_hop_le` | Ngôn ngữ thông báo khác vi / en |
| 409 | `media_hong` | File âm thanh / video hỏng, không nhúng watermark được |
| 429 | `vuot_han_muc_moi_email` | Mời quá `project.email_invite_daily_max` email / 24 giờ (kèm `Retry-After`) |

## 18. Dự án làm trung tâm (kiến trúc mục 3.18)

Từ đợt này **tiền, bài làm và kết quả đi theo cả dự án**, không theo từng nhãn. Các mục cũ nói về đơn giá mỗi nhãn, lease task, duyệt / từ chối / khiếu nại / tranh chấp từng nhãn (4.5 phần giá, 4.9 phần tiến độ, 4.10, 4.14, 5.3–5.5, 7.5) **đã bị thay** bằng mục này.

### 18.1 Doanh nghiệp: giá cả dự án, kênh, checklist

```http
PUT /projects/{id}/pricing
{ "rewardVnd": 50000, "slots": 10, "passThresholdPercent": 80, "deadline": "2027-01-31T00:00:00Z" }

PUT /projects/{id}/channels
{ "allowProfessional": true, "allowLinkGateway": true, "allowCollaborative": false,
  "gateUnitPriceVnd": 300, "gateBudgetVnd": 200000 }
```

- `rewardVnd`: thưởng cho **một bài đạt** (cả bài, không theo câu), ≥ `limits.minRewardVnd`. `slots`: số suất — tối đa bao nhiêu bài đạt được thưởng (`limits.minSlots`–`limits.maxSlots`).
- `passThresholdPercent` = `t`: tỉ lệ câu giống đa số tối thiểu để đạt. Phải nằm trong `[limits.passThresholdMin, limits.passThresholdMax]` — admin đặt bằng setting `project.pass_threshold_min` / `project.pass_threshold_max`. Gợi ý UI: thanh trượt chỉ cho chọn trong khoảng này.
- Cổng link (kênh phụ) có **đơn giá riêng mỗi nhãn khách gán** và **ngân sách riêng**; bật cổng mà thiếu một trong hai → checklist báo `chua_cau_hinh_cong_link`.
- `PUT /projects/{id}/quality-control` **đã bỏ** (không còn tỉ lệ câu vàng, không còn chế độ duyệt).

`GET /projects/{id}/readiness` (mới):

```json
{ "ready": true, "missing": [], "warnings": ["khong_co_cau_vang_kiem_tra"],
  "sampleCount": 12, "questionCount": 10, "qualityGoldCount": 2, "entranceGoldCount": 0,
  "platformFeePercent": 30, "platformFeePerPassVnd": 15000, "estimatedCostVnd": 650000,
  "limits": { "passThresholdMin": 60, "passThresholdMax": 95, "minSlots": 3, "maxSlots": 50, "maxQuestions": 500, "minRewardVnd": 10000 } }
```

- `estimatedCostVnd` = ký quỹ khi publish = `slots × (rewardVnd + platformFeePerPassVnd) + gateBudgetVnd`.
- `questionCount` = số câu THẬT (mẫu không phải câu vàng); câu vàng `qualityCheck` được chèn vào **mọi** bài, labeler không phân biệt được.
- `warnings` không chặn publish. `missing` mới: `chua_cau_hinh_gia`, `thuong_duoi_toi_thieu`, `so_suat_khong_hop_le`, `nguong_dat_ngoai_khoang`, `qua_nhieu_cau`, `chua_cau_hinh_cong_link`.
- `ProjectResponse` / `ProjectListItem` thay `unitPriceVnd`, `redundancy`, `maxRedundancy`, `goldCheckPercent`, `requireReview`, `budgetVnd` bằng: `rewardVnd`, `slots`, `passThresholdPercent`, `questionCount`, `toolKinds` (dạng gán nhãn, mỗi loại công cụ một lần — hiện trên thẻ chợ dự án), `gateUnitPriceVnd`, `gateBudgetVnd`, `finishRequestedAt`, `gradingStartedAt`, `closedAt`, `escrowVnd`, `platformFeePercent`. Trạng thái mới: `grading` (đã đóng thu bài, đang chấm / trong hạn khiếu nại).

### 18.2 Doanh nghiệp: vận hành, kết thúc, kết quả

| Việc | API |
|---|---|
| Tiến độ | `GET /tasks/projects/{id}/progress` → `{ slots, questionCount, inProgress, submitted, expired, cancelled, phase, finishReason, deadline, collectionFinishedAt, gradedAt, appealDeadline, settledAt, passedCount, openAppeals, estimatedCompletionAt }` |
| Danh sách bài | `GET /tasks/projects/{id}/attempts` → `AttemptSummary[]` (mục 18.3) |
| Kết thúc sớm | `POST /projects/{id}/finish` — đóng thu bài ngay: bài đã nộp được chấm, bài đang làm dở hết hạn. Không mở lại được |
| Huỷ | `POST /projects/{id}/cancel` — chỉ khi **đã tạm dừng và chưa có bài nộp**; ngược lại 409 `chua_the_dong` + `closeCheck.reasons` (`chua_dong_bo_tam_dung` → thử lại vài giây; `da_co_bai_nop` → dùng kết thúc sớm) |
| `POST /projects/{id}/complete` | **Đã bỏ** — dự án tự hoàn thành khi hết hạn khiếu nại |
| Kết quả, xuất file | như mục 4.11, nhưng chỉ gồm nhãn của **bài đạt** (và bài được chấp nhận khiếu nại) |
| Nhãn của dự án | `GET /annotations/projects/{id}?status=accepted|excluded|submitted&attemptId=` — `status`: `submitted` (chờ chấm), `accepted` (vào kết quả), `excluded` (bài trượt) |
| Chất lượng | `GET /quality/projects/{id}/summary` → `attemptsSubmitted`, `attemptsGraded`, `attemptsPassed`, `attemptsFailed`, `collectionReason`, `gradedAt`, `majorityUsed`, `samplesEvaluated`, `agreed`, `disputed`, `notApplicable`, `goldAnswers`, `goldAccuracyPercent`, `tools[]`; `GET /quality/projects/{id}/labelers` → mỗi **bài**: `attemptId`, `labelCount` (đã trả lời) / `questionCount`, `agreementPercent`, `agreementSampleCount`, `matchedCount`, `passed`, câu vàng, DS / STAPLE, uy tín |
| Chi phí | `GET /ledger/projects/{id}/cost` → `reservedVnd`, `balanceVnd`, `spentRewardsVnd`, `spentGateVnd`, `returnedVnd`, `slots`, `rewardPerAttemptVnd`, `platformFeePerAttemptVnd`, `paidAttempts`, `heldVnd`, `escrowState` (`active` / `closing` / `closed`) |

`phase`: `open` (đang nhận bài) → `finished` (đóng thu bài, đang chấm) → `graded` (đã chấm, trong hạn khiếu nại) → `settled` (đã chốt). `finishReason`: `du_suat`, `het_han`, `ket_thuc_som`.

### 18.3 Labeler: làm bài như bài kiểm tra

```http
GET  /tasks/projects/{projectId}/slots                  → suất còn lại + bài của tôi (mine)
POST /tasks/projects/{projectId}/attempt                → bắt đầu, hoặc trả lại bài đang làm (idempotent); header X-Device-Id
GET  /tasks/attempts/{attemptId}                        → bài + mọi câu + câu trả lời đã lưu
PUT  /tasks/attempts/{attemptId}/answers/{sampleId}     { "data": { "<tên công cụ>": ... } }   data = null: bỏ trống câu
POST /tasks/attempts/{attemptId}/submit                 → nộp CẢ BÀI (một lần)
POST /tasks/attempts/{attemptId}/abandon                → bỏ bài, trả suất
POST /tasks/attempts/{attemptId}/reports                { "sampleId", "reason", "note" }   báo lỗi một câu
POST /tasks/attempts/{attemptId}/appeal                 { "message" }   khiếu nại cả bài (sau khi chấm, trong hạn)
GET  /tasks/me/attempts                                 → AttemptSummary[] của tôi
```

`AttemptResponse`: `attemptId`, `projectId`, `state` (`inProgress` / `submitted` / `expired` / `cancelled`), `startedAt`, `expiresAt` (≤ hạn chót dự án), `submittedAt`, `labelSchema`, `schemaVersion`, `rewardVnd`, `passThresholdPercent`, `sensitiveContent`, `questionCount`, `answeredCount`, `questions[]` = `{ index, sampleId, modality, fileUrl, content, metadata, answer, answeredAt }` — **xáo trộn theo từng người**, câu vàng nằm lẫn và không có dấu hiệu nào.

- **Lưu nháp từng câu**: `PUT answers` sau mỗi thay đổi (frontend tham khảo debounce ~1 giây và lưu ngay khi chuyển câu). Lưu nháp **không** kiểm định dạng nhãn — được lưu câu làm dở.
- **Nộp**: server kiểm mọi câu đã trả lời bằng đúng bộ kiểm của Crowd.Labeling. Sai → 400 `bai_chua_hop_le`, body có `invalidAnswers: [{ index (1-based), sampleId, code, message }]`, bài **vẫn mở** để sửa. Câu bỏ trống được phép (tính là không giống đa số; câu vàng bỏ trống tính là sai).
- Dự án tạm dừng → nộp bài trả 409 `du_an_tam_dung` (bài vẫn giữ, nộp lại khi dự án chạy tiếp). Hết suất → `het_suat`; đã đóng thu bài → `du_an_da_dong_thu_bai`; đã nộp bài cho dự án → `da_nop_bai`.
- Link xem file sống ngắn: lỗi tải ảnh / âm thanh thì `GET /tasks/attempts/{id}` lấy link mới, **giữ** câu trả lời đang sửa trên máy.

`AttemptSummary` (danh sách / kết quả): các trường trên + `comparableCount`, `matchedCount`, `agreementPercent`, `goldTotal`, `goldCorrect` (chỉ hiện **sau khi chấm**), `passed`, `finalPassed` (sau khiếu nại), `gradedAt`, `appealState` (`none` / `open` / `accepted` / `rejected`), `appealMessage`, `appealNote`, `appealDeadline`, `canAppeal`.

Tiền: bài đạt được trả `rewardVnd` vào **số dư khả dụng ngay khi chấm** (`ledger.reward_hold` = 0; admin đặt > 0 thì treo như doanh thu link). Thu nhập `GET /ledger/me/earnings` có nguồn mới `projectReward`.

### 18.4 Admin

- Khiếu nại bài làm: `GET /tasks/admin/appeals?state=open|accepted|rejected` → `AttemptSummary[]`; `POST /tasks/admin/attempts/{id}/appeal/resolve { "accept": true, "note": "..." }`. Để so bài với đa số: `GET /annotations/projects/{projectId}?attemptId={id}` (nhãn của bài) cạnh `GET /annotations/projects/{projectId}/results` (kết quả gộp).
- Hàng đợi duyệt chung: loại `appeal` nay là khiếu nại **bài làm** (`subjectId` = attemptId, `note` = "giống đa số A% / ngưỡng T%"); loại `dispute` đã bỏ.
- Tổng quan `GET /admin/stats/overview`: `attemptsSubmitted`, `projectsGraded`, `projectsCompleted`, `labelerPayoutsVnd` (từ `funds.held`: thưởng + doanh thu link + hoa hồng), `appealsOpened`; `daily[]` có `attemptsSubmitted`, `projectsGraded`.
- Setting mới: `project.pass_threshold_min` / `max` (x, y), `project.max_questions`, `project.min_slots` / `max_slots`, `project.min_reward_vnd`, `task.attempt_duration`, `task.attempt_max_bytes`, `task.appeal_window`, `ledger.reward_hold`.
- Thông báo mới: loại `ket_qua_bai` (bài đạt / chưa đạt / kết quả khiếu nại; link `/attempts/{attemptId}`), chủ dự án nhận "dự án đã chấm xong". Loại `nhan_duyet`, `nhan_tu_choi`, `tranh_chap` đã bỏ.


## 19. Câu trả lời mở chấm theo nghĩa (kiến trúc mục 3.19)

Công cụ `freeText` (payload `{"text": "..."}`) giờ được **chấm**: lúc dự án kết thúc, hệ thống so ý nghĩa từng câu với câu "ở giữa" của mọi người. Câu lạc đề, gõ bừa hoặc bỏ trống thì không giống đa số. Bước phân tích chạy trên ml-svc nên **chấm và trả công chậm hơn** các dạng khác; frontend phải báo trước.

- `GET /projects/{id}/readiness` → `warnings` có `cham_cau_tra_loi_mo_lau` khi tập nhãn có freeText (không chặn publish).
- `GET /quality/projects/{id}/summary` → `freeTextAnalysis`: `null` (không có câu mở) | `{ "state": "pending" | "done", "method": null | "semantic" | "lexical" }`. `pending` = đã đóng thu bài, đang chờ phân tích; `lexical` = ml-svc không phân tích kịp, đã chấm bằng so chữ (nên hiện ghi chú cho chủ dự án).
- Giai đoạn `finished` (`/tasks/projects/{id}/progress`, `/slots`) của dự án có freeText: hiển thị **"Đang phân tích câu trả lời mở"** thay vì "đang chấm".
- Chỗ cần báo (frontend tham khảo `components/project/free-text-notice.tsx`):
  - trình sửa tập nhãn, khi chọn công cụ freeText (doanh nghiệp);
  - thẻ chợ dự án (`toolKinds` có `freeText`);
  - trang chi tiết dự án của labeler;
  - hộp xác nhận nộp bài;
  - trang kết quả bài khi chưa chấm (`labelSchema.tools[].kind`).
- Setting admin: `quality.freetext_similarity` (0,6), `quality.freetext_lexical_similarity` (0,38), `quality.freetext_analysis_timeout` (3600 s), `quality.freetext_analysis_interval` (5 s).
