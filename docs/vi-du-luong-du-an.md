# Ví dụ dễ hiểu: một dự án đi từ lúc tạo đến lúc trả thưởng

Tài liệu đi theo **một dự án giả định** qua từng bước. Ở mỗi bước, tài liệu ghi:

- chuyện gì xảy ra ngoài đời;
- API nào được gọi;
- **bảng nào trong cơ sở dữ liệu thay đổi** (in ra các dòng, giải thích từng cột);
- service nào được báo tin, và nhận tin xong thì ghi gì.

Dữ liệu là giả định, ID được rút gọn cho dễ đọc. Nhưng tên bảng, tên cột, tên event và luật xử lý đúng như hệ thống đang chạy.

---

# Phần A. Chuẩn bị trước khi đọc

## A.1 Câu chuyện, kể không dùng từ kỹ thuật

Công ty Văn Phong có 3 bình luận của khách hàng và muốn biết mỗi câu là **khen** hay **chê**. Họ lên hệ thống, tạo một dự án:

- Họ đưa 3 bình luận lên, và đặt câu hỏi: *"Bình luận này là khen hay chê?"*.
- Họ cài thêm 1 **câu vàng**: một bình luận họ đã biết chắc đáp án, để kiểm tra người làm có làm thật không.
- Họ trả **20.000 đ cho mỗi người làm đạt**, tối đa **3 người**. Bài **đạt** khi giống số đông ít nhất 75% **và** đúng câu vàng.
- Nền tảng thu phí 30%, tức 6.000 đ cho mỗi người đạt. Vì vậy công ty phải đặt cọc trước 3 × (20.000 + 6.000) = **78.000 đ**.

Admin duyệt dự án. 4 người vào làm: An, Bình, Chi, Dũng. Mỗi người làm cả 4 câu (3 câu thật và câu vàng lẫn vào), không ai biết câu nào là câu vàng.

Công ty bấm kết thúc. Hệ thống chấm:

- An và Bình làm tốt, mỗi người nhận 20.000 đ.
- Chi khác số đông ở một câu, chỉ đạt 66%, nên trượt.
- Dũng giống số đông cả 3 câu nhưng sai câu vàng, nên cũng trượt.

Sau 3 ngày không ai khiếu nại, dự án đóng hẳn. Công ty được trả lại 26.000 đ của suất thứ ba không dùng tới.

## A.2 Nhân vật và ID rút gọn

| ID trong ví dụ | Là ai / cái gì |
|---|---|
| `DN1` | Công ty Văn Phong (vai trò **business**) |
| `L1` An, `L2` Bình, `L3` Chi, `L4` Dũng | 4 người làm (vai trò **labeler**) |
| `AD` | Quản trị viên (vai trò **admin**) |
| `DA1` | Dự án "Khen hay chê?" |
| `M1`, `M2`, `M3` | 3 mẫu thật (3 bình luận cần gán nhãn) |
| `M4` | Mẫu dùng làm câu vàng |
| `B1`, `B2`, `B3`, `B4` | Bài làm của An, Bình, Chi, Dũng |

Trong hệ thống thật, mọi ID là UUID, ví dụ `01a1d57a-d3c2-7f74-ab87-0745ed4c9f33`.

## A.3 Đề bài và đáp án của từng người

| Mẫu | Nội dung | Câu hỏi người làm thấy | Đáp án đúng | An | Bình | Chi | Dũng |
|---|---|---|---|---|---|---|---|
| M1 | "Giao hàng nhanh, đúng mô tả." | Bình luận này là khen hay chê? | khen | khen | khen | **chê** ✘ | khen |
| M2 | "Hàng vỡ, gọi tổng đài không ai nghe." | Bình luận này là khen hay chê? | chê | chê | chê | chê | chê |
| M3 | "Giao chậm nhưng đóng gói rất kỹ." | **Xét cả câu, người viết có hài lòng không?** (câu hỏi riêng) | khen | khen | khen | khen | khen |
| M4 (vàng) | "Chất lượng tệ, phí tiền." | Bình luận này là khen hay chê? | chê | chê | chê | chê | **khen** ✘ |

## A.4 Service nào giữ bảng nào

Hệ thống gồm nhiều service. **Mỗi service có database riêng** và không service nào được ghi vào database của service khác. Các bảng xuất hiện trong ví dụ:

| Service | Database | Bảng dùng trong ví dụ | Giữ cái gì |
|---|---|---|---|
| **identity-svc** | identity_db | `users`, `business_profiles`, `labeler_stats` | Tài khoản, hồ sơ doanh nghiệp, cấp độ người làm |
| **payment-svc** | payment_db | `payment_intents` | Lệnh nạp tiền qua cổng thanh toán |
| **project-svc** | project_db | `projects`, `samples`, `gold_items`, `project_members`, `project_collections`, `attempts`, `annotations`, `sample_results` | Dự án, mẫu, câu vàng, thành viên, đợt thu bài, bài làm, nhãn, kết quả |
| **ledger-svc** | ledger_db | `accounts`, `journal_entries`, `journal_lines`, `project_escrows`, `holds` | **Tiền**: số dư, sổ ghi chép mọi lần chuyển tiền, ký quỹ |
| **quality-svc** (Python) | quality_db | `pending_attempts`, `project_gradings`, `sample_majorities`, `reputations` | Chấm bài, tính uy tín |
| **notification-svc** | notification_db | `notifications` | Thông báo gửi người dùng |
| (mỗi service) | (của nó) | `outbox`, `processed_events` | Thư đi và sổ "đã xử lý thư nào" (mục A.5) |

## A.5 Các service báo tin cho nhau thế nào

Ví dụ: project-svc cần nhờ ledger-svc giữ tiền ký quỹ. Nó **không gọi thẳng** sang ledger. Thay vào đó:

1. project-svc đổi dữ liệu của mình (dự án sang "chờ ký quỹ"), **và cùng lúc** ghi một "lá thư" vào bảng `outbox` của chính nó. Hai việc nằm trong **một transaction**: hoặc cùng thành công, hoặc cùng không có.
2. Một tiến trình nền đọc bảng `outbox`, gửi thư lên **RabbitMQ** (bưu điện), rồi đánh dấu đã gửi.
3. ledger-svc nhận thư. Trong **một transaction**, nó vừa xử lý (giữ tiền) vừa ghi vào bảng `processed_events` rằng "thư số X đã xử lý". Nếu bưu điện lỡ giao trùng thư X lần nữa, ledger thấy X đã có trong sổ và bỏ qua, nên không trừ tiền hai lần.

Mỗi lá thư gọi là một **event**. Trong tài liệu, event được vẽ như sau:

```
project-svc ──[project.publish_requested]──► ledger-svc
   nội dung: { projectId: DA1, ownerId: DN1, escrowAmountVnd: 78000 }
```

Ngoài event, có hai cách đọc dữ liệu của service khác: **gRPC** (gọi hàm trực tiếp, dùng khi cần đọc ngay) và **view** (service chủ mở một "cửa sổ chỉ đọc" vào database của nó). Các bước 9 và 12 có ví dụ.

## A.6 Các trạng thái của một dự án

```
Draft ──publish──► PendingEscrow ──đủ tiền──► PendingApproval ──admin duyệt──► Running
 (nháp)            (chờ ký quỹ)    │           (chờ duyệt)                      (đang nhận bài)
                                   └─thiếu tiền─► về Draft                          │
                                                                          đóng thu bài
                                                                                   ▼
                                     Completed ◄──hết hạn khiếu nại── Grading
                                     (đã xong)                         (đang chấm / chờ khiếu nại)
```

Các setting dùng trong ví dụ đều là giá trị mặc định: hệ số người làm = 2, hạn làm bài = 48 giờ, hạn khiếu nại = 3 ngày, phí nền tảng = 30%, tiền thưởng không bị treo (khả dụng ngay).

---

# Phần B. Từng bước

## Bước 0. Công ty nạp 500.000 đ (01/12, 08:00)

