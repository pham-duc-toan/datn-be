# Hướng dẫn khởi động dự án và test API

**Tài liệu liên quan:** [Kiến trúc backend](kien-truc-backend.md) · [Danh mục event](../contracts/events/CATALOG.md) · [Sổ vấn đề](van-de-can-giai-quyet.md)

Tài liệu này dành cho người lần đầu chạy hệ thống trên máy dev. Sau khi làm theo, bạn sẽ có hạ tầng và 6 service nghiệp vụ đang chạy, kèm **dữ liệu mẫu (seed) đủ cho mọi luồng chính**. Phần cuối là bộ lệnh `curl` để test từng luồng qua gateway.

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
- **Mọi request đều đi qua gateway `:8080`.** Gọi thẳng cổng 81xx chỉ dùng để debug.
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
for p in identity/Crowd.Identity.Api project/Crowd.Project.Api task/Crowd.Tasking.Api \
         annotation/Crowd.Annotation.Api ledger/Crowd.Ledger.Api payment/Crowd.Payment.Api \
         gateway/Crowd.Gateway.Api; do
  dotnet run --project services/$p > logs/$(basename $p).log 2>&1 &
done
grep -h "Seed " logs/*.log        # đợi ~20 giây rồi xem kết quả seed
```

Thư mục `logs/` chỉ để xem tạm. Đừng commit nó.

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
- **ID khớp nhau giữa các service** vì được tính tất định từ tên. Ví dụ dự án P1 ở project-svc, task-svc, annotation-svc và ledger-svc đều là `3d904a1a-…`.
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

| Key | Email | Vai trò | Dùng để test |
|---|---|---|---|
| admin | `admin@crowd.local` | admin | Duyệt dự án, phân xử khiếu nại, đối soát |
| biz1 | `doanhnghiep1@crowd.local` | business | Chủ 4 dự án, đã nạp 2.000.000đ |
| biz2 | `doanhnghiep2@crowd.local` | business | **Chưa có tiền**, có một lệnh nạp 1.000.000đ chưa trả |
| lab1 | `labeler1@crowd.local` | labeler | Có **60.000đ rút được ngay**, 2 nhãn chờ duyệt |
| lab2 | `labeler2@crowd.local` | labeler | 1 nhãn được duyệt, 1 bị từ chối, 1 đang khiếu nại |
| lab3 | `labeler3@crowd.local` | labeler | Chưa vào dự án nào, dùng để test tham gia và bài test đầu vào |
| rev1 | `reviewer1@crowd.local` | labeler (**reviewer của P1**) | Duyệt nhãn với tư cách reviewer |

`rev1` có vai trò hệ thống là `labeler`. "Reviewer" là vai trò *trong dự án*, do chủ dự án gán.

### 4.3 Dự án (tên bắt đầu bằng `[Seed]`, chủ là biz1)

| Key | Trạng thái | Ảnh | Đơn giá | Redundancy | Ngân sách (= ký quỹ) | Đặc điểm |
|---|---|---|---|---|---|---|
| **P1** | `running` | 8 | 20.000 | 2 | 500.000 | Có nhãn ở đủ trạng thái; ảnh 8 là câu vàng kiểm tra chất lượng. Thành viên: lab1, lab2 (labeler), rev1 (reviewer) |
| **P2** | `pendingApproval` | 4 | 10.000 | 1 | 100.000 | Đã ký quỹ, chờ admin duyệt. **Tự hủy sau 72 giờ** nếu không ai duyệt |
| **P3** | `draft` | 4 | 15.000 | 2 | 200.000 | Đã cấu hình đủ, `readiness` = ready. Dùng để test saga publish |
| **P4** | `running` | 7 | 30.000 | 1 | 300.000 | **Bắt buộc test đầu vào**: 3 câu, đậu khi đúng ≥ 60%. Ảnh 5–7 là câu hỏi test |

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

Tiến độ P1 lúc vừa seed: 8 task, 2 hoàn thành (ảnh 1, 2), 1 bị loại (ảnh 8), 8 lượt nộp, cần 14 lượt.

### 4.5 Tiền lúc vừa seed

| Tài khoản | Số dư |
|---|---|
| biz1 | khả dụng **1.100.000**, ký quỹ **796.000** (P1 còn 396.000 sau 4 lần chi × 26.000; P2 100.000; P4 300.000) |
| biz2 | 0. Lệnh nạp 1.000.000 đang `pending` |
| lab1 | khả dụng **60.000** |
| lab2 | treo 20.000, sau ~2 phút worker chuyển sang khả dụng |
| Đối soát | `healthy: true` |

Ở dev, thời gian treo là **2 phút** (`Ledger:ThoiGianTreo` trong [appsettings.Development.json](../services/ledger/Crowd.Ledger.Api/appsettings.Development.json)) và worker quét mỗi 30 giây. Production giữ mặc định 3 ngày.

### 4.6 Các ID hay dùng (cố định, copy được)

```bash
# Tài khoản
ADMIN_ID=e142c9aa-4f06-542c-9ac7-7c9bbaa6f65a
BIZ1_ID=37665dec-c58e-5c8f-a244-476f27222311
BIZ2_ID=d3b5a5c5-0232-5935-9e4a-55812983f1a6
LAB1_ID=1f5e19a7-9f58-589b-8c74-45334ff35d05
LAB2_ID=0398f264-d694-5176-9614-844201175b74
LAB3_ID=14299c5f-f7c3-5075-b65a-cd4fbc2aa194
REV1_ID=0d352aaf-338d-5930-8bd7-e737a04e3ba3

# Dự án
P1=3d904a1a-3920-530b-b055-793baae64f1b    # running
P2=2ae9dc42-5b32-518d-b24f-cdd7da800a96    # pendingApproval
P3=32cdcbe6-837a-5c7c-929d-607db130deba    # draft
P4=2fde6d4c-ee31-57ae-8632-b2e6e0721217    # running, cần test đầu vào

# Nhãn của P1
N_CHO_DUYET_1=374fd43c-82c4-55d6-ae35-f7258f7e063b   # lab1, ảnh 4, chờ duyệt
N_CHO_DUYET_2=925b78bf-31e6-5534-b04d-505a00a570f2   # lab1, ảnh 6, chờ duyệt
N_BI_TU_CHOI=0f95272a-8b7a-5b8c-8f78-58f63a562c75    # lab2, ảnh 2, bị từ chối (khiếu nại được)
N_DANG_KHIEU_NAI=af8391bc-4df6-5176-ba9a-08d1136bc926 # lab2, ảnh 5, chờ admin phân xử
N_DA_DUYET=7efccfd8-ab86-58e6-93d8-9b84d6802967      # lab1, ảnh 1, đã duyệt

# Lệnh nạp chưa trả của biz2
INTENT_BIZ2=8d241233-a997-5c1a-9f2f-2924b419164c
```

### 4.7 Tắt seed / seed lại từ đầu

- **Tắt:** đặt `"Seed": { "Enabled": false }` trong `appsettings.Development.json` của service tương ứng.
- **Seed lại từ đầu:** phải xóa dữ liệu. Lệnh dưới **xóa toàn bộ dữ liệu dev**, gồm cả dữ liệu bạn tự tạo, RabbitMQ và MinIO:

```bash
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 down -v
docker compose -f docker-compose.infra.yml --profile p0 --profile p1 up -d
# rồi chạy lại các service
```

- **Sửa kịch bản:** sửa [KichBanSeed.cs](../shared/seeding/KichBanSeed.cs), chạy `dotnet test shared/test/seeding-tests`, rồi seed lại từ đầu như trên. Seed chỉ chạy trên DB chưa có bản ghi mốc, nên sửa kịch bản mà không xóa dữ liệu thì không có tác dụng.

---

## 5. Chuẩn bị test

Mở **một** terminal Git Bash, dán mục 4.6 và khối dưới đây. Access token sống **15 phút**; hết hạn thì chạy lại khối này.

```bash
G=http://localhost:8080
tok() {   # tok <email> → in ra accessToken
  curl -s -X POST $G/auth/login -H "Content-Type: application/json" \
    -d "{\"email\":\"$1\",\"password\":\"Matkhau@123\"}" | sed -E 's/.*"accessToken":"([^"]+)".*/\1/'
}
ADM=$(tok admin@crowd.local);       BIZ1=$(tok doanhnghiep1@crowd.local); BIZ2=$(tok doanhnghiep2@crowd.local)
LAB1=$(tok labeler1@crowd.local);   LAB2=$(tok labeler2@crowd.local);     LAB3=$(tok labeler3@crowd.local)
REV1=$(tok reviewer1@crowd.local)
echo ${ADM:0:20}...                 # thấy chuỗi eyJ... là đúng
```

Các mục dưới đây giả định **dữ liệu vừa seed**. Đã chạy thử luồng nào rồi thì số dư và trạng thái sẽ khác bảng kỳ vọng. Muốn về lại trạng thái ban đầu thì làm theo mục 4.7.

---

## 6. Test theo luồng

### 6.1 Xác thực

```bash
curl -s $G/me -H "Authorization: Bearer $ADM"                          # roles: ["admin"]
curl -s -X POST $G/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@crowd.local","password":"sai"}'                  # 401, thông báo chung chung (VD-S-13)
curl -s -o /dev/null -w "%{http_code}\n" $G/projects                   # 401: gateway chặn khi thiếu token

