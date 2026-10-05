# Hướng dẫn khởi động dự án và test API

**Tài liệu liên quan:** [Kiến trúc backend](kien-truc-backend.md) · [Danh mục event](../contracts/events/CATALOG.md) · [Sổ vấn đề](van-de-can-giai-quyet.md)

Tài liệu này dành cho người lần đầu chạy hệ thống trên máy dev. Sau khi làm theo, bạn sẽ có hạ tầng và 6 service nghiệp vụ đang chạy, kèm **dữ liệu mẫu (seed) đủ cho mọi luồng chính**. Phần cuối là bộ lệnh `curl` để test từng luồng qua gateway.

Mọi lệnh đều ghi đầy đủ địa chỉ và ID, copy là chạy được. Chỉ có hai loại giá trị phải tự dán vào:

| Chỗ trống | Lấy ở đâu |
|---|---|
| `<token-admin>`, `<token-biz1>`, `<token-biz2>`, `<token-lab1>`, `<token-lab2>`, `<token-lab3>`, `<token-rev1>` | Trường `accessToken` khi đăng nhập tài khoản tương ứng (mục 5) |
| `<assignmentId>`, `<attemptId>`, `<id-du-an-moi>`, `<intentId-moi>`, `<refreshToken>` | Trường cùng tên trong response của lệnh ngay trước đó |

Khi dán, xóa luôn cặp dấu `< >`. Ví dụ `Bearer <token-biz1>` sẽ thành `Bearer eyJhbGciOi...`.

---

## 0. Chạy nhanh (đã quen thì chỉ cần mục này)

```bash
# 1. Hạ tầng: RabbitMQ, MinIO, Postgres của các service P0 + P1
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 up -d

# 2. Build một lần
dotnet build datn.slnx

# 3. Mỗi service một terminal (thứ tự không quan trọng)
dotnet run --project services/identity/Crowd.Identity.Api
dotnet run --project services/project/Crowd.Project.Api
dotnet run --project services/task/Crowd.Tasking.Api
dotnet run --project services/annotation/Crowd.Annotation.Api
dotnet run --project services/ledger/Crowd.Ledger.Api
dotnet run --project services/payment/Crowd.Payment.Api
dotnet run --project services/gateway/Crowd.Gateway.Api

# 4. Đăng nhập thử bằng tài khoản seed (mật khẩu chung: Matkhau@123)
curl -s -X POST http://localhost:8080/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@crowd.local","password":"Matkhau@123"}'
```

---

## 1. Yêu cầu

| Công cụ | Phiên bản | Ghi chú |
|---|---|---|
| Docker Desktop | bản mới | Chạy Postgres, RabbitMQ, MinIO |
| .NET SDK | 10 (xem [global.json](../global.json)) | `dotnet --version` |
| Git Bash | có sẵn khi cài Git | Các lệnh trong tài liệu viết cho bash |
| curl | có sẵn trong Git Bash | Trong **PowerShell 5.1**, `curl` là alias của `Invoke-WebRequest` nên phải gõ `curl.exe` |
| Python 3 | tùy chọn | Chỉ dùng cho mục 6.11 (tạo file ZIP ảnh) |

---

## 2. Khởi động hạ tầng

```bash
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 up -d
docker ps --format "table {{.Names}}\t{{.Status}}"      # đợi mọi container (healthy)
```

`--profile p0` bật Postgres của identity, project, task, annotation. `--profile p1` bật thêm Postgres của ledger và payment. RabbitMQ và MinIO thuộc nhóm core nên luôn được bật. Container `minio-init` chạy một lần để tạo bucket `datasets` rồi tự thoát, đó là bình thường.

| Thành phần | Cổng | Đăng nhập (dev) |
|---|---|---|
| RabbitMQ | 5672, UI **15672** | `datn` / `dev_rabbit_pw` |
| MinIO | 9000, UI **9001** | `datn` / `dev_minio_pw` |
| Postgres identity / project / task / annotation | 5401 / 5402 / 5403 / 5404 | `<tên>_user` / `dev_<tên>_pw` |
| Postgres ledger / payment | 5405 / 5406 | như trên |

