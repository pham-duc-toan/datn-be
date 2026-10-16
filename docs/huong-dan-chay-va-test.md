# Hướng dẫn khởi động dự án và test API

**Tài liệu liên quan:** [Kiến trúc backend](kien-truc-backend.md) · [Tích hợp frontend](tich-hop-frontend.md) · [Danh mục event](../contracts/events/CATALOG.md) · [Sổ vấn đề](van-de-can-giai-quyet.md)

Tài liệu này dành cho người lần đầu chạy hệ thống trên máy dev. Sau khi làm theo, bạn sẽ có hạ tầng và 6 service nghiệp vụ đang chạy, kèm **dữ liệu mẫu (seed) đủ cho mọi luồng chính**. Phần cuối là bộ lệnh `curl` để test từng luồng qua gateway.

Mọi lệnh đều ghi đầy đủ địa chỉ và ID, copy là chạy được. Chỉ có hai loại giá trị phải tự dán vào:

| Chỗ trống | Lấy ở đâu |
|---|---|
| `<token-admin>`, `<token-biz1>`, `<token-biz2>`, `<token-lab1>`, `<token-lab2>`, `<token-lab3>`, `<token-rev1>` | Trường `accessToken` khi đăng nhập tài khoản tương ứng (mục 5) |
| `<assignmentId>`, `<attemptId>`, `<id-du-an-moi>`, `<intentId-moi>`, `<sampleId>`, `<refreshToken>` | Trường cùng tên trong response của lệnh ngay trước đó |

Khi dán, xóa luôn cặp dấu `< >`. Ví dụ `Bearer <token-biz1>` sẽ thành `Bearer eyJhbGciOi...`.

---

## 0. Chạy nhanh (đã quen thì chỉ cần mục này)

```bash
# 1. Hạ tầng: RabbitMQ, MinIO, Mailpit, Postgres của các service P0 (kể cả admin) + P1 + P2 (cổng link)
#    + P3 (quality) + P6 (notification)
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 --profile p2 --profile p3 --profile p6 up -d

# 2. Build một lần
dotnet build datn.slnx

# 3. Mỗi service một terminal (thứ tự không quan trọng; admin-svc giữ setting hệ thống)
dotnet run --project services/admin/Crowd.Admin.Api
dotnet run --project services/identity/Crowd.Identity.Api
dotnet run --project services/project/Crowd.Project.Api
dotnet run --project services/task/Crowd.Tasking.Api
dotnet run --project services/annotation/Crowd.Annotation.Api
dotnet run --project services/ledger/Crowd.Ledger.Api
dotnet run --project services/payment/Crowd.Payment.Api
dotnet run --project services/link/Crowd.Link.Api            # cổng link: link rút gọn của người chia sẻ
dotnet run --project services/gate/Crowd.Gate.Api            # cổng link: trang vượt link
dotnet run --project services/notification/Crowd.Notification.Api   # thông báo + email (Mailpit)
dotnet run --project services/gateway/Crowd.Gateway.Api
cd services/quality && .venv/Scripts/python -m app.main     # quality-svc (Python) — tạo .venv lần đầu: mục 3
cd services/media && .venv/Scripts/python -m app.main       # media-svc (Python): link xem file + watermark — mục 3

# 4. Đăng nhập thử bằng tài khoản seed (mật khẩu chung: Matkhau@123)
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@crowd.local","password":"Matkhau@123"}'
```

## 0.1 Chạy cả hệ thống bằng Docker (demo một lệnh)

Không cần cài .NET hay Python, chỉ cần Docker. Toàn bộ 11 service, hạ tầng và dữ liệu seed chạy trong container, **tách hẳn khỏi môi trường dev**: tên `datn-demo-*`, volume riêng, cổng lệch +10000, nên chạy song song với dev được.

```bash
python deploy/tao_env.py                                      # lần đầu: sinh .env (mật khẩu ngẫu nhiên, không commit)
docker compose -f docker-compose.demo.yml up -d --build      # lần đầu build khoảng 5–10 phút
python tests/e2e/cho_san_sang.py                              # chờ migrate + seed xong (pip install requests)

curl -s -X POST http://localhost:18080/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@crowd.local","password":"Matkhau@123"}'

docker compose -f docker-compose.demo.yml down -v             # tắt và XOÁ dữ liệu demo
```

| Địa chỉ | Dùng để |
|---|---|
| http://localhost:18080 | Gateway: mọi API ở mục 6, thay `8080` bằng `18080` |
| http://localhost:18025 | Mailpit (email) |
| http://localhost:19001 | MinIO console (`datn` / `MINIO_PASSWORD` trong `.env`) |
| http://localhost:25672 | RabbitMQ management (`datn` / `RABBIT_PASSWORD` trong `.env`) |
| 18103 / 18104 / 18107 / 18108 | task / annotation / link / gate gọi thẳng (cho E2E) |

Chỉ gateway (18080) mở ra mạng; mọi cổng còn lại chỉ bind `127.0.0.1` (VD-S-06) — mở từ máy khác trong mạng LAN sẽ không vào được.

Khác với chạy trên máy:
- Link xem / tải file ký bằng `ObjectStorage:PublicUrl` = `http://localhost:19000`, vì trong mạng compose các service gọi MinIO qua `minio:9000`, còn trình duyệt chỉ tới được cổng publish.
- Gateway ghi đè host của `ocelot.json` bằng `DownstreamHosts__<cổng>=<service>`.
- gate-svc và link-svc tin `X-Forwarded-For` từ mạng nội bộ `10.88.0.0/24` (`ForwardedHeaders:TrustedNetworks`); nếu không, mọi khách sẽ mang IP của gateway.
- task-svc tin `X-Forwarded-For` từ cùng mạng đó (IP người nộp nhãn cho chống gian lận).
- Gateway nạp mốc thu hồi phiên từ `http://identity:8101` (`SessionRevocation__IdentityUrl`).

**Bí mật (S-08).** `docker-compose.demo.yml` không chứa mật khẩu nào: mật khẩu 11 database, RabbitMQ, MinIO, khoá API nội bộ, khoá link xem file (`MEDIA_GRANT_KEY`) và khoá watermark (`MEDIA_WATERMARK_KEY`) đọc từ `.env` ở gốc repo (danh sách biến: [.env.example](../.env.example)). Thiếu `.env` thì compose dừng với thông báo `chay python deploy/tao_env.py`. Khoá ký JWT do identity tự sinh lần đầu và giữ trong volume `identity-keys` (không nằm trong image). `.env` và mọi thư mục `secrets/` bị loại khỏi git và khỏi ngữ cảnh build Docker.

| Tình huống | Lệnh |
|---|---|
| Máy mới / CI | `python deploy/tao_env.py` — mật khẩu ngẫu nhiên. `.env` đã có (bản cũ thiếu biến mới) thì script chỉ **bổ sung** biến còn thiếu, không đổi giá trị cũ |
| Stack demo đã chạy **trước** khi có `.env` (volume cũ vẫn giữ mật khẩu dev) | `python deploy/tao_env.py --dev` — dùng lại `dev_<tên>_pw`, không phải xoá dữ liệu |
| Muốn đổi sang mật khẩu ngẫu nhiên cho stack cũ | `docker compose -f docker-compose.demo.yml down -v` (**xoá dữ liệu demo**) rồi `python deploy/tao_env.py --ghi-de` |

Postgres / RabbitMQ chỉ đặt mật khẩu **lúc tạo volume**: đổi `.env` mà giữ volume thì service không đăng nhập được DB.

Chế độ chạy service trên máy (mục 0, `docker-compose.infra.yml` + `appsettings.Development.json`) vẫn dùng mật khẩu dev cố định — chỉ cho máy dev: cổng hạ tầng chỉ bind `127.0.0.1`, và service chạy với môi trường **khác Development** mà còn thấy mật khẩu / khoá dev thì **từ chối khởi động** (lỗi `S-08: moi truong ... dang dung mat khau / khoa DEV o: <khoá cấu hình>`).

**Biến / thành phần mới (đợt 10/2026).** `.env` cũ thiếu biến thì chạy lại `python deploy/tao_env.py` (chỉ bổ sung):

| Biến / thành phần | Dùng cho |
|---|---|
| `IDENTITY_DATA_KEY` | identity mã hoá bí mật 2FA, băm có khoá số giấy tờ (FC-04, FP-05). **Giữ nguyên mãi** — đổi khoá thì 2FA đã bật và số giấy tờ đã lưu không đọc lại được |
| container `redis-gateway` | Gateway đếm rate limit dùng chung (`ConnectionStrings__GatewayRedis`). Redis sập thì gateway tạm đếm trong bộ nhớ |
| Cổng thanh toán giả lập | payment-svc (chỉ Development) có trang "VNPay" / "MoMo" giả ở `/payments/sandbox/vnpay/*`, `/payments/sandbox/momo/*`, ký đúng thuật toán của cổng thật. Cấu hình cổng thật: `PaymentProviders:VnPay:*`, `PaymentProviders:MoMo:*`; bật / tắt bằng setting `payment.enabled_providers` |
| `DatasetImport__AllowedPrivateHosts__0: minio` | Nhập dữ liệu từ S3 chặn mọi IP nội bộ (SSRF); stack demo cho phép riêng host `minio` để E2E nhập từ MinIO như từ S3 |
| `QUALITY_PROJECT_URL`, `QUALITY_INTERNAL_API_KEY` | quality-svc tự bù bản sao dự án thiếu qua API nội bộ của project-svc |
| `MEDIA_AV_CACHE_DIR` / `MEDIA_AV_CACHE_TTL_SECONDS` / `MEDIA_AV_CACHE_MAX_BYTES` | Bộ đệm đĩa bản âm thanh / video đã watermark (mặc định `/tmp/media-wm`, 6 giờ, 5 GB). Image media cần `ffmpeg` (đã cài trong Dockerfile); chạy trên máy cần `ffmpeg` trong PATH |
| setting `ops.alert_email` | Email nhận cảnh báo DLQ (trống = chỉ log + `GET /admin/ops/queues`) |

## 0.2 Bộ test E2E và CI

Bộ E2E nằm ở `tests/e2e/`. Mỗi lần chạy, bộ tổng thể tự tạo tài khoản và dự án mới:

```bash
pip install -r tests/e2e/requirements.txt          # chỉ cần requests; e2e_modality cần thêm ffmpeg
python tests/e2e/chay_hoi_quy.py                   # chạy mọi bộ + kiểm DLQ, outbox, đối soát (máy dev)
E2E_PROFILE=demo python tests/e2e/chay_hoi_quy.py  # chạy trên stack demo (docker compose)
python tests/e2e/e2e_tong_the.py                   # hoặc từng bộ riêng
```

| Bộ | Phủ |
|---|---|
| `e2e_tong_the.py` | Xác thực, nạp tiền, vòng đời dự án + ký quỹ, test đầu vào, lấy/nộp task, đồng thuận + posterior/VOI, duyệt + khiếu nại, vòng tiền tới rút, cửa sổ lấy task, đóng dự án, phân quyền, định dạng nhãn |
| `e2e_settings.py` | Đổi setting lúc chạy (phí, tự duyệt, giới hạn, nạp chuyển khoản, quality nhận setting) |
| `e2e_p3.py` | Đồng thuận, redundancy thích ứng, câu vàng, uy tín |
| `e2e_p2.py` | Cổng link: rút gọn, vượt link, Turnstile, chống trùng IP, hoa hồng, kiểm duyệt (`chay_hoi_quy` chạy hai lần: chi `perClick` và `batched`) |
| `e2e_modality.py` | Ảnh, văn bản, âm thanh, video, cặp, khung ảnh; media sinh bằng ffmpeg (`tao_media.py`) |
| `ep_race_quality.py` | Ép race: hai người nộp đồng thời nhãn cuối của cùng task |
| `ep_race_uy_tin.py` | Ép race: một labeler nộp 8 lượt ở 8 dự án **cùng lúc** → quality-svc quyết định 8 task song song; số lần khớp trong `reputations` phải bằng đúng số dòng `label_agreements` (trước khi có khoá theo labeler, 2/3 lần chạy bị đếm thiếu 1) |
| `e2e_kyb_thong_bao.py` | KYB doanh nghiệp (khai hồ sơ, giấy phép, từ chối → nộp lại → duyệt, chặn publish khi chưa xác minh), thông báo trong ứng dụng + **email thật qua Mailpit**, tuỳ chọn email, gộp thông báo, khoá / mở khoá tài khoản |
| `e2e_dong_cho_chi.py` | Kịch bản TLA+ tìm ra (NC-B-01): dừng container ledger, duyệt và đóng dự án, bật lại. Nhãn đã duyệt phải được trả dù `completed` tới trước `approved` (chỉ chạy với `E2E_PROFILE=demo`) |
| `e2e_bao_ve_du_lieu.py` | Link file qua media-svc (không lộ MinIO), chủ dự án nhận bản gốc, labeler / reviewer nhận ảnh có watermark, admin giải mã ảnh "bị lộ" ra đúng người (labeler 1 ≠ labeler 2, ảnh khác của cùng người vẫn ra người đó), token bị sửa → 403, hết hạn → 410, tắt watermark bằng setting |
| `e2e_hoan_thien.py` | Xác thực email (bấm link thật trong Mailpit), quên / đặt lại / đổi mật khẩu, thu hồi phiên ở gateway, hồ sơ + avatar + kỹ năng, cấp độ / huy hiệu / xếp hạng, nháp + báo lỗi task, chi phí / ETA, cảnh báo lệch, xuất YOLO sai loại dự án (nội dung zip YOLO / VOC kiểm ở unit test `HoanThienTests`), thu nhập, tranh chấp (cả hai kết cục, kiểm sổ cái), hồ sơ gian lận + khoá, audit + báo cáo + thông báo toàn hệ thống, rate limit 429 |
| `e2e_luu_tru.py` | Lưu trữ & dọn dẹp: một dòng mỗi phiên đăng nhập (làm mới 5 lần vẫn một dòng, dùng lại token cũ → thu hồi phiên), claim `email_verified` / `kyb_verified`, không còn `task.leased`, audit không lưu chi tiết `annotation.submitted` nhưng báo cáo vẫn đếm, mẫu lưu một lần (`annotation_samples`), gate không giữ mẫu dự án không bật cổng link, **dọn outbox đã gửi ở cả 11 service** (hạ `outbox.retention` xuống 1 giờ), dọn dự án đã đóng ở task-svc + quality-svc (tiến độ, báo cáo chất lượng, nhãn và uy tín giữ nguyên) |
| `e2e_cong_cu.py` | Công cụ gán nhãn mở rộng + gộp nhãn (kiến trúc 3.15): ảnh có khung kèm thuộc tính (OCR), điểm mốc, đường gấp khúc, mặt nạ, số, chép chữ, văn bản tự do — nộp sai bị từ chối đúng lý do, quality-svc tính đồng thuận, kết quả gộp và file COCO / CSV / YOLO; điểm mốc lệch nhau → tranh chấp → xin thêm người → người thứ ba phá thế hoà; văn bản có **quan hệ** giữa các đoạn; dữ liệu **danh sách** với xếp hạng (Borda) + thang điểm (trung vị); **theo dõi đối tượng** trong video; bài test đầu vào và câu vàng bằng điểm mốc (chấm theo đường chéo ảnh); đổi ngưỡng ghép bằng setting lúc đang chạy. Cần ffmpeg |
| `e2e_bao_mat_tai_khoan.py` | 2FA (bật, đăng nhập hai bước, sai mã / hết lượt, mã dự phòng, tắt, admin đặt lại), xác minh danh tính + đủ tuổi (một giấy tờ một tài khoản), rate limit dùng chung qua Redis |
| `e2e_cong_thanh_toan.py` | Nạp qua VNPay / MoMo (cổng giả lập): chữ ký sai, IPN trùng, sửa số tiền, Return URL không ghi tiền, `sync`, đối soát lệnh treo, hết hạn |
| `e2e_hoa_don.py` | Hoá đơn VAT: tạm tính, xuất tháng, số hoá đơn liên tục, HTML / XML, không sửa / xoá được |
| `e2e_bao_ve_du_an.py` | Nội dung nhạy cảm / NDA / yêu cầu xác minh, ép nghỉ giải lao (429), rời dự án, lời mời (email chưa có tài khoản, cần đồng ý), danh sách ưu tiên / chặn, đánh giá, gợi ý dự án, đơn hết hạn, xoá file sau khi đóng, đơn giá tối thiểu |
| `e2e_nhap_nguon_ngoai.py` | Nhập dữ liệu từ S3 (MinIO), danh sách URL, Google Drive; chặn SSRF |
| `e2e_xuat_mo_rong.py` | Khung xoay, track ẩn / hiện lại, xuất yolo-obb / yolo-seg / voc / coco-keypoints / conll / mot, bộ đệm kết quả gộp |
| `e2e_chi_so_chat_luong.py` | Krippendorff alpha mọi loại công cụ, Fleiss' kappa, STAPLE mặt nạ; quality-svc tự bù bản sao dự án thiếu |
| `e2e_watermark_av.py` | Watermark âm thanh (FLAC) / video (H.264): tua (Range 206), bộ đệm, admin giải mã ra đúng labeler, tắt bằng setting. Cần ffmpeg |
| `e2e_hang_doi_duyet.py` | Hàng đợi duyệt chung (KYB, dự án, khiếu nại, link, giới thiệu nghi lạm dụng), audit lượt xem giấy tờ (S-07), cổng link chỉ cho công cụ làm nhanh, mẫu bài toán, audit chỉ-ghi-thêm ở mức CSDL, cảnh báo DLQ |
| `e2e_thong_bao_realtime.py` | Ngôn ngữ thông báo, WebSocket qua gateway (vé dùng một lần, mã đóng 4401), email 2FA bắt buộc, thư mời người chưa có tài khoản, email cảnh báo DLQ, hạn giữ thông báo |
| `e2e_thanh_vien.py` | Vào dự án (kiến trúc 3.16): mời theo email / userId, một và nhiều người (kết quả từng dòng, giới hạn lô); **VD-S-16**: mời theo email ra cùng một kết quả cho email có / chưa có tài khoản / đã là thành viên / bị chặn, chủ dự án không thấy userId tới khi chấp nhận, lời mời câm, hạn mức email / 24 giờ (429 + `Retry-After`); mã mời (gõ tay, hết lượt, hết hạn, thu hồi, **6 người tranh 3 lượt**, dùng được khi dự án còn Nháp); kiểm cấp độ / uy tín ở `join`, lúc mời, mã mời và **trước** bài test; chế độ `approval` (gửi đơn, trùng, từ chối + thời gian chờ, rút, duyệt, uy tín tụt lúc duyệt, đơn tự đóng khi vào bằng đường khác) và `approval` kèm bài test (đạt → đơn kèm điểm); thông báo cho cả hai phía; nhận task thật sau khi vào. Cấp độ / uy tín đặt thẳng trong `identity_db.labeler_stats` |