# Đăng ký tài khoản mới (không đăng ký được role admin)
curl -s -X POST $G/auth/register -H "Content-Type: application/json" \
  -d '{"email":"moi@test.vn","password":"Matkhau@123","displayName":"Nguoi moi","roles":["labeler"]}'

# Làm mới / đăng xuất: lấy refreshToken từ response của /auth/login
curl -s -X POST $G/auth/refresh -H "Content-Type: application/json" -d '{"refreshToken":"<refreshToken>"}'
curl -s -X POST $G/auth/logout  -H "Content-Type: application/json" -d '{"refreshToken":"<refreshToken>"}'   # luôn 204
```

### 6.2 Xem dữ liệu seed

```bash
curl -s "$G/projects?mine=true" -H "Authorization: Bearer $BIZ1"                 # 4 dự án [Seed]
curl -s $G/projects/$P1 -H "Authorization: Bearer $BIZ1"
curl -s $G/projects/$P3/readiness -H "Authorization: Bearer $BIZ1"               # ready: true
curl -s "$G/projects/$P1/samples?page=1&pageSize=20" -H "Authorization: Bearer $BIZ1"   # imageUrl mở được trên trình duyệt trong 5 phút
curl -s $G/projects/$P1/members -H "Authorization: Bearer $BIZ1"
curl -s $G/tasks/projects/$P1/progress -H "Authorization: Bearer $BIZ1"
curl -s $G/ledger/me/balance -H "Authorization: Bearer $BIZ1"
curl -s "$G/projects" -H "Authorization: Bearer $LAB3"                          # labeler thấy dự án công khai
```

### 6.3 Doanh nghiệp nạp tiền (payment → ledger)

biz2 có sẵn một lệnh nạp chưa trả. Trả nó qua trang sandbox:

```bash
curl -s $G/payments/deposits/$INTENT_BIZ2 -H "Authorization: Bearer $BIZ2"       # status: pending
curl -s -X POST "$G/payments/sandbox/checkout/$INTENT_BIZ2/pay?amount=1000000"  # cổng giả lập ký HMAC rồi gọi webhook
sleep 3
curl -s $G/payments/deposits/$INTENT_BIZ2 -H "Authorization: Bearer $BIZ2"       # status: succeeded
curl -s $G/ledger/me/balance -H "Authorization: Bearer $BIZ2"                    # businessAvailableVnd: 1000000
```

Kiểm tra thêm:
- Gọi lại `pay` lần nữa: không cộng tiền thêm (webhook lặp lại bị bỏ qua).
- Gọi `pay` với `amount=10000` trên một lệnh khác: bị từ chối `sai_so_tien`.

Tạo lệnh nạp mới (bắt buộc header `Idempotency-Key`; gửi hai lần cùng key thì vẫn chỉ có một lệnh):

```bash
curl -s -X POST $G/payments/deposits -H "Authorization: Bearer $BIZ2" \
  -H "Content-Type: application/json" -H "Idempotency-Key: nap-thu-1" -d '{"amountVnd":500000}'