**Ngoài đời:** Công ty Văn Phong đã đăng ký tài khoản, xác thực email và được admin duyệt hồ sơ doanh nghiệp (KYB). Giờ họ nạp tiền vào ví.

**identity_db** đã có sẵn từ lúc đăng ký:

`users`:

| id | email | display_name | status | email_verified_at |
|---|---|---|---|---|
| DN1 | vanphong@vd.vn | Công ty Văn Phong | Active | 30/11 15:00 |

- `status`: `Active` hoặc `Blocked`. Tài khoản bị khoá thì không làm gì được.
- `email_verified_at`: thời điểm bấm link xác thực. Chưa xác thực thì chưa được publish dự án hay rút tiền.

`business_profiles`:

| user_id | company_name | tax_code | status |
|---|---|---|---|
| DN1 | Công ty TNHH Văn Phong | 0109998887 | Verified |

`status = Verified` nghĩa là admin đã duyệt KYB. Chỉ doanh nghiệp đã duyệt mới publish được dự án.

**Gọi:** `POST /payments/deposits {"amountVnd": 500000}`. Hệ thống trả về link thanh toán; công ty trả tiền ở cổng (MoMo / VNPay / sandbox). Cổng báo về là đã trả xong.

**payment_db.`payment_intents`:**

| id | business_id | amount_vnd | provider | status | provider_txn_id |
|---|---|---|---|---|---|
| NT1 | DN1 | 500000 | sandbox | Pending → **Succeeded** | SBX-123 |

- Tiền luôn lưu bằng **số nguyên đồng** (`bigint`), không dùng số thực, để không sai số làm tròn.
- `provider_txn_id`: mã giao dịch phía cổng thanh toán, dùng khi đối soát.

**Gửi đi:**
```
payment-svc ──[deposit.confirmed]──► ledger-svc, notification-svc
   nội dung: { intentId: NT1, businessId: DN1, amountVnd: 500000 }
```

**ledger-svc ghi sổ.** Sổ cái ghi theo kiểu **ghi kép**: mỗi lần tiền di chuyển là một **bút toán** (`journal_entries`), gồm nhiều dòng (`journal_lines`). **Tổng các dòng luôn bằng 0**: tiền chỉ chuyển từ chỗ này sang chỗ khác, không tự sinh ra hay mất đi.

`journal_entries`:

| seq | type | reference | description |
|---|---|---|---|
| #1 | Deposit | deposit:NT1 | Nạp tiền qua sandbox |

- `seq`: số thứ tự toàn hệ thống.
- `reference`: không trùng cho cùng một loại bút toán. Thư `deposit.confirmed` có đến hai lần thì lần thứ hai bị chặn, không cộng tiền hai lần.
- Bảng còn có `prev_hash`, `hash`: mỗi bút toán băm cả bút toán trước nó, nên ai sửa lén một bút toán cũ thì chuỗi băm bị gãy. Bảng cũng cấm UPDATE / DELETE.

`journal_lines` của bút toán #1:

| account_code | amount |
|---|---|
| gateway:sandbox | −500.000 |
| business:DN1:available | +500.000 |

`accounts` (số dư, cộng dồn từ các dòng trên):

| code | balance | Nghĩa |
|---|---|---|
| business:DN1:available | 500.000 | Ví của công ty, tiền dùng được |

**notification-svc** tạo một dòng `notifications` cho DN1: "Nạp tiền thành công — Đã cộng 500.000 đ vào ví doanh nghiệp."

---

## Bước 1. Tạo dự án (08:10)

**Ngoài đời:** Công ty bấm "Tạo dự án", nhập tên, chọn loại dữ liệu là văn bản, để dự án công khai.

**Gọi:** `POST /projects`
```json
{ "name": "Khen hay chê?", "description": "Bình luận của khách", "modality": "text", "visibility": "public" }
```

**project_db.`projects`.** Dòng mới. Bảng này có nhiều cột nên được chia nhóm:

| Nhóm | Cột | Giá trị | Ý nghĩa |
|---|---|---|---|
| Định danh | `id` | DA1 | |
| | `owner_id` | DN1 | Chủ dự án. Chỉ chủ mới sửa được dự án |
| | `name`, `description` | Khen hay chê?, Bình luận của khách | |
| | `modality` | text | Loại dữ liệu: image / text / audio / video / pair / list. Tạo rồi thì không đổi được |
| Trạng thái | `status` | **Draft** | Đang soạn |
| | `status_reason` | null | Lý do khi bị trả về (ví dụ "thiếu tiền ký quỹ") |
| | `visibility` | Public | **Public**: hiện ở chợ dự án. **Private**: chỉ người được mời / có mã mới vào. **Approval**: phải gửi đơn, chủ duyệt |
| Câu hỏi và công cụ | `label_schema` | null | Chưa cấu hình (bước 2) |
| Tiền | `reward_vnd` | 0 | Tiền thưởng cho mỗi bài đạt (bước 3) |
| | `slots` | 0 | Số suất thưởng (bước 3) |
| | `pass_threshold_percent` | 80 | Ngưỡng đạt, mặc định 80 (bước 3) |
| | `platform_fee_percent` | 0 | Phí nền tảng. **Chỉ chốt lúc publish**; sau đó admin đổi phí thì dự án này vẫn giữ mức cũ |
| | `question_count` | 0 | Số câu thật trong một bài. Chốt lúc publish |
| Kênh làm việc | `allow_professional` | true | Người làm vào web làm trực tiếp |
| | `allow_link_gateway` | false | Khách lạ làm vài câu khi bấm link rút gọn (cổng vượt link) |
| | `gate_budget_vnd`, `gate_unit_price_vnd` | 0 | Ngân sách và đơn giá cho cổng vượt link |
| Điều kiện tham gia | `min_level`, `min_reputation` | null | Cấp độ / uy tín tối thiểu. null là không yêu cầu |
| | `require_verified_identity` | false | Có bắt người làm xác minh căn cước không |
| | `require_entrance_test` | false | Có bắt làm bài test đầu vào không |
| Bảo vệ dữ liệu | `nda_required`, `sensitive_content`, `delete_files_after_close` | false | Ký cam kết bảo mật, cảnh báo nội dung nhạy cảm, xoá file sau khi đóng |
| Thời điểm | `created_at` | 01/12 08:10 | |
| | `deadline` | null | Hạn chót nhận bài |
| | `published_at`, `closed_at`… | null | Điền dần theo vòng đời |
| | `was_escrowed` | false | Đã từng giữ ký quỹ chưa. Nếu huỷ, cột này cho biết có phải hoàn tiền không |

**project_db.`project_members`.** Công ty tự thành thành viên với vai trò chủ:

| project_id | user_id | role | state | joined_at |
|---|---|---|---|---|
| DA1 | DN1 | Owner | Active | 08:10 |

**Gửi đi:** `project-svc ──[member.added]──► admin-svc` (admin chỉ ghi vào nhật ký hệ thống).

---

## Bước 2. Đặt câu hỏi và công cụ gán nhãn (08:15)

**Ngoài đời:** Công ty tạo một công cụ "chọn một trong hai: khen / chê" và viết câu hỏi cho công cụ đó.

**Gọi:** `PATCH /projects/DA1`
```json
{ "labelSchema": {
    "tools": [ { "name": "cam_xuc", "kind": "classification", "classes": ["khen", "che"],
                 "question": "Bình luận này là khen hay chê?" } ] } }
```

**`projects.label_schema`** (cột kiểu JSON) của DA1 được lưu ở dạng đầy đủ:
```json
{ "modality": "text",
  "tools": [ { "name": "cam_xuc", "kind": "classification", "classes": ["khen", "che"],
               "question": "Bình luận này là khen hay chê?", "required": true, "allowMultiple": false } ] }
```