Tài khoản doanh nghiệp do E2E tự tạo được **xác minh KYB tự động** (`tro_giup.dang_ky(..., xac_minh=True)`, mặc định) để các bộ cũ publish được như trước. Tương tự, mọi tài khoản E2E tự tạo được **xác thực email** bằng link thật trong Mailpit (`xac_thuc=True`, mặc định) — chưa xác thực thì không rút tiền / publish được.

**Giới hạn tần suất khi chạy E2E.** E2E đăng nhập / đăng ký hàng trăm lần từ một IP, vượt hạn mức mặc định của gateway (20 lần xác thực / phút / IP). `cho_san_sang.py`, `chay_hoi_quy.py` và `tro_giup.py` tự **nâng** các setting `gateway.rate_limit_*` lên rất lớn lúc bắt đầu; `chay_hoi_quy.py` trả về mặc định lúc kết thúc. Chạy lẻ một bộ rồi dừng giữa chừng thì hạn mức có thể còn ở mức cao — đặt lại ở màn hình setting admin nếu cần.

Mật khẩu RabbitMQ (để kiểm DLQ) E2E đọc từ biến `E2E_RABBIT_PASSWORD`, hoặc từ `.env` khi `E2E_PROFILE=demo`, mặc định `dev_rabbit_pw` (máy dev).

Kết quả từng bộ ghi vào `tests/e2e/ket-qua/`. Địa chỉ và tên container đọc từ `moi_truong.py` (`E2E_PROFILE`, `E2E_GATEWAY`...).

**CI** (`.github/workflows/ci.yml`, mỗi push và pull request):
1. `unit`: `dotnet test -c Release` và `pytest` của quality-svc.
2. `e2e`: `python deploy/tao_env.py` (mật khẩu ngẫu nhiên) → `docker compose -f docker-compose.demo.yml up --build` trên máy sạch → `cho_san_sang.py` → `chay_hoi_quy.py`. Lỗi thì tải log mọi container về dạng artifact.

---

## 1. Yêu cầu

| Công cụ | Phiên bản | Ghi chú |
|---|---|---|
| Docker Desktop | bản mới | Chạy Postgres, RabbitMQ, MinIO |
| .NET SDK | 10 (xem [global.json](../global.json)) | `dotnet --version` |
| Git Bash | có sẵn khi cài Git | Các lệnh trong tài liệu viết cho bash |
| curl | có sẵn trong Git Bash | Trong **PowerShell 5.1**, `curl` là alias của `Invoke-WebRequest` nên phải gõ `curl.exe` |
| Python | **3.12** | Chạy `quality-svc` (mục 3). Cũng dùng cho mục 6.11 (tạo file ZIP ảnh) |
| FFmpeg (ffprobe) | tùy chọn | Đo thời lượng / kích thước audio, video khi nạp (mục 6.14). Windows: `winget install Gyan.FFmpeg`, mở terminal mới để có trong PATH. Không có thì manifest audio/video phải khai `durationSec` |

---

## 2. Khởi động hạ tầng

```bash
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 --profile p2 --profile p3 --profile p6 up -d
docker ps --format "table {{.Names}}\t{{.Status}}"      # đợi mọi container (healthy)
```

`--profile p0` bật Postgres của identity, project, task, annotation **và admin** (setting hệ thống, mục 6.16). `--profile p1` bật thêm Postgres của ledger và payment. `--profile p2` bật hạ tầng cổng link: Postgres của link và gate, Redis riêng của gate, ClickHouse. `--profile p3` bật Postgres của quality. `--profile p6` bật Postgres của notification. RabbitMQ và MinIO thuộc nhóm core nên luôn được bật. Container `minio-init` chạy một lần để tạo bucket `datasets` rồi tự thoát, đó là bình thường.

| Thành phần | Cổng | Đăng nhập (dev) |
|---|---|---|
| RabbitMQ | 5672, UI **15672** | `datn` / `dev_rabbit_pw` |
| MinIO | 9000, UI **9001** | `datn` / `dev_minio_pw` |
| Postgres identity / project / task / annotation | 5401 / 5402 / 5403 / 5404 | `<tên>_user` / `dev_<tên>_pw` |
| Postgres ledger / payment | 5405 / 5406 | như trên |
| Postgres quality | 5421 | `quality_user` / `dev_quality_pw` |
| Postgres admin | 5411 | `admin_user` / `dev_admin_pw` |
| Postgres notification | 5410 | `notification_user` / `dev_notification_pw` |
| Mailpit (hộp thư giả, nhận mọi email) | SMTP 1025, UI **8025** | không cần. Mở http://localhost:8025 để đọc email thông báo |
| Postgres link / gate | 5407 / 5408 | `link_user` / `dev_link_pw`, `gate_user` / `dev_gate_pw` |
| Redis gate | 6381 | không mật khẩu (dev). `docker exec -it datn-redis-gate redis-cli` |
| ClickHouse gate | HTTP **8123** | `gate_user` / `dev_gate_pw`, database `gate_db` |

Ví dụ mở psql: `docker exec -it datn-db-ledger psql -U ledger_user -d ledger_db`

---

## 3. Chạy các service

| Service | Cổng | Lệnh |
|---|---|---|
| gateway (Ocelot) | **8080** | `dotnet run --project services/gateway/Crowd.Gateway.Api` |
| admin-svc | 8111 | `dotnet run --project services/admin/Crowd.Admin.Api` |
| identity-svc | 8101 | `dotnet run --project services/identity/Crowd.Identity.Api` |
| project-svc | 8102 | `dotnet run --project services/project/Crowd.Project.Api` |
| task-svc | 8103 | `dotnet run --project services/task/Crowd.Tasking.Api` |
| annotation-svc | 8104 | `dotnet run --project services/annotation/Crowd.Annotation.Api` |
| ledger-svc | 8105 | `dotnet run --project services/ledger/Crowd.Ledger.Api` |
| payment-svc | 8106 | `dotnet run --project services/payment/Crowd.Payment.Api` |
| link-svc | 8107 | `dotnet run --project services/link/Crowd.Link.Api` |
| gate-svc | 8108 | `dotnet run --project services/gate/Crowd.Gate.Api` |
| notification-svc | 8110 | `dotnet run --project services/notification/Crowd.Notification.Api` |
| quality-svc (Python) | 8201 | `cd services/quality && .venv/Scripts/python -m app.main` |
| media-svc (Python) | 8109 | `cd services/media && .venv/Scripts/python -m app.main` |

**quality-svc lần đầu** cần tạo môi trường Python (một lần):

```bash
cd services/quality
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt      # Linux/macOS: .venv/bin/pip
.venv/Scripts/python -m pytest -q                   # 111 test logic: đồng thuận, gộp nhãn (bộ ví dụ chung với C#), uy tín, Dawid-Skene, hợp đồng envelope, setting
```

**media-svc lần đầu** (phát file mẫu qua token + watermark chìm — **không có database**, chỉ cần RabbitMQ + MinIO):

```bash
cd services/media
python -m venv .venv
.venv/Scripts/pip install -r requirements.txt
.venv/Scripts/python -m pytest -q                   # 25 test: token chéo C#/Python, độ bền watermark, Range
```

Biến `MEDIA_*` chỉ giữ hạ tầng ([app/config.py](../services/media/app/config.py)): RabbitMQ, MinIO, `MEDIA_GRANT_KEY` (phải **trùng** `MediaLinks:GrantKey` của project / task / annotation / gate), `MEDIA_WATERMARK_KEY` (giữ nguyên mãi — đổi thì ảnh đã phát không giải mã được nữa).

Biến môi trường tiền tố `QUALITY_` chỉ còn giữ phần hạ tầng (chuỗi kết nối, RabbitMQ, JWKS, môi trường — xem [app/config.py](../services/quality/app/config.py)). Production phải đặt `QUALITY_ENVIRONMENT=Production` (tắt seed). Chu kỳ Dawid–Skene, trọng số uy tín… là **setting hệ thống**: muốn DS chạy mỗi phút thì `PUT /admin/settings/quality.ds_interval {"value":60}` (mục 6.16).

- **Thứ tự không quan trọng.** Mỗi service tự migrate database của mình khi khởi động (VD-O-03), rồi tự seed (mục 4). Các service không gọi nhau lúc seed.
- **Setting:** mỗi service nạp bản sao setting trong DB của mình rồi xin admin-svc phát lại toàn bộ. admin-svc chưa chạy thì service dùng giá trị mặc định của catalog, khi admin-svc lên sẽ tự đồng bộ (log `Setting: nap N gia tri tu ban sao, da xin admin-svc phat lai`).
- **Mọi request đều đi qua gateway `http://localhost:8080`.** Gọi thẳng cổng 81xx chỉ dùng để debug.
- Khi khởi động lần đầu trên database trống, log mỗi service sẽ có một dòng `Seed ...`:

```text
Setting: khoi tao 83 khoa moi, 83 khoa trong danh muc     (admin-svc, lần đầu)
Seed identity: tao 7 tai khoan, mat khau chung 'Matkhau@123'
Seed project: tao 7 du an, 34 mau
Seed task: 7 du an, 34 task, 14 luot da nop
Seed annotation: tao 14 nhan
Seed ledger: 6 ky quy, 7 lan chi tra, 6 khoan treo da giai phong
Seed payment: tao 2 lenh nap
Seed quality: them 5 / 5 du an          (log của quality-svc, định dạng Python)
```

Từ lần chạy thứ hai, log sẽ là `Seed ...: da co du lieu seed — bo qua` (identity, payment) hoặc `da du du an seed — bo qua` (project, task, annotation, ledger). DB cũ chỉ có P1–P4 thì lần chạy đầu sau khi cập nhật code sẽ seed thêm P5–P7.

Nếu muốn bật cả 11 service C# trong **một** terminal Git Bash (log ghi ra thư mục `logs/`):

```bash
mkdir -p logs
dotnet run --project services/admin/Crowd.Admin.Api           > logs/admin.log 2>&1 &
dotnet run --project services/identity/Crowd.Identity.Api     > logs/identity.log 2>&1 &
dotnet run --project services/project/Crowd.Project.Api       > logs/project.log 2>&1 &
dotnet run --project services/task/Crowd.Tasking.Api          > logs/task.log 2>&1 &
dotnet run --project services/annotation/Crowd.Annotation.Api > logs/annotation.log 2>&1 &
dotnet run --project services/ledger/Crowd.Ledger.Api         > logs/ledger.log 2>&1 &
dotnet run --project services/payment/Crowd.Payment.Api       > logs/payment.log 2>&1 &
dotnet run --project services/link/Crowd.Link.Api             > logs/link.log 2>&1 &
dotnet run --project services/gate/Crowd.Gate.Api             > logs/gate.log 2>&1 &
dotnet run --project services/notification/Crowd.Notification.Api > logs/notification.log 2>&1 &
dotnet run --project services/gateway/Crowd.Gateway.Api       > logs/gateway.log 2>&1 &
grep -h "Seed " logs/*.log        # đợi ~20 giây rồi xem kết quả seed
```

Thư mục `logs/` chỉ để xem tạm. File `*.log` đã nằm trong `.gitignore`.

---

## 4. Dữ liệu seed

### 4.1 Cơ chế

```text
            shared/seeding/KichBanSeed.cs   ← MỘT kịch bản, nguồn sự thật duy nhất
                          │  (ID tất định: SeedIds.Tu("project:p1") → luôn cùng một Guid)
   ┌──────────┬───────────┼────────────┬─────────────┬────────────┐
identity    project      task       annotation      ledger      payment
tài khoản   dự án, ảnh   task,      nhãn +          ký quỹ,     lệnh nạp
            (MinIO),     lượt nộp   duyệt/khiếu nại chi trả
            câu vàng
```

- **Mỗi service tự ghi phần của mình vào database riêng.** Không service nào ghi vào DB của service khác, đúng nguyên tắc database-per-service.
- **ID khớp nhau giữa các service** vì được tính tất định từ tên. Ví dụ dự án P1 ở project-svc, task-svc, annotation-svc và ledger-svc đều là `3d904a1a-3920-530b-b055-793baae64f1b`.
- **Seed đi qua code nghiệp vụ thật, không ghi bảng bằng tay:**
  - Domain: `LabelingProject.Tao` → `YeuCauPublish` → `Duyet`, `Assignment.Tao` → `Nop`, `LabelAnnotation.Duyet` / `TuChoi` / `KhieuNai`.
  - Service: `MoneyFlowService` của ledger.
  - Processor: task, annotation và ledger phát lại các event `member.added`, `project.published`, `gold_set.updated` vào chính processor của mình.
  - Nhờ vậy kịch bản sai luật (ví dụ ngân sách không đủ ký quỹ) sẽ ném lỗi ngay khi seed.
- **quality-svc (Python) không đọc được kịch bản C#**, nên test [QualitySeedSnapshotTests](../shared/test/seeding-tests/QualitySeedSnapshotTests.cs) xuất bản sao 5 dự án seed đang chạy ra [shared/seeding/quality-seed.json](../shared/seeding/quality-seed.json), quality-svc nạp file này lúc khởi động. Sửa kịch bản thì chạy `UPDATE_SEED_SNAPSHOT=1 dotnet test shared/test/seeding-tests`.
- **Seed không phát event nào ra RabbitMQ.** Code nghiệp vụ có xếp event vào outbox, nhưng seeder gỡ các event đó trước khi lưu (`SeedOutbox.BoEventChuaGui`), vì service nào cũng đã tự seed phần của mình.
- **Ledger seed bằng "đồng hồ lùi về quá khứ"** (`DongHoCoDinh`). Nhãn được duyệt 4 ngày trước thì khoản treo đã hết hạn, nên labeler rút được tiền ngay.
- **Chỉ chạy khi môi trường là `Development` và `Seed:Enabled = true`** ([`SeedSwitch`](../shared/seeding/SeedHelpers.cs)). Ở production, quên tắt cờ cũng không seed được.
- **Chạy lại an toàn, theo từng dự án:** dự án nào đã có thì bỏ qua, dự án mới thêm vào kịch bản thì được seed thêm. Ví dụ DB cũ đã có P1–P4 thì lần chạy sau chỉ thêm P5–P7.
- [shared/test/seeding-tests](../shared/test/seeding-tests/KichBanSeedTests.cs) kiểm kịch bản đúng luật: ngân sách ≥ ký quỹ tối thiểu, không vượt redundancy, reviewer không tự duyệt, khiếu nại còn trong hạn…