```

### 6.4 Publish dự án: saga ký quỹ (project → ledger → project)

```bash
curl -s -X POST $G/projects/$P3/publish -H "Authorization: Bearer $BIZ1"         # status: pendingEscrow
sleep 3
curl -s $G/projects/$P3 -H "Authorization: Bearer $BIZ1"                         # status: pendingApproval
curl -s $G/ledger/me/balance -H "Authorization: Bearer $BIZ1"                    # khả dụng −200.000, ký quỹ +200.000
```

Luồng event: `project.publish_requested` → ledger giữ tiền → `escrow.reserved` → project chuyển sang chờ duyệt. Nhánh thiếu tiền (`escrow.rejected`) xem mục 6.11.

### 6.5 Admin duyệt / từ chối dự án

```bash
curl -s $G/projects/pending-approval -H "Authorization: Bearer $ADM"             # thấy P2 (và P3 nếu đã làm 6.4)
curl -s -X POST $G/projects/$P2/approve -H "Authorization: Bearer $ADM"          # → running, phát project.published
curl -s -X POST $G/projects/$P3/reject -H "Authorization: Bearer $ADM" \
  -H "Content-Type: application/json" -d '{"reason":"Anh khong phu hop"}'       # → cancelled, ledger hoàn ký quỹ
sleep 3; curl -s $G/ledger/me/balance -H "Authorization: Bearer $BIZ1"
```

Labeler thường không được duyệt: `curl -s -X POST $G/projects/$P2/approve -H "Authorization: Bearer $LAB1"` → **403**.

### 6.6 Labeler nhận task, nộp, bỏ qua (task → annotation)

```bash
curl -s -X POST $G/tasks/projects/$P1/next -H "Authorization: Bearer $LAB1"
# → 200 kèm assignmentId, imageUrl (xem ảnh để biết màu), labelClasses; 204 = hết task cho bạn
A=<assignmentId>
curl -s -X POST $G/tasks/assignments/$A/submit -H "Authorization: Bearer $LAB1" \
  -H "Content-Type: application/json" -d '{"labels":["vang"]}'
