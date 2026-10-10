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

---

## 1. Quy ước chung

| Mục | Quy ước |
|---|---|
| Base URL | Gateway: `http://localhost:8080` (dev). Mọi đường dẫn trong tài liệu tính từ đây |
| Định dạng | JSON, UTF-8. Tên trường **camelCase** |
| Enum | Chuỗi camelCase: `"running"`, `"pendingReview"`, `"qualityCheck"`… Gửi lên cũng dùng dạng này |
| ID | UUID dạng chuỗi |
| Thời gian | ISO 8601 có múi giờ, server trả UTC: `"2026-10-06T08:59:13.364+00:00"` |
| Tiền | **Số nguyên đồng (VND)**, tên trường kết thúc bằng `Vnd`. Không có số lẻ |
| Phân trang | Query `page` (từ 1) và `pageSize` (1–100, mặc định 20). Response: `{ "items": [...], "page", "pageSize", "total" }` |
| Xác thực | Header `Authorization: Bearer <accessToken>` cho mọi API trừ `/auth/*` |
| Idempotency | Hai API tiền bắt buộc header `Idempotency-Key` (mục 4.1, 5.7) |

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

---

## 2. Xác thực

### 2.1 Đăng ký, đăng nhập

```http
POST /auth/register
{ "email": "a@b.com", "password": "toi-thieu-8-ky-tu", "displayName": "Nguyen Van A", "roles": ["labeler"] }
→ 201 { "userId": "..." }
→ 409 { "error": "Email da duoc dang ky." }
→ 400 ValidationProblemDetails
```

- `roles`: một hoặc nhiều trong `business`, `labeler`, `sharer`. Không tự đăng ký được `admin`.
- Mật khẩu 8–128 ký tự. Tên hiển thị tối đa 100 ký tự.

