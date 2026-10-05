# Hệ thống gán nhãn dữ liệu cộng đồng

## Đặc tả chức năng và đề xuất kiến trúc

**Phiên bản:** 1.1 — bổ sung Phần III (hướng phát triển mở rộng)

---

## Mục lục

- [Phần I. Tổng quan](#phần-i-tổng-quan)
- [Phần II. Đặc tả chức năng](#phần-ii-đặc-tả-chức-năng)
- [Phần III. Hướng phát triển mở rộng](#phần-iii-hướng-phát-triển-mở-rộng)

---

# Phần I. Tổng quan

## 1.1. Mô tả bài toán

Nền tảng trung gian kết nối **doanh nghiệp cần dữ liệu đã gán nhãn** với **cộng đồng người gán nhãn**. Doanh nghiệp nạp tiền và đăng bài toán; người dùng chọn bài toán, gán nhãn và nhận thù lao. Nền tảng thu phí trên mỗi giao dịch.

Về lâu dài, bộ dữ liệu đã gán nhãn không dừng lại ở việc giao cho một doanh nghiệp. Nó có thể được **chia sẻ, bán, cho thuê**, và đi kèm **code / notebook** do cộng đồng viết, theo mô hình gần với Kaggle. Phạm vi đồ án là Phần II; hướng mở rộng được mô tả ở [Phần III](#phần-iii-hướng-phát-triển-mở-rộng).

## 1.2. Các vai trò

| Vai trò                         | Mô tả                                       | Dòng tiền       |
| ------------------------------- | ------------------------------------------- | --------------- |
| **Doanh nghiệp** (Requester)    | Đăng dự án, nạp tiền, nghiệm thu nhãn       | Chi tiền        |
| **Người gán nhãn** (Labeler)    | Chọn dự án, gán nhãn, nhận thù lao          | Nhận tiền       |
| **Người chia sẻ link** (Sharer) | Rút gọn link, kiếm tiền theo lượt vượt link | Nhận tiền       |
| **Người vượt link**             | Khách vãng lai, giải nhãn để mở nội dung    | Không giao dịch |
| **Admin**                       | Kiểm duyệt, xử lý tranh chấp, vận hành      | Quản trị        |

Một tài khoản có thể đồng thời là Labeler và Sharer.

## 1.3. Ba kênh thu thập nhãn

| Kênh                  | Người thực hiện                    | Chất lượng | Sản lượng  | Đơn giá |
| --------------------- | ---------------------------------- | ---------- | ---------- | ------- |
| **Chuyên nghiệp**     | Labeler đã đăng ký, có điểm uy tín | Cao        | Trung bình | Cao     |
| **Cổng link**         | Khách vãng lai vượt link           | Thấp       | Rất lớn    | Rẻ      |
| **Cộng tác realtime** | Nhóm labeler cùng xử lý một mẫu    | Cao        | Thấp       | Cao     |

Mỗi nhãn gắn thuộc tính `source`; doanh nghiệp tự chọn có chấp nhận nguồn đó không.

## 1.4. Vòng luân chuyển giá trị

```
Doanh nghiệp ──── nạp tiền, ký quỹ ────► Nền tảng
      ▲                                      │
      │                                      │ chia thù lao / CPM
   nhãn thu được                             ▼
      │                             Labeler / Người chia sẻ link
      │                                      │
      └──── gán nhãn ─── Người vượt link ◄───┘ phát link rút gọn
```

---

# Phần II. Đặc tả chức năng

## 2.1. Chức năng chung

| Mã    | Chức năng                                       |
| ----- | ----------------------------------------------- |
| FC-01 | Đăng ký tài khoản, chọn vai trò, xác thực email |
| FC-02 | Đăng nhập, đăng xuất, quản lý phiên             |
| FC-03 | Quên mật khẩu, đổi mật khẩu                     |
| FC-04 | Xác thực hai lớp (tùy chọn)                     |
| FC-05 | Quản lý hồ sơ cá nhân, ảnh đại diện             |
| FC-06 | Thông báo trong ứng dụng và qua email           |
| FC-07 | Phân quyền theo vai trò (RBAC)                  |
| FC-08 | Đa ngôn ngữ Việt / Anh                          |

## 2.2. Doanh nghiệp

### Tài khoản và ví

| Mã    | Chức năng                                                |
| ----- | -------------------------------------------------------- |
| FB-01 | Khai báo hồ sơ doanh nghiệp, upload giấy phép kinh doanh |
| FB-02 | Chờ admin xác minh (KYB)                                 |
| FB-03 | Nạp tiền qua VNPay / MoMo / chuyển khoản                 |
| FB-04 | Xem số dư khả dụng và số dư đang ký quỹ                  |
| FB-05 | Lịch sử giao dịch, xuất hóa đơn VAT                      |

### Quản lý dự án

| Mã    | Chức năng                                                       |
| ----- | --------------------------------------------------------------- |
| FB-10 | Tạo dự án, chọn loại bài toán                                   |
| FB-11 | Upload dataset (ZIP / CSV / JSON) hoặc kết nối S3, Google Drive |
| FB-12 | Định nghĩa tập nhãn (label schema)                              |
| FB-13 | Viết hướng dẫn gán nhãn kèm ví dụ đúng/sai                      |
| FB-14 | Cấu hình đơn giá, số người gán trùng, deadline, ngân sách       |
| FB-15 | Đặt điều kiện tham gia: cấp độ, điểm uy tín, bài test đầu vào   |
| FB-16 | Tạo bài test đầu vào và tập câu hỏi vàng (gold standard)        |
| FB-17 | Chọn kênh phân phối: chuyên nghiệp / cổng link / cộng tác       |
| FB-18 | Quản lý vòng đời dự án                                          |

**Loại bài toán hỗ trợ:** phân loại ảnh, bounding box, segmentation, phân loại văn bản, NER, sentiment, so sánh cặp, transcript audio, tracking video, so sánh phản hồi LLM (RLHF).

**Vòng đời dự án:**

```
Nháp → Chờ duyệt → Đang chạy → Tạm dừng → Hoàn thành
                       │                        ▲
                       └──────► Hủy ────────────┘
                              (hoàn tiền phần chưa dùng)
```

### Theo dõi và nghiệm thu

| Mã    | Chức năng                                     |
| ----- | --------------------------------------------- |
| FB-20 | Dashboard tiến độ, chi phí, thời gian còn lại |
| FB-21 | Duyệt / từ chối từng nhãn kèm lý do           |
| FB-22 | Xem độ đồng thuận và danh sách mẫu tranh chấp |
| FB-23 | Đánh giá labeler, danh sách ưu tiên hoặc chặn |
| FB-24 | Cảnh báo phân bố nhãn lệch (class imbalance)  |
| FB-25 | Export: JSON, CSV, COCO, YOLO, Pascal VOC     |

## 2.3. Người gán nhãn

| Mã    | Chức năng                                                 |
| ----- | --------------------------------------------------------- |
| FL-01 | Khai báo hồ sơ kỹ năng: lĩnh vực, ngôn ngữ, loại bài toán |
| FL-02 | Tìm kiếm và lọc dự án; gợi ý dự án phù hợp năng lực       |
| FL-03 | Xem hướng dẫn, làm bài test đầu vào                       |
| FL-04 | Workspace gán nhãn riêng theo từng loại bài toán          |
| FL-05 | Phím tắt bàn phím đầy đủ, lưu nháp, nút bỏ qua            |
| FL-06 | Nhận task theo cơ chế lease, khóa 15 phút tránh trùng     |
| FL-07 | Báo cáo task lỗi hoặc nội dung không phù hợp              |
| FL-08 | Lịch sử công việc, tỉ lệ được duyệt                       |
| FL-09 | Khiếu nại khi bị từ chối                                  |
| FL-10 | Ví: số dư khả dụng và chờ đối soát                        |
| FL-11 | Yêu cầu rút tiền, thống kê thu nhập theo ngày/dự án       |
| FL-12 | Điểm uy tín, cấp độ, huy hiệu, bảng xếp hạng              |

## 2.4. Cổng link rút gọn (mô hình link4m)

### Công cụ cho người chia sẻ link

| Mã    | Chức năng                                                        |
| ----- | ---------------------------------------------------------------- |
| FS-01 | Rút gọn link đơn lẻ                                              |
| FS-02 | Quick Link — rút gọn nhanh không cần vào dashboard               |
| FS-03 | Mass Shrinker — rút gọn hàng loạt                                |
| FS-04 | Full Page Script — nhúng JS vào blog, tự động rút gọn link ngoài |
| FS-05 | API rút gọn cho tự động hóa                                      |
| FS-06 | Tùy chọn link: alias, mật khẩu, hẹn giờ hết hạn, gom chiến dịch  |
| FS-07 | Thống kê doanh thu theo ngày/tháng, Top Link, nguồn traffic      |
| FS-08 | Chương trình giới thiệu — hưởng 10% thu nhập người được mời      |
| FS-09 | Rút tiền qua Momo / ngân hàng                                    |

### Trang vượt link

```
Bước 1: Đếm ngược 5–10 giây + banner quảng cáo
   ▼
Bước 2: Giải 1–3 bài gán nhãn
        (k câu vàng đã biết đáp án + m câu thật cần thu thập)
   ▼
Bước 3: Chấm điểm CHỈ trên câu vàng
   ├─ Không đạt → cấp bộ mới, không phát token
   └─ Đạt       → phát token một lần (JWT, TTL 3 phút, gắn domain)
                  ghi nhận nhãn của câu thật
                  cộng doanh thu cho người chia sẻ link
   ▼
Bước 4: Nút "Lấy link" → chuyển hướng về link đích
```

### Công thức doanh thu

```
CPM = (số nhãn hợp lệ / số lượt vượt link × 1000)
      × đơn giá nhãn
      × (1 − phí nền tảng)
```

**Ví dụ:** 1.000 lượt × 2 nhãn/lượt × 60% hợp lệ = 1.200 nhãn.
Đơn giá 200đ, phí 30% → **168.000đ / 1.000 lượt**.

> **Ưu điểm cốt lõi:** bot không giải được nhãn → không sinh nhãn hợp lệ → CPM tự động về 0. Cơ chế chống gian lận nằm sẵn trong mô hình kinh tế, không cần thêm tầng riêng.

### Chống lạm dụng

- Chỉ tính 1 lượt/IP/24h cho mỗi link
- Chặn tự vượt link của chính mình
- Cloudflare Turnstile đặt **trước** lớp gán nhãn — bot rẻ tiền bị chặn, không tốn task nào
- Phát hiện traffic bất thường: spike đột ngột, referrer giả, dải IP tập trung
- Phát hiện farm: nhiều tài khoản cùng thiết bị, cùng phương thức rút tiền
- Chống lạm dụng referral: tài khoản clone tự mời nhau
- Token dùng một lần, lưu `jti` vào Redis chống replay

### Kiểm duyệt link đích

Mô hình rút gọn link tại Việt Nam bị lạm dụng nhiều cho nội dung vi phạm bản quyền, cờ bạc, phishing, mã độc. **Bắt buộc có:**

- Quét URL qua Google Safe Browsing API / VirusTotal khi tạo link
- Blacklist domain do admin quản lý
- Nút báo cáo link vi phạm ở trang vượt link
- Hàng đợi kiểm duyệt thủ công cho link bị báo cáo nhiều
- Tự động vô hiệu hóa link và giữ doanh thu khi xác nhận vi phạm

## 2.5. Gán nhãn cộng tác realtime

### Phân biệt hai chế độ

|                        | Đồng thuận (mặc định)        | Cộng tác realtime      |
| ---------------------- | ---------------------------- | ---------------------- |
| Nhiều người cùng 1 mẫu | Có                           | Có                     |
| Thấy được nhau         | **Không**                    | Có                     |
| Mục đích               | Đo chất lượng, majority vote | Chia việc mẫu phức tạp |
| Kết quả                | n bản nhãn độc lập → gộp     | 1 bản nhãn chung       |
| Trả tiền               | Mỗi người 1 suất             | Chia theo đóng góp     |

> **Cảnh báo thiết kế:** cho thấy nhau làm mất tính độc lập → không dùng majority vote được nữa (bias neo — người vào sau bắt chước người vào trước). Chế độ realtime chỉ dùng cho mẫu nhiều đối tượng: ảnh vệ tinh, ảnh y tế, segmentation phức tạp.

### Chức năng

| Mã    | Chức năng                                                      |
| ----- | -------------------------------------------------------------- |
| FR-01 | Phòng làm việc theo task, vai trò Leader / Member / Viewer     |
| FR-02 | Con trỏ chuột trực tiếp, danh sách người online, màu định danh |
| FR-03 | Đồng bộ tức thì khi thêm/sửa/xóa object                        |
| FR-04 | Khóa mềm object đang được người khác chọn                      |
| FR-05 | Phân công vùng làm việc hoặc theo lớp nhãn                     |
| FR-06 | Chat trong phòng, ghim comment vào tọa độ trên ảnh             |
| FR-07 | Lịch sử thao tác, undo/redo cục bộ, rollback phiên bản         |
| FR-08 | Hàng đợi offline, tự đồng bộ khi có mạng lại                   |
| FR-09 | Leader chốt phiên, chia công theo đóng góp                     |

**Quy tắc chia công** (tránh người ăn không):

```
điểm = 0.6 × (số object hợp lệ tạo ra / tổng)
     + 0.4 × (thời gian thao tác thực / tổng)
```

Leader điều chỉnh ±20% kèm lý do, mọi điều chỉnh ghi log để labeler khiếu nại được.

## 2.6. AI hỗ trợ gán nhãn

| Mã    | Chức năng                                                              | Giá trị                                       |
| ----- | ---------------------------------------------------------------------- | --------------------------------------------- |
| FA-01 | **Pre-labeling** — train model yếu từ 10% dữ liệu, tự gán phần còn lại | Giảm 50–70% thời gian                         |
| FA-02 | **Active learning** — ưu tiên đẩy mẫu model bất định cao               | Giảm số nhãn cần thiết                        |
| FA-03 | **Gợi ý tương tác (SAM)** — click một điểm, sinh mask                  | Bỏ thao tác vẽ tay                            |
| FA-04 | Tự sinh câu hỏi vàng từ nhãn đã duyệt có đồng thuận cao                | Giám sát liên tục                             |
| FA-05 | LLM kiểm tra guideline, phát hiện chỗ mơ hồ                            | Chất lượng nhãn kém phần lớn do guideline tồi |

> **Ranh giới cứng:** ML chỉ **gợi ý**, không bao giờ tự ghi nhãn cuối cùng mà không có người xác nhận.

## 2.7. Đảm bảo chất lượng

| Mã    | Chức năng                                                                |
| ----- | ------------------------------------------------------------------------ |
| FQ-01 | Ước lượng độ tin cậy từng labeler bằng **Dawid–Skene** / MACE            |
| FQ-02 | Đo đồng thuận: Fleiss' kappa, Krippendorff's alpha, IoU matching, STAPLE |
| FQ-03 | **Redundancy thích ứng**                                                 |
| FQ-04 | Trộn câu hỏi vàng giám sát liên tục                                      |

**Redundancy thích ứng:**

```
2 người đầu trùng khớp  → dừng, tiết kiệm chi phí
Tranh chấp              → tăng lên 5 người hoặc đẩy reviewer cấp cao
```

## 2.8. Bảo mật dữ liệu

| Mã    | Chức năng                                                               |
| ----- | ----------------------------------------------------------------------- |
| FP-01 | Ký NDA điện tử trước khi vào dự án nhạy cảm                             |
| FP-02 | Ảnh stream qua proxy có **watermark chìm mang ID labeler**              |
| FP-03 | Chặn tải ảnh gốc                                                        |
| FP-04 | Tự động che PII: blur khuôn mặt, biển số, số CCCD                       |
| FP-05 | **Private pool** — dự án riêng tư chỉ mời labeler đã xác minh danh tính |
| FP-06 | Audit log: ai xem mẫu nào, lúc nào, bao lâu                             |

## 2.9. Phúc lợi người gán nhãn

Áp dụng cho dự án kiểm duyệt nội dung nhạy cảm:

- Cảnh báo trước, yêu cầu đồng ý rõ ràng, cho phép rời bất kỳ lúc nào không bị phạt
- Giới hạn thời gian phơi nhiễm liên tục, ép nghỉ giữa phiên
- Chế độ làm mờ mặc định, người dùng chủ động bỏ mờ
- Không hiển thị nội dung nhạy cảm cho tài khoản chưa xác minh đủ 18 tuổi

## 2.10. Admin

| Mã    | Chức năng                                                                              |
| ----- | -------------------------------------------------------------------------------------- |
| FM-01 | Duyệt xác minh doanh nghiệp, khóa / mở tài khoản                                       |
| FM-02 | Duyệt dự án trước khi public                                                           |
| FM-03 | Quản lý danh mục loại bài toán, template workspace                                     |
| FM-04 | Cấu hình phí hoa hồng, đơn giá tối thiểu, ngưỡng rút tiền, thời gian treo ví           |
| FM-05 | Duyệt lệnh nạp / rút, đối soát giao dịch                                               |
| FM-06 | Xử lý tranh chấp giữa doanh nghiệp và labeler                                          |
| FM-07 | Chống gian lận: làm quá nhanh, chọn một nhãn liên tục, trùng IP/thiết bị, đa tài khoản |
| FM-08 | Kiểm duyệt link đích, blacklist domain                                                 |
| FM-09 | Thống kê, báo cáo, log hệ thống, thông báo toàn hệ thống                               |

## 2.11. Dòng tiền và ký quỹ

**Escrow** — khi doanh nghiệp publish dự án, hệ thống trừ và giữ ngay:

```
số task × đơn giá × số người gán trùng × (1 + phí nền tảng)
```

Doanh nghiệp không rút được phần này. Tiền chỉ giải phóng cho labeler khi nhãn được duyệt.

**Chi tiết vận hành:**

| Hạng mục          | Quy định                                                        |
| ----------------- | --------------------------------------------------------------- |
| Thời gian treo ví | 3–7 ngày trước khi labeler rút được                             |
| Thuế TNCN         | Khấu trừ 10% với chi trả cá nhân từ 2 triệu đồng, xuất chứng từ |
| Đối soát          | Tự động cuối ngày giữa ví nội bộ và cổng thanh toán             |
| Hủy dự án         | Hoàn tiền theo tỉ lệ phần ngân sách chưa dùng                   |

---

# Phần III. Hướng phát triển mở rộng

> **Phạm vi:** phần này **ngoài phạm vi đồ án**. Nó mô tả hướng phát triển để hệ thống đi xa hơn một sàn thuê người gán nhãn. Mã chức năng được đặt trước để sau này truy vết sang thiết kế, nhưng các quyết định kiến trúc chi tiết **chưa chốt** (xem 3.6).

## 3.1. Ý tưởng chung

Hiện tại, dữ liệu đi một chiều: doanh nghiệp trả tiền → cộng đồng gán nhãn → doanh nghiệp tải về rồi rời nền tảng. Bộ dữ liệu đã gán nhãn là tài sản có giá trị **sau** khi dự án kết thúc, nhưng giá trị đó đang bị bỏ phí.

Hướng mở rộng biến nền tảng thành **nơi dữ liệu tiếp tục sống và sinh lời**:

```
 Dự án gán nhãn (Phần II)
          │ hoàn thành, doanh nghiệp chọn chế độ chia sẻ
          ▼
 ┌─────────────────────────┐   mua / thuê / gán nhãn đổi lượt tải
 │ Bộ dữ liệu (Dataset)    │◄──────────────────────────────── Người dùng dữ liệu
 │ phiên bản, thẻ dữ liệu, │
 │ chỉ số chất lượng nhãn  │──── code, notebook, model ─────► Tác giả code
 └─────────────────────────┘   (public / private / bán / thuê)
          │ doanh thu bán lại
          ▼
 Chủ dữ liệu · Labeler đã đóng góp (tùy chọn) · Nền tảng
```

**Lợi thế so với Kaggle hay các kho dữ liệu khác:** nhãn ở đây **có chứng nhận chất lượng đo được**. Đó là độ đồng thuận (kappa), độ tin cậy từng labeler (Dawid–Skene) và tỉ lệ đúng trên câu vàng (mục 2.7). Thêm vào đó, nếu thiếu nhãn, người mua có thể **đặt thêm nhãn ngay trên cùng nền tảng**.

## 3.2. Vai trò mới

| Vai trò | Mô tả | Dòng tiền |
| --- | --- | --- |
| **Chủ dữ liệu** (Data owner) | Doanh nghiệp sở hữu bộ dữ liệu, quyết định chế độ chia sẻ và giá | Nhận tiền |
| **Người dùng dữ liệu** (Data consumer) | Tải bộ dữ liệu công khai, mua, thuê, hoặc gán nhãn để đổi lượt tải | Chi tiền / chi công |
| **Tác giả code** (Code author) | Viết notebook, script, model trên một bộ dữ liệu; chia sẻ hoặc bán | Nhận tiền |

Một tài khoản có thể giữ nhiều vai trò. Ví dụ: một labeler cũng có thể là người dùng dữ liệu và tác giả code.

## 3.3. Chợ dữ liệu (Data Marketplace)

### Chế độ chia sẻ bộ dữ liệu

Chủ dữ liệu chọn **một** chế độ cho mỗi phiên bản bộ dữ liệu:

| Chế độ | Ai truy cập được | Cách nhận | Ghi chú |
| --- | --- | --- | --- |
| **Riêng tư** (mặc định) | Chủ dữ liệu và người được mời | — | Giống hiện tại |
| **Công khai** | Mọi người | Tải miễn phí | Chủ dữ liệu chọn giấy phép: CC0, CC BY, CC BY-NC… |
| **Bán** | Người đã mua | Trả tiền một lần | Chọn loại giấy phép: nghiên cứu / thương mại / **độc quyền** (bán cho đúng một người) |
| **Cho thuê** | Người đang trong thời hạn thuê | Trả theo thời hạn | **Không cho tải file gốc**, chỉ dùng qua API hoặc notebook trên nền tảng (xem ghi chú dưới) |
| **Gán nhãn để mở khóa** | Người đủ "lượt tải" | Gán nhãn hợp lệ đổi lấy lượt tải | Người không có tiền vẫn dùng được dữ liệu; nền tảng nhận thêm nhãn |

> **Ghi chú quan trọng về "cho thuê":** đã tải được file về máy thì không thể thu hồi. Vì vậy "thuê mà vẫn cho tải" thực chất là **bán rẻ**. Thuê chỉ có nghĩa khi dữ liệu **không rời nền tảng**: người thuê truy cập qua API có hạn hoặc chạy code ngay trên nền tảng (3.4, chế độ "mang code đến dữ liệu").

### Chức năng

| Mã | Chức năng |
| --- | --- |
| FD-01 | Đóng gói dự án đã hoàn thành thành **bộ dữ liệu có phiên bản** (v1, v2… khi gán thêm nhãn); mỗi phiên bản bất biến |
| FD-02 | **Thẻ dữ liệu** (datasheet): nguồn gốc, tập nhãn, số mẫu, phân bố lớp, chỉ số chất lượng (kappa, tỉ lệ đúng câu vàng), giấy phép |
| FD-03 | Chọn chế độ chia sẻ và giá cho từng phiên bản; chuyển chế độ (riêng tư → công khai…) có ghi log |
| FD-04 | Trang khám phá: tìm kiếm, lọc theo loại bài toán / lĩnh vực / giấy phép / giá / điểm chất lượng; xếp hạng, đánh giá, bình luận |
| FD-05 | **Xem trước** một phần mẫu có watermark (tái dùng FP-02), không tải được trước khi có quyền |
| FD-06 | Mua / thuê: thanh toán qua ví (tái dùng ledger + payment), cấp **quyền truy cập** (entitlement) có thời hạn hoặc vĩnh viễn |
| FD-07 | Tải về theo định dạng export (FB-25) qua link có hạn; mỗi bản tải gắn **dấu vân tay người mua** để truy vết khi bị phát tán |
| FD-08 | **Gán nhãn để mở khóa**: gán N nhãn hợp lệ (có kiểm câu vàng) → nhận lượt tải. Lượt tải **không quy đổi ra tiền** |
| FD-09 | **Chia doanh thu cho labeler** (chủ dữ liệu tùy chọn bật): một phần tiền bán được chia cho labeler theo số nhãn đã được duyệt của họ trong bộ dữ liệu |
| FD-10 | **Đặt thêm nhãn**: người mua thấy thiếu nhãn/lớp thì tạo dự án gán nhãn tiếp trên chính bộ dữ liệu → ra phiên bản mới |
| FD-11 | Yêu cầu hoàn tiền trong thời hạn nếu dữ liệu sai mô tả (tranh chấp do admin xử lý, tái dùng FM-06) |
| FD-12 | Admin kiểm duyệt bộ dữ liệu trước khi công khai/bán: quyền sở hữu, dữ liệu cá nhân, nội dung cấm |

### Chia doanh thu khi bán

```
Giá bán
 ├─ Phí nền tảng            (vd 20%)
 ├─ Quỹ labeler (tùy chọn)  (vd 10%) → chia theo số nhãn được duyệt
 └─ Chủ dữ liệu             (phần còn lại)
```

**Ví dụ:** bán 1.000.000đ, phí 20%, quỹ labeler 10%. Nền tảng nhận 200.000đ, quỹ labeler 100.000đ, chủ dữ liệu 700.000đ. Một labeler đóng góp 300/3.000 nhãn được duyệt nhận 10.000đ.

Chia doanh thu cho labeler là **điểm khác biệt** so với các sàn gán nhãn thông thường, nơi người gán nhãn chỉ được trả một lần. Nó cũng khuyến khích labeler làm kỹ, vì nhãn tốt giúp bộ dữ liệu bán chạy hơn.

### Luồng "gán nhãn để mở khóa"

```
Người dùng chọn bộ dữ liệu cần tải (giá: 200 lượt)
   ▼
Gán nhãn task của dự án đang chạy (đúng cơ chế kênh chuyên nghiệp / cổng link)
   ▼
Mỗi nhãn hợp lệ (đạt câu vàng) → +1 lượt   ← chống farm lượt bằng chính câu vàng
   ▼
Đủ 200 lượt → cấp quyền tải phiên bản đó
```

Doanh nghiệp đang chạy dự án được thêm nhân lực gán nhãn. Người dùng không có tiền vẫn có dữ liệu. Nền tảng đứng giữa và hưởng lợi từ cả hai phía. Cơ chế này giống cổng link ở mục 2.4: đổi công gán nhãn lấy một thứ có giá trị.

### Rủi ro bắt buộc xử lý trước khi mở bán

| Rủi ro | Hướng giải |
| --- | --- |
| Doanh nghiệp bán dữ liệu **không thuộc quyền của mình** (ảnh lấy trên mạng, dữ liệu khách hàng) | Cam kết quyền sở hữu khi đăng bán; admin kiểm duyệt (FD-12); cơ chế khiếu nại vi phạm bản quyền và gỡ xuống |
| **Dữ liệu cá nhân** (khuôn mặt, biển số, CCCD) bị phát tán | Bắt buộc chạy che PII (FP-04) trước khi công khai/bán; tuân thủ Nghị định 13/2023/NĐ-CP và Luật Bảo vệ dữ liệu cá nhân |
| Quyền của labeler với nhãn mình làm | Điều khoản dịch vụ ghi rõ nhãn thuộc về chủ dữ liệu; chia doanh thu (FD-09) là ưu đãi, không phải nghĩa vụ |
| Người mua **bán lại / phát tán** | Dấu vân tay theo người mua (FD-07); phát hiện bộ dữ liệu trùng khi có người upload lại (băm ảnh theo nội dung) |
| **Farm lượt tải** bằng nhãn rác | Lượt chỉ tính trên nhãn đạt câu vàng; giới hạn lượt/ngày; tái dùng fraud-svc |
| Bán độc quyền rồi đã có người tải bản công khai trước đó | Không cho chuyển sang "độc quyền" một phiên bản đã từng công khai |

## 3.4. Code và notebook trên bộ dữ liệu

Mỗi bộ dữ liệu có một khu **Code**, giống tab "Code" của Kaggle. Ở đó tác giả đăng notebook phân tích, script tiền xử lý, code huấn luyện, hoặc model đã huấn luyện.

### Chế độ chia sẻ code

| Chế độ | Mô tả |
| --- | --- |
| **Công khai** | Ai cũng xem, sao chép (fork), chạy lại |
| **Riêng tư** | Chỉ tác giả và người được mời |
| **Bán** | Mua để xem mã nguồn và tải về, có giấy phép đi kèm |
| **Cho thuê / trả theo lượt chạy** | Người dùng **không thấy mã nguồn**, chỉ gọi chạy hoặc gọi API của model; trả tiền theo lượt/thời gian |

> **Tương tự dữ liệu:** code đã cho xem thì không thu hồi được. Muốn "cho thuê" code thì code phải **chạy trên nền tảng** và chỉ trả về kết quả.

### Ba mức triển khai (làm dần)

| Mức | Nội dung | Độ khó |
| --- | --- | --- |
| **1. Lưu trữ** | Đăng notebook/script/model như một kho file có phiên bản; xem notebook dạng tĩnh (đã chạy sẵn kết quả) | Thấp |
| **2. Chạy trên nền tảng** | Môi trường notebook chạy trong sandbox (container cô lập, giới hạn CPU/GPU/thời gian, chặn mạng ra ngoài), gắn bộ dữ liệu chỉ đọc | Cao |
| **3. Mang code đến dữ liệu** (*compute-to-data*) | Bộ dữ liệu riêng tư/cho thuê **không bao giờ rời nền tảng**; người dùng gửi code vào chạy, chỉ nhận kết quả (chỉ số, model) đã được kiểm | Rất cao |

Mức 3 cho phép **doanh nghiệp kiếm tiền từ dữ liệu nhạy cảm mà không cần đưa dữ liệu ra ngoài** (dữ liệu y tế, tài chính). Đây là hướng có giá trị nhất nhưng khó nhất.

### Chức năng

| Mã | Chức năng |
| --- | --- |
| FN-01 | Đăng notebook / script / model gắn với một phiên bản bộ dữ liệu; lưu phiên bản code |
| FN-02 | Chọn chế độ chia sẻ và giá; chọn giấy phép mã nguồn (MIT, Apache 2.0, độc quyền…) |
| FN-03 | Xem notebook tĩnh, bình luận, bình chọn, fork |
| FN-04 | Chạy notebook trong sandbox với hạn mức tài nguyên theo gói; dữ liệu gắn chỉ đọc |
| FN-05 | Mua code / thuê lượt chạy / gọi API model (tái dùng ledger + entitlement của FD-06) |
| FN-06 | Compute-to-data: chạy code trên dữ liệu không được tải về, kiểm kết quả đầu ra trước khi trả |
| FN-07 | Hồ sơ tác giả: số notebook, lượt fork, doanh thu, huy hiệu |

### Rủi ro

| Rủi ro | Hướng giải |
| --- | --- |
| Chạy **code không tin cậy** (đào coin, tấn công mạng nội bộ, thoát container) | Sandbox cô lập mạnh (gVisor / Firecracker), không mạng ra ngoài, giới hạn tài nguyên, máy chạy tách khỏi cụm nghiệp vụ |
| **Rò dữ liệu riêng tư** qua kết quả trả về (in cả bộ dữ liệu ra output) | Giới hạn kích thước/loại output; với compute-to-data chỉ cho trả về chỉ số và model, kiểm duyệt trước khi trả |
| **Chi phí GPU** vượt doanh thu | Hạn mức miễn phí nhỏ, trả phí theo giờ GPU; hàng đợi ưu tiên |
| Notebook bán chứa code đạo của người khác | Báo cáo vi phạm, so trùng mã nguồn, gỡ xuống và hoàn tiền |

## 3.5. Các hướng mở rộng khác (đề xuất)

| Mã | Hướng | Mô tả | Tận dụng cái đã có | Độ khó |
| --- | --- | --- | --- | --- |
| FX-01 | **Cuộc thi** (kiểu Kaggle Competitions) | Doanh nghiệp đăng bài toán kèm giải thưởng; người chơi nộp dự đoán, chấm tự động trên **tập kiểm tra ẩn nhãn**; bảng xếp hạng công khai/riêng tách nhau để chống "học vẹt bảng xếp hạng" | Tập kiểm tra chính là nhãn đã duyệt; giải thưởng giữ bằng **escrow** (saga mục 2.11) | Trung bình |
| FX-02 | **Đặt hàng dữ liệu** (data bounty) | Người cần dữ liệu đăng yêu cầu ("cần 10.000 ảnh biển số xe máy VN"); người khác đề xuất bộ có sẵn, hoặc yêu cầu được chuyển thành dự án gán nhãn | Luồng dự án + ký quỹ hiện có | Thấp |
| FX-03 | **Đánh giá mô hình AI bởi con người** | Doanh nghiệp AI gửi cặp câu trả lời của LLM để người thật so sánh; xuất bảng xếp hạng mô hình kiểu "đấu trường" | Loại bài toán so sánh cặp / RLHF đã có; **cổng link** cho sản lượng lớn | Trung bình |
| FX-04 | **Kiểm tra sức khỏe bộ dữ liệu** | Tự phát hiện ảnh trùng, nhãn nghi sai (confident learning), lớp mất cân bằng; đề xuất mẫu cần gán lại | quality-svc, ml-svc; FB-24 | Trung bình |
| FX-05 | **SDK và CLI** | Tải dữ liệu, nộp bài thi, đẩy notebook bằng dòng lệnh (giống `kaggle` CLI); xuất sang Hugging Face Datasets | API công khai qua gateway | Thấp |
| FX-06 | **Chế độ SaaS nội bộ** | Doanh nghiệp dùng nền tảng với **nhân viên của chính họ** (không thuê cộng đồng), trả phí thuê bao | Toàn bộ workspace, QA, private pool (FP-05) | Thấp |
| FX-07 | **Học và chứng chỉ cho labeler** | Khóa học ngắn theo loại bài toán (y tế, pháp lý…), làm bài kiểm tra → chứng chỉ → mở dự án giá cao | Bài test đầu vào, cấp độ, huy hiệu (FL-12) | Thấp |
| FX-08 | **Dữ liệu tiếng Việt chuyên biệt** | Định vị vào mảng dữ liệu Việt còn thiếu: NLP tiếng Việt, giọng nói vùng miền, chữ viết tay, biển số, hóa đơn | Toàn bộ nền tảng; là lợi thế cạnh tranh thay vì tính năng | — |
| FX-09 | **Triển khai riêng** (on-premise) | Bản cài trong hạ tầng của khách hàng nhạy cảm (ngân hàng, bệnh viện) | Kiến trúc container hóa sẵn | Cao |
| FX-10 | **Dữ liệu tổng hợp và tăng cường** | Sinh thêm mẫu (augmentation, dữ liệu tổng hợp) cho lớp hiếm, người thật chỉ kiểm lại | ml-svc; ranh giới "ML chỉ gợi ý" (mục 2.6) | Cao |

## 3.6. Thứ tự gợi ý và câu hỏi mở

**Thứ tự gợi ý.** Làm từ những phần tái dùng được nhiều hạ tầng sẵn có nhất:

1. **Chợ dữ liệu mức cơ bản**: FD-01 → FD-07. Gồm chế độ riêng tư / công khai / bán, tái dùng ledger, payment, media.
2. **Gán nhãn để mở khóa + chia doanh thu cho labeler**: FD-08, FD-09. Đây là điểm khác biệt, gắn chặt với lõi gán nhãn.
3. **Code mức 1** (lưu trữ, xem tĩnh) + **cuộc thi** (FX-01): giá trị lớn, chưa cần chạy code của người dùng.
4. **Code mức 2** (sandbox) → **cho thuê dữ liệu/code**.
5. **Compute-to-data** (mức 3): khi đã vận hành sandbox ổn định.

**Câu hỏi mở.** Cần đưa ra phương án và chốt khi bắt đầu từng giai đoạn:

- Chợ dữ liệu là **service mới** (catalog, entitlement, đơn hàng) hay mở rộng project-svc? Ranh giới "dự án" và "bộ dữ liệu" nằm ở đâu?
- "Lượt tải" lưu ở ledger dưới dạng tài khoản **phi tiền tệ** hay tách hẳn một service điểm thưởng?
- Dấu vân tay người mua (FD-07) gắn vào file như thế nào để không làm hỏng dữ liệu huấn luyện?
- Sandbox chạy code: tự dựng (Kubernetes + gVisor) hay thuê dịch vụ notebook bên ngoài?
- Mô hình thu phí: phí trên từng giao dịch, thuê bao, hay kết hợp?

---