# Hoặc bỏ qua thay vì nộp:
# curl -s -X POST $G/tasks/assignments/$A/release -H "Authorization: Bearer $LAB1"

curl -s $G/tasks/assignments/mine -H "Authorization: Bearer $LAB1"
curl -s "$G/annotations/projects/$P1?status=pendingReview" -H "Authorization: Bearer $BIZ1"   # nhãn vừa nộp xuất hiện (assignment.submitted)
```

Kiểm tra thêm:
- Nộp lại cùng `assignmentId`: bị từ chối `lease_khong_con`.
- `lab3` gọi `next` ở P1 khi chưa tham gia: **403** `khong_phai_thanh_vien`.

### 6.7 Duyệt nhãn → labeler có tiền (annotation → ledger)

```bash
curl -s "$G/annotations/projects/$P1?status=pendingReview" -H "Authorization: Bearer $REV1"   # 2 nhãn chờ duyệt của lab1
curl -s -X POST $G/annotations/$N_CHO_DUYET_1/approve -H "Authorization: Bearer $REV1"
curl -s -X POST $G/annotations/$N_CHO_DUYET_2/reject -H "Authorization: Bearer $BIZ1" \
  -H "Content-Type: application/json" -d '{"reason":"Anh mau xanh duong, ban chon dung roi nhung thu tu choi"}'