```http
POST /auth/login
{ "email": "a@b.com", "password": "..." }
→ 200 { "accessToken": "...", "refreshToken": "...", "tokenType": "Bearer", "expiresIn": 900 }
→ 401 (sai email hoặc mật khẩu — thông báo chung, không nói sai cái nào)
→ 403 tài khoản bị khóa
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
| `refreshToken` | 14 ngày | **Dùng một lần**: mỗi lần làm mới trả cặp token mới, token cũ hết hiệu lực |

```http
POST /auth/refresh   { "refreshToken": "..." }   → 200 cặp token mới | 401
POST /auth/logout    { "refreshToken": "..." }   → 204 (luôn 204)
```

Cách làm khuyến nghị:

- Gặp 401 thì gọi `/auth/refresh` **một lần duy nhất** (khóa lại để nhiều request song song không cùng làm mới), lưu cặp mới, rồi gọi lại request cũ. Refresh cũng 401 thì đưa về trang đăng nhập.
- **Không dùng lại refresh token cũ.** Backend coi việc dùng lại một token đã xoay là dấu hiệu bị đánh cắp và thu hồi **cả họ token**: mọi tab/thiết bị đang dùng họ đó phải đăng nhập lại. Hai tab cùng làm mới bằng một token cũng gây ra chuyện này, nên đồng bộ token giữa các tab (ví dụ BroadcastChannel).

---

## 3. Bản đồ màn hình → API theo vai trò

| Vai trò | Màn hình | API chính |
|---|---|---|
| Doanh nghiệp | Ví, nạp tiền | `GET /ledger/me/balance`, `POST /payments/deposits`, `GET /payments/deposits/{id}` |
| | Danh sách dự án của tôi | `GET /projects?mine=true` |
| | Wizard tạo dự án | `POST /projects`, `PUT /projects/{id}/label-schema`, `/guideline`, `/pricing`, `/channels`, `/eligibility` |
| | Nạp dữ liệu | `POST /projects/{id}/datasets` (ZIP), `POST /projects/{id}/uploads` + `POST /projects/{id}/datasets/manifest`, `GET /projects/{id}/datasets`, `GET /projects/{id}/samples` |
| | Câu hỏi vàng | `GET/POST /projects/{id}/gold-items`, `DELETE /projects/{id}/gold-items/{goldItemId}` |
| | Checklist + publish | `GET /projects/{id}/readiness`, `POST /projects/{id}/publish` |
| | Kiểm soát chất lượng | `PUT /projects/{id}/quality-control`, `GET /quality/projects/{id}/summary`, `GET /quality/projects/{id}/labelers` |
| | Theo dõi dự án | `GET /projects/{id}`, `GET /tasks/projects/{id}/progress`, `POST /projects/{id}/pause` / `resume` / `complete` / `cancel` |
| | Thành viên | `GET/POST /projects/{id}/members`, `.../block`, `.../unblock`, `DELETE .../members/{userId}` |
| | Duyệt nhãn | `GET /annotations/projects/{id}?status=pendingReview`, `POST /annotations/{id}/approve` / `reject`, `POST /annotations/projects/{id}/approve-agreed`, `GET /annotations/{id}/history` |
| | Kết quả, tải về | `GET /annotations/projects/{id}/results`, `GET /annotations/projects/{id}/export?format=json\|csv\|coco` |
| Labeler | Chợ dự án | `GET /projects`, `GET /projects/{id}` |
| | Tham gia, bài test | `POST /projects/{id}/join`, `POST /projects/{id}/entrance-test/attempts`, `.../attempts/{attemptId}/submit`, `GET .../attempts` |
| | Workspace gán nhãn | `POST /tasks/projects/{id}/next`, `POST /tasks/assignments/{id}/submit`, `POST /tasks/assignments/{id}/release`, `GET /tasks/assignments/mine` |
| | Lịch sử, khiếu nại, uy tín | `GET /annotations/mine`, `POST /annotations/{id}/appeal`, `GET /quality/me` |
| | Ví, rút tiền | `GET /ledger/me/balance`, `GET /ledger/me/transactions`, `POST /ledger/withdrawals`, `GET /ledger/withdrawals/mine` |
| Reviewer (trong dự án) | Duyệt nhãn của dự án được giao | Giống phần duyệt nhãn của doanh nghiệp |
| Người chia sẻ (sharer) | Quản lý link, chiến dịch | `POST /links`, `POST /links/bulk`, `GET /links`, `GET/PUT/DELETE /links/{id}`, `GET/POST /links/campaigns` |
| | API key, Quick Link | `POST/GET /links/api-key`, `GET /links/quick?api=&url=` |
| | Thống kê, giới thiệu, ví | `GET /gate/stats/me/daily` / `top-links` / `sources` / `outcomes`, `GET /links/referrals/me`, `POST /links/referrals/claim`, ví như labeler (mục 5.7) |
| Khách vãng lai | Trang vượt link (không đăng nhập) | `GET /g/{code}`, `POST /g/{code}/sessions`, `POST /g/sessions/{id}/submit`, `GET /go/{code}?t=`, `POST /links/r/{code}/report` |
| Admin | Duyệt dự án | `GET /projects/pending-approval`, `POST /projects/{id}/approve` / `reject` |
| | Khiếu nại | `GET /annotations/appeals`, `POST /annotations/{id}/appeal/resolve` |
| | Đối soát sổ cái | `GET /ledger/admin/reconciliation` |
| | Chạy Dawid–Skene ngay | `POST /quality/admin/dawid-skene/run` |
| | Setting hệ thống (phí, tự duyệt, hạn mức…) | `GET /admin/settings`, `PUT /admin/settings/{key}`, `GET /admin/settings/{key}/history` |
| | Duyệt rút tiền | `GET /ledger/admin/withdrawals`, `POST /ledger/admin/withdrawals/{id}/approve` / `reject` |
| | Đối chiếu nạp chuyển khoản | `GET /payments/admin/deposits`, `POST /payments/admin/deposits/{id}/approve` / `reject` |
| | Kiểm duyệt link | `GET /links/admin/review-queue`, `POST /links/admin/{id}/disable` / `dismiss-reports`, `GET/POST/DELETE /links/admin/blocked-domains` |

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

- `modality`: `image` | `text` | `audio` | `video` | `pair`. **Không đổi được sau khi tạo**: nó quyết định loại file được nạp và công cụ được dùng.
- `visibility`: `public` (labeler tự tham gia được) | `private` (chủ dự án thêm thành viên).

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
| `classes` | Danh sách lớp (1–50 ký tự mỗi lớp, tối đa 100 lớp, không trùng). Bắt buộc với mọi `kind` trừ `transcription`, `pairwise`. Phân loại cần ≥ 2 lớp |
| `allowMultiple` | Chỉ `classification`: cho chọn nhiều lớp (mặc định `false`) |
| `required` | Mặc định `true`. `false` = labeler được bỏ trống công cụ này |
| `minItems`, `maxItems` | Công cụ dạng danh sách (bbox, polygon, span, temporalSegment): số mục tối thiểu / tối đa (mặc định 0 / 1000) |
| `maxLength` | Chỉ `transcription`: độ dài tối đa (mặc định 5000) |
| `allowTie` | Chỉ `pairwise`: cho chọn "ngang nhau". **Mặc định `true`**; khai `false` để bắt buộc chọn một bên |
| `matchThreshold` | Ngưỡng 0–1 để chấm câu vàng (mục 6.5). Bỏ trống = mặc định của loại công cụ |

Công cụ nào dùng được với loại dữ liệu nào:

| kind | image | text | audio | video | pair |
|---|---|---|---|---|---|
| `classification` | ✔ | ✔ | ✔ | ✔ | ✔ |
| `bbox` | ✔ | | | | |
| `polygon` | ✔ | | | | |
| `span` | | ✔ | | | |
| `transcription` | | | ✔ | ✔ | |
| `temporalSegment` | | | ✔ | ✔ | |
| `pairwise` | | | | | ✔ |

Sai hình dạng → 400 `tap_nhan_sai_dinh_dang` (`detail` liệt kê từng chỗ sai). Sai luật (tên trùng, công cụ không hợp loại dữ liệu…) → 400 `tap_nhan_khong_hop_le`. Frontend có thể kiểm trước bằng [label-schema.schema.json](../contracts/labeling/label-schema.schema.json) (ví dụ thư viện `ajv`, draft 2020-12).

### 4.5 Hướng dẫn, giá, kênh, điều kiện tham gia

```http
PUT /projects/{id}/guideline
{ "markdown": "# Huong dan\n...", "examples": [ { "sampleId": "...", "label": "xe", "isCorrect": true, "explanation": "..." } ] }