Ví dụ mở psql: `docker exec -it datn-db-ledger psql -U ledger_user -d ledger_db`

---

## 3. Chạy các service

| Service | Cổng | Lệnh |
|---|---|---|
| gateway (Ocelot) | **8080** | `dotnet run --project services/gateway/Crowd.Gateway.Api` |
| identity-svc | 8101 | `dotnet run --project services/identity/Crowd.Identity.Api` |
| project-svc | 8102 | `dotnet run --project services/project/Crowd.Project.Api` |
| task-svc | 8103 | `dotnet run --project services/task/Crowd.Tasking.Api` |
| annotation-svc | 8104 | `dotnet run --project services/annotation/Crowd.Annotation.Api` |
| ledger-svc | 8105 | `dotnet run --project services/ledger/Crowd.Ledger.Api` |
| payment-svc | 8106 | `dotnet run --project services/payment/Crowd.Payment.Api` |

- **Thứ tự không quan trọng.** Mỗi service tự migrate database của mình khi khởi động (VD-O-03), rồi tự seed (mục 4). Các service không gọi nhau lúc seed.
- **Mọi request đều đi qua gateway `http://localhost:8080`.** Gọi thẳng cổng 81xx chỉ dùng để debug.
- Khi khởi động lần đầu trên database trống, log mỗi service sẽ có một dòng `Seed ...`:

```text
Seed identity: tao 7 tai khoan, mat khau chung 'Matkhau@123'
Seed project: tao 4 du an, 23 anh mau
Seed task: 4 du an, 23 task, 8 luot da nop
Seed annotation: tao 8 nhan
Seed ledger: 3 ky quy, 4 lan chi tra, 3 khoan treo da giai phong
Seed payment: tao 2 lenh nap
```

Từ lần chạy thứ hai, log sẽ là `Seed ...: da co du lieu seed — bo qua`.

Nếu muốn bật cả 7 service trong **một** terminal Git Bash (log ghi ra thư mục `logs/`):