| Trường | Ý nghĩa |
|---|---|
| `tools` | Danh sách công cụ. Một câu có thể dùng nhiều công cụ, ví dụ vừa chọn loại vừa khoanh vùng trên ảnh |
| `name` | Tên công cụ. Câu trả lời luôn có dạng `{"cam_xuc": …}`, tức trả lời theo tên công cụ |
| `kind` | Loại công cụ: `classification` (chọn lớp), `bbox` (khoanh hộp), `span` (bôi chữ), `transcription` (gõ lại lời nói), `ranking` (xếp hạng)… Loại công cụ quyết định cách so khớp khi chấm |
| `classes` | Các lựa chọn. Người làm chỉ được chọn trong danh sách này |
| `required` | true: bỏ trống câu này thì bị tính là không trả lời |
| `allowMultiple` | false: chỉ được chọn một lớp |
| `question` | **Câu hỏi của công cụ**, hiện ngay trên công cụ. Mỗi công cụ một câu; công cụ nào thiếu thì chưa publish được (lưu nháp vẫn được) |

---

## Bước 3. Đặt giá (08:16)

**Gọi:** `PATCH /projects/DA1`
```json
{ "pricing": { "rewardVnd": 20000, "slots": 3, "passThresholdPercent": 75, "deadline": "2026-12-31T00:00:00Z" } }
```

**`projects`** đổi:

| Cột | Cũ → mới | Ý nghĩa |
|---|---|---|
| `reward_vnd` | 0 → **20.000** | Mỗi bài đạt được 20.000 đ. Trả **theo cả bài**, không theo từng câu |
| `slots` | 0 → **3** | Tối đa 3 bài đạt được trả tiền. Hệ thống chỉ cho chọn trong khoảng 3–50 |
| `pass_threshold_percent` | 80 → **75** | Bài đạt khi giống số đông ≥ 75% **và** đúng hết câu vàng. Chỉ cho chọn trong khoảng 60–95 |
| `deadline` | null → 31/12 | Sau ngày này dự án tự đóng thu bài |

Nếu nhập thưởng 5.000 đ (dưới mức tối thiểu 10.000 đ) hoặc ngưỡng 99% thì API trả lỗi 400 ngay.

---

## Bước 4. Nạp 4 mẫu (08:20)

**Ngoài đời:** Công ty dán 4 bình luận. Riêng M3 họ muốn hỏi khác đi, nên đặt câu hỏi riêng cho nó.

**Gọi:** `POST /projects/DA1/samples`
```json
{ "rows": [
  { "name": "M1", "text": "Giao hàng nhanh, đúng mô tả." },
  { "name": "M2", "text": "Hàng vỡ, gọi tổng đài không ai nghe." },
  { "name": "M3", "text": "Giao chậm nhưng đóng gói rất kỹ.", "question": "Xét cả câu, người viết có hài lòng không?" },
  { "name": "M4", "text": "Chất lượng tệ, phí tiền." } ] }
```

**project_db.`samples`:**

| project_id | id | modality | content | question | sha256 | metadata | ingest_state | excluded_at |
|---|---|---|---|---|---|---|---|---|
| DA1 | M1 | text | `{"text": "Giao hàng nhanh, đúng mô tả."}` | null | a1f3… | `{"length": 28}` | ready | null |
| DA1 | M2 | text | `{"text": "Hàng vỡ, gọi tổng đài không ai nghe."}` | null | 8e9a… | `{"length": 36}` | ready | null |
| DA1 | M3 | text | `{"text": "Giao chậm nhưng đóng gói rất kỹ."}` | **Xét cả câu, người viết có hài lòng không?** | c698… | `{"length": 32}` | ready | null |
| DA1 | M4 | text | `{"text": "Chất lượng tệ, phí tiền."}` | null | b171… | `{"length": 24}` | ready | null |

| Cột | Ý nghĩa |
|---|---|
| `content` | Nội dung với văn bản. Mẫu là file (ảnh, âm thanh, video) thì cột này null, và có `storage_key` trỏ tới file trong kho MinIO |
| `question` | **Ngữ cảnh / câu hỏi riêng** của mẫu (tuỳ chọn), hiện ở đầu câu, phía trên câu hỏi của công cụ. Người làm thấy: M3 → câu riêng + câu hỏi công cụ; M1, M2, M4 → chỉ câu hỏi công cụ |
| `sha256` | Mã băm của nội dung, dùng chống nạp trùng: **cùng nội dung + cùng câu hỏi** thì bị từ chối. Cùng một ảnh nhưng hỏi hai câu khác nhau thì được, vì là hai mẫu |
| `metadata` | Thông tin phụ: độ dài chữ, kích thước ảnh, thời lượng âm thanh… |
| `ingest_state` | Tiến trình nạp: `queued` → `processing` → `ready` hoặc `failed`. Văn bản `ready` ngay. File thì phải chờ tiến trình nền đọc file, tính băm và metadata |
| `excluded_at` | Điền khi mẫu bị loại vì người làm báo mẫu lỗi và được chấp nhận. Mẫu bị loại không tính khi chấm |

**Mỗi mẫu chính là một câu hỏi trong bài làm.** Hệ thống không có bảng "task" riêng.

---

## Bước 5. Biến M4 thành câu vàng (08:25)

**Gọi:** `POST /projects/DA1/gold-items`
```json
{ "items": [ { "sampleId": "M4", "purpose": "qualityCheck", "expectedPayload": { "cam_xuc": { "labelIds": ["che"] } } } ] }
```

**project_db.`gold_items`:**

| id | project_id | sample_id | purpose | expected_payload |
|---|---|---|---|---|
| V1 | DA1 | M4 | QualityCheck | `{"cam_xuc": {"labelIds": ["che"]}}` |

| Cột | Ý nghĩa |
|---|---|
| `sample_id` | Câu vàng **vẫn là một mẫu bình thường** trong `samples`; bảng này chỉ thêm đáp án |
| `purpose` | `QualityCheck`: trộn vào bài làm để kiểm tra. `EntranceTest`: dùng cho bài test đầu vào |
| `expected_payload` | Đáp án đúng, cùng dạng với câu trả lời người làm gửi lên |

**Đáp án câu vàng không bao giờ rời project-svc.** Người làm không biết câu nào là câu vàng; các service khác chỉ được biết "đúng 1/1" hay "đúng 0/1".

**Kiểm tra trước khi publish:** `GET /projects/DA1/readiness` trả về:
```json
{ "ready": true, "missing": [], "sampleCount": 4, "questionCount": 3, "qualityGoldCount": 1,
  "platformFeePercent": 30, "platformFeePerPassVnd": 6000, "estimatedCostVnd": 78000 }
```
- `questionCount = 3`: 4 mẫu trừ 1 câu vàng.
- `estimatedCostVnd = 78.000`: 3 suất × (20.000 + 6.000 phí).
- Còn thiếu gì thì `missing` liệt kê, ví dụ `chua_co_cau_hoi`. Giao diện hiển thị thành danh sách cần làm.

---

## Bước 6. Publish: giữ tiền ký quỹ (08:30)

**Ngoài đời:** Công ty bấm "Publish". Hệ thống phải **khoá 78.000 đ** trong ví họ trước khi cho dự án chạy, để chắc chắn có tiền trả người làm.

Tiền nằm ở **ledger-svc**, dự án nằm ở **project-svc**: hai database khác nhau. Vì vậy việc này đi qua **2 lá thư**. Cách làm nhiều bước nối nhau bằng thư như vậy gọi là **saga**.

### 6a. project-svc nhận lệnh publish (08:30:00)

**Gọi:** `POST /projects/DA1/publish`

Trong **một transaction**, project-svc:

1. Đổi `projects`:

   | Cột | Cũ → mới |
   |---|---|
   | `status` | Draft → **PendingEscrow** (chờ ký quỹ) |
   | `platform_fee_percent` | 0 → **30** (chốt từ setting `fee.platform_percent`) |
   | `question_count` | 0 → **3** |