sleep 3
curl -s $G/ledger/me/balance -H "Authorization: Bearer $LAB1"     # pendingVnd +20000 (sau 2 phút chuyển sang availableVnd)
curl -s $G/annotations/$N_CHO_DUYET_1/history -H "Authorization: Bearer $BIZ1"
```

Kiểm tra thêm:
- Duyệt lại nhãn đã duyệt: **409** `da_duyet`. Một nhãn chỉ sinh tiền một lần (VD-M-02).
- `lab1` gọi `approve`: **404**. Người không có quyền duyệt dự án nhận 404 chứ không phải 403, để không lộ việc nhãn đó có tồn tại.

### 6.8 Khiếu nại (labeler → admin)

```bash
# lab2 khiếu nại nhãn bị từ chối 2 giờ trước
curl -s -X POST $G/annotations/$N_BI_TU_CHOI/appeal -H "Authorization: Bearer $LAB2" \
  -H "Content-Type: application/json" -d '{"message":"Anh nay co sac vang, de nghi xem lai"}'
curl -s $G/annotations/mine -H "Authorization: Bearer $LAB2"      # appealedCount: 2

# Admin xem hàng đợi và phân xử
curl -s "$G/annotations/appeals?page=1&pageSize=20" -H "Authorization: Bearer $ADM"
curl -s -X POST $G/annotations/$N_DANG_KHIEU_NAI/appeal/resolve -H "Authorization: Bearer $ADM" \
  -H "Content-Type: application/json" -d '{"accept":true,"note":"Chap nhan"}'       # → approved, lab2 được trả 20.000
curl -s -X POST $G/annotations/$N_BI_TU_CHOI/appeal/resolve -H "Authorization: Bearer $ADM" \
  -H "Content-Type: application/json" -d '{"accept":false,"note":"Anh mau do"}'     # → bị từ chối vĩnh viễn
```

Khiếu nại lần hai trên cùng nhãn: **409**. Nếu khiếu nại trước còn đang chờ thì lỗi là `khong_the_khieu_nai`; nếu đã bị bác thì lỗi là `da_khieu_nai`.

### 6.9 Tham gia dự án và bài test đầu vào

```bash
# P1 không yêu cầu test → tham gia thẳng
curl -s -X POST $G/projects/$P1/join -H "Authorization: Bearer $LAB3"
curl -s -X POST $G/tasks/projects/$P1/next -H "Authorization: Bearer $LAB3"       # giờ đã nhận được task

# P4 yêu cầu test → join bị chặn (409), phải làm bài
curl -s -X POST $G/projects/$P4/join -H "Authorization: Bearer $LAB3"
curl -s -X POST $G/projects/$P4/entrance-test/attempts -H "Authorization: Bearer $LAB3"
# → attemptId + 3 câu (sampleId, imageUrl). Server không trả đáp án.
```

Đáp án của 3 câu (xem ảnh cũng thấy):

| sampleId | Đáp án |
|---|---|
| `25bb3dd9-5900-5e1c-9d57-f84ed2c72645` | `do` |
| `4a1785a8-3058-5313-a0e8-017d9070854d` | `xanh_la` |
| `cf021f75-a9f0-5f11-be77-fc81779763de` | `vang` |

```bash
AT=<attemptId>
curl -s -X POST $G/projects/$P4/entrance-test/attempts/$AT/submit -H "Authorization: Bearer $LAB3" \
  -H "Content-Type: application/json" -d '{"answers":[
    {"sampleId":"25bb3dd9-5900-5e1c-9d57-f84ed2c72645","labels":["do"]},
    {"sampleId":"4a1785a8-3058-5313-a0e8-017d9070854d","labels":["xanh_la"]},
    {"sampleId":"cf021f75-a9f0-5f11-be77-fc81779763de","labels":["vang"]}]}'
# → scorePercent 100, passed true, joinedProject true
curl -s $G/projects/$P4/entrance-test/attempts -H "Authorization: Bearer $LAB3"   # lịch sử (tối đa 3 lần)
```

### 6.10 Labeler rút tiền (ledger → payment → ledger)

```bash
curl -s $G/ledger/me/balance -H "Authorization: Bearer $LAB1"                    # availableVnd: 60000
curl -s -X POST $G/ledger/withdrawals -H "Authorization: Bearer $LAB1" \
  -H "Content-Type: application/json" -H "Idempotency-Key: rut-001" \
  -d '{"amountVnd":50000,"bankAccount":"VCB-0123456789"}'                        # state: requested