PUT /projects/{id}/pricing
{ "unitPriceVnd": 2000, "redundancy": 2, "maxRedundancy": 3, "budgetVnd": 500000, "deadline": "2027-01-31T00:00:00Z" }

PUT /projects/{id}/quality-control
{ "goldCheckPercent": 10 }

PUT /projects/{id}/channels
{ "allowProfessional": true, "allowLinkGateway": false, "allowCollaborative": false }

PUT /projects/{id}/eligibility
{ "minLevel": null, "minReputation": null, "requireEntranceTest": true, "entranceQuestionCount": 5, "entrancePassPercent": 80 }
```

- `unitPriceVnd`: thù lao **mỗi nhãn được duyệt**. `redundancy`: mỗi mẫu cần mấy người gán độc lập (mỗi người được trả riêng).
- `maxRedundancy` (tùy chọn, mặc định = `redundancy`, tối đa 10): **trần redundancy thích ứng**. Mẫu mà các labeler chọn khác nhau (không lựa chọn nào quá bán) sẽ được hệ thống tự xin thêm người gán, tối đa tới trần này. Bằng `redundancy` là tắt tính năng. Ký quỹ tính theo trần; phần không dùng tới được trả lại khi kết thúc dự án.
- `goldCheckPercent` (0–50, mặc định 10): phần trăm số lần cấp task là **câu vàng kiểm tra** (câu vàng `purpose: "qualityCheck"`, mục 4.7). Labeler không phân biệt được với task thật; câu kiểm tra **không trả tiền**. Trang dự án phía labeler nên công khai con số này.
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
→ { "items": [ { "id", "datasetId", "modality", "originalName", "sizeBytes",
                 "fileUrl": "http://localhost:9000/...",   // null với text / pair
                 "content": null,                         // {"text"} hoặc {"prompt","a","b"} với text / pair
                 "metadata": { "width": 300, "height": 200 } } ], ... }
```