### 4.2 Tài khoản (mật khẩu chung `Matkhau@123`)

| Key | Email | ID | Vai trò | Dùng để test |
|---|---|---|---|---|
| admin | `admin@crowd.local` | `e142c9aa-4f06-542c-9ac7-7c9bbaa6f65a` | admin | Duyệt dự án, phân xử khiếu nại, đối soát |
| biz1 | `doanhnghiep1@crowd.local` | `37665dec-c58e-5c8f-a244-476f27222311` | business | Chủ 7 dự án, đã nạp 2.000.000đ |
| biz2 | `doanhnghiep2@crowd.local` | `d3b5a5c5-0232-5935-9e4a-55812983f1a6` | business | **Chưa có tiền**, có một lệnh nạp 1.000.000đ chưa trả |
| lab1 | `labeler1@crowd.local` | `1f5e19a7-9f58-589b-8c74-45334ff35d05` | labeler | Có **63.000đ rút được ngay**, 2 nhãn P1 chờ duyệt. Thành viên P1, P5, P6, P7 |
| lab2 | `labeler2@crowd.local` | `0398f264-d694-5176-9614-844201175b74` | labeler | Ở P1: 1 nhãn được duyệt, 1 bị từ chối, 1 đang khiếu nại. Thành viên P1, P5, P7 |
| lab3 | `labeler3@crowd.local` | `14299c5f-f7c3-5075-b65a-cd4fbc2aa194` | labeler | Chưa vào dự án nào, dùng để test tham gia và bài test đầu vào |
| rev1 | `reviewer1@crowd.local` | `0d352aaf-338d-5930-8bd7-e737a04e3ba3` | labeler (**reviewer của P1**) | Duyệt nhãn với tư cách reviewer |

`rev1` có vai trò hệ thống là `labeler`. "Reviewer" là vai trò *trong dự án*, do chủ dự án gán.

biz1 và biz2 đã được **xác minh KYB** sẵn (hồ sơ `verified`), nên publish được ngay. Doanh nghiệp đăng ký mới phải nộp hồ sơ và chờ admin duyệt (mục 6.18).

### 4.3 Dự án (tên bắt đầu bằng `[Seed]`, chủ là biz1)

| Key | ID | Trạng thái | Ảnh | Đơn giá | Redundancy | Ngân sách (= ký quỹ) | Đặc điểm |
|---|---|---|---|---|---|---|---|
| **P1** | `3d904a1a-3920-530b-b055-793baae64f1b` | `running` | 8 | 20.000 | 2 | 500.000 | Có nhãn ở đủ trạng thái; ảnh 8 là câu vàng kiểm tra chất lượng. Thành viên: lab1, lab2 (labeler), rev1 (reviewer) |
| **P2** | `2ae9dc42-5b32-518d-b24f-cdd7da800a96` | `pendingApproval` | 4 | 10.000 | 1 | 100.000 | Đã ký quỹ, chờ admin duyệt. **Tự hủy sau 72 giờ** nếu không ai duyệt |
| **P3** | `32cdcbe6-837a-5c7c-929d-607db130deba` | `draft` | 4 | 15.000 | 2 | 200.000 | Đã cấu hình đủ, `readiness` = ready. Dùng để test saga publish |
| **P4** | `2fde6d4c-ee31-57ae-8632-b2e6e0721217` | `running` | 7 | 30.000 | 1 | 300.000 | **Bắt buộc test đầu vào**: 3 câu, đậu khi đúng ≥ 60%. Ảnh 5–7 là câu hỏi test |

**P8** (`00ed5dcd-3888-5f15-a4d8-ab7c5692f5ad`, chủ là **biz2**, `draft`, 4 ảnh, đơn giá 20.000, redundancy 2, ngân sách 300.000): tập nhãn hình học — `khung` (bbox **xoay được**, lớp `vat_the`), `vung` (polygon, `mau_chinh`), `diem` (keypoint, `tam`). Bấm publish để thử cả luồng với khung xoay và các định dạng xuất mới. Ở chế độ dev, admin-svc seed sẵn 4 mẫu bài toán (`GET /templates`).

P1–P4 là dự án **ảnh**, tập nhãn có một công cụ phân loại tên `label` với các lớp `do`, `xanh_la`, `xanh_duong`, `vang`. Ảnh mẫu là PNG một màu 256×256 có viền sáng, nên nhìn ảnh là biết đáp án đúng. Phí nền tảng chốt 30%, cộng thêm vào ký quỹ chứ không trừ vào thù lao (VD-M-15).

Ba dự án loại dữ liệu khác, cùng đang `running`, ngân sách 50.000:

| Key | ID | Loại dữ liệu | Mẫu | Đơn giá | Redundancy | Công cụ trong tập nhãn | Nhãn seed |
|---|---|---|---|---|---|---|---|
| **P5** | `8dc63fe1-e23e-5097-b891-51fe72b61245` | `text` | 4 câu | 5.000 | 1 | `cam_xuc` (classification: `tich_cuc`, `tieu_cuc`, `trung_tinh`), `thuc_the` (span: `TEN_NGUOI`, `DIA_DIEM`, `TO_CHUC`) | câu 1: lab1 chờ duyệt; câu 2: lab2 đã duyệt |
| **P6** | `8309618e-25b2-5b99-a301-0632197c96e1` | `audio` | 4 đoạn | 8.000 | 1 | `loi_noi` (transcription), `doan` (temporalSegment: `giong_noi`, `nhac`, `im_lang`). `segmentSeconds: 10` | đoạn 0–10s: lab1 chờ duyệt |
| **P7** | `12d52faa-09b4-5cd1-b21d-16984c952fec` | `pair` | 3 cặp | 3.000 | 2 | `tot_hon` (pairwise, cho hòa), `an_toan` (classification: `an_toan`, `khong_an_toan`) | cặp 1: lab1 + lab2 đều chọn `a`, đã duyệt → kết quả gộp `a`; cặp 2: lab2 chờ duyệt |

P6 có hai file WAV trong MinIO: `cuoc-goi-01.wav` dài 25 giây được **cắt thành 3 đoạn** (0–10, 10–20, 20–25; ba mẫu dùng chung một file, khác `segmentStart`/`segmentEnd`), và `loi-chao.wav` dài 8 giây (ngắn hơn một đoạn nên giữ nguyên). Âm thanh là một nốt sine, chỉ để test luồng.

### 4.4 Nhãn của P1

| Ảnh (đáp án) | lab1 | lab2 |
|---|---|---|
| 1 (`do`) | `do`, **duyệt** 4 ngày trước | `do`, **duyệt** lúc seed (tiền treo 2 phút) |
| 2 (`do`) | `do`, **duyệt** 4 ngày trước | `vang`, **bị từ chối** 2 giờ trước, còn khiếu nại được |
| 3 (`xanh_la`) | `xanh_la`, **duyệt** ~4 ngày trước (rev1 duyệt) | — |
| 4 (`xanh_la`) | `xanh_la`, **chờ duyệt** | — |
| 5 (`xanh_duong`) | — | `xanh_la`, bị từ chối rồi **đang khiếu nại** |
| 6 (`xanh_duong`) | `xanh_duong`, **chờ duyệt** | — |
| 7 (`vang`) | — | — |
| 8 (`vang`) | câu vàng → task `excluded` | |

ID các nhãn dùng trong mục 6:

| Nhãn | ID | Trạng thái lúc seed |
|---|---|---|
| lab1, ảnh 4 | `374fd43c-82c4-55d6-ae35-f7258f7e063b` | chờ duyệt |
| lab1, ảnh 6 | `925b78bf-31e6-5534-b04d-505a00a570f2` | chờ duyệt |
| lab2, ảnh 2 | `0f95272a-8b7a-5b8c-8f78-58f63a562c75` | bị từ chối, khiếu nại được |
| lab2, ảnh 5 | `af8391bc-4df6-5176-ba9a-08d1136bc926` | đang khiếu nại, chờ admin |
| lab1, ảnh 1 | `7efccfd8-ab86-58e6-93d8-9b84d6802967` | đã duyệt |

Tiến độ P1 lúc vừa seed: 8 task, 2 hoàn thành (ảnh 1, 2), 1 bị loại (ảnh 8), 8 lượt nộp, cần 14 lượt.

### 4.5 Tiền lúc vừa seed

| Tài khoản | Số dư |
|---|---|
| biz1 | khả dụng **950.000**, ký quỹ **931.700** (P1 còn 396.000 sau 4 lần chi × 26.000; P2 100.000; P4 300.000; P5 còn 43.500; P6 50.000; P7 còn 42.200) |
| biz2 | 0. Lệnh nạp 1.000.000 đang `pending`, ID `8d241233-a997-5c1a-9f2f-2924b419164c` |
| lab1 | khả dụng **63.000** (60.000 ở P1 + 3.000 ở P7) |
| lab2 | khả dụng 8.000 (P5 5.000 + P7 3.000), treo 20.000 (P1), sau ~2 phút worker chuyển sang khả dụng |
| Đối soát | `healthy: true` |

Ở dev, thời gian treo là **2 phút**: admin-svc khởi tạo setting `ledger.hold_duration = 120` giây khi chạy ở Development (production giữ mặc định 3 ngày). Worker quét theo `ledger.hold_release_interval` (mặc định 60 giây). Cả hai đổi được lúc chạy (mục 6.16).

### 4.6 Tắt seed / seed lại từ đầu

- **Tắt:** đặt `"Seed": { "Enabled": false }` trong `appsettings.Development.json` của service tương ứng.
- **Seed lại từ đầu:** phải xóa dữ liệu. Lệnh dưới **xóa toàn bộ dữ liệu dev**, gồm cả dữ liệu bạn tự tạo, RabbitMQ và MinIO:

```bash
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 down -v
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 up -d
# rồi chạy lại các service
```

- **Sửa kịch bản:** sửa [KichBanSeed.cs](../shared/seeding/KichBanSeed.cs), chạy `dotnet test shared/test/seeding-tests`, rồi seed lại từ đầu như trên. Seed chỉ chạy trên DB chưa có bản ghi mốc, nên sửa kịch bản mà không xóa dữ liệu thì không có tác dụng.

---

## 5. Lấy token

Đăng nhập từng tài khoản và copy giá trị `accessToken` trong response. Token sống **15 phút**; hết hạn thì đăng nhập lại.

```bash
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" -d '{"email":"admin@crowd.local","password":"Matkhau@123"}'
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" -d '{"email":"doanhnghiep1@crowd.local","password":"Matkhau@123"}'
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" -d '{"email":"doanhnghiep2@crowd.local","password":"Matkhau@123"}'
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" -d '{"email":"labeler1@crowd.local","password":"Matkhau@123"}'
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" -d '{"email":"labeler2@crowd.local","password":"Matkhau@123"}'
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" -d '{"email":"labeler3@crowd.local","password":"Matkhau@123"}'
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" -d '{"email":"reviewer1@crowd.local","password":"Matkhau@123"}'
```

Response có dạng:

```json
{"accessToken":"eyJhbGciOiJSUzI1NiIs...","refreshToken":"...","tokenType":"Bearer","expiresIn":900}
```

Phần `eyJ...` của `accessToken` là thứ dán vào chỗ `<token-...>` trong các lệnh bên dưới.

Các mục dưới đây giả định **dữ liệu vừa seed**. Đã chạy thử luồng nào rồi thì số dư và trạng thái sẽ khác bảng kỳ vọng. Muốn về lại trạng thái ban đầu thì làm theo mục 4.6.

---

## 6. Test theo luồng

### 6.1 Xác thực

```bash
curl -s http://localhost:8080/me -H "Authorization: Bearer <token-admin>"           # roles: ["admin"]

curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@crowd.local","password":"sai"}'                               # 401, thông báo chung chung (VD-S-13)

curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/projects             # 401: gateway chặn khi thiếu token

# Đăng ký tài khoản mới (không đăng ký được role admin)
curl -s -X POST http://localhost:8080/auth/register -H "Content-Type: application/json" \
  -d '{"email":"moi@test.vn","password":"Matkhau@123","displayName":"Nguoi moi","roles":["labeler"]}'

# Làm mới / đăng xuất: lấy refreshToken từ response của /auth/login
curl -s -X POST http://localhost:8080/auth/refresh -H "Content-Type: application/json" -d '{"refreshToken":"<refreshToken>"}'
curl -s -X POST http://localhost:8080/auth/logout  -H "Content-Type: application/json" -d '{"refreshToken":"<refreshToken>"}'   # luôn 204
```

### 6.2 Xem dữ liệu seed

```bash
curl -s "http://localhost:8080/projects?mine=true" -H "Authorization: Bearer <token-biz1>"        # 7 dự án [Seed]
curl -s http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b -H "Authorization: Bearer <token-biz1>"
curl -s http://localhost:8080/projects/32cdcbe6-837a-5c7c-929d-607db130deba/readiness -H "Authorization: Bearer <token-biz1>"   # ready: true

# fileUrl trong response mở được trên trình duyệt trong 5 phút; metadata có width/height của ảnh
curl -s "http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/samples?page=1&pageSize=20" -H "Authorization: Bearer <token-biz1>"
# Mẫu văn bản (P5): không có fileUrl, nội dung nằm trong "content"
curl -s "http://localhost:8080/projects/8dc63fe1-e23e-5097-b891-51fe72b61245/samples?page=1&pageSize=20" -H "Authorization: Bearer <token-biz1>"

curl -s http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/members -H "Authorization: Bearer <token-biz1>"
curl -s http://localhost:8080/tasks/projects/3d904a1a-3920-530b-b055-793baae64f1b/progress -H "Authorization: Bearer <token-biz1>"
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-biz1>"
curl -s http://localhost:8080/projects -H "Authorization: Bearer <token-lab3>"                     # labeler thấy dự án công khai
```

### 6.3 Doanh nghiệp nạp tiền (payment → ledger)

biz2 có sẵn một lệnh nạp chưa trả. Trả nó qua trang sandbox:

```bash
curl -s http://localhost:8080/payments/deposits/8d241233-a997-5c1a-9f2f-2924b419164c -H "Authorization: Bearer <token-biz2>"   # status: pending

# Cổng giả lập ký HMAC rồi gọi webhook
curl -s -X POST "http://localhost:8080/payments/sandbox/checkout/8d241233-a997-5c1a-9f2f-2924b419164c/pay?amount=1000000"

sleep 3
curl -s http://localhost:8080/payments/deposits/8d241233-a997-5c1a-9f2f-2924b419164c -H "Authorization: Bearer <token-biz2>"   # status: succeeded
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-biz2>"                                         # businessAvailableVnd: 1000000
```

Gọi lại `pay` lần nữa thì không cộng tiền thêm, vì webhook lặp lại bị bỏ qua.

Tạo lệnh nạp mới. Header `Idempotency-Key` là bắt buộc; gửi hai lần cùng key thì vẫn chỉ có một lệnh, cùng `intentId`:

```bash
curl -s -X POST http://localhost:8080/payments/deposits -H "Authorization: Bearer <token-biz2>" \
  -H "Content-Type: application/json" -H "Idempotency-Key: nap-thu-1" -d '{"amountVnd":500000}'

# Trả sai số tiền → 409 sai_so_tien
curl -s -X POST "http://localhost:8080/payments/sandbox/checkout/<intentId-moi>/pay?amount=10000"
```

### 6.4 Publish dự án: saga ký quỹ (project → ledger → project)

```bash
curl -s -X POST http://localhost:8080/projects/32cdcbe6-837a-5c7c-929d-607db130deba/publish -H "Authorization: Bearer <token-biz1>"   # status: pendingEscrow
sleep 3
curl -s http://localhost:8080/projects/32cdcbe6-837a-5c7c-929d-607db130deba -H "Authorization: Bearer <token-biz1>"                 # status: pendingApproval
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-biz1>"                                             # khả dụng −200.000, ký quỹ +200.000
```