```bash
mkdir -p logs
dotnet run --project services/identity/Crowd.Identity.Api     > logs/identity.log 2>&1 &
dotnet run --project services/project/Crowd.Project.Api       > logs/project.log 2>&1 &
dotnet run --project services/task/Crowd.Tasking.Api          > logs/task.log 2>&1 &
dotnet run --project services/annotation/Crowd.Annotation.Api > logs/annotation.log 2>&1 &
dotnet run --project services/ledger/Crowd.Ledger.Api         > logs/ledger.log 2>&1 &
dotnet run --project services/payment/Crowd.Payment.Api       > logs/payment.log 2>&1 &
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
- **Seed không phát event nào ra RabbitMQ.** Code nghiệp vụ có xếp event vào outbox, nhưng seeder gỡ các event đó trước khi lưu (`SeedOutbox.BoEventChuaGui`), vì service nào cũng đã tự seed phần của mình.
- **Ledger seed bằng "đồng hồ lùi về quá khứ"** (`DongHoCoDinh`). Nhãn được duyệt 4 ngày trước thì khoản treo đã hết hạn, nên labeler rút được tiền ngay.
- **Chỉ chạy khi môi trường là `Development` và `Seed:Enabled = true`** ([`SeedSwitch`](../shared/seeding/SeedHelpers.cs)). Ở production, quên tắt cờ cũng không seed được.
- **Chạy lại an toàn:** nếu đã có bản ghi mốc (ví dụ dự án P1) thì service bỏ qua.
- [shared/test/seeding-tests](../shared/test/seeding-tests/KichBanSeedTests.cs) kiểm kịch bản đúng luật: ngân sách ≥ ký quỹ tối thiểu, không vượt redundancy, reviewer không tự duyệt, khiếu nại còn trong hạn…

### 4.2 Tài khoản (mật khẩu chung `Matkhau@123`)

| Key | Email | ID | Vai trò | Dùng để test |
|---|---|---|---|---|
| admin | `admin@crowd.local` | `e142c9aa-4f06-542c-9ac7-7c9bbaa6f65a` | admin | Duyệt dự án, phân xử khiếu nại, đối soát |
| biz1 | `doanhnghiep1@crowd.local` | `37665dec-c58e-5c8f-a244-476f27222311` | business | Chủ 4 dự án, đã nạp 2.000.000đ |
| biz2 | `doanhnghiep2@crowd.local` | `d3b5a5c5-0232-5935-9e4a-55812983f1a6` | business | **Chưa có tiền**, có một lệnh nạp 1.000.000đ chưa trả |
| lab1 | `labeler1@crowd.local` | `1f5e19a7-9f58-589b-8c74-45334ff35d05` | labeler | Có **60.000đ rút được ngay**, 2 nhãn chờ duyệt |
| lab2 | `labeler2@crowd.local` | `0398f264-d694-5176-9614-844201175b74` | labeler | 1 nhãn được duyệt, 1 bị từ chối, 1 đang khiếu nại |
| lab3 | `labeler3@crowd.local` | `14299c5f-f7c3-5075-b65a-cd4fbc2aa194` | labeler | Chưa vào dự án nào, dùng để test tham gia và bài test đầu vào |
| rev1 | `reviewer1@crowd.local` | `0d352aaf-338d-5930-8bd7-e737a04e3ba3` | labeler (**reviewer của P1**) | Duyệt nhãn với tư cách reviewer |

`rev1` có vai trò hệ thống là `labeler`. "Reviewer" là vai trò *trong dự án*, do chủ dự án gán.

### 4.3 Dự án (tên bắt đầu bằng `[Seed]`, chủ là biz1)

| Key | ID | Trạng thái | Ảnh | Đơn giá | Redundancy | Ngân sách (= ký quỹ) | Đặc điểm |
|---|---|---|---|---|---|---|---|
| **P1** | `3d904a1a-3920-530b-b055-793baae64f1b` | `running` | 8 | 20.000 | 2 | 500.000 | Có nhãn ở đủ trạng thái; ảnh 8 là câu vàng kiểm tra chất lượng. Thành viên: lab1, lab2 (labeler), rev1 (reviewer) |
| **P2** | `2ae9dc42-5b32-518d-b24f-cdd7da800a96` | `pendingApproval` | 4 | 10.000 | 1 | 100.000 | Đã ký quỹ, chờ admin duyệt. **Tự hủy sau 72 giờ** nếu không ai duyệt |
| **P3** | `32cdcbe6-837a-5c7c-929d-607db130deba` | `draft` | 4 | 15.000 | 2 | 200.000 | Đã cấu hình đủ, `readiness` = ready. Dùng để test saga publish |
| **P4** | `2fde6d4c-ee31-57ae-8632-b2e6e0721217` | `running` | 7 | 30.000 | 1 | 300.000 | **Bắt buộc test đầu vào**: 3 câu, đậu khi đúng ≥ 60%. Ảnh 5–7 là câu hỏi test |

Mọi dự án dùng tập nhãn `do`, `xanh_la`, `xanh_duong`, `vang`. Ảnh mẫu là PNG một màu 256×256 có viền sáng, nên nhìn ảnh là biết đáp án đúng. Phí nền tảng chốt 30%, cộng thêm vào ký quỹ chứ không trừ vào thù lao (VD-M-15).

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
| biz1 | khả dụng **1.100.000**, ký quỹ **796.000** (P1 còn 396.000 sau 4 lần chi × 26.000; P2 100.000; P4 300.000) |
| biz2 | 0. Lệnh nạp 1.000.000 đang `pending`, ID `8d241233-a997-5c1a-9f2f-2924b419164c` |
| lab1 | khả dụng **60.000** |
| lab2 | treo 20.000, sau ~2 phút worker chuyển sang khả dụng |
| Đối soát | `healthy: true` |

Ở dev, thời gian treo là **2 phút** (`Ledger:ThoiGianTreo` trong [appsettings.Development.json](../services/ledger/Crowd.Ledger.Api/appsettings.Development.json)) và worker quét mỗi 30 giây. Production giữ mặc định 3 ngày.

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
curl -s "http://localhost:8080/projects?mine=true" -H "Authorization: Bearer <token-biz1>"        # 4 dự án [Seed]
curl -s http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b -H "Authorization: Bearer <token-biz1>"
curl -s http://localhost:8080/projects/32cdcbe6-837a-5c7c-929d-607db130deba/readiness -H "Authorization: Bearer <token-biz1>"   # ready: true

# imageUrl trong response mở được trên trình duyệt trong 5 phút
curl -s "http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/samples?page=1&pageSize=20" -H "Authorization: Bearer <token-biz1>"

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

```bash
# 200 kèm assignmentId, imageUrl (xem ảnh để biết màu), labelClasses; 204 = hết task cho bạn
curl -s -X POST http://localhost:8080/tasks/projects/3d904a1a-3920-530b-b055-793baae64f1b/next -H "Authorization: Bearer <token-lab1>"