`fileUrl` chỉ sống **5 phút**; hết hạn thì gọi lại API để lấy link mới.

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
```

Sau đó hỏi lại `GET /projects/{id}` mỗi 1–2 giây trong tối đa ~30 giây:

- `pendingApproval`: đã giữ tiền, chờ admin duyệt (quá 72 giờ không ai duyệt thì tự hủy, hoàn tiền).
- `draft` kèm `statusReason`: thiếu tiền. Hiện lý do + nút nạp tiền.

### 4.9 Vận hành

```http
GET /tasks/projects/{id}/progress
→ { "projectId", "totalTasks": 120, "openTasks": 80, "completedTasks": 35, "excludedTasks": 5,
    "activeLeases": 3, "submissions": 82, "requiredSubmissions": 230 }

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

Thành viên (dự án `private` cần thêm labeler bằng tay; reviewer luôn do chủ dự án thêm):

```http
GET    /projects/{id}/members        → [ { "userId", "role": "labeler", "state": "active", "joinedAt" } ]
POST   /projects/{id}/members        { "userId": "...", "role": "reviewer" }
POST   /projects/{id}/members/{userId}/block | /unblock
DELETE /projects/{id}/members/{userId}
```

Chặn labeler thì lượt task họ đang giữ bị thu hồi ngay.

### 4.10 Duyệt nhãn

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

**Gợi ý từ đồng thuận.** Khi một mẫu đủ người gán, quality-svc so các nhãn và đặt `consensusAgrees` trên từng nhãn: `true` (khớp kết quả số đông), `false` (lệch), `null` (chưa đủ người, hoặc tập nhãn không có công cụ phân loại / so sánh cặp để so). Cờ chỉ là gợi ý, **không** tự duyệt và không tự trả tiền. Màn hình duyệt nên tô nổi các nhãn `false` để người duyệt xem kỹ.

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
        "tot_hon": { "kind": "pairwise", "method": "majority", "final": { "choice": "a" }, "votes": { "a": 2 }, "disputed": false },
        "an_toan": { "kind": "classification", "method": "majority", "final": { "labelIds": ["an_toan"] }, "votes": { "an_toan": 2 }, "disputed": false }
      },
      "labels": [ { "tot_hon": { "choice": "a" }, "an_toan": { "labelIds": ["an_toan"] } }, { ... } ]
    }
  ]
}
```

- Kết quả **gộp theo từng công cụ** từ các nhãn đã duyệt:
  - `classification`, `pairwise`: `method: "majority"`. Một lựa chọn thắng khi được **hơn một nửa** số người chọn. Không lựa chọn nào quá bán thì `final: null`, `disputed: true` (tranh chấp, nên làm nổi bật cho doanh nghiệp xem lại).
  - Các công cụ còn lại (khung, đa giác, đoạn văn bản, chép lời, đoạn thời gian): `method: "none"`, **chưa gộp tự động**. Kết quả là `labels` (mỗi người một phần tử).
- `labelDistribution`: số lần mỗi lớp xuất hiện, theo từng công cụ. Dùng cho biểu đồ cảnh báo lệch lớp.
- `samples[].consensusStatus` (`agreed` | `disputed` | `notApplicable` | `null`) và `consensusFinal`: kết quả đồng thuận của quality-svc tính trên **mọi** nhãn đã nộp (kể cả chưa duyệt), khác với `tools` chỉ tính trên nhãn đã duyệt.

```http
GET /annotations/projects/{id}/export?format=json|csv|coco     → file tải về (Content-Disposition)
```

- `json`: giống `results`.
- `csv`: một dòng một mẫu, một cột một công cụ (UTF-8 có BOM, mở bằng Excel được).
- `coco`: chỉ dự án ảnh có công cụ `bbox` / `polygon`; dự án khác → 400 `coco_chi_cho_khung_anh`.

Gọi bằng `fetch` có header Authorization rồi tạo blob để tải (thẻ `<a href>` không gửi được token).

### 4.12 Chỉ số chất lượng (quality-svc)

Chủ dự án và admin xem được; người khác nhận 404.

```http
GET /quality/projects/{id}/summary
→ { "projectId", "labelCount": 15, "tasksEvaluated": 7,
    "agreed": 7, "disputed": 0, "notApplicable": 0, "waitingMoreLabels": 0,
    "redundancyIncreases": 1,
    "goldAnswers": 2, "goldAccuracyPercent": 50,
    "tools": [ { "tool": "cam_xuc", "krippendorffAlpha": 0.81, "itemCount": 7, "labelCount": 14, "computedAt": "..." } ] }