2. Ghi thư vào `outbox`:

   | Cột | Giá trị | Ý nghĩa |
   |---|---|---|
   | id | TH1 | Mã thư (`eventId`) |
   | event_type | project.publish_requested | Loại thư |
   | correlation_id | C1 | Mã của "cả câu chuyện publish". Mọi thư sinh ra từ đây đều mang C1, nên tra log theo C1 là thấy toàn bộ chuỗi |
   | envelope_json | (bên dưới) | Toàn bộ lá thư |
   | occurred_at | 08:30:00.000 | Lúc ghi |
   | published_at | null → 08:30:00.300 | Lúc tiến trình nền gửi lên RabbitMQ xong |
   | attempt_count, last_error, next_attempt_at | 0, null, … | Gửi lỗi thì thử lại, giãn cách dần |

```
project-svc ──[project.publish_requested]──► ledger-svc, admin-svc
   nội dung: { projectId: DA1, ownerId: DN1, projectName: "Khen hay chê?", escrowAmountVnd: 78000 }
```

API trả về ngay `status: "pendingEscrow"`, chưa chờ ledger xử lý xong.

### 6b. ledger-svc giữ tiền (08:30:00.4)

Trong **một transaction**, ledger-svc:

1. Ghi `processed_events`: `(event_id = TH1, handler = "ledger-svc.project-publish-requested")`, để đánh dấu đã xử lý thư TH1.
2. Kiểm ví DN1: còn 500.000 ≥ 78.000, đủ.
3. Ghi bút toán #2:

   | seq | type | reference | description |
   |---|---|---|---|
   | #2 | EscrowReserve | escrow:DA1 | Ký quỹ dự án |

   | account_code | amount |
   |---|---|
   | business:DN1:available | −78.000 |
   | escrow:project:DA1 | +78.000 |

4. Tạo dòng `project_escrows`:

   | Cột | Giá trị | Ý nghĩa |
   |---|---|---|
   | project_id, owner_id | DA1, DN1 | |
   | reserved_vnd | 78.000 | Đã giữ bao nhiêu |
   | state | **Active** | `Active` → (`Closing`) → `Closed` |
   | slots, reward_vnd, platform_fee_vnd | 0, 0, 0 | Chưa biết. Sẽ điền khi dự án chạy (bước 7) |
   | gate_budget_vnd, gate_spent_vnd | 0, 0 | Ngân sách cổng vượt link và phần đã tiêu |
   | expected_paid_attempts, closed_at | null | Dùng lúc đóng (bước 15) |

5. Ghi thư trả lời vào `outbox` của ledger:

```
ledger-svc ──[escrow.reserved]──► project-svc
   nội dung: { projectId: DA1, amountVnd: 78000 }
   correlation_id = C1, causation_id = TH1  (= "thư này được gửi vì thư TH1")
```

`accounts` lúc này:

| code | balance |
|---|---|
| business:DN1:available | 500.000 → **422.000** |
| escrow:project:DA1 | **78.000** |

Ký quỹ là một **tài khoản riêng của dự án DA1** trong sổ cái. Tiền ở đây không dùng được cho dự án khác.

### 6c. project-svc nhận tin đã đủ tiền (08:30:01)

Trong **một transaction**:
- `processed_events`: `(event_id = escrow.reserved, handler = "project-svc.escrow-reserved")`.
- `projects`: `status` PendingEscrow → **PendingApproval**, `was_escrowed = true`, `submitted_for_approval_at = 08:30:01`.

**Nếu ví không đủ tiền:** ledger gửi `escrow.reserved` thay bằng `escrow.rejected`. project đưa dự án về `Draft`, ghi `status_reason = "Không đủ số dư để ký quỹ"`, và notification báo cho công ty.

---

## Bước 7. Admin duyệt, dự án bắt đầu chạy (09:00)

**Ngoài đời:** Admin xem danh sách chờ duyệt (`GET /projects/pending-approval`), thấy dự án ổn, bấm duyệt.

**Gọi:** `POST /projects/DA1/approve` (người gọi: AD)

Trong **một transaction**, project-svc:

1. `projects`: `status` → **Running**, `published_at = 09:00`.
2. Tạo dòng **`project_collections`**, tức **đợt thu bài**:

   | Cột | Giá trị | Ý nghĩa |
   |---|---|---|
   | project_id | DA1 | Mỗi dự án có đúng một đợt thu bài |
   | max_participants | **6** | **Trần số người làm = số suất × hệ số = 3 × 2**. Cho nhiều người làm hơn số suất để "số đông" đáng tin hơn; tiền vẫn chỉ trả cho 3 bài đạt nộp sớm nhất |
   | paused_at | null | Điền khi chủ tạm dừng |
   | finish_reason | null | Lý do đóng thu bài (bước 11) |
   | graded_at, appeal_deadline | null | Lúc chấm xong, hạn khiếu nại (bước 13) |
   | settled_at, passed_count | null | Lúc chốt, số bài đạt (bước 15) |

   **Dòng này là "ổ khoá" của dự án.** Mỗi khi có người bắt đầu làm, nộp bài, hay chủ bấm dừng / kết thúc, hệ thống khoá dòng này trước (`SELECT … FOR UPDATE`), xong việc mới mở. Vì vậy, khi chỉ còn 1 chỗ mà 2 người bấm cùng lúc, chỉ một người vào được, không bao giờ vượt trần 6.

3. Ghi thư:

```
project-svc ──[project.published]──► ledger-svc, notification-svc, ml-svc
   nội dung: { projectId: DA1, ownerId: DN1, rewardVnd: 20000, platformFeeVnd: 6000, slots: 3,
               passThresholdPercent: 75, questionCount: 3, deadline: 31/12, labelSchema: {…}, … }
```

**Bên nhận làm gì:**
- **ledger-svc** điền điều khoản vào `project_escrows`: `slots = 3`, `reward_vnd = 20000`, `platform_fee_vnd = 6000`. Từ giờ ledger tự chặn: không bao giờ trả quá 3 bài cho dự án này.
- **notification-svc** gửi công ty: "Dự án đã được duyệt và đang chạy".

`GET /tasks/projects/DA1/slots` lúc này trả `{ slots: 3, maxParticipants: 6, submitted: 0, inProgress: 0, available: 6, phase: "open" }`.

---

## Bước 8. Bốn người vào dự án (10:00)

**Ngoài đời:** An mở "Chợ dự án" (`GET /projects/market`) và thấy thẻ: "Khen hay chê? · 20.000 đ/bài · 3 suất · 3 câu · ngưỡng 75%". Chợ chỉ hiện dự án công khai, đang chạy, mà người xem **đủ điều kiện** làm ngay. An bấm "Tham gia". Bình, Chi, Dũng cũng vậy.

**Gọi (mỗi người):** `POST /projects/DA1/join`

**project_db.`project_members`:**

| project_id | user_id | role | state | blocked_by_owner_list | joined_at |
|---|---|---|---|---|---|
| DA1 | DN1 | Owner | Active | false | 08:10 |
| DA1 | L1 (An) | Labeler | Active | false | 10:00 |
| DA1 | L2 (Bình) | Labeler | Active | false | 10:00 |
| DA1 | L3 (Chi) | Labeler | Active | false | 10:01 |
| DA1 | L4 (Dũng) | Labeler | Active | false | 10:01 |

| Cột | Ý nghĩa |
|---|---|
| `role` | Owner (chủ), Labeler (người làm), Reviewer (người duyệt) |
| `state` | Active, hoặc Blocked (bị chủ chặn khỏi dự án) |
| `blocked_by_owner_list` | true nếu công ty đã đưa người này vào danh sách chặn chung của họ |

Bảng này dùng để **phân quyền**: muốn xem mẫu, mở bài, xem kết quả của DA1 thì phải có dòng `Active` ở đây.

**Gửi đi (mỗi người):** `project-svc ──[member.added {via: "join"}]──► admin-svc` (ghi nhật ký).

Nếu dự án là **Private** thì người làm phải nhập mã mời (bảng `invite_codes`). Nếu là **Approval** thì phải gửi đơn (bảng `join_requests`). Nếu có bài test đầu vào thì phải làm test trước (bảng `entrance_attempts`). Nếu có NDA thì phải ký trước (bảng `project_consents`).

