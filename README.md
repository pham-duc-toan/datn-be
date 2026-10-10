# Hệ thống gán nhãn dữ liệu cộng đồng — backend

Microservice (.NET 10 + một service Python) cho nền tảng gán nhãn: doanh nghiệp ký quỹ và đăng dự án, labeler nhận task và được trả tiền qua sổ cái ghi kép, kiểm soát chất lượng bằng đồng thuận / câu vàng / redundancy thích ứng, và kênh thu thập qua cổng vượt link.

## Chạy thử bằng một lệnh

Chỉ cần Docker:

```bash
docker compose -f docker-compose.demo.yml up -d --build
pip install requests && python tests/e2e/cho_san_sang.py     # chờ migrate + seed xong
```

Gateway ở `http://localhost:18080`. Tài khoản mẫu `admin@crowd.local`, `doanhnghiep1@crowd.local`, `labeler1@crowd.local`… với mật khẩu chung `Matkhau@123`. Chạy toàn bộ kiểm thử E2E trên stack này:

```bash
E2E_PROFILE=demo python tests/e2e/chay_hoi_quy.py
```

## Tài liệu

| Tài liệu | Nội dung |
|---|---|
| [docs/huong-dan-chay-va-test.md](docs/huong-dan-chay-va-test.md) | Chạy trên máy dev, chạy bằng Docker, test từng luồng, E2E, CI, sự cố thường gặp |
| [docs/kien-truc-backend.md](docs/kien-truc-backend.md) | Kiến trúc, các quyết định kỹ thuật và lý do |
| [docs/tich-hop-frontend.md](docs/tich-hop-frontend.md) | Hợp đồng API cho frontend |
| [docs/van-de-can-giai-quyet.md](docs/van-de-can-giai-quyet.md) | Sổ vấn đề và hướng nâng cao |
| [docs/thi-nghiem/](docs/thi-nghiem/) | Thí nghiệm có số liệu: redundancy thích ứng (NC-D-01), hiệu năng (NC-B-06) |

## Cấu trúc

```
services/      mỗi service một thư mục (identity, project, task, annotation, ledger, payment,
               link, gate, admin, gateway; quality viết bằng Python)
shared/        thư viện dùng chung (auth, messaging, outbox, settings, labeling, storage, seeding)
contracts/     JSON Schema tập nhãn + danh mục event
tests/e2e/     kiểm thử đầu-cuối qua API thật
experiments/   mã thí nghiệm
deploy/docker/ Dockerfile; docker-compose.demo.yml (cả hệ thống), docker-compose.infra.yml (hạ tầng cho dev)
```