sleep 8
curl -s $G/ledger/withdrawals/mine -H "Authorization: Bearer $LAB1"              # state: completed
curl -s "$G/ledger/me/transactions?page=1&pageSize=20" -H "Authorization: Bearer $LAB1"
```

Kiểm tra thêm:
- Gửi lại với cùng `Idempotency-Key: rut-001`: trả về đúng lệnh cũ, không trừ tiền lần hai.
- Rút 10.000: **400** `duoi_muc_toi_thieu` (tối thiểu 50.000).
- Rút nhiều hơn số dư: **409** `khong_du_so_du`.

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
curl -s -X POST $G/projects -H "Authorization: Bearer $BIZ2" -H "Content-Type: application/json" \
  -d '{"name":"Thu thieu tien","description":"x","taskType":"imageClassification","visibility":"public"}'
PX=<id>
curl -s -X PUT $G/projects/$PX/label-schema -H "Authorization: Bearer $BIZ2" -H "Content-Type: application/json" \
  -d '{"classes":["do","xanh_la","vang"],"allowMultiple":false}'
curl -s -X PUT $G/projects/$PX/guideline -H "Authorization: Bearer $BIZ2" -H "Content-Type: application/json" \
  -d '{"markdown":"Chon theo mau.","examples":[]}'
curl -s -X PUT $G/projects/$PX/pricing -H "Authorization: Bearer $BIZ2" -H "Content-Type: application/json" \
  -d '{"unitPriceVnd":10000,"redundancy":1,"budgetVnd":100000,"deadline":"2027-12-31T00:00:00Z"}'
curl -s -X POST $G/projects/$PX/datasets -H "Authorization: Bearer $BIZ2" -F "name=lo-1" -F "file=@anh.zip"
curl -s -X POST $G/projects/$PX/publish -H "Authorization: Bearer $BIZ2"
sleep 3
curl -s $G/projects/$PX -H "Authorization: Bearer $BIZ2"     # status: draft, statusReason: "So du kha dung 0d, can ky quy 100000d..."
```

Cùng file ZIP này cũng dùng được để test **upload dataset** cho bất kỳ dự án nháp nào.

### 6.12 Quản lý dự án đang chạy

```bash
# Thành viên (FP-05, FB-23)
curl -s -X POST $G/projects/$P2/members -H "Authorization: Bearer $BIZ1" \
  -H "Content-Type: application/json" -d "{\"userId\":\"$LAB3_ID\",\"role\":\"labeler\"}"
curl -s -X POST $G/projects/$P1/members/$LAB2_ID/block -H "Authorization: Bearer $BIZ1"     # lab2 bị thu hồi lease, không nhận task được
curl -s -X POST $G/tasks/projects/$P1/next -H "Authorization: Bearer $LAB2"                 # 403 bi_chan_khoi_du_an
curl -s -X POST $G/projects/$P1/members/$LAB2_ID/unblock -H "Authorization: Bearer $BIZ1"

# Tạm dừng / chạy tiếp → task-svc ngừng / mở cấp task
curl -s -X POST $G/projects/$P1/pause  -H "Authorization: Bearer $BIZ1"
curl -s -X POST $G/tasks/projects/$P1/next -H "Authorization: Bearer $LAB1"                 # 403 du_an_khong_chay
curl -s -X POST $G/projects/$P1/resume -H "Authorization: Bearer $BIZ1"

# Câu hỏi vàng: chỉ sửa được khi Nháp hoặc Tạm dừng
curl -s $G/projects/$P1/gold-items -H "Authorization: Bearer $BIZ1"

# Kết quả và xuất file (FB-22, FB-25)
curl -s $G/annotations/projects/$P1/results -H "Authorization: Bearer $BIZ1"
curl -s "$G/annotations/projects/$P1/export?format=csv" -H "Authorization: Bearer $BIZ1" -o ketqua.csv
curl -s "$G/annotations/projects/$P1/export?format=json" -H "Authorization: Bearer $BIZ1"

# Kết thúc → ledger trả phần ký quỹ còn dư về biz1
curl -s -X POST $G/projects/$P4/complete -H "Authorization: Bearer $BIZ1"
sleep 3; curl -s $G/ledger/me/balance -H "Authorization: Bearer $BIZ1"   # khả dụng +300.000 (P4 chưa chi đồng nào)
```