---

## Bước 9. An bắt đầu làm bài (10:05)

**Gọi:** `POST /tasks/projects/DA1/attempt` (người gọi: An)

**project-svc kiểm tra lần lượt:**
1. An đã nộp bài dự án này chưa? Chưa. Nếu đã nộp thì trả lỗi `da_nop_bai`. Nếu An đang có bài làm dở thì trả lại đúng bài đó, không tạo bài mới.
2. An có trong `project_members` với `Active` không? Có.
3. **Hỏi identity-svc qua gRPC:** tài khoản An có bị khoá không, cấp độ bao nhiêu, uy tín bao nhiêu? Dự án không yêu cầu cấp / uy tín nên qua. identity không trả lời thì báo lỗi 503, vì không kiểm được thì không cho làm.
4. **Khoá dòng `project_collections` của DA1**, đếm số người đang làm hoặc đã nộp: 0 < 6, còn chỗ.
5. Lập đề: 3 mẫu thật + câu vàng M4, **xáo thứ tự riêng cho An**.
6. Hạn làm = bây giờ + 48 giờ = 03/12 10:05 (nếu hạn chót dự án sớm hơn thì lấy hạn chót).

**project_db.`attempts`.** Dòng mới:

| Cột | Giá trị | Ý nghĩa |
|---|---|---|
| id | B1 | Bài của An |
| project_id, labeler_id | DA1, L1 | Mỗi người chỉ có **một** bài đang làm hoặc đã nộp trong một dự án (có index UNIQUE chặn) |
| state | **InProgress** | `InProgress` → `Submitted`; hoặc `Expired` (quá hạn), `Cancelled` (bỏ bài) |
| started_at, expires_at | 01/12 10:05, **03/12 10:05** | |
| question_order | **[M3, M1, M4, M2]** | Thứ tự câu đã xáo. Câu vàng M4 lẫn ở vị trí 3 |
| submitted_at, answered_count | null | Điền lúc nộp |
| gold_total, gold_correct, gold_answers | 0, 0, null | Điền lúc nộp |
| client_ip, device_id | null | Điền lúc nộp (dùng chống gian lận) |
| comparable_count, matched_count, agreement_percent, passed, rewarded, graded_at | null | **Kết quả chấm**, điền ở bước 13 |
| appeal_state | None | Khiếu nại: None / Open / Accepted / Rejected |

**An nhận được đề:**
```
Câu 1  "Giao chậm nhưng đóng gói rất kỹ."      — Xét cả câu, người viết có hài lòng không?   [khen] [chê]
Câu 2  "Giao hàng nhanh, đúng mô tả."           — Bình luận này là khen hay chê?               [khen] [chê]
Câu 3  "Chất lượng tệ, phí tiền."               — Bình luận này là khen hay chê?               [khen] [chê]
Câu 4  "Hàng vỡ, gọi tổng đài không ai nghe."   — Bình luận này là khen hay chê?               [khen] [chê]
```
Đề **không có** dấu hiệu nào cho biết câu 3 là câu vàng.

**Trong lúc An làm:** mỗi lần An chọn đáp án, trình duyệt lưu nháp vào **localStorage của máy An**. Backend không biết gì cho tới lúc nộp, nên database không ghi gì thêm.

Bình, Chi, Dũng làm giống hệt (bài B2, B3, B4); mỗi người có thứ tự câu riêng.

---

## Bước 10. Nộp bài (An 10:20, Bình 10:30, Chi 10:40, Dũng 10:50)

**Gọi:** `POST /tasks/attempts/B1/submit` (An)
```json
{ "answers": [
  { "sampleId": "M3", "data": { "cam_xuc": { "labelIds": ["khen"] } } },
  { "sampleId": "M1", "data": { "cam_xuc": { "labelIds": ["khen"] } } },
  { "sampleId": "M4", "data": { "cam_xuc": { "labelIds": ["che"] } } },
  { "sampleId": "M2", "data": { "cam_xuc": { "labelIds": ["che"] } } } ] }
```

**project-svc làm trong MỘT transaction:**

1. Kiểm bài. Bài lỗi thì trả 400 kèm danh sách câu sai, và bài vẫn giữ để sửa. Các lỗi có thể gặp:
   - gửi trùng một câu;
   - gửi câu không có trong đề;
   - chọn lớp không có trong `classes`.
2. Khoá `project_collections`, rồi khoá dòng `attempts` B1.
3. **Chấm câu vàng ngay:** M4 An chọn "che", đáp án "che", vậy đúng 1/1.
4. Ghi **câu trả lời câu thật** vào `annotations`. Câu vàng không ghi ở đây.
5. Đổi bài sang `Submitted`.
6. Nếu đây là bài thứ 6 (đủ trần) thì đóng thu bài luôn. Hiện mới có 1 bài, chưa đóng.
7. Ghi thư `attempt.submitted` vào outbox.

**`attempts`** sau khi cả 4 người nộp:

| id | labeler | state | submitted_at | answered_count | gold_total | gold_correct | gold_answers | client_ip |
|---|---|---|---|---|---|---|---|---|
| B1 | An | Submitted | 10:20 | 4 | 1 | 1 | `{M4: che}` | 113.161.x.x |
| B2 | Bình | Submitted | 10:30 | 4 | 1 | 1 | `{M4: che}` | 14.232.x.x |
| B3 | Chi | Submitted | 10:40 | 4 | 1 | 1 | `{M4: che}` | 27.72.x.x |
| B4 | Dũng | Submitted | 10:50 | 4 | 1 | **0** | `{M4: khen}` | 42.115.x.x |

- `answered_count`: số câu đã trả lời, tính cả câu vàng.
- `gold_answers`: câu trả lời của câu vàng **chỉ nằm ở đây**.
- `submitted_at`: quan trọng. Khi số bài đạt nhiều hơn số suất, ai nộp sớm hơn thì được thưởng.

**project_db.`annotations`.** Mỗi người 3 dòng (3 câu thật), tổng cộng 12 dòng:

| project_id | attempt_id | sample_id | labeler_id | source | payload |
|---|---|---|---|---|---|
| DA1 | B1 | M1 | L1 | Professional | `{"cam_xuc": {"labelIds": ["khen"]}}` |
| DA1 | B1 | M2 | L1 | Professional | `{"cam_xuc": {"labelIds": ["che"]}}` |
| DA1 | B1 | M3 | L1 | Professional | `{"cam_xuc": {"labelIds": ["khen"]}}` |
| DA1 | B2 | M1 | L2 | Professional | `{"cam_xuc": {"labelIds": ["khen"]}}` |
| DA1 | B2 | M2 | L2 | Professional | `{"cam_xuc": {"labelIds": ["che"]}}` |
| DA1 | B2 | M3 | L2 | Professional | `{"cam_xuc": {"labelIds": ["khen"]}}` |
| DA1 | B3 | M1 | L3 | Professional | `{"cam_xuc": {"labelIds": ["che"]}}` ← Chi sai |
| DA1 | B3 | M2 | L3 | Professional | `{"cam_xuc": {"labelIds": ["che"]}}` |
| DA1 | B3 | M3 | L3 | Professional | `{"cam_xuc": {"labelIds": ["khen"]}}` |
| DA1 | B4 | M1 | L4 | Professional | `{"cam_xuc": {"labelIds": ["khen"]}}` |
| DA1 | B4 | M2 | L4 | Professional | `{"cam_xuc": {"labelIds": ["che"]}}` |
| DA1 | B4 | M3 | L4 | Professional | `{"cam_xuc": {"labelIds": ["khen"]}}` |

| Cột | Ý nghĩa |
|---|---|
| `(project_id, attempt_id, sample_id)` | Khoá chính: trong bài này, câu này, người này trả lời gì |
| `labeler_id` | Ai trả lời. null nếu là khách vô danh từ cổng vượt link |
| `source` | Nguồn: `Professional` (làm trên web), `LinkGateway` (khách vượt link), `Collaborative` |
| `payload` | Câu trả lời, theo tên công cụ |