# Nộp nhãn cho assignmentId vừa nhận
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/submit -H "Authorization: Bearer <token-lab1>" \
  -H "Content-Type: application/json" -d '{"labels":["vang"]}'

# Hoặc bỏ qua thay vì nộp
curl -s -X POST http://localhost:8080/tasks/assignments/<assignmentId>/release -H "Authorization: Bearer <token-lab1>"

curl -s http://localhost:8080/tasks/assignments/mine -H "Authorization: Bearer <token-lab1>"

# Nhãn vừa nộp xuất hiện bên annotation-svc (qua event assignment.submitted)
curl -s "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b?status=pendingReview" -H "Authorization: Bearer <token-biz1>"
```

Kiểm tra thêm:
- Nộp lại cùng `assignmentId`: bị từ chối `lease_khong_con`.
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

# Bắt đầu bài test → attemptId + 3 câu (sampleId, imageUrl). Server không trả đáp án.
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
    {"sampleId":"25bb3dd9-5900-5e1c-9d57-f84ed2c72645","labels":["do"]},
    {"sampleId":"4a1785a8-3058-5313-a0e8-017d9070854d","labels":["xanh_la"]},
    {"sampleId":"cf021f75-a9f0-5f11-be77-fc81779763de","labels":["vang"]}]}'
# → scorePercent 100, passed true, joinedProject true

# Lịch sử làm bài (tối đa 3 lần)
curl -s http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/entrance-test/attempts -H "Authorization: Bearer <token-lab3>"
```

### 6.10 Labeler rút tiền (ledger → payment → ledger)

```bash
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-lab1>"          # availableVnd: 60000

curl -s -X POST http://localhost:8080/ledger/withdrawals -H "Authorization: Bearer <token-lab1>" \
  -H "Content-Type: application/json" -H "Idempotency-Key: rut-001" \
  -d '{"amountVnd":50000,"bankAccount":"VCB-0123456789"}'                                       # state: requested

sleep 8
curl -s http://localhost:8080/ledger/withdrawals/mine -H "Authorization: Bearer <token-lab1>"     # state: completed
curl -s "http://localhost:8080/ledger/me/transactions?page=1&pageSize=20" -H "Authorization: Bearer <token-lab1>"
```

Kiểm tra thêm:
- Gửi lại với cùng `Idempotency-Key: rut-001`: trả về đúng lệnh cũ, không trừ tiền lần hai.
- Rút 10.000: **400** `duoi_muc_toi_thieu` (tối thiểu 50.000).
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
  -d '{"name":"Thu thieu tien","description":"x","taskType":"imageClassification","visibility":"public"}'

curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/label-schema -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"classes":["do","xanh_la","vang"],"allowMultiple":false}'

curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/guideline -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"markdown":"Chon theo mau.","examples":[]}'

curl -s -X PUT http://localhost:8080/projects/<id-du-an-moi>/pricing -H "Authorization: Bearer <token-biz2>" -H "Content-Type: application/json" \
  -d '{"unitPriceVnd":10000,"redundancy":1,"budgetVnd":100000,"deadline":"2027-12-31T00:00:00Z"}'

curl -s -X POST http://localhost:8080/projects/<id-du-an-moi>/datasets -H "Authorization: Bearer <token-biz2>" -F "name=lo-1" -F "file=@anh.zip"

curl -s -X POST http://localhost:8080/projects/<id-du-an-moi>/publish -H "Authorization: Bearer <token-biz2>"
sleep 3