### 6.13 Phân quyền chiều ngang (BOLA)

Truy cập tài nguyên của người khác trả **404** chứ không phải 403, để kẻ dò không biết được tài nguyên đó có tồn tại hay không.

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X PUT $G/projects/$P3/guideline -H "Authorization: Bearer $BIZ2" \
  -H "Content-Type: application/json" -d '{"markdown":"hack","examples":[]}'          # 404: biz2 không sửa được dự án của biz1
curl -s -o /dev/null -w "%{http_code}\n" $G/payments/deposits/$INTENT_BIZ2 -H "Authorization: Bearer $BIZ1"   # 404: không xem được lệnh nạp của người khác
curl -s -o /dev/null -w "%{http_code}\n" "$G/annotations/projects/$P1?status=pendingReview" -H "Authorization: Bearer $LAB1"   # 404: labeler không xem được hàng duyệt
curl -s -o /dev/null -w "%{http_code}\n" -X POST $G/projects/$P2/approve -H "Authorization: Bearer $LAB1"     # 403: sai VAI TRÒ (RBAC) thì là 403
```

---

## 7. Kiểm tra tổng sau khi test

```bash
curl -s $G/ledger/admin/reconciliation -H "Authorization: Bearer $ADM"
# healthy: true, unbalancedEntries / balanceMismatches / negativeAccounts / brokenChain đều rỗng
```

Chạy lúc nào cũng phải ra `healthy: true`. Nếu không, một luồng tiền nào đó đang sai.

Xem event chạy qua hệ thống: mở RabbitMQ UI tại http://localhost:15672, tab **Queues**. Mỗi queue có tên dạng `<service>.<event>`, ví dụ `ledger-svc.annotation-approved`. Hàng đợi dead-letter nằm ở exchange `datn.dlx`.

---

## 8. Sự cố thường gặp

| Hiện tượng | Nguyên nhân / cách xử lý |
|---|---|
| Service báo lỗi kết nối Postgres | Chưa bật đúng profile. Ledger và payment cần `--profile p1` |
| `401` dù vừa đăng nhập | Token hết hạn sau 15 phút. Chạy lại khối ở mục 5 |
| `curl` trong PowerShell báo lỗi tham số | Dùng `curl.exe`, hoặc dùng Git Bash |
| Log `Seed ... da co du lieu seed — bo qua` nhưng dữ liệu khác bảng ở mục 4 | DB đã bị thay đổi bởi các lần test trước. Seed lại từ đầu theo mục 4.7 |
| Seed ném lỗi khi khởi động (ví dụ `khong_du_so_du`, `chuyen_trang_thai_khong_hop_le`) | Kịch bản bị sửa sai luật. Chạy `dotnet test shared/test/seeding-tests` để biết sai ở đâu |
| `imageUrl` trả 403 / hết hạn | Link MinIO chỉ sống 5 phút (S-07). Gọi lại API để lấy link mới |
| Lỗi bucket `datasets` không tồn tại | `minio-init` chưa chạy. Chạy lại `docker compose ... up -d` |
| Nhãn đã duyệt nhưng labeler chưa thấy tiền | ledger-svc chưa chạy, hoặc event đang nằm trong outbox của annotation. Bật ledger lên thì event sẽ được giao (at-least-once) |
| P2 tự chuyển sang `cancelled` | Đúng thiết kế: dự án chờ duyệt quá 72 giờ thì tự hủy và hoàn ký quỹ (compensation của saga) |
