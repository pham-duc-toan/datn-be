# Hệ thống gán nhãn dữ liệu cộng đồng

## Đặc tả chức năng và đề xuất kiến trúc

**Phiên bản:** 1.0

---

## Mục lục

- [Phần I. Tổng quan](#phần-i-tổng-quan)
- [Phần II. Đặc tả chức năng](#phần-ii-đặc-tả-chức-năng)

---

# Phần I. Tổng quan

## 1.1. Mô tả bài toán

Nền tảng trung gian kết nối **doanh nghiệp cần dữ liệu đã gán nhãn** với **cộng đồng người gán nhãn**. Doanh nghiệp nạp tiền và đăng bài toán; người dùng chọn bài toán, gán nhãn và nhận thù lao. Nền tảng thu phí trên mỗi giao dịch.

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
