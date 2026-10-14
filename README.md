# Hệ thống gán nhãn dữ liệu cộng đồng — backend

Microservice (.NET 10 + hai service Python) cho nền tảng gán nhãn: doanh nghiệp ký quỹ và đăng dự án, labeler nhận task và được trả tiền qua sổ cái ghi kép, 15 công cụ gán nhãn trên 6 loại dữ liệu (ảnh, văn bản, âm thanh, video, cặp, danh sách — từ phân loại, khung, mặt nạ tới theo dõi đối tượng và xếp hạng), kiểm soát chất lượng bằng đồng thuận (gộp nhãn nhiều người cho mọi công cụ) / câu vàng / redundancy thích ứng, kênh thu thập qua cổng vượt link, xác minh doanh nghiệp (KYB), tài khoản đầy đủ (xác thực email, quên mật khẩu, hồ sơ, cấp độ / huy hiệu / xếp hạng), tranh chấp nhãn, chống gian lận, nhật ký hệ thống, thông báo trong ứng dụng + email, giới hạn tần suất và thu hồi phiên ở gateway, bảo vệ dữ liệu (chặn tải bản gốc, watermark chìm truy ra người làm lộ ảnh).

## Chạy thử bằng một lệnh

Chỉ cần Docker:

```bash
python deploy/tao_env.py                                       # lần đầu: sinh .env (mật khẩu ngẫu nhiên, không commit)
docker compose -f docker-compose.demo.yml up -d --build
pip install requests && python tests/e2e/cho_san_sang.py     # chờ migrate + seed xong
```

Gateway ở `http://localhost:18080`, email thông báo xem ở Mailpit `http://localhost:18025`. Tài khoản mẫu `admin@crowd.local`, `doanhnghiep1@crowd.local`, `labeler1@crowd.local`… với mật khẩu chung `Matkhau@123`. Chạy toàn bộ kiểm thử E2E trên stack này:

```bash
E2E_PROFILE=demo python tests/e2e/chay_hoi_quy.py
```

## Tài liệu

| Tài liệu | Nội dung |
|---|---|
| [docs/huong-dan-chay-va-test.md](docs/huong-dan-chay-va-test.md) | Chạy trên máy dev, chạy bằng Docker, test từng luồng, E2E, CI, sự cố thường gặp |
| [docs/kien-truc-backend.md](docs/kien-truc-backend.md) | Kiến trúc, các quyết định kỹ thuật và lý do (mục 3.14: dữ liệu nào lớn theo người dùng và dọn thế nào; mục 3.15: công cụ gán nhãn và thuật toán gộp nhãn) |
| [docs/tich-hop-frontend.md](docs/tich-hop-frontend.md) | Hợp đồng API cho frontend |
| [docs/van-de-can-giai-quyet.md](docs/van-de-can-giai-quyet.md) | Sổ vấn đề và hướng nâng cao |
| [docs/thi-nghiem/](docs/thi-nghiem/) | Thí nghiệm có số liệu: redundancy thích ứng (NC-D-01), hiệu năng (NC-B-06), đặc tả TLA+ dòng tiền (NC-B-01) |

## Cấu trúc

```
services/      mỗi service một thư mục (identity, project, task, annotation, ledger, payment,
               link, gate, notification, admin, gateway; quality và media viết bằng Python)
shared/        thư viện dùng chung (auth, messaging, outbox, settings, labeling, storage, seeding)
contracts/     JSON Schema tập nhãn + bộ ví dụ gộp nhãn dùng chung C# / Python + danh mục event
tests/e2e/     kiểm thử đầu-cuối qua API thật
spec/tla/      đặc tả TLA+ dòng tiền + đóng dự án, chạy bằng TLC (python spec/tla/chay_tlc.py)
experiments/   mã thí nghiệm
deploy/        Dockerfile (deploy/docker/), tao_env.py sinh .env cho stack demo;
               docker-compose.demo.yml (cả hệ thống), docker-compose.infra.yml (hạ tầng cho dev)
```