Luồng event: `project.publish_requested` → ledger giữ tiền → `escrow.reserved` → project chuyển sang chờ duyệt. Nhánh thiếu tiền (`escrow.rejected`) xem mục 6.11.

### 6.5 Admin duyệt / từ chối dự án

```bash
curl -s http://localhost:8080/projects/pending-approval -H "Authorization: Bearer <token-admin>"     # thấy P2 (và P3 nếu đã làm 6.4)

# Duyệt P2 → running, phát project.published
curl -s -X POST http://localhost:8080/projects/2ae9dc42-5b32-518d-b24f-cdd7da800a96/approve -H "Authorization: Bearer <token-admin>"

# Từ chối P3 → cancelled, ledger hoàn ký quỹ
curl -s -X POST http://localhost:8080/projects/32cdcbe6-837a-5c7c-929d-607db130deba/reject -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"reason":"Anh khong phu hop"}'

sleep 3
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-biz1>"
```

Labeler thường không được duyệt dự án (**403**):

```bash
curl -s -X POST http://localhost:8080/projects/2ae9dc42-5b32-518d-b24f-cdd7da800a96/approve -H "Authorization: Bearer <token-lab1>"
```

### 6.6 Labeler nhận task, nộp, bỏ qua (task → annotation)

**Định dạng nhãn.** Mỗi dự án có một **tập nhãn** gồm một hoặc nhiều **công cụ** ([shared/labeling](../shared/labeling/LabelSchema.cs)), ví dụ P1 có một công cụ phân loại tên `label`. Khi nộp, labeler **chỉ gửi phần dữ liệu** trong trường `payload`: một object, khóa là tên công cụ. Với P1 đó là `{"label": {"labelIds": ["vang"]}}`. Server kiểm hình dạng bằng JSON Schema ([contracts/labeling](../contracts/labeling)) và kiểm nghĩa theo tập nhãn và mẫu (lớp có trong tập nhãn, khung nằm trong ảnh, đoạn thời gian nằm trong đoạn audio...). Response trả nhãn về ở dạng đầy đủ, `taskType` là loại dữ liệu:

```json
{"taskType":"image","schemaVersion":1,"data":{"label":{"labelIds":["vang"]}}}
```

```bash
# 200 kèm assignmentId, modality, fileUrl (xem ảnh để biết màu), metadata (width/height), labelSchema; 204 = hết task cho bạn
curl -s -X POST http://localhost:8080/tasks/projects/3d904a1a-3920-530b-b055-793baae64f1b/next -H "Authorization: Bearer <token-lab1>"

# Nộp nhãn cho assignmentId vừa nhận
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/submit -H "Authorization: Bearer <token-lab1>" \
  -H "Content-Type: application/json" -d '{"payload":{"label":{"labelIds":["vang"]}}}'

# Hoặc bỏ qua thay vì nộp
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/release -H "Authorization: Bearer <token-lab1>"

curl -s http://localhost:8080/tasks/assignments/mine -H "Authorization: Bearer <token-lab1>"

# Nhãn vừa nộp xuất hiện bên annotation-svc (qua event assignment.submitted)
curl -s "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b?status=pendingReview" -H "Authorization: Bearer <token-biz1>"
```

Kiểm tra thêm:
- Nộp lại cùng `assignmentId`: bị từ chối `lease_khong_con`.
- Thiếu trường `payload` (ví dụ gửi kiểu cũ `{"labels":["vang"]}`): **400** `thieu_nhan`.
- `payload` sai hình dạng, ví dụ thiếu tên công cụ `{"payload":{"labelIds":["vang"]}}` hoặc thêm công cụ lạ: **400** `nhan_sai_dinh_dang`, `detail` chỉ rõ chỗ sai.
- Chọn hai lớp ở công cụ chỉ cho chọn một, hoặc lớp không có trong tập nhãn: **400** `nhan_khong_hop_le`.
- lab3 nhận task ở P1 khi chưa tham gia: **403** `khong_phai_thanh_vien`.

```bash
curl -s -X POST http://localhost:8080/tasks/projects/3d904a1a-3920-530b-b055-793baae64f1b/next -H "Authorization: Bearer <token-lab3>"
```

### 6.7 Duyệt nhãn → labeler có tiền (annotation → ledger)

```bash
# 2 nhãn chờ duyệt của lab1
curl -s "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b?status=pendingReview" -H "Authorization: Bearer <token-rev1>"

# rev1 duyệt nhãn ảnh 4 của lab1
curl -s -X POST http://localhost:8080/annotations/374fd43c-82c4-55d6-ae35-f7258f7e063b/approve -H "Authorization: Bearer <token-rev1>"

# biz1 từ chối nhãn ảnh 6 của lab1
curl -s -X POST http://localhost:8080/annotations/925b78bf-31e6-5534-b04d-505a00a570f2/reject -H "Authorization: Bearer <token-biz1>" \
  -H "Content-Type: application/json" -d '{"reason":"Thu tu choi"}'

sleep 3
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-lab1>"     # pendingVnd +20000 (sau 2 phút chuyển sang availableVnd)
curl -s http://localhost:8080/annotations/374fd43c-82c4-55d6-ae35-f7258f7e063b/history -H "Authorization: Bearer <token-biz1>"
```

Kiểm tra thêm:
- Duyệt lại nhãn đã duyệt: **409** `da_duyet`. Một nhãn chỉ sinh tiền một lần (VD-M-02).
- lab1 gọi `approve`: **404**. Người không có quyền duyệt dự án nhận 404 chứ không phải 403, để không lộ việc nhãn đó có tồn tại.

```bash
curl -s -X POST http://localhost:8080/annotations/374fd43c-82c4-55d6-ae35-f7258f7e063b/approve -H "Authorization: Bearer <token-rev1>"   # 409
curl -s -X POST http://localhost:8080/annotations/925b78bf-31e6-5534-b04d-505a00a570f2/approve -H "Authorization: Bearer <token-lab1>"   # 404
```

### 6.8 Khiếu nại (labeler → admin)

```bash
# lab2 khiếu nại nhãn ảnh 2 (bị từ chối 2 giờ trước)
curl -s -X POST http://localhost:8080/annotations/0f95272a-8b7a-5b8c-8f78-58f63a562c75/appeal -H "Authorization: Bearer <token-lab2>" \
  -H "Content-Type: application/json" -d '{"message":"Anh nay co sac vang, de nghi xem lai"}'

curl -s http://localhost:8080/annotations/mine -H "Authorization: Bearer <token-lab2>"      # appealedCount: 2

# Admin xem hàng đợi khiếu nại
curl -s "http://localhost:8080/annotations/appeals?page=1&pageSize=20" -H "Authorization: Bearer <token-admin>"

# Chấp nhận khiếu nại ảnh 5 → approved, lab2 được trả 20.000
curl -s -X POST http://localhost:8080/annotations/af8391bc-4df6-5176-ba9a-08d1136bc926/appeal/resolve -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"accept":true,"note":"Chap nhan"}'

# Bác khiếu nại ảnh 2 → bị từ chối vĩnh viễn
curl -s -X POST http://localhost:8080/annotations/0f95272a-8b7a-5b8c-8f78-58f63a562c75/appeal/resolve -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"accept":false,"note":"Anh mau do"}'
```

Khiếu nại lần hai trên cùng nhãn: **409**. Nếu khiếu nại trước còn đang chờ thì lỗi là `khong_the_khieu_nai`; nếu đã bị bác thì lỗi là `da_khieu_nai`.

### 6.9 Tham gia dự án và bài test đầu vào

```bash
# P1 không yêu cầu test → tham gia thẳng
curl -s -X POST http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/join -H "Authorization: Bearer <token-lab3>"
curl -s -X POST http://localhost:8080/tasks/projects/3d904a1a-3920-530b-b055-793baae64f1b/next -H "Authorization: Bearer <token-lab3>"   # giờ đã nhận được task

# P4 yêu cầu test → join bị chặn (409), phải làm bài
curl -s -X POST http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/join -H "Authorization: Bearer <token-lab3>"

# Bắt đầu bài test → attemptId, labelSchema + 3 câu (sampleId, fileUrl, metadata). Server không trả đáp án.
curl -s -X POST http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/entrance-test/attempts -H "Authorization: Bearer <token-lab3>"
```

Đáp án của 3 câu (xem ảnh cũng thấy):

| sampleId | Đáp án |
|---|---|
| `25bb3dd9-5900-5e1c-9d57-f84ed2c72645` | `do` |
| `4a1785a8-3058-5313-a0e8-017d9070854d` | `xanh_la` |
| `cf021f75-a9f0-5f11-be77-fc81779763de` | `vang` |

```bash
curl -s -X POST http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/entrance-test/attempts/<attemptId>/submit \
  -H "Authorization: Bearer <token-lab3>" -H "Content-Type: application/json" -d '{"answers":[
    {"sampleId":"25bb3dd9-5900-5e1c-9d57-f84ed2c72645","payload":{"label":{"labelIds":["do"]}}},
    {"sampleId":"4a1785a8-3058-5313-a0e8-017d9070854d","payload":{"label":{"labelIds":["xanh_la"]}}},
    {"sampleId":"cf021f75-a9f0-5f11-be77-fc81779763de","payload":{"label":{"labelIds":["vang"]}}}]}'
# → scorePercent 100, passed true, joinedProject true
# Câu trả lời sai định dạng → 400 nhan_sai_dinh_dang, bài CHƯA bị tính là đã nộp.

# Lịch sử làm bài (tối đa 3 lần)
curl -s http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/entrance-test/attempts -H "Authorization: Bearer <token-lab3>"
```

### 6.10 Labeler rút tiền (ledger → payment → ledger)

```bash
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-lab1>"          # availableVnd: 60000

curl -s -X POST http://localhost:8080/ledger/withdrawals -H "Authorization: Bearer <token-lab1>" \
  -H "Content-Type: application/json" -H "Idempotency-Key: rut-001" \
  -d '{"amountVnd":50000,"bankAccount":"VCB-0123456789"}'                                       # state: requested (≤ 2.000.000 nên tự duyệt)

sleep 8
curl -s http://localhost:8080/ledger/withdrawals/mine -H "Authorization: Bearer <token-lab1>"     # state: completed
curl -s "http://localhost:8080/ledger/me/transactions?page=1&pageSize=20" -H "Authorization: Bearer <token-lab1>"
```

Kiểm tra thêm:
- Gửi lại với cùng `Idempotency-Key: rut-001`: trả về đúng lệnh cũ, không trừ tiền lần hai.
- Rút 10.000: **400** `duoi_muc_toi_thieu` (tối thiểu 50.000 — setting `ledger.withdraw_min_vnd`).
- Lệnh vượt `ledger.withdraw_auto_approve_max_vnd` (mặc định 2.000.000) hoặc khi tắt `ledger.withdraw_auto_approve` thì nằm ở `pendingApproval` chờ admin (mục 6.16).
- Rút nhiều hơn số dư: **409** `khong_du_so_du`.

```bash
curl -s -X POST http://localhost:8080/ledger/withdrawals -H "Authorization: Bearer <token-lab1>" \
  -H "Content-Type: application/json" -H "Idempotency-Key: rut-nho" -d '{"amountVnd":10000,"bankAccount":"VCB-0123456789"}'      # 400
curl -s -X POST http://localhost:8080/ledger/withdrawals -H "Authorization: Bearer <token-lab1>" \
  -H "Content-Type: application/json" -H "Idempotency-Key: rut-lon" -d '{"amountVnd":5000000,"bankAccount":"VCB-0123456789"}'    # 409
```

### 6.11 Nhánh thiếu tiền: escrow.rejected (cần một file ZIP)

biz2 **chưa trả lệnh nạp** (bỏ qua mục 6.3) thì số dư bằng 0. Tạo dự án, nạp ảnh rồi publish thì ledger sẽ từ chối ký quỹ.

Tạo `anh.zip` gồm 3 ảnh PNG một màu (Python có sẵn `zlib`):

```bash
python - <<'EOF'
import struct, zlib, zipfile
def png(r, g, b, n=64):
    def khoi(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    hang = b"".join(b"\x00" + bytes([r, g, b]) * n for _ in range(n))
    return b"\x89PNG\r\n\x1a\n" + khoi(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 2, 0, 0, 0)) + khoi(b"IDAT", zlib.compress(hang)) + khoi(b"IEND", b"")
with zipfile.ZipFile("anh.zip", "w") as z:
    for ten, mau in {"do.png": (220, 40, 40), "xanh.png": (40, 170, 60), "vang.png": (235, 200, 40)}.items():
        z.writestr(ten, png(*mau))
EOF
```

```bash
# Tạo dự án → copy "id" trong response
curl -s -X POST http://localhost:8080/projects -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"name":"Thu thieu tien","description":"x","modality":"image","visibility":"public"}'

curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/label-schema -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"modality":"image","tools":[{"name":"label","kind":"classification","classes":["do","xanh_la","vang"]}]}'

curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/guideline -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"markdown":"Chon theo mau.","examples":[]}'

curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/pricing -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"unitPriceVnd":10000,"redundancy":1,"budgetVnd":100000,"deadline":"2027-12-31T00:00:00Z"}'

curl -s -X POST http://localhost:8080/projects/<id-du-an-moi>/datasets -H "Authorization: Bearer <token-biz2>" -F "name=lo-1" -F "file=@anh.zip"

# (Tùy chọn) Thêm câu vàng. Lấy "id" của ảnh vang.png từ danh sách mẫu, dán vào <sampleId>.
# expectedPayload chỉ là phần dữ liệu, giống payload khi labeler nộp.
curl -s "http://localhost:8080/projects/<id-du-an-moi>/samples?page=1&pageSize=20" -H "Authorization: Bearer <token-biz2>"
curl -s -X POST http://localhost:8080/projects/<id-du-an-moi>/gold-items -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"items":[{"sampleId":"<sampleId>","expectedPayload":{"label":{"labelIds":["vang"]}},"purpose":"qualityCheck"}]}'

curl -s -X POST http://localhost:8080/projects/<id-du-an-moi>/publish -H "Authorization: Bearer <token-biz2>"
sleep 3

# status: draft, statusReason: "So du kha dung 0d, can ky quy 100000d. Hay nap them tien."
curl -s http://localhost:8080/projects/<id-du-an-moi> -H "Authorization: Bearer <token-biz2>"
```

Cùng file ZIP này cũng dùng được để test **upload dataset** cho bất kỳ dự án **ảnh** nháp nào. Dữ liệu khác ảnh (và ảnh lớn) đi đường upload thẳng + manifest, xem mục 6.14.

### 6.12 Quản lý dự án đang chạy

```bash
# Thêm lab3 vào P2 (FP-05) — theo userId HOẶC email; nhiều người một lần, mã mời: mục 6.21
curl -s -X POST http://localhost:8080/projects/2ae9dc42-5b32-518d-b24f-cdd7da800a96/members -H "Authorization: Bearer <token-biz1>" \
  -H "Content-Type: application/json" -d '{"userId":"14299c5f-f7c3-5075-b65a-cd4fbc2aa194","role":"labeler"}'

# Chặn lab2 khỏi P1 (FB-23) → lab2 bị thu hồi lease, nhận task bị 403 bi_chan_khoi_du_an
curl -s -X POST http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/members/0398f264-d694-5176-9614-844201175b74/block -H "Authorization: Bearer <token-biz1>"
curl -s -X POST http://localhost:8080/tasks/projects/3d904a1a-3920-530b-b055-793baae64f1b/next -H "Authorization: Bearer <token-lab2>"
curl -s -X POST http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/members/0398f264-d694-5176-9614-844201175b74/unblock -H "Authorization: Bearer <token-biz1>"

# Tạm dừng P1 → task-svc ngừng cấp task (403 du_an_khong_chay) → chạy tiếp
curl -s -X POST http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/pause -H "Authorization: Bearer <token-biz1>"
curl -s -X POST http://localhost:8080/tasks/projects/3d904a1a-3920-530b-b055-793baae64f1b/next -H "Authorization: Bearer <token-lab1>"
curl -s -X POST http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/resume -H "Authorization: Bearer <token-biz1>"

# Câu hỏi vàng: chỉ sửa được khi Nháp hoặc Tạm dừng. Đáp án ở dạng đầy đủ:
# "expectedPayload": {"taskType":"image","schemaVersion":1,"data":{"label":{"labelIds":["vang"]}}}
# Cách thêm câu vàng: xem mục 6.11 (dự án nháp mới).
curl -s http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/gold-items -H "Authorization: Bearer <token-biz1>"

# Kết quả và xuất file (FB-22, FB-25). Kết quả gộp THEO TỪNG CÔNG CỤ trong "tools":
#   classification / pairwise: đa số tuyệt đối (method "majority", final, votes, disputed);
#   bbox / polygon / keypoint / polyline / mask / temporalSegment / track: ghép cụm (method "cluster"); span / relation: "majority";
#   transcription: bản ở giữa ("medoid"); number: trung vị ("median"); ranking: Borda ("borda"); freeText: không gộp ("none"), xem "labels".
#   final = null và disputed = true khi không có kết quả được quá nửa số người ủng hộ (mục 6.20).
# format=coco chỉ dùng cho dự án ảnh có công cụ bbox / polygon (xem mục 6.14).
curl -s http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b/results -H "Authorization: Bearer <token-biz1>"
curl -s "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b/export?format=csv" -H "Authorization: Bearer <token-biz1>" -o ketqua.csv
curl -s "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b/export?format=json" -H "Authorization: Bearer <token-biz1>"

# Hoàn thành P4 → ledger trả phần ký quỹ còn dư về biz1 (khả dụng +300.000, vì P4 chưa chi đồng nào).
# Dự án đã chạy phải TẠM DỪNG trước (đang chạy → 409 can_tam_dung_truoc).
curl -s -X POST http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/pause -H "Authorization: Bearer <token-biz1>"
sleep 2
curl -s -X POST http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/complete -H "Authorization: Bearer <token-biz1>"
sleep 3
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-biz1>"
```