```

| Trường | Ý nghĩa |
|---|---|
| `tasksEvaluated` | Số task đã đủ người và được tính đồng thuận (trạng thái cuối của mỗi task) |
| `agreed` / `disputed` / `notApplicable` | Đồng thuận / vẫn tranh chấp khi đã hết trần / tập nhãn không có công cụ gộp được |
| `waitingMoreLabels` | Task đang tranh chấp, đã xin thêm người, chờ người gán tiếp |
| `redundancyIncreases` | Tổng số lần đã xin thêm người |
| `goldAccuracyPercent` | Tỉ lệ trả lời đúng câu vàng kiểm tra của cả dự án; `null` khi chưa có câu nào |
| `tools[].krippendorffAlpha` | Mức đồng thuận đã trừ phần trùng hợp ngẫu nhiên (1 = hoàn toàn nhất trí, ≤ 0 = như đoán bừa). Chỉ có cho công cụ phân loại một lớp và so sánh cặp; cập nhật theo lô (mặc định 15 phút) |

```http
GET /quality/projects/{id}/labelers
→ [ { "labelerId", "labelCount": 7, "agreementPercent": 100, "agreementSampleCount": 7,
      "goldAnswers": 1, "goldAccuracyPercent": 100,
      "dawidSkeneSkill": { "cam_xuc": 0.97 }, "reputation": 90 } ]
```

`dawidSkeneSkill` theo từng công cụ (0–1); rỗng khi labeler có dưới 5 nhãn trong dự án. Admin chạy lô Dawid–Skene ngay bằng `POST /quality/admin/dawid-skene/run`.

---

## 5. Labeler: nhận việc, gán nhãn, nhận tiền

### 5.1 Chợ dự án và tham gia

```http
GET  /projects?status=running&page=1&pageSize=20   dự án công khai đang chạy + dự án mình đã tham gia
GET  /projects/{id}                        chi tiết, có labelSchema + guideline để xem trước
POST /projects/{id}/join                   → 200 { "userId", "role": "labeler", "state": "active", "joinedAt" }
```

`join` trả 409 `can_lam_test` nếu dự án yêu cầu bài test đầu vào, 409 `du_an_rieng_tu` nếu dự án riêng tư.

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

- Đậu thì tự thành thành viên (`joinedProject: true`).
- Câu trả lời sai định dạng → 400, bài **chưa** bị tính là đã nộp; sửa rồi nộp lại.
- Server không bao giờ trả đáp án.

### 5.3 Nhận task

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

```http
POST /tasks/assignments/{assignmentId}/submit   { "payload": { ... } }
→ 200 { "assignmentId", "taskCompleted": false }
→ 400 nhan_sai_dinh_dang | nhan_khong_hop_le | thieu_nhan   (lease VẪN giữ — sửa rồi nộp lại)
→ 409 lease_het_han | lease_khong_con