# status: draft, statusReason: "So du kha dung 0d, can ky quy 100000d. Hay nap them tien."
curl -s http://localhost:8080/projects/<id-du-an-moi> -H "Authorization: Bearer <token-biz2>"
```

Cùng file ZIP này cũng dùng được để test **upload dataset** cho bất kỳ dự án nháp nào.

### 6.12 Quản lý dự án đang chạy

```bash
# Thêm lab3 vào P2 (FP-05)
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

# Câu hỏi vàng: chỉ sửa được khi Nháp hoặc Tạm dừng
curl -s http://localhost:8080/projects/3d904a1a-3920-530b-b055-793baae64f1b/gold-items -H "Authorization: Bearer <token-biz1>"

# Kết quả và xuất file (FB-22, FB-25)
curl -s http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b/results -H "Authorization: Bearer <token-biz1>"
curl -s "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b/export?format=csv" -H "Authorization: Bearer <token-biz1>" -o ketqua.csv
curl -s "http://localhost:8080/annotations/projects/3d904a1a-3920-530b-b055-793baae64f1b/export?format=json" -H "Authorization: Bearer <token-biz1>"

# Hoàn thành P4 → ledger trả phần ký quỹ còn dư về biz1 (khả dụng +300.000, vì P4 chưa chi đồng nào)
curl -s -X POST http://localhost:8080/projects/2fde6d4c-ee31-57ae-8632-b2e6e0721217/complete -H "Authorization: Bearer <token-biz1>"
sleep 3
curl -s http://localhost:8080/ledger/me/balance -H "Authorization: Bearer <token-biz1>"
```

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

---

## 7. Kiểm tra tổng sau khi test

```bash
curl -s http://localhost:8080/ledger/admin/reconciliation -H "Authorization: Bearer <token-admin>"
# healthy: true, unbalancedEntries / balanceMismatches / negativeAccounts / brokenChain đều rỗng
```

Chạy lúc nào cũng phải ra `healthy: true`. Nếu không, một luồng tiền nào đó đang sai.

Xem event chạy qua hệ thống: mở RabbitMQ UI tại http://localhost:15672, tab **Queues**. Mỗi queue có tên dạng `<service>.<event>`, ví dụ `ledger-svc.annotation-approved`. Hàng đợi dead-letter nằm ở exchange `datn.dlx`.

---

## 8. Sự cố thường gặp

| Hiện tượng | Nguyên nhân / cách xử lý |
|---|---|
| Service báo lỗi kết nối Postgres | Chưa bật đúng profile. Ledger và payment cần `--profile p1` |
| `401` dù vừa đăng nhập | Token hết hạn sau 15 phút. Đăng nhập lại (mục 5) |
| `401` ngay lập tức | Còn sót dấu `< >` khi dán token, hoặc dán nhầm `refreshToken` |
| `curl` trong PowerShell báo lỗi tham số | Dùng `curl.exe`, hoặc dùng Git Bash |
| Log `Seed ... da co du lieu seed — bo qua` nhưng dữ liệu khác bảng ở mục 4 | DB đã bị thay đổi bởi các lần test trước. Seed lại từ đầu theo mục 4.6 |
| Seed ném lỗi khi khởi động (ví dụ `khong_du_so_du`, `chuyen_trang_thai_khong_hop_le`) | Kịch bản bị sửa sai luật. Chạy `dotnet test shared/test/seeding-tests` để biết sai ở đâu |
| `imageUrl` trả 403 / hết hạn | Link MinIO chỉ sống 5 phút (S-07). Gọi lại API để lấy link mới |
| Lỗi bucket `datasets` không tồn tại | `minio-init` chưa chạy. Chạy lại `docker compose ... up -d` |
| Nhãn đã duyệt nhưng labeler chưa thấy tiền | ledger-svc chưa chạy, hoặc event đang nằm trong outbox của annotation. Bật ledger lên thì event sẽ được giao (at-least-once) |
| P2 tự chuyển sang `cancelled` | Đúng thiết kế: dự án chờ duyệt quá 72 giờ thì tự hủy và hoàn ký quỹ (compensation của saga) |