**Đóng dự án khi còn việc dở** (xem [kiến trúc 3.4.1](kien-truc-backend.md#341-đóng-dự-án-không-để-labeler-làm-không-công)): hoàn thành / hủy trả **409 `chua_the_dong`** kèm `closeCheck` cho tới khi:
- không còn lượt labeler đang giữ (`con_luot_dang_lam` — chờ họ nộp, hoặc lease hết hạn);
- không còn nhãn chờ duyệt (`con_nhan_cho_duyet`) và khiếu nại đang mở (`con_khieu_nai`);
- nhãn bị từ chối đã qua hạn khiếu nại `annotation.appeal_window`, mặc định 7 ngày (`con_han_khieu_nai`, có `appealWindowEndsAt`). Muốn thử nhanh thì đặt setting này = 0.

Sau khi đóng, duyệt / từ chối / khiếu nại nhãn của dự án đó → 409 `du_an_da_ket_thuc`. Endpoint nội bộ `/internal/...` của task-svc (8103) và annotation-svc (8104) cần header `X-Internal-Key` (dev: `dev_internal_key`), gateway không định tuyến.

### 6.13 Phân quyền chiều ngang (BOLA)

Truy cập tài nguyên của người khác trả **404** chứ không phải 403, để kẻ dò không biết được tài nguyên đó có tồn tại hay không.

```bash
# 404: biz2 không sửa được dự án P3 của biz1
curl -s -o /dev/null -w "%{http_code}\n" -X PUT http://localhost:8080/projects/32cdcbe6-837a-5c7c-929d-607db130deba/guideline \
  -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" -d '{"markdown":"hack","examples":[]}'

# 404: biz1 không xem được lệnh nạp của biz2
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8080/payments/deposits/8d241233-a997-5c1a-9f2f-2924b419164c -H "Authorization: Bearer <token-biz1>"

# 404: labeler không xem được hàng duyệt nhãn
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b?status=pendingReview" -H "Authorization: Bearer <token-lab1>"

# 403: sai VAI TRÒ (RBAC) thì là 403
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8080/projects/2ae9dc42-5b32-518d-b24f-cdd7da800a96/approve -H "Authorization: Bearer <token-lab1>"
```

### 6.14 Nhiều loại dữ liệu: văn bản, âm thanh, video, cặp, khung ảnh

Mỗi dự án chọn **một loại dữ liệu** (`modality`) lúc tạo: `image`, `text`, `audio`, `video`, `pair`, `list`. Tập nhãn là danh sách **công cụ**, mỗi công cụ có `name` (tự đặt, là khóa trong payload) và `kind`:

| kind | Dùng cho | Dữ liệu nộp của công cụ | Server kiểm thêm | Cách gộp kết quả |
|---|---|---|---|---|
| `classification` | mọi loại | `{"labelIds":["a"]}` | lớp có trong tập nhãn; `allowMultiple: false` thì đúng một lớp | đa số tuyệt đối |
| `bbox` | image | `[{"labelId":"xe","x":10,"y":20,"w":50,"h":40}]` | khung nằm trong ảnh (`metadata.width/height`) | ghép cụm theo IoU, trung vị toạ độ |
| `polygon` | image | `[{"labelId":"duong","points":[[0,0],[10,0],[10,10]]}]` | 3–500 đỉnh, nằm trong ảnh | ghép cụm theo IoU, lấy hình ở giữa |
| `span` | text | `[{"labelId":"TEN_NGUOI","start":4,"end":8}]` | `0 ≤ start < end ≤` độ dài văn bản | đoạn được quá nửa số người ghi y hệt |
| `transcription` | audio, video, image | `{"text":"..."}` | dài ≤ `maxLength` (mặc định 5000) | bản chép ở giữa |
| `temporalSegment` | audio, video | `[{"labelId":"nhac","start":0,"end":2.5}]` | nằm trong thời lượng **của đoạn**, tính từ đầu đoạn | ghép cụm theo IoU thời gian, trung vị |
| `pairwise` | pair | `{"choice":"a"}` (`a` / `b` / `tie`) | `tie` bị từ chối khi khai `allowTie: false` (mặc định cho hòa) | đa số tuyệt đối |

Tám công cụ thêm sau (`freeText`, `number`, `keypoint`, `polyline`, `mask`, `track`, `relation`, `ranking`), thuộc tính của từng đối tượng và dữ liệu `list`: mục 6.20.

Công cụ có `"required": false` thì được bỏ trống. Câu vàng chấm theo từng công cụ: phân loại so tập lớp, bbox theo IoU ≥ 0,5, polygon IoU, span F1, transcription CER ≤ 0,1, temporalSegment IoU thời gian; ngưỡng đổi được bằng `matchThreshold` của công cụ.

**Test trên dữ liệu seed P5–P7** (lab1 là thành viên):

```bash
# P5 văn bản: task có "content":{"text":...}, "metadata":{"length":...}, không có fileUrl
curl -s -X POST http://localhost:8080/tasks/projects/8dc63fe1-e23e-5097-b891-51fe72b61245/next -H "Authorization: Bearer <token-lab1>"
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/submit -H "Authorization: Bearer <token-lab1>" -H "Content-Type: application/json" \
  -d '{"payload":{"cam_xuc":{"labelIds":["tich_cuc"]},"thuc_the":[{"labelId":"TO_CHUC","start":0,"end":8}]}}'
# span vượt độ dài văn bản → 400 nhan_khong_hop_le

# P6 âm thanh: fileUrl nghe được, metadata có durationSec + segmentStart/segmentEnd (đoạn cắt từ file dài)
curl -s -X POST http://localhost:8080/tasks/projects/8309618e-25b2-5b99-a301-0632197c96e1/next -H "Authorization: Bearer <token-lab1>"
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/submit -H "Authorization: Bearer <token-lab1>" -H "Content-Type: application/json" \
  -d '{"payload":{"loi_noi":{"text":"xin chao"},"doan":[{"labelId":"giong_noi","start":0,"end":2}]}}'
# "end" lớn hơn durationSec của đoạn → 400

# P7 cặp câu trả lời: content có prompt, a, b
curl -s -X POST http://localhost:8080/tasks/projects/12d52faa-09b4-5cd1-b21d-16984c952fec/next -H "Authorization: Bearer <token-lab1>"
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/submit -H "Authorization: Bearer <token-lab1>" -H "Content-Type: application/json" \
  -d '{"payload":{"tot_hon":{"choice":"a"},"an_toan":{"labelIds":["an_toan"]}}}'

# Kết quả P7: cặp 1 có tools.tot_hon.final = {"choice":"a"}, votes {"a":2}
curl -s http://localhost:8080/annotations/projects/12d52faa-09b4-5cd1-b21d-16984c952fec/results -H "Authorization: Bearer <token-biz1>"
```

**Nạp dữ liệu không phải ZIP: upload thẳng lên MinIO + manifest.** File (audio, video, ảnh lớn) không đi qua service:

1. `POST /projects/{id}/uploads` xin link: mỗi file nhận một `key` và `uploadUrl` (ký sẵn, sống 1 giờ). Đuôi file được lọc sơ bộ theo loại dữ liệu; tối đa 100 file, mỗi file ≤ 5 GB.
2. `PUT` file lên `uploadUrl`.
3. `POST /projects/{id}/datasets/manifest` với `rows` (≤ 1.000 dòng, gửi kèm) hoặc `manifestKey` (key của file `.jsonl` / `.json` đã upload, ≤ 50.000 dòng). Trả **202**, lô ở trạng thái `pending`.
4. Worker nền xử lý lô: đọc file, kiểm **loại thật theo nội dung** (đổi tên `.exe` thành `.png` vẫn bị loại), đo kích thước ảnh, đo thời lượng / kích thước audio-video bằng **ffprobe**, cắt đoạn theo `segmentSeconds`, bỏ mẫu trùng. Xem kết quả ở `GET /projects/{id}/datasets`: `status` `ready` / `failed`, `sampleCount`, `skippedCount`, `errorSummary` (lý do từng dòng bị bỏ).

Mỗi dòng manifest theo loại dữ liệu:

| modality | Dòng manifest |
|---|---|
| image, audio, video | `{"file":"<key>","name":"tuy-chon"}`. Không có ffprobe thì audio/video phải khai thêm `"durationSec"` |
| text | `{"text":"...","name":"tuy-chon"}` |
| pair | `{"prompt":"tuy chon","a":"...","b":"..."}` |

Ví dụ trọn vẹn một dự án âm thanh cắt đoạn 10 giây (cần một file WAV, ví dụ tạo bằng `ffmpeg -f lavfi -i "sine=frequency=500:duration=23" -ac 1 -ar 16000 cuoc-goi.wav`):

```bash
curl -s -X POST http://localhost:8080/projects -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"name":"Chep loi cuoc goi","description":"x","modality":"audio","visibility":"public"}'
curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/label-schema -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"modality":"audio","segmentSeconds":10,"tools":[{"name":"chep","kind":"transcription"},{"name":"doan","kind":"temporalSegment","classes":["noi","im"],"required":false}]}'

# 1. Xin link upload → copy "key" và "uploadUrl"
curl -s -X POST http://localhost:8080/projects/<id-du-an-moi>/uploads -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"files":[{"name":"cuoc-goi.wav","sizeBytes":736078}]}'
# 2. Upload thẳng lên MinIO (dán nguyên uploadUrl, giữ dấu nháy)
curl -s -X PUT --upload-file cuoc-goi.wav "<uploadUrl>"
# 3. Tạo lô manifest → 202, status pending
curl -s -X POST http://localhost:8080/projects/<id-du-an-moi>/datasets/manifest -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"name":"lo-1","rows":[{"file":"<key>","name":"cuoc-goi.wav"}]}'
# 4. Vài giây sau: status ready, sampleCount 3 (file 23 giây → đoạn 0–10, 10–20, 20–23)
curl -s http://localhost:8080/projects/<id-du-an-moi>/datasets -H "Authorization: Bearer <token-biz1>"
curl -s "http://localhost:8080/projects/<id-du-an-moi>/samples?page=1&pageSize=20" -H "Authorization: Bearer <token-biz1>"
```

Sau đó cấu hình hướng dẫn, giá, publish, admin duyệt, labeler tham gia và nhận task như mục 6.4–6.6.

Dự án **ảnh có khung** dùng tập nhãn như sau; task trả `metadata.width/height` để vẽ khung, và kết quả xuất được COCO:

```bash
curl -s -X PUT http://localhost:8080/projects/<id-du-an-anh>/label-schema -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"modality":"image","tools":[{"name":"loai","kind":"classification","classes":["ngoai_troi","trong_nha"]},{"name":"vat","kind":"bbox","classes":["xe","nguoi"],"required":false},{"name":"vung","kind":"polygon","classes":["duong"],"required":false}]}'

# Nộp: khung tràn ra ngoài ảnh → 400; polygon dưới 3 đỉnh → 400
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/submit -H "Authorization: Bearer <token-lab1>" -H "Content-Type: application/json" \
  -d '{"payload":{"loai":{"labelIds":["ngoai_troi"]},"vat":[{"labelId":"xe","x":20,"y":30,"w":100,"h":50}],"vung":[{"labelId":"duong","points":[[0,200],[300,200],[300,150],[0,150]]}]}}'

# Sau khi duyệt: COCO (images có width/height, mỗi đối tượng ĐÃ GỘP từ các nhãn đã duyệt là một annotation)
curl -s "http://localhost:8080/annotations/projects/<id-du-an-anh>/export?format=coco" -H "Authorization: Bearer <token-biz1>" -o ketqua.coco.json
```

Kiểm tra thêm:
- Upload ZIP cho dự án không phải ảnh: **409** `zip_chi_cho_anh`.
- Xin link file `.wav` cho dự án video, hoặc `.exe` cho dự án ảnh: **400** `duoi_file_khong_hop_le`.
- Dòng manifest trỏ tới file của dự án khác: dòng bị bỏ, `errorSummary` ghi `khong thuoc du an nay`.
- File WAV đổi tên thành `.mp4` nạp vào dự án video: ffprobe thấy không có hình, dòng bị bỏ (`khong phai video`).
- Đang có lô `pending` / `ingesting` thì chưa publish được.

### 6.15 Kiểm soát chất lượng (P3): đồng thuận, redundancy thích ứng, câu vàng kiểm tra, uy tín

`quality-svc` (Python) chạy nền, không chặn luồng gán nhãn:

```
task-svc ──gold.answered────────────► quality-svc ──reputation.changed──► task-svc (lọc minReputation)
annotation-svc ──annotation.submitted─►     │
task-svc ──task.redundancy_reached──►      ├─ khớp      → consensus.reached ──► annotation-svc (cờ khớp/lệch, chỉ gợi ý)
                                            └─ tranh chấp → redundancy.increase_requested ──► task-svc (+1 người, ≤ trần)
```

- **Câu vàng kiểm tra:** câu vàng mục đích `qualityCheck` được trộn vào luồng task theo tỉ lệ `goldCheckPercent` (mặc định 10%). Response giống hệt task thật. Trả lời không tạo nhãn, không trả tiền; task-svc chấm ngay và báo `gold.answered`.
- **Đồng thuận:** khi task đủ người, quality gộp **từng công cụ** của tập nhãn: `classification` / `pairwise` theo đa số tuyệt đối; khung, đa giác, điểm mốc, đường, mặt nạ, đoạn thời gian, chuỗi khung theo ghép cụm; đoạn văn bản, quan hệ theo phần tử trùng hệt; bản chép, số, xếp hạng theo bản ở giữa / trung vị / Borda (mục 6.20). Công cụ `freeText` không gộp; tập nhãn chỉ có `freeText` thì trạng thái là `notApplicable`.
- **Redundancy thích ứng:** tranh chấp mà chưa chạm trần `maxRedundancy` thì quality xin thêm **một** người. task-svc mở lại task, quality tính lại khi đủ người. Ký quỹ tối thiểu tính theo **trần**, nên lượt thêm luôn có tiền; phần không dùng được trả lại khi kết thúc dự án.
- **Duyệt vẫn là việc của người:** đồng thuận chỉ đánh dấu nhãn khớp / lệch (`consensusAgrees`). Doanh nghiệp có nút duyệt hàng loạt các nhãn khớp.
- **Uy tín 0–100** = 60% độ chính xác câu vàng + 40% mức khớp đồng thuận (có Dawid–Skene thì dùng độ tin cậy DS thay cho mức khớp thô), làm mượt Bayes cho người mới. Công thức: [services/quality/app/reputation.py](../services/quality/app/reputation.py).

Dự án seed **P7** (cặp câu trả lời, `12d52faa-09b4-5cd1-b21d-16984c952fec`) có sẵn trần 3. Nhãn seed sẵn không đi qua quality-svc (seed không phát event), nên hãy thử trên **cặp 3** — cặp chưa ai làm trên DB vừa seed: lab1 chọn `a`, lab2 chọn `b` → tranh chấp → task cặp 3 lên redundancy 3 → lab3 tham gia P7 (`POST /projects/12d52faa-09b4-5cd1-b21d-16984c952fec/join`) rồi nhận đúng task đó và phá thế hòa. lab1 nhận phải cặp 2 thì gọi `release` rồi nhận lại (task được chọn ngẫu nhiên). Tự tạo dự án thì làm như sau:

```bash
# Giá: redundancy 2, trần 3 → readiness.estimatedCostVnd tính theo 3 người
curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/pricing -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"unitPriceVnd":1000,"redundancy":2,"maxRedundancy":3,"budgetVnd":50000,"deadline":"2027-12-31T00:00:00Z"}'

# Tỉ lệ câu vàng kiểm tra trộn vào task (0–50%). Thêm câu vàng purpose "qualityCheck" như mục 6.11.
curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/quality-control -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"goldCheckPercent":20}'
```

Sau khi publish và labeler nộp đủ:

```bash
# Nhãn có cờ consensusAgrees (true / false / null = chưa có kết quả hoặc không gộp được)
curl -s "http://localhost:8080/annotations/projects/<id-du-an-moi>?status=pendingReview" -H "Authorization: Bearer <token-biz1>"
curl -s "http://localhost:8080/annotations/projects/<id-du-an-moi>?consensusAgrees=false" -H "Authorization: Bearer <token-biz1>"

# Duyệt hàng loạt mọi nhãn chờ duyệt khớp đồng thuận (tối đa 500 mỗi lần) → { approvedCount, skippedOwnCount }
curl -s -X POST http://localhost:8080/annotations/projects/<id-du-an-moi>/approve-agreed -H "Authorization: Bearer <token-biz1>"

# Kết quả có thêm consensusStatus / consensusFinal theo từng mẫu
curl -s http://localhost:8080/annotations/projects/<id-du-an-moi>/results -H "Authorization: Bearer <token-biz1>"

# Chỉ số chất lượng (chủ dự án / admin)
curl -s http://localhost:8080/quality/projects/<id-du-an-moi>/summary  -H "Authorization: Bearer <token-biz1>"
curl -s http://localhost:8080/quality/projects/<id-du-an-moi>/labelers -H "Authorization: Bearer <token-biz1>"

# Chạy Dawid–Skene ngay (bình thường chạy định kỳ 15 phút) — admin
curl -s -X POST http://localhost:8080/quality/admin/dawid-skene/run -H "Authorization: Bearer <token-admin>"

# Labeler xem điểm của mình (không có số câu vàng — tránh đoán câu nào là câu vàng)
curl -s http://localhost:8080/quality/me -H "Authorization: Bearer <token-lab1>"
```

Xem trong database:

```bash
docker exec datn-db-task psql -U task_user -d task_db -c "select sample_id, redundancy_target, state from tasks where project_id='<id-du-an-moi>'"
docker exec datn-db-quality psql -U quality_user -d quality_db -c "select task_id, target, status, final from consensus_rounds order by decided_at desc limit 10"
docker exec datn-db-task psql -U task_user -d task_db -c "select user_id, reputation, reputation_at from labeler_cache"
```

Kiểm tra thêm:
- `maxRedundancy` nhỏ hơn `redundancy` hoặc lớn hơn 10: **400** `tran_redundancy_khong_hop_le`.
- `goldCheckPercent` ngoài 0–50: **400** `ti_le_cau_vang_khong_hop_le`.
- Labeler gọi `/quality/projects/{id}/summary`: **404**.
- Câu vàng chỉ được trộn khi labeler **còn task thật** để làm, và mỗi câu vàng mỗi người chỉ gặp một lần.

**Chính sách dừng mua nhãn** (mặc định `majority`). Đổi sang `posterior` hoặc `voi` để gộp có trọng số theo độ chính xác từng labeler ([NC-D-01](thi-nghiem/nc-d-01-redundancy-thich-ung.md)):

```bash
curl -s -X PUT http://localhost:8080/admin/settings/quality.redundancy_policy -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":"posterior","reason":"thu chinh sach hau nghiem"}'
# giá trị khác majority/posterior/voi → 400 gia_tri_khong_hop_le
```

Áp cho task **đủ người sau khi đổi**. Với `posterior`, hai labeler mới (độ chính xác mặc định 0,7) chọn trùng nhau **chưa đủ** τ = 0,95, nên task xin thêm người tới trần. Chạm trần mà vẫn chưa đủ thì `consensus_rounds.status = disputed`, `final` rỗng.

---

### 6.16 Setting hệ thống (admin): phí, tự duyệt, hạn mức

Mọi tham số nghiệp vụ và vận hành nằm trong bảng `settings` của admin-svc (155 khóa, xem [catalog.json](../shared/settings/catalog.json)). Đổi lúc đang chạy, **áp dụng cho thao tác mới** (dự án đã publish giữ phí cũ, lease đang chạy giữ hạn cũ…).

```bash
# Toàn bộ setting: key, group, type, value, defaultValue, min, max, unit, effect, description, version
curl -s http://localhost:8080/admin/settings -H "Authorization: Bearer <token-admin>"
curl -s http://localhost:8080/admin/settings/fee.platform_percent -H "Authorization: Bearer <token-admin>"

# Đổi phí nền tảng 30% → 20% (dự án publish SAU lúc này chịu 20%)
curl -s -X PUT http://localhost:8080/admin/settings/fee.platform_percent -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":20,"reason":"Khuyen mai thang 10"}'
curl -s http://localhost:8080/admin/settings/fee.platform_percent/history -H "Authorization: Bearer <token-admin>"

# Sai kiểu / ngoài giới hạn → 400; min > max (vd payment.deposit_min_vnd > deposit_max_vnd) → bị chặn
curl -s -X PUT http://localhost:8080/admin/settings/fee.platform_percent -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":95}'                                              # 400
```

Kiểm tra service đã nhận: `docker exec datn-db-project psql -U project_user -d project_db -c "select key, value, version from settings_replica where key='fee.platform_percent'"`.

**Tự duyệt dự án** — `project.auto_approve`:

```bash
curl -s -X PUT http://localhost:8080/admin/settings/project.auto_approve -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":true}'
# Publish một dự án nháp (mục 6.4): ký quỹ xong là chuyển thẳng running, không vào hàng đợi admin.
```

**Tự duyệt nhãn khớp đồng thuận** — `annotation.auto_approve_agreed`: bật lên thì khi quality-svc báo task `agreed`, các nhãn chờ duyệt khớp đồng thuận được hệ thống duyệt luôn (`reviewerId` rỗng, lịch sử `auto_approved`) và labeler được trả tiền.

**Hàng đợi duyệt rút tiền** — `ledger.withdraw_auto_approve` (bật), `ledger.withdraw_auto_approve_max_vnd` (2.000.000):

```bash
# Số dư seed nhỏ: hạ mức rút tối thiểu xuống 10.000 và ngưỡng tự duyệt xuống 20.000.
# lab2 có 28.000 khả dụng sau khi khoản treo P1 giải phóng (mục 4.5).
curl -s -X PUT http://localhost:8080/admin/settings/ledger.withdraw_min_vnd -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":10000}'
curl -s -X PUT http://localhost:8080/admin/settings/ledger.withdraw_auto_approve_max_vnd -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":20000}'
curl -s -X POST http://localhost:8080/ledger/withdrawals -H "Authorization: Bearer <token-lab2>" \
  -H "Content-Type: application/json" -H "Idempotency-Key: rut-cho-duyet" \
  -d '{"amountVnd":25000,"bankAccount":"VCB-0123456789"}'                                   # state: pendingApproval, tiền đã bị giữ

curl -s "http://localhost:8080/ledger/admin/withdrawals?state=pendingApproval" -H "Authorization: Bearer <token-admin>"
curl -s -X POST http://localhost:8080/ledger/admin/withdrawals/<id>/approve -H "Authorization: Bearer <token-admin>"   # → requested → completed
# hoặc từ chối: tiền về lại availableVnd của labeler
curl -s -X POST http://localhost:8080/ledger/admin/withdrawals/<id>/reject -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"reason":"Sai so tai khoan"}'
```

**Nạp tiền chuyển khoản thủ công** — `payment.manual_transfer_enabled` (bật), `payment.manual_transfer_auto_approve_max_vnd` (0 = luôn chờ admin), `payment.manual_transfer_bank_info` (thông tin tài khoản hiện cho doanh nghiệp):

```bash
curl -s -X POST http://localhost:8080/payments/deposits/manual -H "Authorization: Bearer <token-biz1>" \
  -H "Content-Type: application/json" -H "Idempotency-Key: ck-001" -d '{"amountVnd":500000}'
# → status pending, transferCode "CROWD…" (nội dung chuyển khoản), bankInfo, checkoutUrl null

curl -s -X POST http://localhost:8080/payments/deposits/<intentId>/transferred -H "Authorization: Bearer <token-biz1>"   # awaitingApproval

curl -s http://localhost:8080/payments/admin/deposits -H "Authorization: Bearer <token-admin>"                      # hàng đợi đối chiếu
curl -s -X POST http://localhost:8080/payments/admin/deposits/<intentId>/approve -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"bankTxnRef":"FT26100812345"}'          # succeeded → ledger cộng ví biz1
curl -s -X POST http://localhost:8080/payments/admin/deposits/<intentId>/reject -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"reason":"Khong thay tien ve"}'
```

Một mã sao kê (`bankTxnRef`) chỉ dùng cho một lệnh nạp — dùng lại → **409** `ma_giao_dich_trung`.

**Còn ở appsettings / biến môi trường** (không phải setting): chuỗi kết nối, RabbitMQ / MinIO, khóa ký JWT, cổng, `Media:FfprobePath`, `Consumers:DeliveryLimit` (topology queue), `Saga:BoQuaKyQuy` (cờ chỉ dùng ở dev), `Seed:Enabled`.

### 6.17 Cổng link (P2): người chia sẻ rút gọn link, khách vượt link, tiền về ví

Không có tài khoản seed nào là sharer, nên đăng ký một tài khoản mới (một tài khoản có thể vừa là labeler vừa là sharer):

```bash
curl -s -X POST http://localhost:8080/auth/register -H "Content-Type: application/json" \
  -d '{"email":"sharer1@crowd.local","password":"Matkhau@123","displayName":"Sharer 1","roles":["sharer"]}'
# đăng nhập lấy <token-sharer> như mục 5
```

**Doanh nghiệp bật cổng link cho dự án.** Chỉ dự án dữ liệu ảnh / văn bản / cặp, công cụ là phân loại chọn một hoặc so sánh cặp, có câu vàng mục đích `qualityCheck`. Ngân sách phải **lớn hơn** mức ký quỹ tối thiểu: cổng link chỉ tiêu phần vượt.

```bash
# Trên một dự án nháp (mục 6.11): bật kênh cổng link, thêm câu vàng qualityCheck, rồi publish và duyệt như mục 6.4–6.5
curl -s -X PUT http://localhost:8080/projects/<id>/channels -H "Authorization: Bearer <token-biz1>" \
  -H "Content-Type: application/json" -d '{"allowProfessional":true,"allowLinkGateway":true,"allowCollaborative":false}'
# Sau publish: ngân sách cổng = ngân sách − số mẫu × trần redundancy × (đơn giá + phí)
docker exec datn-db-gate psql -U gate_user -d gate_db -c "select * from gate_budgets"
```

**Người chia sẻ tạo link** (bằng token, API key, Quick Link hoặc hàng loạt):

```bash
curl -s -X POST http://localhost:8080/links -H "Authorization: Bearer <token-sharer>" \
  -H "Content-Type: application/json" -d '{"url":"https://example.com/bai-viet","alias":"bai-viet-1"}'
# → status pendingScan; ~2 giây sau worker quét xong → active. shortUrl = http://localhost:8080/g/bai-viet-1

curl -s -X POST http://localhost:8080/links/api-key -H "Authorization: Bearer <token-sharer>"      # apiKey "lk_..." (hiện MỘT lần)
curl -s "http://localhost:8080/links/quick?api=<apiKey>&url=https://example.com/x"                # trả về chuỗi link rút gọn
curl -s -X POST http://localhost:8080/links/bulk -H "X-Api-Key: <apiKey>" -H "Content-Type: application/json" \
  -d '{"urls":["https://example.org/a","khong-phai-url"]}'                                          # mỗi URL một dòng kết quả

# Link đích độc hại (dev giả lập: tên miền malware.example.test) → bị chặn sau khi quét
curl -s -X POST http://localhost:8080/links -H "Authorization: Bearer <token-sharer>" \
  -H "Content-Type: application/json" -d '{"url":"https://malware.example.test/x"}'                 # vài giây sau: status blocked
```

**Khách vượt link** (không cần đăng nhập). Dev dùng khoá test của Cloudflare Turnstile, token dummy `XXXX.DUMMY.TOKEN.XXXX` luôn đạt:

```bash
curl -s http://localhost:8080/g/bai-viet-1                    # requiresPassword, countdownSeconds, turnstileSiteKey
curl -s -X POST http://localhost:8080/g/bai-viet-1/sessions -H "Content-Type: application/json" \
  -d '{"turnstileToken":"XXXX.DUMMY.TOKEN.XXXX"}'
# → sessionId, answerableAt, labelSchema, questions [{sampleId, modality, content|fileUrl, metadata}]
#   (1 câu vàng + 2 câu thật TRỘN LẪN — response không cho biết câu nào là vàng)

# Đợi hết đếm ngược (setting gate.countdown, mặc định 8 giây), trả lời TẤT CẢ câu:
curl -s -X POST http://localhost:8080/g/sessions/<sessionId>/submit -H "Content-Type: application/json" \
  -d '{"answers":{"<sampleId-1>":{"loai":{"labelIds":["cho"]}},"<sampleId-2>":{"loai":{"labelIds":["meo"]}},"<sampleId-3>":{"loai":{"labelIds":["cho"]}}}}'
# đạt  → {"passed":true,"redirectUrl":"/go/bai-viet-1?t=...","redirectExpiresAt":...}
# trượt → {"passed":false,"newSession":{...}}  (bộ câu mới)

curl -s -i "http://localhost:8080/go/bai-viet-1?t=<token>"     # 302 Location: https://example.com/bai-viet
curl -s -i "http://localhost:8080/go/bai-viet-1?t=<token>"     # lần 2: 410 token_da_dung
```

**Tiền và thống kê:**

```bash
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-sharer>"   # pendingVnd += 2 × (đơn giá − phí/nhãn)
curl -s http://localhost:8080/gate/stats/me/daily -H "Authorization: Bearer <token-sharer>"
curl -s http://localhost:8080/gate/stats/me/outcomes -H "Authorization: Bearer <token-sharer>"   # tinhTien / trungIp / tuVuot / truotCauVang / hetNganSach
curl -s "http://localhost:8123/?user=gate_user&password=dev_gate_pw&database=gate_db" --data "SELECT kind, outcome, count() FROM click_events GROUP BY kind, outcome"
```

Kiểm tra thêm:
- Vượt lại **từ cùng máy** đã tạo link: vẫn mở được link nhưng **không** tính tiền (`tuVuot` — cùng IP với lúc tạo link). Vượt hai lần cùng IP trong 24 giờ: lần hai `trungIp`.
- Nhãn khách vượt link nằm ở annotation-svc với `source: "linkGateway"`, `taskId: null`. Duyệt nhãn này **không** chi tiền (sharer đã được trả theo lượt).
- Hết ngân sách cổng: bộ câu trả về rỗng (chỉ đếm ngược), ledger không chi vượt — ký quỹ dành cho labeler giữ nguyên.
- **Giới thiệu:** `GET /links/referrals/me` lấy mã; tài khoản **mới đăng ký** gọi `POST /links/referrals/claim {"code":"..."}`. Khi người được mời kiếm tiền qua cổng (và đã vượt `link.referral_min_earnings_vnd`), người giới thiệu nhận 10% vào `pendingVnd`.
- **Kiểm duyệt:** khách bấm báo cáo `POST /links/r/<code>/report {"reason":"..."}`. Đủ `link.report_review_threshold` IP khác nhau thì link vào `GET /links/admin/review-queue`. Admin `POST /links/admin/<id>/disable {"reason":"...","withholdRevenue":true}` → trang vượt link 404, doanh thu đang treo của link chuyển sang `platform:withheld`.
- Chặn tên miền: `POST /links/admin/blocked-domains {"domain":"casino.com","reason":"..."}` — chặn cả tên miền con, link đang chạy tới đó bị vô hiệu hoá ngay.
- **Chi theo lô:** đặt `ledger.gate_payout_mode = batched` (và `ledger.gate_batch_interval` nhỏ, vd 10 giây, cho dễ thấy). Lượt vượt link giờ chỉ nằm ở `gate_clicks` với `state = Queued`, ví chưa đổi. Sau một chu kỳ thì `state = Paid` và `pendingVnd` tăng, số tiền như chế độ theo lượt. Xem: `docker exec datn-db-ledger psql -U ledger_user -d ledger_db -c "select state, count(*), max(settled_at) from gate_clicks group by 1"`.

### 6.18 Xác minh doanh nghiệp (KYB), thông báo, khoá tài khoản

```bash
# Doanh nghiệp mới đăng ký → hồ sơ draft; publish bị chặn 409 chua_xac_minh_doanh_nghiep
curl -s -X POST http://localhost:8080/auth/register -H "Content-Type: application/json" \
  -d '{"email":"biz-moi@crowd.local","password":"Matkhau@123","displayName":"Biz moi","roles":["business"]}'
# đăng nhập lấy <token-biz-moi> như mục 5
curl -s http://localhost:8080/kyb/me -H "Authorization: Bearer <token-biz-moi>"

# Khai hồ sơ (mã số thuế 10 chữ số; sai → 400 ma_so_thue_khong_hop_le)
curl -s -X PUT http://localhost:8080/kyb/me -H "Authorization: Bearer <token-biz-moi>" -H "Content-Type: application/json" \
  -d '{"companyName":"Cong ty Moi","taxCode":"0312345678","address":"Ha Noi"}'

# Xin link tải giấy phép (.pdf/.png/.jpg, tối đa 10 MB) → uploadUrl; PUT file thẳng lên MinIO; rồi nộp
curl -s -X POST http://localhost:8080/kyb/me/license -H "Authorization: Bearer <token-biz-moi>" -H "Content-Type: application/json" \
  -d '{"fileName":"giay-phep.pdf","sizeBytes":1234}'
curl -s -X PUT --data-binary @giay-phep.pdf "<uploadUrl>"
curl -s -X POST http://localhost:8080/kyb/me/submit -H "Authorization: Bearer <token-biz-moi>"     # → submitted

# Admin: hàng đợi, chi tiết (licenseUrl xem được 5 phút), duyệt hoặc từ chối (bắt buộc lý do)
curl -s "http://localhost:8080/kyb/admin?status=submitted" -H "Authorization: Bearer <token-admin>"
curl -s http://localhost:8080/kyb/admin/<businessId> -H "Authorization: Bearer <token-admin>"
curl -s -X POST http://localhost:8080/kyb/admin/<businessId>/reject -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"reason":"Giay phep bi mo"}'
#   → doanh nghiệp sửa + nộp lại (2 lệnh trên), admin duyệt:
curl -s -X POST http://localhost:8080/kyb/admin/<businessId>/approve -H "Authorization: Bearer <token-admin>"
#   → doanh nghiệp LÀM MỚI TOKEN (POST /auth/refresh) để token mang claim kyb_verified = true → publish được
#     (token cũ vẫn 409 chua_xac_minh_doanh_nghiep cho tới khi hết hạn 15 phút)

# Thông báo (mọi vai trò): số chưa đọc, danh sách, đọc, tuỳ chọn email
curl -s http://localhost:8080/notifications/me/unread-count -H "Authorization: Bearer <token-biz-moi>"
curl -s http://localhost:8080/notifications/me -H "Authorization: Bearer <token-biz-moi>"
curl -s -X POST http://localhost:8080/notifications/me/read-all -H "Authorization: Bearer <token-biz-moi>"
curl -s http://localhost:8080/notifications/me/preferences -H "Authorization: Bearer <token-biz-moi>"
curl -s -X PUT http://localhost:8080/notifications/me/preferences -H "Authorization: Bearer <token-biz-moi>" \
  -H "Content-Type: application/json" -d '{"items":[{"type":"du_an","emailEnabled":false}]}'
#   loại bắt buộc (kyb_ket_qua, tai_khoan, rut_tien) không tắt được → 400 loai_bat_buoc
# Email: mở http://localhost:8025 (Mailpit) — "Hồ sơ doanh nghiệp bị từ chối", "Doanh nghiệp đã được xác minh"...

# Admin khoá / mở khoá tài khoản (không khoá được admin → 409 khong_khoa_admin)
curl -s "http://localhost:8080/users/admin?search=labeler3" -H "Authorization: Bearer <token-admin>"
curl -s -X POST http://localhost:8080/users/admin/14299c5f-f7c3-5075-b65a-cd4fbc2aa194/block -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"reason":"Thu khoa"}'
#   → lab3 đăng nhập 403, refresh token 401, nhận task 403 tai_khoan_bi_khoa (sau vài giây), có email "Tài khoản bị khoá"
curl -s -X POST http://localhost:8080/users/admin/14299c5f-f7c3-5075-b65a-cd4fbc2aa194/unblock -H "Authorization: Bearer <token-admin>"
```

Xem trong database:

```bash
docker exec datn-db-identity psql -U identity_user -d identity_db -c "select user_id, status, company_name, tax_code, reject_reason from business_profiles"
docker exec datn-db-notification psql -U notification_user -d notification_db -c "select user_id, type, title, merged_count, email_state, created_at from notifications order by created_at desc limit 10"
```

Thông báo loại phát sinh nhiều (`nhan_duyet`, `tien_kha_dung`) được **gộp**: sự kiện mới cộng dồn vào dòng cùng loại còn chưa đọc (`merged_count` tăng), nên sharer có nghìn click / ngày chỉ thấy một dòng "Tiền đã khả dụng".

### 6.19 Lưu trữ và dọn dẹp (kiến trúc mục 3.14)

Mọi hạn giữ là setting (nhóm hạ tầng / dự án), đổi lúc đang chạy:

| Setting | Mặc định | Dọn cái gì |
|---|---|---|
| `outbox.retention` | 7 ngày (tối thiểu 1 giờ) | Event **đã gửi** trong bảng `outbox` của mọi service |
| `consumers.processed_retention` | 14 ngày (tối thiểu 1 ngày) | Dấu vết chống xử lý trùng `processed_events` |
| `outbox.cleanup_interval` | 5 phút | Chu kỳ của các worker dọn (kể cả dọn dự án đã đóng) |
| `quality.fraud_submission_retention` | 7 ngày | Lượt nộp dùng xét gian lận |
| `admin.audit_retention_days` | 365 ngày | Nhật ký chi tiết `audit_events` |
| `project.closed_data_retention` | 90 ngày (0 = không dọn) | Task, lượt giữ, nhãn thô của dự án đã hoàn thành / huỷ |

```bash
# Xem worker dọn chạy: hạ hạn giữ outbox xuống 1 giờ, chu kỳ 10 giây
curl -s -X PUT http://localhost:8080/admin/settings/outbox.retention -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":3600,"reason":"thu don dep"}'
curl -s -X PUT http://localhost:8080/admin/settings/outbox.cleanup_interval -H "Authorization: Bearer <token-admin>" \
  -H "Content-Type: application/json" -d '{"value":10,"reason":"thu don dep"}'
# ~10 giây sau, log mỗi service có dòng "Don ha tang su kien: xoa N event da gui truoc ..."
docker exec datn-db-task psql -U task_user -d task_db -c \
  "select count(*) from outbox where published_at < now() - interval '65 minutes'"      # → 0
# Xong thì đặt lại 604800 và 300.

# Dọn NGAY một dự án đã hoàn thành / huỷ (không đợi 90 ngày) — admin
curl -s -X POST http://localhost:8080/tasks/admin/projects/<projectId>/purge -H "Authorization: Bearer <token-admin>"
#   → {"purgedNow":true,"deletedTasks":3,"deletedAssignments":6,...}; dự án chưa đóng → 409 du_an_chua_dong
curl -s -X POST http://localhost:8080/quality/admin/projects/<projectId>/archive -H "Authorization: Bearer <token-admin>"
#   → {"purgedNow":true,"deleted":{"labels":6,"consensus_rounds":3,"task_targets":3,"ds_state":1}}
# Sau đó tiến độ và báo cáo chất lượng vẫn xem được (bản chụp), nhãn đã duyệt và uy tín labeler không đổi:
curl -s http://localhost:8080/tasks/projects/<projectId>/progress -H "Authorization: Bearer <token-biz1>"       # frozenAt có giá trị
curl -s http://localhost:8080/quality/projects/<projectId>/summary -H "Authorization: Bearer <token-biz1>"     # archivedAt có giá trị

# Phiên đăng nhập: một dòng mỗi phiên dù làm mới bao nhiêu lần
docker exec datn-db-identity psql -U identity_user -d identity_db -c \
  "select user_id, generation, last_rotated_at, expires_at, revoked_at from login_sessions order by last_rotated_at desc limit 5"
```

**Nâng cấp từ bản trước đợt tối ưu này.** Migration tự chạy lúc khởi động service (identity bỏ `refresh_tokens` → mọi người đăng nhập lại một lần; annotation tách `annotation_samples`; admin dựng `stats_daily` từ nhật ký đang có). Còn hai việc cần dữ liệu của service khác nên nằm trong một script chạy tay **một lần**:

```bash
python deploy/don_du_lieu_cu.py                 # xem trước (không ghi gì)
python deploy/don_du_lieu_cu.py --thuc-hien     # dev;  stack demo: thêm --demo
```

Script xoá mẫu mà gate-svc giữ cho dự án đã publish nhưng không bật cổng link (hoặc cổng đã đóng), và điền mốc đóng (`closed_at`) ở quality-svc cho dự án đã đóng từ trước. Dung lượng file của Postgres chỉ giảm sau `VACUUM FULL` (autovacuum chỉ trả chỗ cho chính bảng đó dùng lại).

---

### 6.20 Công cụ gán nhãn mở rộng và gộp nhãn (kiến trúc mục 3.15)

Tám công cụ thêm sau, dùng y như các công cụ ở mục 6.14 (khai trong tập nhãn, nộp theo `name`). Định dạng đầy đủ, hàm mã hoá mặt nạ và bảng lỗi: [tích hợp frontend](tich-hop-frontend.md) mục 6.3 và ví dụ 11–18.

| kind | Dùng cho | Dữ liệu nộp | Server kiểm thêm | Gộp |
|---|---|---|---|---|
| `freeText` | mọi loại | `{"text":"..."}` | không rỗng khi bắt buộc; ≤ `maxLength` | không gộp, không chấm câu vàng |
| `number` | mọi loại | `{"value":4}` | `min`, `max`, `integer` của công cụ | trung vị; khớp khi lệch ≤ `tolerance` |
| `keypoint` | image | `[{"labelId":"mui","x":100,"y":85}]` | điểm nằm trong ảnh | ghép cụm (lệch ≤ 2% đường chéo ảnh), trung vị |
| `polyline` | image | `[{"labelId":"vach","points":[[0,140],[200,60]]}]` | 2–500 đỉnh trong ảnh, độ dài > 0 | ghép cụm (lệch trung bình ≤ 3% đường chéo), lấy đường ở giữa |
| `mask` | image | `[{"labelId":"troi","rle":{"size":[cao,rộng],"counts":[...]}}]` | `size` trùng ảnh; tổng `counts` = số pixel; dạng chuẩn | ghép cụm theo IoU, bỏ phiếu từng pixel |
| `track` | video | `[{"labelId":"xe","keyframes":[{"t":0,"x":10,"y":20,"w":60,"h":60},{"t":6,...}]}]` | ≥ 2 keyframe, `t` tăng dần trong thời lượng, khung trong khung hình | ghép cụm theo IoU không-thời gian, lấy chuỗi ở giữa |
| `relation` | text | `[{"labelId":"lam_tai","from":{"start":0,"end":3},"to":{"start":17,"end":20}}]` | hai đầu là hai đoạn đã có trong công cụ `spanTool` | quan hệ được quá nửa số người ghi y hệt |
| `ranking` | list | `{"order":[2,0,3,1]}` | hoán vị đủ các mục của mẫu | điểm Borda; khớp khi Kendall ≥ 0,8 |

**Thuộc tính của từng đối tượng:** công cụ dạng danh sách khai thêm `"attributes":[{"name":"noi_dung","type":"text","required":true},{"name":"mau","type":"choice","options":["do","xanh"]}]`; mỗi đối tượng nộp kèm `"attributes":{"noi_dung":"CAM DO XE","mau":"do"}`. OCR = `bbox` + thuộc tính `text`.

**Dữ liệu danh sách** (`modality: "list"`, cho `ranking`): dòng manifest `{"prompt":"...","items":["A","B","C","D"]}` (2–50 mục); task trả `content.items` và `metadata.itemCount`.

```bash
# Dự án xếp hạng: tạo dự án list → tập nhãn → nạp một mẫu 4 mục (rồi đặt giá, publish, duyệt như mục 6.11, 6.4, 6.5)
curl -s -X POST http://localhost:8080/projects -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"name":"Xep hang cau tra loi","description":"thu","modality":"list","visibility":"public"}'          # → copy "id"
curl -s -X PUT http://localhost:8080/projects/<projectId>/label-schema -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"modality":"list","tools":[{"name":"xep","kind":"ranking"},{"name":"diem","kind":"number","integer":true,"min":1,"max":5,"tolerance":1}]}'
curl -s -X POST http://localhost:8080/projects/<projectId>/datasets/manifest -H "Authorization: Bearer <token-biz1>" -H "Content-Type: application/json" \
  -d '{"name":"lo","rows":[{"prompt":"Cau tra loi nao tot nhat?","items":["Tra loi A","Tra loi B","Tra loi C","Tra loi D"]}]}'

# Labeler nộp (sau khi nhận task như mục 6.6): thứ tự tốt nhất → kém nhất theo chỉ số mục
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/submit -H "Authorization: Bearer <token-lab1>" -H "Content-Type: application/json" \
  -d '{"payload":{"xep":{"order":[0,1,2,3]},"diem":{"value":4}}}'
#   thiếu mục → 400 nhan_khong_hop_le "phai xep du 4 muc (dang co 3)"; lặp mục → 400 nhan_sai_dinh_dang

# Kết quả gộp (sau khi duyệt): tools.xep = {"method":"borda","final":{"order":[...]},"scores":{...},"agrees":[...]}
curl -s http://localhost:8080/annotations/projects/<projectId>/results -H "Authorization: Bearer <token-biz1>"
```

**Gộp nhãn nhiều người.** Cùng một thuật toán chạy ở hai chỗ: quality-svc gộp nhãn **vừa nộp** (đồng thuận → `consensusAgrees`, xin thêm người khi tranh chấp), annotation-svc gộp nhãn **đã duyệt** (`results`, file xuất). Một người *khớp* kết quả gộp của công cụ dạng danh sách khi F1 giữa các đối tượng của họ và các đối tượng đã gộp ≥ `quality.consensus_min_f1` (0,8); không quá một nửa số người khớp thì công cụ đó tranh chấp (`final: null`).

| Setting | Mặc định | Ý nghĩa |
|---|---|---|
| `labeling.threshold.keypoint` / `.polyline` | 0,98 / 0,97 | Độ giống tối thiểu = 1 − (độ lệch / đường chéo ảnh) |
| `labeling.threshold.mask` / `.track` | 0,5 | IoU tối thiểu (mặt nạ; chuỗi khung theo không-thời gian) |
| `labeling.threshold.relation` / `.ranking` | 1,0 / 0,8 | F1 khi chấm quan hệ; tỉ lệ cặp mục xếp cùng chiều |
| `quality.consensus_min_f1` | 0,8 | F1 để một người tính là khớp kết quả gộp (công cụ dạng danh sách) |

Các ngưỡng `labeling.threshold.*` dùng cho **cả** chấm câu vàng lẫn ghép đối tượng khi gộp; `matchThreshold` khai trên công cụ luôn được ưu tiên.

```bash
# Xem đồng thuận của quality-svc cho một dự án: mỗi task một dòng mỗi vòng (moreRequested = đã xin thêm người)
docker exec datn-db-quality psql -U quality_user -d quality_db -c \
  "select task_id, target, status, label_count, final from consensus_rounds where project_id = '<projectId>' order by decided_at"

# Hai bản cài đặt thuật toán gộp (C# và Python) đối chiếu bằng bộ ví dụ chung contracts/labeling/testdata/gop.vectors.json
dotnet test shared/test/labeling-tests                                   # C#: 59 test
cd services/quality && .venv/Scripts/python -m pytest -q tests/test_gop_vectors.py     # Python: 62 test trên cùng bộ ví dụ
# Sửa thuật toán / thêm ví dụ: sinh lại phần "expected" bằng C#, xem git diff, rồi sửa app/gop.py cho test Python xanh
UPDATE_GOP_VECTORS=1 dotnet test shared/test/labeling-tests

python tests/e2e/e2e_cong_cu.py                                          # E2E qua API thật (cần ffmpeg)
```

**Nâng cấp từ bản trước.** Không có migration ở C#. quality-svc tự chạy `007_gop_nhan.sql` (thêm cột `labels.sample_metadata`). Dự án đang chạy có khung / đa giác / đoạn / chép lời trước đây không có đồng thuận (`notApplicable`), từ bản này được tính — task tranh chấp sẽ được xin thêm người tới `maxRedundancy`. File COCO / YOLO / VOC xuất **khung đã gộp** thay vì khung của từng người.

---

### 6.21 Vào dự án: mời nhiều người, mã mời, đơn xin tham gia (kiến trúc mục 3.16)

Ba chế độ `visibility` (chỉ đổi khi dự án còn Nháp): `public` (labeler tự `join`), `approval` (labeler gửi đơn, chủ dự án duyệt), `private` (chỉ vào khi được mời / có mã mời). Mọi đường vào đều kiểm `minLevel` / `minReputation` ngay lúc vào, cùng mã lỗi với task-svc (`chua_du_cap_do`, `chua_du_uy_tin`, `tai_khoan_bi_khoa`, `khong_phai_labeler`).

```bash
P=3d904a1a-3920-530b-b055-793baae64f1b   # P1 của biz1

# Mời nhiều người một lần theo email và / hoặc userId (tối đa project.member_batch_max = 100)
curl -s -X POST http://localhost:8080/projects/$P/members/batch -H "Authorization: Bearer <token-biz1>" \
  -H "Content-Type: application/json" -d '{"role":"labeler","emails":["labeler3@crowd.local","khong-co@crowd.local","labeler1@crowd.local"]}'
# → {"added":0,"invited":3,"results":[{"input":"labeler3@crowd.local","userId":null,"status":"invited","invitationId":"..."}, ...]}
#   Ba dòng giống hệt (VD-S-16): không biết email nào có tài khoản / đã là thành viên.
#   lab3 thấy lời mời ở GET /projects/invitations/mine và tự chấp nhận; khong-co@ nhận thư mời đăng ký (Mailpit);
#   labeler1 đã là thành viên → lời mời "câm", không nhận gì.
# Theo userId thì kết quả chi tiết (added / da_la_thanh_vien / chua_du_cap_do ...) và thêm thẳng.

# Mã mời: 10 lượt, hạn 48 giờ (không gửi thì 7 ngày, không giới hạn lượt)
curl -s -X POST http://localhost:8080/projects/$P/invite-codes -H "Authorization: Bearer <token-biz1>" \
  -H "Content-Type: application/json" -d '{"maxUses":10,"expiresInHours":48,"note":"nhom K66"}'
# → {"id":"...","code":"K7QX3MWPA9TRHC2N","usedCount":0,"status":"active",...}
curl -s http://localhost:8080/projects/$P/invite-codes -H "Authorization: Bearer <token-biz1>"                     # xem lại, đếm lượt
curl -s -X DELETE http://localhost:8080/projects/$P/invite-codes/<id> -H "Authorization: Bearer <token-biz1>"     # thu hồi

# Labeler nhập mã (chữ thường, gạch, khoảng trắng đều được) → thành viên ngay, kể cả dự án riêng tư
curl -s -X POST http://localhost:8080/projects/join-by-code -H "Authorization: Bearer <token-lab3>" \
  -H "Content-Type: application/json" -d '{"code":"k7qx-3mwp-a9tr-hc2n"}'
# 409 ma_moi_het_luot / ma_moi_het_han / ma_moi_da_thu_hoi / da_la_thanh_vien; 404 mã không tồn tại
```

Chế độ `approval`: tạo dự án với `"visibility":"approval"` (hoặc `PUT /projects/{id}` khi còn Nháp), publish như thường.

```bash
# Labeler: join thẳng → 409 can_xin_duyet; gửi đơn
curl -s -X POST http://localhost:8080/projects/<id>/join-requests -H "Authorization: Bearer <token-lab3>" \
  -H "Content-Type: application/json" -d '{"message":"Em đã gán nhãn ảnh 2 năm"}'
curl -s http://localhost:8080/projects/join-requests/mine -H "Authorization: Bearer <token-lab3>"                 # đơn của tôi
curl -s -X DELETE http://localhost:8080/projects/<id>/join-requests/mine -H "Authorization: Bearer <token-lab3>"  # rút đơn

# Chủ dự án (nhận thông báo "Đơn xin tham gia mới"): hàng đợi kèm tên, cấp độ, uy tín
curl -s "http://localhost:8080/projects/<id>/join-requests?status=pending" -H "Authorization: Bearer <token-biz1>"
curl -s -X POST http://localhost:8080/projects/<id>/join-requests/<requestId>/approve -H "Authorization: Bearer <token-biz1>"
curl -s -X POST http://localhost:8080/projects/<id>/join-requests/<requestId>/reject -H "Authorization: Bearer <token-biz1>" \
  -H "Content-Type: application/json" -d '{"reason":"Dự án đã đủ người"}'
# Bị từ chối: gửi lại sau project.join_request_cooldown (mặc định 24 giờ), trước đó 409 chua_duoc_xin_lai
```

Dự án `approval` có bài test đầu vào: không gửi đơn tay được (`can_lam_test`). Làm bài như mục 6.9; đạt thì response có `"joinedProject": false, "joinRequestId": "..."`, và đơn trong hàng đợi của chủ dự án mang `scorePercent`.

### 6.22 Đợt hoàn thiện 10/2026 (thử nhanh bằng tay)

Chi tiết API ở [tích hợp frontend mục 17](tich-hop-frontend.md#17-bổ-sung-đợt-hoàn-thiện-102026); mỗi luồng đều có một bộ E2E ở mục 0.2. Vài lệnh đáng thử trên stack demo (`TOKEN_ADMIN`, `TOKEN_BIZ` lấy như mục 5):

```bash
# Hàng đợi duyệt chung + tóm tắt
curl -s http://localhost:18080/admin/approvals/summary -H "Authorization: Bearer $TOKEN_ADMIN"
curl -s "http://localhost:18080/admin/approvals?status=pending" -H "Authorization: Bearer $TOKEN_ADMIN"

# Độ sâu DLQ, mẫu bài toán
curl -s http://localhost:18080/admin/ops/queues -H "Authorization: Bearer $TOKEN_ADMIN"
curl -s http://localhost:18080/templates -H "Authorization: Bearer $TOKEN_BIZ"

# Nạp qua VNPay giả lập: lấy checkoutUrl, mở trên trình duyệt, bấm "Thanh toán"
curl -s -X POST http://localhost:18080/payments/deposits -H "Authorization: Bearer $TOKEN_BIZ" \
  -H "Idempotency-Key: $(uuidgen)" -H "Content-Type: application/json" -d '{"amountVnd":200000,"provider":"vnpay"}'

# Chỉ số chất lượng mở rộng của P1 (alphaMetric, fleissKappa)
curl -s http://localhost:18080/quality/projects/3d904a1a-3920-530b-b055-793baae64f1b/summary -H "Authorization: Bearer $TOKEN_BIZ"
```

Kiểm phân vùng `tasks`: `docker exec datn-demo-db-task psql -U task_user -d task_db -c "\d+ tasks"` (16 phân vùng `tasks_p00`…`tasks_p15`). Kiểm audit chỉ-ghi-thêm: `UPDATE audit_events ...` trong `admin_db` báo `audit_events chi ghi them`.

## 7. Kiểm tra tổng sau khi test

```bash
curl -s http://localhost:8080/ledger/admin/reconciliation -H "Authorization: Bearer <token-admin>"
# healthy: true, unbalancedEntries / balanceMismatches / negativeAccounts / brokenChain đều rỗng
```

Chạy lúc nào cũng phải ra `healthy: true`. Nếu không, một luồng tiền nào đó đang sai.

Xem event chạy qua hệ thống: mở RabbitMQ UI tại http://localhost:15672, tab **Queues**. Mỗi queue có tên dạng `<service>.<event>`, ví dụ `ledger-svc.annotation-approved`. Hàng đợi dead-letter nằm ở exchange `datn.dlx`.

---

Muốn chạy toàn bộ kiểm tra tự động thay vì từng lệnh: xem mục 0.2.

## 7.1 Chạy lại thí nghiệm (NC-D-01, NC-B-06, NC-B-01)

**NC-B-01 — TLA+** (cần Java 11+; lần đầu tự tải `tla2tools.jar`):

```bash
python spec/tla/chay_tlc.py            # 6 biến thể: bản đúng không vi phạm, 5 biến thể lỗi phải bị bắt (~40 giây)
python spec/tla/chay_tlc.py hien_tai   # một biến thể; trace rút gọn ở spec/tla/ket-qua/<tên>.trace.txt
```


Mã ở `experiments/`, môi trường Python riêng:

```bash
cd experiments
python -m venv .venv && .venv/Scripts/pip install -r requirements.txt

# NC-D-01 — redundancy thích ứng (không cần hệ thống chạy; tải benchmark vào experiments/.cache/)
.venv/Scripts/python redundancy/chay.py        # ~25 phút → redundancy/ket-qua/ket-qua.json + docs/thi-nghiem/img/nc-d-01-*.png
.venv/Scripts/python redundancy/bao_cao.py     # in các bảng markdown cho báo cáo

# NC-B-06 — cần hệ thống dev đang chạy (mục 3)
cd perf
../.venv/Scripts/python ledger_cpm.py --mode perClick --mode batched                 # ledger cô lập: vhost bench, DB ledger_bench, cổng 8195
../.venv/Scripts/python ledger_cpm.py --mode perClick --mode batched --so-instance 3
../.venv/Scripts/python task_lease.py --muc 10,50,100,200,400 --giay 30 --tien-trinh 8 --ra task-lease.json
../.venv/Scripts/python ve.py                                                        # vẽ lại biểu đồ
```

- `task_lease.py` tạo **dự án mới** (20.000 mẫu) và 400 labeler `bench-*@crowd.local` mỗi lần chạy, trong DB dev. Ký quỹ nạp qua sandbox.
- Kiểm `cpu_client_max` trong kết quả: gần 1,0 nghĩa là bộ sinh tải đã bão hoà. Khi đó tăng `--tien-trinh`, vì con số đo được là trần của client chứ không phải của service ([NC-B-06 mục 2.3](thi-nghiem/nc-b-06-hieu-nang.md#23-sai-lầm-phương-pháp-đã-sửa)).
- Đang chạy service thì build sẽ lỗi khoá DLL. Dừng service trước khi build.

## 8. Sự cố thường gặp

| Hiện tượng | Nguyên nhân / cách xử lý |
|---|---|
| Service báo lỗi kết nối Postgres | Chưa bật đúng profile. Ledger và payment cần `--profile p1` |
| `401` dù vừa đăng nhập | Token hết hạn sau 15 phút. Đăng nhập lại (mục 5) |
| `401` ngay lập tức | Còn sót dấu `< >` khi dán token, hoặc dán nhầm `refreshToken` |
| `curl` trong PowerShell báo lỗi tham số | Dùng `curl.exe`, hoặc dùng Git Bash |
| Log `Seed ... da du du an seed — bo qua` nhưng dữ liệu khác bảng ở mục 4 | DB đã bị thay đổi bởi các lần test trước. Seed lại từ đầu theo mục 4.6 |
| Seed ném lỗi khi khởi động (ví dụ `khong_du_so_du`, `chuyen_trang_thai_khong_hop_le`) | Kịch bản bị sửa sai luật. Chạy `dotnet test shared/test/seeding-tests` để biết sai ở đâu |
| `fileUrl` trả 403 / hết hạn | Link MinIO chỉ sống 5 phút (S-07). Gọi lại API để lấy link mới |
| Lô manifest `failed`, `errorSummary` ghi `khong doc duoc thoi luong` | project-svc không tìm thấy ffprobe. Cài FFmpeg, hoặc đặt `Media:FfprobePath` trong appsettings, hoặc khai `durationSec` trong dòng manifest |
| Đổi setting mà service không thấy giá trị mới | Xem bản sao: `docker exec datn-db-<svc> psql -U <svc>_user -d <svc>_db -c "select * from settings_replica where key='...'"`. Chưa có version mới → service đó chưa nhận `setting.changed` (đang tắt, hoặc message nằm DLQ `<svc>.setting-changed.dlq`). Bật lại service là nó tự xin snapshot |
| `/admin/...` trả 502 | admin-svc chưa chạy, hoặc chưa bật `--profile p0` (Postgres admin) |
| `/links/...`, `/g/...`, `/go/...` trả 502 | link-svc / gate-svc chưa chạy, hoặc chưa bật `--profile p2` |
| Link đứng mãi ở `pendingScan` | link-svc không chạy (worker quét nằm trong nó), hoặc Safe Browsing lỗi mạng (khi có `UrlSafety:GoogleApiKey`) — xem log `Quet link loi` |
| `GET /g/<code>` trả 404 dù link `active` | gate chưa nhận `link.activated` (gate tắt lúc link được kích hoạt sẽ nhận khi bật lại), hoặc link hết hạn |
| Bộ câu hỏi luôn rỗng | Không có dự án nào phục vụ được: chưa bật kênh cổng link, công cụ không phải phân loại chọn một / so sánh cặp, thiếu câu vàng `qualityCheck`, hoặc ngân sách không vượt mức tối thiểu (`select * from gate_budgets` trong gate_db) |
| `turnstile_that_bai` | Thiếu `turnstileToken`, hoặc gate không gọi được `challenges.cloudflare.com` (cần mạng). Offline thì đặt `Gate:Turnstile:Enabled = false` trong appsettings.Development.json của gate |
| `/quality/...` trả 502 | quality-svc chưa chạy (mục 3) hoặc chưa bật `--profile p3` |
| Log quality `Bo qua dong thuan task ...: chua co ban sao du an` | Dự án publish **trước khi** quality-svc chạy lần đầu (không phải dự án seed): quality không có tập nhãn và trần redundancy nên bỏ qua. Dự án publish sau đó được tính bình thường |
| Lô manifest đứng mãi ở `pending` | project-svc không chạy (worker nạp dữ liệu chạy bên trong nó). Bật lên là lô được xử lý; lô `ingesting` dở dang được làm lại từ đầu |
| `docker compose up` báo `minio Error ... failed to resolve` / `no such manifest` / `pull access denied` | Đang dùng bản compose cũ trỏ tới `quay.io/minio/minio`: MinIO đã gỡ image chính thức khỏi cả Docker Hub lẫn quay.io, máy nào chưa có sẵn image trong cache đều không kéo được. `git pull` bản mới — compose dùng `pgsty/minio` (bản dựng từ mã nguồn MinIO do cộng đồng duy trì, ghim phiên bản, đọc được volume cũ) |
| Lỗi bucket `datasets` không tồn tại | `minio-init` chưa chạy. Chạy lại `docker compose ... up -d` |
| Nhãn đã duyệt nhưng labeler chưa thấy tiền | ledger-svc chưa chạy, hoặc event đang nằm trong outbox của annotation. Bật ledger lên thì event sẽ được giao (at-least-once) |
| Mời thành viên / `join` / mã mời / đơn xin tham gia trả 503 `dich_vu_khong_san_sang` | project-svc không gọi được identity-svc (đang tắt), hoặc `InternalApi:IdentityUrl` / `InternalApi:Key` của project và identity không khớp. Không tra được hồ sơ thì project-svc **không** cho vào dự án (kiến trúc 3.16) |
| Hoàn thành / hủy dự án trả 503 `dich_vu_khong_san_sang` | project-svc không gọi được task-svc hoặc annotation-svc (đang tắt), hoặc `InternalApi:Key` / địa chỉ trong appsettings của project, task, annotation không khớp nhau |
| P2 tự chuyển sang `cancelled` | Đúng thiết kế: dự án chờ duyệt quá `project.approval_timeout` (mặc định 72 giờ) thì tự hủy và hoàn ký quỹ (compensation của saga) |