Đây là **bảng lớn nhất hệ thống** về lâu dài, nên được thiết kế rất gọn:
- **Chỉ có 6 cột**, và chỉ INSERT, không bao giờ UPDATE.
- **Không có cột trạng thái** "đã chấp nhận / bị loại". Trạng thái đó **suy ra** từ bài làm ở bảng `attempts` (bước 13). Khi chấm xong hay khi khiếu nại được chấp nhận, hệ thống chỉ sửa **1 dòng `attempts`** thay vì hàng trăm dòng nhãn.

**Gửi đi (mỗi lần nộp):**
```
project-svc ──[attempt.submitted]──► quality-svc
   nội dung: { attemptId: B1, projectId: DA1, labelerId: L1, startedAt: 10:05, submittedAt: 10:20,
               answeredCount: 3, goldTotal: 1, goldCorrect: 1, clientIp: "113.161.x.x" }
```
Thư này **chỉ là tín hiệu "có bài mới"**. Nó không mang câu trả lời: quality cần thì tự đọc qua gRPC.

**quality-svc** ghi vào `pending_attempts`, một hàng chờ việc:

| attempt_id | project_id | queued_at | fraud_checked_at | freetext_done_at |
|---|---|---|---|---|
| B1 | DA1 | 10:20 | null → 10:20:05 | null |

Tiến trình nền đọc bài B1 qua gRPC rồi **soi gian lận**: làm quá nhanh (dưới 2 giây/câu)? chọn cùng một đáp án liên tục? nhiều tài khoản chung IP hay thiết bị? Có nghi vấn thì tạo hồ sơ `fraud_cases` cho admin xem. Ở đây không có gì. Xét xong thì **xoá dòng** khỏi hàng chờ.

Sau 4 bài nộp, `GET /tasks/projects/DA1/slots` trả `{ submitted: 4, available: 2, phase: "open" }`. Còn 2 chỗ (trần 6) nên dự án **vẫn mở**.

---

## Bước 11. Đóng thu bài (11:00)

Có 3 cách đóng thu bài:

| Cách | Khi nào | `finish_reason` |
|---|---|---|
| Đủ người | Bài thứ 6 nộp: đóng ngay trong transaction nộp bài | `du_suat` |
| Hết hạn | Tiến trình nền quét mỗi 30 giây thấy đã qua `deadline` | `het_han` |
| Kết thúc sớm | Chủ dự án bấm "Kết thúc" | `ket_thuc_som` |

**Ngoài đời:** Công ty thấy 4 bài là đủ, bấm "Kết thúc sớm".

**Gọi:** `POST /projects/DA1/finish`

Trong **một transaction**, project-svc:
- `projects`: `status` Running → **Grading**, `finish_requested_at = grading_started_at = 11:00`.
- `project_collections`: `finish_reason = ket_thuc_som`.
- Bài nào đang `InProgress` (làm dở) chuyển `Expired`. Ở đây không có.
- Ghi thư:

```
project-svc ──[project.collection_finished]──► quality-svc
   nội dung: { projectId: DA1, reason: "ket_thuc_som", finishedAt: 11:00, excludedSampleIds: [],
               attempts: [ {B1, L1, goldCorrect 1/1}, {B2, L2, 1/1}, {B3, L3, 1/1}, {B4, L4, 0/1} ] }
   correlation_id = C2
```
`excludedSampleIds`: các mẫu đã bị loại vì báo lỗi, không tính khi chấm. Ở đây rỗng.

---

## Bước 12. Chấm bài (quality-svc, 11:00:01)

**quality_db.`project_gradings`.** quality ghi nhận việc chấm:

| Cột | Giá trị | Ý nghĩa |
|---|---|---|
| project_id | DA1 | |
| requested_at | 11:00:00.5 | Lúc nhận yêu cầu chấm |
| graded_at | null → 11:00:01 | Lúc chấm xong. Nếu project-svc tạm không trả lời, cột này vẫn null và tiến trình nền sẽ thử chấm lại |
| majority_used | true | Có dùng "số đông" không. Dưới 3 bài thì false: chỉ xét câu vàng |
| ds_at, reputation_at | null | Điền ở bước 14 |

**quality đọc dữ liệu qua gRPC** từ project-svc: cấu hình dự án (tập nhãn, ngưỡng 75%, 3 suất), danh sách 4 bài đã nộp, và 12 câu trả lời. quality **không lưu bản sao** những thứ này.

### Bước 12.1: Tìm "số đông" cho từng câu thật

Mỗi câu lấy câu trả lời của **cả 4 người**, đáp án nhiều phiếu nhất là số đông. Một câu cần ít nhất 2 người trả lời.

| Câu | Phiếu | Số đông | An | Bình | Chi | Dũng |
|---|---|---|---|---|---|---|
| M1 | khen 3 – chê 1 | **khen** | giống | giống | **khác** | giống |
| M2 | chê 4 | **chê** | giống | giống | giống | giống |
| M3 | khen 4 | **khen** | giống | giống | giống | giống |

**quality_db.`sample_majorities`:**

| project_id | sample_id | status | final | label_count |
|---|---|---|---|---|
| DA1 | M1 | agreed | `{"cam_xuc": {"labelIds": ["khen"]}}` | 4 |
| DA1 | M2 | agreed | `{"cam_xuc": {"labelIds": ["che"]}}` | 4 |
| DA1 | M3 | agreed | `{"cam_xuc": {"labelIds": ["khen"]}}` | 4 |

`status`: `agreed` (có số đông) hoặc `disputed` (hoà, hoặc không đủ người).

### Bước 12.2: Tính điểm từng bài

- `comparable_count` = số câu đem so được (3). Câu bỏ trống vẫn được tính, như trả lời khác.
- `matched_count` = số câu giống số đông.
- `agreement_percent` = làm tròn xuống của 100 × giống / so được.
- **Đạt** khi `agreement_percent ≥ 75` **VÀ** đúng hết câu vàng.

| Bài | so được | giống | % | Câu vàng | Đạt? |
|---|---|---|---|---|---|
| B1 An | 3 | 3 | 100 | 1/1 | ✔ |
| B2 Bình | 3 | 3 | 100 | 1/1 | ✔ |
| B3 Chi | 3 | 2 | **66** | 1/1 | ✘ (66 < 75) |
| B4 Dũng | 3 | 3 | 100 | **0/1** | ✘ (sai câu vàng) |

Trường hợp của Dũng cho thấy vì sao cần câu vàng: "số đông" chỉ bắt được người làm khác mọi người. Câu vàng bắt được cả người làm sai dù đang giống số đông.

### Bước 12.3: Chọn ai được thưởng

Lấy tối đa **3 suất** cho các bài đạt, **nộp sớm nhất trước**. Có 2 bài đạt, An (10:20) và Bình (10:30), nên cả hai được thưởng. Suất thứ ba bỏ trống.

Nếu có 5 bài đạt thì chỉ 3 bài nộp sớm nhất được thưởng; 2 bài kia vẫn ghi "đạt" nhưng "không được thưởng".

**Gửi đi:**
```
quality-svc ──[project.graded]──► project-svc, ledger-svc, notification-svc
   nội dung: { projectId: DA1, ownerId: DN1, passThresholdPercent: 75, majorityUsed: true, gradedAt: 11:00:01,
     attempts: [
       { B1, L1, comparable 3, matched 3, 100%, gold 1/1, passed: true,  rewarded: true  },
       { B2, L2, comparable 3, matched 3, 100%, gold 1/1, passed: true,  rewarded: true  },
       { B3, L3, comparable 3, matched 2,  66%, gold 1/1, passed: false, rewarded: false },
       { B4, L4, comparable 3, matched 3, 100%, gold 0/1, passed: false, rewarded: false } ] }
   correlation_id = C2
```
quality ghi `sample_majorities`, cập nhật `project_gradings` và ghi thư này **trong cùng một transaction**.

---

## Bước 13. Ghi kết quả và trả thưởng (11:00:02)