POST /tasks/assignments/{assignmentId}/release   → 204 (trả task về hàng đợi ngay)
GET  /tasks/assignments/mine                     → [ LeaseResponse ] các task đang giữ (để khôi phục sau khi tải lại trang)
```

Nộp xong thì gọi `next` để lấy task tiếp theo.

### 5.5 Lịch sử và khiếu nại

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

Người chia sẻ link (`sharer`) dùng **cùng ví và cùng API** này: doanh thu cổng link vào `pendingVnd` rồi `availableVnd` như thu nhập labeler (mục 12).

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

---

## 6. Workspace gán nhãn: hợp đồng chi tiết

### 6.1 Frontend nhận gì

```json
{
  "assignmentId": "...", "projectId": "...", "taskId": "...", "sampleId": "...",
  "modality": "audio",
  "fileUrl": "http://localhost:9000/datasets/...wav?X-Amz-...",
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
| `modality` | Chọn khung hiển thị mẫu: ảnh, văn bản, player âm thanh, player video, hai cột A/B |
| `fileUrl` | Link xem / nghe file, sống 5 phút. `null` với text / pair. Hết hạn khi đang làm thì gọi lại `next` (trả lại đúng task, kèm link mới) |
| `content` | Nội dung text (`{"text"}`) hoặc pair (`{"prompt"?, "a", "b"}`). `null` với dữ liệu file |
| `metadata` | Thông tin mẫu, chỉ có những trường liên quan: `width`, `height` (ảnh, video), `durationSec`, `segmentStart`, `segmentEnd`, `sourceDurationSec` (audio, video), `length` (text) |
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

#### `transcription` — chép lời (audio, video)

- UI: ô nhập nhiều dòng, đếm ký tự theo `maxLength`.
- Gửi: `{ "text": "xin chao cac ban" }`.

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
| `video` | `<video src=fileUrl>` | Giống audio. Chưa vẽ khung lên video được (mục 11) |
| `pair` | `content.prompt`, `content.a`, `content.b` | Nên đảo ngẫu nhiên vị trí trái/phải trên màn hình để giảm thiên vị, nhưng **gửi lên theo tên gốc** `a` / `b` |

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

Ngưỡng đổi được bằng `matchThreshold` của công cụ.

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
      "defaultValue": 30, "value": 30, "version": 1, "updatedAt": "...", "updatedBy": null, "choices": null }, ... ]   (108 mục)

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

---

## 8. Dữ liệu cập nhật trễ: khi nào phải hỏi lại

Các service trao đổi với nhau qua hàng đợi sự kiện, nên một số thay đổi **xuất hiện ở service khác sau vài trăm ms đến vài giây**. Backend hiện **chưa có WebSocket / thông báo đẩy**; frontend hỏi lại (polling) có giới hạn số lần:

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
| `can_lam_test`, `du_an_rieng_tu`, `da_la_thanh_vien` | 409 | Không tham gia được |
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
| `khong_phai_sharer`, `chua_xac_thuc` | 403 / 401 | Tài khoản chưa có vai trò sharer / API key sai |
| `ma_gioi_thieu_sai`, `tu_gioi_thieu`, `qua_han_nhap_ma`, `da_co_nguoi_gioi_thieu`, `gioi_thieu_vong` | 404 / 409 | Nhập mã giới thiệu không được |
| `link_khong_ton_tai` | 404 | Trang vượt link: link không tồn tại / chưa duyệt xong / hết hạn / bị vô hiệu hoá |
| `sai_mat_khau`, `turnstile_that_bai` | 403 | Trang vượt link: mật khẩu sai / xác minh chống bot thất bại |
| `chua_het_dem_nguoc`, `da_nop` | 409 | Nộp trước khi hết đếm ngược / nộp trùng |
| `thieu_cau_tra_loi`, `nhan_sai_dinh_dang`, `nhan_khong_hop_le` | 400 | Thiếu câu trả lời / nhãn sai (phiên vẫn còn, sửa rồi nộp lại) |
| `phien_het_han`, `token_da_dung` | 410 | Bộ câu hết hạn / token mở link đã dùng: tải lại trang |
| `token_khong_hop_le` | 403 | Token mở link sai hoặc hết hạn (3 phút) |
| `xung_dot_setting` | 409 | (admin) Setting mâu thuẫn với setting khác (min > max) |
| `dinh_dang_chua_ho_tro`, `coco_chi_cho_khung_anh` | 400 | Định dạng xuất không hợp lệ |

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
| Mỗi công cụ dạng danh sách | ≤ 1.000 mục (mặc định); polygon 3–500 đỉnh |
| Câu vàng | ≤ 2.000 mỗi dự án (`project.gold_items_max`) |
| Phân trang | `pageSize` ≤ 100 |