Ba service cùng nhận thư `project.graded`, và mỗi service tự làm phần của mình.

### 13a. project-svc ghi kết quả vào bài làm

**`attempts`** (các cột kết quả):

| id | comparable_count | matched_count | agreement_percent | passed | rewarded | graded_at |
|---|---|---|---|---|---|---|
| B1 An | 3 | 3 | 100 | true | true | 11:00:01 |
| B2 Bình | 3 | 3 | 100 | true | true | 11:00:01 |
| B3 Chi | 3 | 2 | 66 | false | false | 11:00:01 |
| B4 Dũng | 3 | 3 | 100 | false | false | 11:00:01 |

**`project_collections`:**

| Cột | Giá trị | Ý nghĩa |
|---|---|---|
| graded_at | 01/12 11:00:01 | |
| appeal_deadline | **04/12 11:00:01** | = lúc chấm + 3 ngày. Người trượt được khiếu nại trước hạn này |

Dự án **vẫn ở `Grading`**, chưa xong hẳn vì còn hạn khiếu nại.

**Trạng thái của 12 nhãn giờ suy ra được, không sửa dòng nào trong `annotations`:**

| Nhãn của | Bài | Trạng thái suy ra | Vào kết quả giao công ty? |
|---|---|---|---|
| An, Bình | đạt | Accepted | Có |
| Chi, Dũng | trượt, chưa khiếu nại | Excluded | Không |

**Gửi tiếp:**
```
project-svc ──[project.grading_applied]──► identity-svc
   nội dung: { projectId: DA1, approvedLabelerIds: [L1, L2] }
```
Thư này chỉ gửi **sau khi** project đã ghi xong kết quả. Lý do: identity sẽ hỏi project "An có bao nhiêu câu được chấp nhận", nên phải hỏi lúc project đã cập nhật.

### 13b. ledger-svc trả tiền

Với mỗi bài `rewarded: true`, ledger kiểm: dự án có ký quỹ? chưa trả quá 3 bài? bài này chưa trả lần nào? Rồi ghi 2 bút toán.

**Bút toán #3: thưởng bài B1** (`type = RewardPayout`, `reference = attempt:B1`):

| account_code | amount |
|---|---|
| escrow:project:DA1 | −26.000 |
| labeler:L1:pending | +20.000 |
| platform:fee | +6.000 |

**`holds`**: khoản tiền **treo** trước khi người làm rút được:

| Cột | Giá trị | Ý nghĩa |
|---|---|---|
| id | H1 | |
| kind | ProjectReward | Loại: thưởng dự án / tiền cổng vượt link / hoa hồng giới thiệu |
| source_id | B1 | Bài làm sinh ra khoản này. Mỗi bài chỉ có một khoản |
| labeler_id, project_id | L1, DA1 | |
| amount_vnd | 20.000 | |
| held_at, release_at | 11:00:02, 11:00:02 | `release_at` = lúc treo + thời gian treo. Setting `ledger.reward_hold = 0` nên giải phóng ngay |
| state | Held → **Released** | Hoặc `Withheld` nếu bị giữ lại vì gian lận |

**Bút toán #4: giải phóng khoản treo** (`type = HoldRelease`, `reference = hold:H1`):

| account_code | amount |
|---|---|
| labeler:L1:pending | −20.000 |
| labeler:L1:available | +20.000 |

Bình được ghi y hệt: bút toán #5, #6 và khoản treo H2.

**`accounts` sau khi trả thưởng:**

| code | balance | Nghĩa |
|---|---|---|
| business:DN1:available | 422.000 | Ví công ty (chưa đổi) |
| escrow:project:DA1 | 78.000 → **26.000** | Còn lại tiền của suất thứ ba |
| labeler:L1:available | **20.000** | An rút được |
| labeler:L2:available | **20.000** | Bình rút được |
| platform:fee | +12.000 | Nền tảng thu 2 × 6.000 |

**ledger gửi:** `funds.held` và `hold.expired` cho mỗi người (notification nhận để báo tin).

### 13c. notification-svc báo tin

`notifications`:

| user_id | type | title | body |
|---|---|---|---|
| L1 An | ket_qua_bai | Bài làm đã đạt | Bạn giống đa số 100% (ngưỡng 75%) và đúng hết câu kiểm tra. Tiền thưởng đã được trả vào ví. |
| L1 An | tien_kha_dung | Tiền đã khả dụng | 20.000 đ đã chuyển sang số dư khả dụng — có thể rút. |
| L3 Chi | ket_qua_bai | Bài làm chưa đạt | Bạn giống đa số 66% (ngưỡng 75%)… Bạn có thể khiếu nại trước 04/12. |
| DN1 | du_an | Dự án đã chấm xong | 2/4 bài đạt, 2 bài nộp sớm nhất được trả thưởng. Phần ký quỹ còn dư hoàn về ví khi hết hạn khiếu nại. |

(Bình và Dũng nhận thông báo tương tự.)

---

## Bước 14. Uy tín và cấp độ (11:00 → 11:00:10)

### 14a. Uy tín (quality-svc là chủ)

Sau khi project ghi xong kết quả, quality tính lại **uy tín** (thang 0–100) cho cả 4 người. Bằng chứng được cộng dồn qua mọi dự án đã làm, đọc từ project qua gRPC. Công thức:

```
làm_mượt(đúng, tổng) = (đúng + 1,4) / (tổng + 2)
uy_tín = 100 × ( 0,6 × làm_mượt(câu vàng) + 0,4 × làm_mượt(giống số đông) )
```

Phần "làm mượt" giúp người mới không nhảy vọt lên 100 chỉ vì làm đúng một câu, và cũng không rơi về 0 chỉ vì sai một câu.

| Người | Câu vàng | Giống số đông | Tính | `reputation` |
|---|---|---|---|---|
| An | 1/1 → 0,800 | 3/3 → 0,880 | 0,48 + 0,352 | **83** |
| Bình | 1/1 → 0,800 | 3/3 → 0,880 | | **83** |
| Chi | 1/1 → 0,800 | 2/3 → 0,680 | 0,48 + 0,272 | **75** |
| Dũng | 0/1 → 0,467 | 3/3 → 0,880 | 0,28 + 0,352 | **63** |

**quality_db.`reputations`** (dòng của An):

| labeler_id | reputation | gold_total | gold_correct | agree_total | agree_count | ds_skill |
|---|---|---|---|---|---|---|
| L1 | 83 | 1 | 1 | 3 | 3 | null |

`ds_skill` là điểm tính theo phương pháp Dawid–Skene; nó chỉ có khi người làm có từ 5 nhãn trở lên.

`project_gradings`: `ds_at = 11:00:01`, `reputation_at = 11:00:05`.

**Gửi đi:** `quality-svc ──[reputation.changed {userId, reputation}]──► identity-svc` (mỗi người một thư).

**Uy tín dùng để làm gì?** Dự án sau đặt `min_reputation = 80` thì Chi (75) và Dũng (63) không vào được. identity đọc uy tín qua **view** `v_reputations`, một "cửa sổ chỉ đọc" vào database của quality, không chép điểm sang identity.

### 14b. Cấp độ (identity-svc)

identity nhận `project.grading_applied` và `reputation.changed`, rồi xếp người làm vào hàng chờ `level_dirty`. Tiến trình nền hỏi project qua gRPC xem mỗi người có bao nhiêu câu được chấp nhận, so với bảng luật cấp độ, rồi ghi `labeler_stats`:

| user_id | level | updated_at |
|---|---|---|
| L1 An | 1 | 11:00:03 |
| … | 1 | … |

Mới 3 câu được chấp nhận nên chưa đủ để lên cấp 2.

### 14c. Công ty xem kết quả

**Gọi:** `GET /annotations/projects/DA1/results`. Kết quả **chỉ gộp nhãn đã được chấp nhận** (An, Bình):