---

## 11. Backend chưa có / cần lưu ý

| Mục | Ảnh hưởng tới frontend |
|---|---|
| **CORS chưa bật ở gateway** | Frontend chạy ở origin khác (ví dụ `http://localhost:5173`) sẽ bị trình duyệt chặn. Tạm thời dùng proxy của dev server (Vite `server.proxy` trỏ `/auth`, `/me`, `/projects`, `/tasks`, `/annotations`, `/ledger`, `/payments`, `/quality`, `/admin`, `/links`, `/g`, `/go`, `/gate` về `http://localhost:8080`); backend cần thêm chính sách CORS trước khi deploy |
| Upload thẳng lên MinIO | Link trỏ tới `http://localhost:9000` ở dev. Trình duyệt báo lỗi CORS khi `PUT` thì cấu hình MinIO cho phép origin của frontend (biến môi trường `MINIO_API_CORS_ALLOW_ORIGIN`) |
| Không có WebSocket / thông báo | Dùng polling theo mục 8 |
| Chưa gộp tự động khung, đa giác, đoạn văn bản, chép lời, đoạn thời gian | Màn hình kết quả hiện danh sách nhãn đã duyệt của từng người (`labels`) |
| Chưa có: khung trên video, điểm mốc (keypoint), đường gấp khúc, mặt nạ pixel, OCR, trả lời tự do cho văn bản / ảnh, thuộc tính cho từng đối tượng, quan hệ giữa thực thể, xếp hạng > 2 câu trả lời | Không làm UI cho các dạng này; thang điểm 1–5 tạm dùng `classification` với các lớp `"1"`…`"5"` |
| Chưa có API hồ sơ người dùng (tên người gán, cấp độ, uy tín) | Màn hình duyệt chỉ có `labelerId` |

---

## 12. Cổng link: người chia sẻ và trang vượt link

Kênh thu thập thứ hai (đặc tả 2.4). **Người chia sẻ** (vai trò `sharer`) rút gọn link và chia sẻ. **Khách vãng lai** mở link, giải 1–3 câu gán nhãn nhỏ (có câu vàng) thì được chuyển tới link đích. Người chia sẻ được trả tiền theo lượt vượt hợp lệ, vào **cùng ví** với thu nhập labeler (mục 5.7, rút tiền như labeler).

### 12.1 Người chia sẻ: tạo và quản lý link

```http
POST /links
{ "url": "https://example.com/bai-viet", "alias": "bai-viet-1", "password": "tuy-chon", "expiresAt": "2027-01-01T00:00:00Z", "campaignId": null }
→ 201 { "id", "code": "bai-viet-1", "shortUrl": "http://localhost:8080/g/bai-viet-1", "destinationUrl", "domain",
        "status": "pendingScan", "statusReason": null, "hasPassword": true, "expiresAt", "campaignId", "createdAt" }
```

- Mọi trường trừ `url` đều tuỳ chọn. Không có `alias` thì backend sinh mã ngẫu nhiên.
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

**API key (FS-05) và Quick Link (FS-02)** — cho công cụ tự động / plugin trình duyệt:

```http
POST /links/api-key            → { "apiKey": "lk_…", "prefix": "lk_3f9a1b", "createdAt" }   ← key đầy đủ CHỈ hiện lần này
GET  /links/api-key            → { "apiKey": null, "prefix", "createdAt" }  |  404 nếu chưa tạo
POST /links           (header X-Api-Key: lk_…)        ← cùng body như trên, không cần token
GET  /links/quick?api=lk_…&url=https://...&alias=...  → text/plain "http://localhost:8080/g/aB3xY9k"
```

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