| Mẫu | Câu hỏi | Phiếu (chỉ bài đạt) | Kết quả cuối |
|---|---|---|---|
| M1 | Bình luận này là khen hay chê? | khen 2 | **khen** |
| M2 | Bình luận này là khen hay chê? | chê 2 | **chê** |
| M3 | Xét cả câu, người viết có hài lòng không? | khen 2 | **khen** |

Lưu ý: "số đông **để chấm**" ở bước 12 dùng nhãn của **cả 4 người**. Còn "kết quả **giao cho công ty**" chỉ dùng nhãn của **bài đạt**, nên kết quả không lẫn nhãn của người làm kém.

project-svc lưu kết quả gộp này vào **`sample_results`** để lần sau đọc nhanh:

| project_id | sample_id | tools | fingerprint | computed_at |
|---|---|---|---|---|
| DA1 | M1 | `{"cam_xuc": {"method": "majority", "votes": {"khen": 2}, "final": {"labelIds": ["khen"]}, "disputed": false}}` | 677d… | 11:05 |
| DA1 | M2 | `{… "votes": {"che": 2} …}` | 677d… | 11:05 |
| DA1 | M3 | `{… "votes": {"khen": 2} …}` | 677d… | 11:05 |

`fingerprint` là mã băm của các nhãn đầu vào. Nếu sau này khiếu nại của Chi được chấp nhận thì đầu vào đổi, mã băm đổi, và hệ thống tự gộp lại. Phép gộp do quality làm qua gRPC, vì thuật toán gộp chỉ viết một lần bằng Python.

---

## Bước 15. Hết hạn khiếu nại: đóng hẳn dự án (04/12, 11:00:30)

**Ngoài đời:** 3 ngày trôi qua, không ai khiếu nại.

Tiến trình nền của project-svc quét mỗi 30 giây. Nó thấy đã qua `appeal_deadline` và không còn khiếu nại mở, nên làm trong **một transaction**:
- `project_collections`: `settled_at = 04/12 11:00:30`, `passed_count = 2`.
- `projects`: `status` Grading → **Completed**, `closed_at = 04/12 11:00:30`.
- Ghi thư:

```
project-svc ──[project.completed]──► ledger-svc, quality-svc, notification-svc
   nội dung: { projectId: DA1, ownerId: DN1, passedAttemptCount: 2 }
```

**ledger-svc** kiểm: đã trả đủ 2 bài chưa? Đủ rồi. Vậy trả phần còn lại cho công ty.

Bút toán #7 (`type = EscrowRelease`, `reference = release:DA1`, "Trả ký quỹ còn dư khi hoàn thành"):

| account_code | amount |
|---|---|
| escrow:project:DA1 | −26.000 |
| business:DN1:available | +26.000 |

`project_escrows`: `state` → **Closed**, `closed_at = 04/12 11:00:31`.

Nếu thư trả thưởng của ai đó **chưa tới kịp** (thư có thể đến lộn thứ tự), ledger chuyển ký quỹ sang `Closing` và ghi `expected_paid_attempts = 2`. Trả xong bài còn thiếu thì nó mới tự hoàn tiền dư.

- **quality-svc** lấy mốc này để bắt đầu đếm thời hạn lưu trữ, hết hạn thì dọn dữ liệu trung gian.
- **notification-svc** báo công ty: "Đã hoàn 26.000 đ ký quỹ chưa dùng về ví doanh nghiệp."

### Nếu Chi khiếu nại (trước 04/12)

1. Chi gọi `POST /tasks/attempts/B3/appeal {"message": "Câu 1 khách khen giao nhanh mà"}`. Bài B3 đổi: `appeal_state = Open`, `appealed_at`, `appeal_message`.
2. Còn khiếu nại mở thì dự án **không đóng**, dù đã qua hạn.
3. Admin xem ở `GET /tasks/admin/appeals` rồi quyết định:
   - **Chấp nhận:** `appeal_state = Accepted`. Nhãn của Chi tự thành Accepted, không sửa dòng nhãn nào. project gửi `attempt.appeal_resolved`, ledger trả Chi 20.000 đ lấy từ ký quỹ còn lại. Khi đóng dự án, `passedAttemptCount = 3` nên không còn tiền hoàn.
   - **Từ chối:** `appeal_state = Rejected`, chỉ báo cho Chi.

---

# Phần C. Nhìn lại toàn bộ

## C.1 Dòng thời gian

| Thời điểm | Việc | Trạng thái dự án |
|---|---|---|
| 01/12 08:00 | Nạp 500.000 đ | |
| 08:10 – 08:25 | Tạo, đặt câu hỏi, đặt giá, nạp mẫu, đặt câu vàng | Draft |
| 08:30:00 | Publish | PendingEscrow |
| 08:30:01 | Ledger giữ 78.000 đ | PendingApproval |
| 09:00 | Admin duyệt, mở đợt thu bài (trần 6 người) | Running |
| 10:00 – 10:50 | 4 người vào, làm, nộp | Running |
| 11:00 | Kết thúc sớm | Grading |
| 11:00:01 | Chấm xong: 2 đạt, 2 trượt | Grading |
| 11:00:02 | Trả 20.000 đ cho An, Bình | Grading |
| 04/12 11:00:30 | Hết hạn khiếu nại, hoàn 26.000 đ | **Completed** |

## C.2 Tiền đi đâu

| Sau bước | Ví công ty | Ký quỹ DA1 | An | Bình | Nền tảng |
|---|---|---|---|---|---|
| 0. Nạp | 500.000 | | | | |
| 6. Publish | 422.000 | 78.000 | | | |
| 13. Trả thưởng | 422.000 | 26.000 | 20.000 | 20.000 | 12.000 |
| 15. Đóng | **448.000** | 0 | 20.000 | 20.000 | 12.000 |

Kiểm tra: 448.000 + 20.000 + 20.000 + 12.000 = **500.000**. Không đồng nào sinh ra hay mất đi.

## C.3 Tất cả thư (event) trong ví dụ

| Thư | Ai gửi | Ai nhận | Nhận xong thì làm gì |
|---|---|---|---|
| `deposit.confirmed` | payment | ledger, notification | Cộng tiền vào ví; báo tin |
| `member.added` | project | admin | Ghi nhật ký |
| `project.publish_requested` | project | ledger, admin | Giữ ký quỹ |
| `escrow.reserved` / `escrow.rejected` | ledger | project | Chuyển chờ duyệt / về nháp |
| `project.published` | project | ledger, notification, ml | Chốt điều khoản trả tiền; báo tin |
| `attempt.submitted` | project | quality | Soi gian lận |
| `project.collection_finished` | project | quality | Chấm bài |
| `project.graded` | quality | project, ledger, notification | Ghi kết quả; trả thưởng; báo tin |
| `project.grading_applied` | project | identity | Tính lại cấp độ |
| `funds.held`, `hold.expired` | ledger | notification | Báo "có tiền", "rút được" |
| `reputation.changed` | quality | identity | Tính lại cấp độ |
| `project.completed` | project | ledger, quality, notification | Hoàn ký quỹ dư; đếm hạn lưu trữ; báo tin |

## C.4 Mỗi bảng được ghi ở bước nào

| Bảng | Bước ghi |
|---|---|
| `payment_intents` | 0 |
| `projects` | 1, 2, 3, 6a, 6c, 7, 11, 15 |
| `project_members` | 1, 8 |
| `samples` | 4 |
| `gold_items` | 5 |
| `project_escrows` | 6b, 7, 15 |
| `journal_entries` / `journal_lines` / `accounts` | 0, 6b, 13b, 15 |
| `project_collections` | 7, 11, 13a, 15 |
| `attempts` | 9, 10, 13a |
| `annotations` | 10 (chỉ một lần, không bao giờ sửa) |
| `pending_attempts` | 10 (thêm rồi xoá) |
| `project_gradings`, `sample_majorities` | 12, 14 |
| `holds` | 13b |
| `reputations` | 14a |
| `labeler_stats` | 14b |
| `sample_results` | 14c |
| `notifications` | 0, 7, 13c, 15 |
| `outbox` / `processed_events` | Mỗi lần gửi / nhận thư |
