# Kiến trúc Backend — Hệ thống gán nhãn dữ liệu cộng đồng

**Phiên bản:** 1.0 · **Kiểu kiến trúc:** Microservices, event-driven
**Tài liệu nguồn:** [Đặc tả chức năng](../dac-ta-he-thong-gan-nhan-cong-dong.md)

---

## 1. Nguyên tắc thiết kế

| # | Nguyên tắc | Hệ quả cụ thể |
|---|---|---|
| 1 | Mỗi service một **datastore container riêng**, chọn theo access pattern | Service A không chạm được dữ liệu service B: khác tiến trình, khác credential. Mỗi service chỉ được cấp đúng connection string của mình |
| 2 | Không gọi HTTP đồng bộ giữa các service trong đường nóng | Giao tiếp qua event; dữ liệu chéo giữ dưới dạng bản sao read-only cập nhật bằng event |
| 3 | Tiền không bao giờ đi qua transaction phân tán | `ledger-svc` là service duy nhất ghi số dư; mọi thứ khác là saga |
| 4 | Đường ghi nóng phải mỏng | `task`/`annotation`/`gate`: validate → ghi → phát event. Tính toán nặng đẩy sang worker |
| 5 | ML chỉ gợi ý, không bao giờ ghi nhãn cuối | Ranh giới cứng ở mục 2.6 đặc tả |
| 6 | Outbox cho mọi event, idempotency cho mọi consumer | RabbitMQ là at-least-once |

**Chọn ngôn ngữ theo lực đẩy, không theo sở thích:**

- **C# (.NET 10 LTS)** — mặc định cho toàn bộ nghiệp vụ. Không dùng .NET 9: bản STS đã hết hỗ trợ từ 5/2026, sẽ bị Trivy báo CVE ở `NC-A-10`.
- **Python** — `quality`, `ml`, `fraud`: Dawid–Skene, MACE, Krippendorff's alpha, STAPLE, SAM đều nằm sẵn trong hệ sinh thái numpy/scipy/PyTorch. Viết lại bằng C# là lãng phí.
- **Node.js/TypeScript** — `collab`: cần CRDT (Yjs) để merge thao tác đồng thời và hàng đợi offline (FR-03, FR-08). .NET không có thư viện tương đương ở mức production.

---

## 2. Bản đồ service

```
                                  ┌──────────────────┐
   Web app (Next.js) ────────────►│   api-gateway    │  :8080
   Trang vượt link   ────────────►│ Ocelot · authN   │
   API công khai (FS-05) ────────►│  rate limit      │
                                  └────────┬─────────┘
                                           │
                          ┌────────────────┴──────────────┐
                          ▼                               ▼
                  ┌──────────────┐               (đường trực tiếp:
                  │   web-bff    │ :8081          gate, media, collab
                  │ gộp đa nguồn │                — không qua BFF)
                  └──────┬───────┘
                         │
  ┌──────────┬───────────┼───────────┬───────────┬───────────┬──────────┐
  ▼          ▼           ▼           ▼           ▼           ▼          ▼
identity  project      task     annotation     link        gate      media
 :8101     :8102      :8103       :8104       :8107       :8108      :8109
identity_db project_db task_db  annotation_db  link_db   ClickHouse  (không DB)
  │          │           │           │           │           │          │
  └──────────┴───────────┴───────────┴───────────┴───────────┴──────────┤
                                                                        │
                              ┌─────────────────────┐                   │
                              │      RabbitMQ       │◄──────────────────┘
                              │  topic: datn.events │
                              └──────────┬──────────┘
                                         │
   ┌───────────┬──────────┬──────────────┼───────────┬───────────┬──────────┐
   ▼           ▼          ▼              ▼           ▼           ▼          ▼
 quality      ml       fraud          ledger      payment     collab      admin
  :8201     :8202     :8203           :8105       :8106       :8301      :8111
 Python     Python    Python            C#          C#        Node        C#
quality_db   ml_db   fraud_db        ledger_db  payment_db  collab_db  admin_db
                                                                        ▲
                                                    notification :8110 ─┘
                                                     notification_db
```

### 2.1. Bảng service

**17 service, 19 container datastore.** `web-bff` không sở hữu dữ liệu. `api-gateway` chỉ có Redis (rate limit). 15 service nghiệp vụ mỗi cái một datastore riêng — 4 trong số đó có thêm Redis riêng, và `gate-svc` dùng ClickHouse thay Postgres. Chi tiết ở mục 3.2.

| Service | Port | Database | Lang | Sở hữu (bảng gốc) | Lực đẩy tách |
|---|---|---|---|---|---|
| `api-gateway` | 8080 | redis-gateway | C# | — | Edge: authN, rate limit, Turnstile, correlationId |
| `web-bff` | 8081 | — | C# | — | Gộp đa nguồn cho dashboard; nơi DUY NHẤT được biết nhiều service |
| `identity-svc` | 8101 | identity_db | C# | users, credentials, sessions, roles, 2fa, profiles, reputation, badges | Bảo mật: DB chứa hash mật khẩu tách khỏi mọi thứ |
| `project-svc` | 8102 | project_db | C# | projects, label_schemas, guidelines, datasets, samples, gold_sets, entrance_tests, **project_members** | State machine vòng đời dự án |
| `task-svc` | 8103 | task_db + redis-task | C# | tasks, assignments, leases, redundancy_state, labeler_cache, project_members_cache | Đường nóng, gắn Redis, scale theo số labeler |
| `annotation-svc` | 8104 | annotation_db | C# | annotations, versions, drafts, reviews, appeals, export_jobs, project_members_cache | Ghi nóng nhất, dữ liệu lớn nhất |
| `ledger-svc` | 8105 | ledger_db | C# | accounts, journal_entries, holds, escrows | **Nhất quán tuyệt đối** |
| `payment-svc` | 8106 | payment_db | C# | payment_intents, payouts, provider_txns, reconciliations | Tích hợp ngoài, webhook + retry, cô lập rủi ro |
| `link-svc` | 8107 | link_db | C# | links, campaigns, api_keys, referral_codes, referrals, blocked_domains, link_reports | Bounded context của Sharer (**đã có**, mục 3.6) |
| `gate-svc` | 8108 | **ClickHouse** + redis-gate + Postgres gate_db (bản sao + outbox) | C# | Redis: phiên, jti, chống trùng IP, stream; ClickHouse: click_events; Postgres: gate_projects, gate_samples, gate_gold_items, gate_links, gate_budgets | **Traffic ~100× phần còn lại**, sập độc lập (**đã có**, mục 3.6) |
| `media-svc` | 8109 | **không có** | **Python** | — (người xem nhúng thẳng vào ảnh, setting giữ trong bộ nhớ) (**đã có**, mục 3.13) | Phát file mẫu qua token + watermark chìm (CPU); Python vì OpenCV / numpy |
| `notification-svc` | 8110 | notification_db | C# | notifications, email_preferences; bản sao contacts, project_owners, withdrawal_owners (**đã có**, mục 3.11) | Fan-out chậm không được chặn ai |
| `admin-svc` | 8111 | admin_db | C# | **settings, setting_history** (đã có); review_queues, disputes, audit_log, requests_readmodel (P6) | Chủ của setting hệ thống (mục 3.10). Quyền cao nhất — giới hạn blast radius |
| `quality-svc` | 8201 | quality_db | Python | consensus_rounds, label_agreements, gold_answers, ds_skills, project_metrics, reputations | Ngôn ngữ khác + tính batch nặng |
| `ml-svc` | 8202 | ml_db (pgvector) | Python | models, predictions, uncertainty_scores | GPU, scale độc lập, sập vẫn chạy được hệ thống |
| `fraud-svc` | 8203 | fraud_db | Python | signals, fingerprints, farm_clusters | Batch + ML |
| `collab-svc` | 8301 | collab_db + redis-collab | Node | rooms, yjs_snapshots, op_log, contributions | **Stateful WebSocket**, sticky session, CRDT |

### 2.2. Ánh xạ sang mã chức năng đặc tả

| Service | Mã chức năng |
|---|---|
| identity | FC-01→08, FB-01, FB-02, FM-01 (dữ liệu + API duyệt KYB, khoá / mở tài khoản — mục 3.11), FL-01, FL-12 (cấp độ, huy hiệu, xếp hạng — mục 3.12) |
| project | FB-10→18, FB-24, FL-02, FL-03 |
| task | FL-05 (nháp), FL-06, FL-07 (báo lỗi task), FB-15, FB-20 (ETA), FQ-03 |
| annotation | FL-04, FL-08, FL-09, FB-21, FB-24 (cảnh báo lệch), FB-25 (COCO / YOLO / VOC) |
| ledger | FB-04, FB-05, FB-20 (chi phí), FL-10, FL-11 (thống kê thu nhập), FM-06 (tranh chấp — mục 3.12), mục 2.11 |
| payment | FB-03, FS-09, FM-05 |
| link | FS-01→08, mục "Kiểm duyệt link đích" |
| gate | Toàn bộ luồng "Trang vượt link", công thức CPM, chống lạm dụng |
| quality | FQ-01→04, FB-22, FA-04, FM-07 (chống gian lận — gộp thay fraud-svc, mục 3.12) |
| ml | FA-01→05 |
| collab | FR-01→09 |
| media | FP-02 (watermark ảnh, âm thanh, video — 3.13, 3.17), FP-03 (chặn tải bản gốc) — mục 3.13. **Bỏ** FP-04 che PII (xung đột dự án gán nhãn mặt / biển số) và nhật ký từng lượt xem |
| notification | FC-06 (+ đẩy realtime qua WebSocket), FC-08 (ngôn ngữ thông báo / email) — mục 3.17 |
| admin | FM-03 (mẫu bài toán), FM-09 (báo cáo, thông báo toàn hệ thống), FP-06 (nhật ký sự kiện, chỉ-ghi-thêm ở mức CSDL), hàng đợi duyệt chung (read model), cảnh báo DLQ — mục 3.17. Duyệt FM-01/02/05/08 gọi API của service chủ |
| fraud | *(không tách — FM-07 nằm ở quality)*; chống lạm dụng referral để sau |

---

## 3. Mười quyết định kỹ thuật cốt lõi

### 3.1. Cô lập datastore ở mức tiến trình — *database server per service*

Mỗi service có **container datastore độc lập** của riêng nó — Postgres, ClickHouse hoặc Redis tùy access pattern (mục 3.2) — với cổng riêng, credential riêng, volume riêng. Đây là nấc phân tách cao nhất trong thang database-per-service:

```
1. Chung bảng, phân biệt bằng cột            ← không phải microservice
2. Chung database, mỗi service một schema    ← yếu, dễ vi phạm
3. Mỗi service một database, chung server    ← cô lập logic, không cô lập vận hành
4. Mỗi service một server riêng              ← ĐANG DÙNG
```

Ở nấc 4, `task-svc` không chạm được `ledger_db` vì **nó không có credential** — mỗi service chỉ được cấp đúng connection string của mình qua biến môi trường. Cô lập đồng thời ở 4 mức: dữ liệu, hiệu năng, sự cố, và tài nguyên.

**Giới hạn cần nói rõ — đã kiểm chứng bằng thực nghiệm:** mọi container nằm chung một Docker network, nên `db-ledger` vẫn **phân giải được tên và mở được cổng** từ container khác. Cô lập ở đây là **bằng credential, không phải bằng mạng**:

```
Trong container task, mở ledger_db            → FATAL: database "ledger_db" does not exist   ✓
Từ container task nối mạng sang db-ledger
  VỚI credential đúng của ledger              → kết nối thành công                            ✗
```

Đây là chuẩn mực công nghiệp và đủ cho môi trường phát triển — kẻ tấn công phải có sẵn mật khẩu thì mới khai thác được, mà mật khẩu thì chỉ nằm trong biến môi trường của đúng service sở hữu. Muốn đóng nốt đường mạng thì dùng NetworkPolicy của Kubernetes ở nhánh `NC-A`, chứ không phải bằng Docker Compose.

**Hệ quả kèm theo:** không thể có foreign key, transaction, hay `JOIN` xuyên service — kể cả trong lúc vá lỗi khẩn cấp. Mọi liên kết dữ liệu phải đi qua event và bản sao read-only (mục 4).

**Lợi ích trực tiếp — tinh chỉnh riêng từng service.** Ba tier cấu hình:

| Tier | `shared_buffers` | `max_connections` | Service |
|---|---|---|---|
| hot | 192 MB | 50 | task, annotation |
| mid | 64 MB | 30 | ledger, quality, collab |
| lean | 32 MB | 20 | 9 service CRUD còn lại |

`annotation_db` ghi nặng nhất được cấp bộ đệm gấp 6 lần `notification_db` — điều không làm được khi dùng chung server.

**Chi phí RAM:**

| Nhóm | Ước tính |
|---|---|
| 14 PostgreSQL | ~1,2 GB |
| 1 ClickHouse (`gate`) | ~0,4 GB |
| 4 Redis | ~40 MB |
| Core (RabbitMQ, MinIO, Jaeger, Mailpit) | ~0,8 GB |
| **Tổng nếu bật hết** | **~2,4 GB** |
| **Chỉ P0** (4 PG + 2 Redis + core) | **~1,2 GB** |

Giảm bằng compose profile theo phase (mục 7): chỉ bật datastore của phase đang làm.

### 3.2. Chọn datastore theo access pattern

Không mặc định Postgres cho tất cả. Mỗi service chọn store hợp với hình dạng truy vấn của nó:

| Service | Store | Cổng host | Vì sao |
|---|---|---|---|
| identity | PostgreSQL | 5401 | Quan hệ, giao dịch |
| project | PostgreSQL (JSONB) | 5402 | `label_schema` mỗi dự án một hình dạng |
| task | PostgreSQL + **Redis riêng** | 5403 / 6380 | `SKIP LOCKED` cho hàng đợi; Redis giữ lease TTL |
| annotation | PostgreSQL (JSONB + GIN) | 5404 | Nhãn **đa hình** theo loại bài toán: cột `payload jsonb` + `task_type` + `schema_version` (định dạng ở `shared/labeling`), GIN `jsonb_path_ops` trên `payload`. Cùng cách lưu cho nhãn ở task-svc và đáp án câu vàng ở project/task |
| ledger | PostgreSQL | 5405 | ACID là toàn bộ lý do service này tồn tại |
| payment | PostgreSQL | 5406 | ACID |
| link | PostgreSQL | 5407 | CRUD quan hệ |
| **gate** | **ClickHouse** + **Redis riêng** + Postgres nhỏ | 8123 / 6381 / 5408 | `click_events` append-only hàng triệu dòng/ngày, truy vấn 100% là aggregate theo thời gian (FS-07). Postgres chỉ cho đường **nguội** (bản sao + outbox cần transaction) — đường nóng không chạm |
| quality | PostgreSQL | 5421 | Ma trận tin cậy, khối lượng nhỏ |
| ml | PostgreSQL + **pgvector** | 5422 | Embedding cho active learning; model artifact để MinIO |
| fraud | PostgreSQL (`WITH RECURSIVE`) | 5423 | Bài toán đồ thị nhưng nông (2–3 hop) |
| collab | PostgreSQL (`bytea`) + **Redis riêng** | 5431 / 6382 | Yjs update là blob append-only; Redis giữ presence |
| media | PostgreSQL (partition theo tháng) | 5409 | Audit append-only, truy vấn theo `sample_id` |
| notification | PostgreSQL | 5410 | CRUD |
| admin | PostgreSQL | 5411 | Setting hệ thống + audit log. Chạy từ P0 (profile `p0`) vì mọi service đọc setting |
| api-gateway | **Redis riêng** | 6379 | Rate limit, Turnstile nonce |

**Nguyên tắc:** thêm một loại database là thêm một bộ kỹ năng, một cách backup, một cách giám sát, một nguồn lỗi. Chỉ đổi khi access pattern thực sự không hợp Postgres — vốn đã cover JSONB + GIN, mảng, partition, full-text, pgvector.

**Ba lựa chọn đã cân nhắc rồi bác bỏ:**

| Cân nhắc | Quyết định | Lý do |
|---|---|---|
| MongoDB cho `annotation` | Không | JSONB cho cả tự do *và* validate theo `label_schema` *và* transaction với `reviews`/`appeals`. Với hệ thống mà chất lượng nhãn là sản phẩm, mất khả năng kiểm chứng là đánh đổi sai |
| Neo4j cho `fraud` | Không | Đồ thị nông; `WITH RECURSIVE` đủ ở quy mô này. Neo4j chỉ đáng khi truy vấn đồ thị là nghiệp vụ chính |
| MongoDB cho `notification` | Không | Không có lực đẩy nào |

**Redis phải tách như Postgres.** Bốn instance riêng cho gateway / task / gate / collab. Dùng chung một Redis thì `gate-svc` `FLUSHDB` nhầm sẽ xóa sạch lease của `task-svc` — đúng loại rò rỉ ranh giới mà kiến trúc này tồn tại để ngăn. Redis idle ~10 MB nên tách gần như miễn phí.

**Hai thứ dùng chung là có chủ ý:**

- **RabbitMQ** — bus tồn tại để nối các service; cách ly bằng vhost + user riêng từng service.
- **MinIO** — mỗi service một bucket + access key giới hạn đúng bucket đó. Cách ly tương đương mà không tốn 15 container.

**Tổng container hạ tầng:** 14 PostgreSQL + 1 ClickHouse + 4 Redis + RabbitMQ + MinIO + Jaeger + Mailpit.

### 3.3. Tiền: sổ cái ghi kép (double-entry)

`ledger-svc` là service duy nhất chạm số dư. **Nguồn sự thật là bảng bút toán** (`journal_entries` + `journal_lines`, số tiền có dấu); `accounts.balance` chỉ là bộ nhớ đệm được cập nhật **trong cùng transaction** và đối soát lại mỗi ngày.

```
Tài khoản:
  business:{id}:available   escrow:project:{id}        ← ký quỹ tách THEO DỰ ÁN
  labeler:{id}:pending      labeler:{id}:available
  platform:fee              platform:tax_withheld
  payout:in_flight                                     ← tiền rút đang trên đường tới ngân hàng
  gateway:{provider}                                   ← đối ứng thế giới ngoài, DUY NHẤT được âm

Nạp tiền           gateway:sandbox        → business:X:available
Publish dự án      business:X:available   → escrow:project:P   (số mẫu × redundancy × (đơn giá + phí))
Duyệt nhãn         escrow:project:P       → labeler:Y:pending  (đơn giá)
                   escrow:project:P       → platform:fee       (phí, VD-M-15)
Hết treo 3–7 ngày  labeler:Y:pending      → labeler:Y:available
Xin rút            labeler:Y:available    → payout:in_flight   (thực nhận)
                   labeler:Y:available    → platform:tax_withheld (10% TNCN nếu ≥ 2 triệu/lần)
Cổng chuyển xong   payout:in_flight       → gateway:sandbox
Cổng thất bại      bút toán ĐẢO — trả labeler toàn bộ, kể cả thuế đã giữ
Hủy / hoàn thành   escrow:project:P       → business:X:available   (toàn bộ số dư còn lại, VD-M-10)
```

**Vì sao ký quỹ tách theo dự án** (khác bản đầu `business:{id}:escrow`): số dư của `escrow:project:P` **chính là** phần chưa dùng của dự án đó — hoàn tiền lấy đúng con số này, không tính lại (VD-M-10); chi trả vượt ký quỹ của một dự án bị chặn ngay, không ăn sang tiền của dự án khác (VD-M-03).

**Các lớp bảo vệ:**

| Rủi ro | Chặn bằng |
|---|---|
| Double-spend (VD-M-01) | Mọi lần ghi sổ xếp hàng sau `pg_advisory_xact_lock`; trừ tiền bằng `UPDATE … WHERE balance + Δ >= 0`, 0 dòng = không đủ tiền |
| Trả trùng (VD-M-02) | `processed_events` **và** `UNIQUE(type, reference)` trên bút toán **và** `UNIQUE(annotation_id)` trên khoản treo |
| Chi vượt redundancy (VD-M-03) | Đếm khoản treo theo `task_id` trong cùng transaction |
| Sửa sổ (VD-M-04) | Trigger chặn `UPDATE/DELETE/TRUNCATE` (kể cả chủ DB) + **chuỗi băm** SHA-256: mỗi bút toán mang hash bút toán trước |
| Số âm ở biên (VD-M-05) | Số tiền người dùng xin luôn > 0; dòng bút toán `CHECK (amount <> 0)`, tổng mỗi bút toán = 0 kiểm ngay khi tạo |
| Gọi cổng trong transaction (VD-M-09) | payment-svc ba pha: ghi ý định → commit → gọi cổng → transaction mới ghi kết quả; lệnh kẹt thì **tra cứu ngược** cổng |

Đối soát (`GET /ledger/admin/reconciliation`, FM-05) kiểm trên một snapshot `REPEATABLE READ`: tổng mỗi bút toán = 0, tổng toàn hệ thống = 0, `balance = SUM(lines)`, không tài khoản nào âm (trừ cổng), chuỗi băm nguyên vẹn.

Mọi API tiền nhận header `Idempotency-Key`. Webhook cổng thanh toán ký HMAC-SHA256, so sánh thời gian hằng số; đến 3 lần thì `UNIQUE(provider, provider_txn_id)` chặn ghi trùng.

### 3.4. Saga: publish dự án

```
project-svc                              ledger-svc
  [Nháp]
    │ project.publish_requested ──────────►  Reserve escrow (idempotent theo projectId)
    │                                          ├─ đủ tiền  → escrow.reserved ────┐
    │                                          └─ thiếu    → escrow.rejected ────┤
    ▼                                                                            │
  [Chờ ký quỹ] ◄─────────────────────────────────────────────────────────────────┘
    │ escrow.reserved  → [Chờ duyệt] ──(admin.project_approved)──► [Đang chạy]
    └ escrow.rejected  → [Nháp] + lý do

Compensation: [Chờ duyệt] quá 72h không ai duyệt → project.cancelled → ledger hoàn escrow.
```

**Danh sách saga đầy đủ:**

| Saga | Chuỗi | Compensation |
|---|---|---|
| Publish dự án | project → ledger → project | Hoàn escrow nếu timeout hoặc admin từ chối |
| Duyệt nhãn | annotation → ledger (hold) → *(3–7 ngày)* → ledger (release) | Tranh chấp thắng → `dispute.resolved` đảo bút toán |
| Hủy / hoàn thành dự án | project (đã **tạm dừng**) → hỏi task + đóng sổ annotation (HTTP nội bộ) → `project.completed` / `cancelled` → task (đóng), ledger (hoàn phần chưa dùng) | Đóng sổ xong mà project lưu lỗi → bấm lại (idempotent) hoặc chạy tiếp (`project.resumed` mở sổ) |
| Rút tiền | ledger (giữ) → payment (gọi ngoài) → ledger (chốt hoặc hoàn) | `payout.failed` → trả về số dư khả dụng |
| Vượt link | gate → annotation (ghi nhãn) + ledger (CPM) | Không cần: at-least-once + consumer idempotent |
| Chốt phiên collab | collab → annotation (1 bản chung) → ledger (chia theo đóng góp) | Leader chỉnh ±20% → bút toán điều chỉnh, ghi log |

### 3.4.1. Đóng dự án: không để labeler làm không công

Hoàn thành hoặc hủy dự án **đã chạy** là lúc ledger trả ký quỹ còn lại về doanh nghiệp. Sau đó nhãn nào được duyệt cũng không còn tiền trả. Vì vậy chỉ đóng được khi **không còn việc nào có thể sinh tiền cho labeler**:

```
POST /projects/{id}/complete | /cancel   (Running → 409 can_tam_dung_truoc: phải Tạm dừng trước)
  1. task-svc        GET  /internal/projects/{id}/close-check
       đã nhận project.paused? còn lượt đang giữ? → bao nhiêu lượt nộp thật (không tính câu vàng)
  2. annotation-svc  POST /internal/projects/{id}/close {submittedCount}
       MỘT transaction, khoá project_terms FOR UPDATE:
         số nhãn chuyên nghiệp == submittedCount   (không nhãn nào đang trên đường)
         0 nhãn chờ duyệt, 0 khiếu nại đang mở
         0 nhãn bị từ chối còn trong hạn khiếu nại  (chống "từ chối hàng loạt rồi đóng ngay")
       đạt → closed_at = now (đóng sổ)
  3. đạt cả hai → Completed / Cancelled → project.completed / project.cancelled {ApprovedAnnotationCount}
  4. ledger: đã chi đủ số nhãn đã duyệt → hoàn ký quỹ còn lại, Closed
             chưa đủ (annotation.approved đi queue khác, còn đang trên đường) → Closing:
               vẫn chi cho nhãn đã duyệt, không chi cổng link; chi đủ thì tự hoàn + Closed
  chưa đạt → 409 chua_the_dong + closeCheck (lý do, số liệu, hạn khiếu nại cuối)
  gọi nội bộ lỗi → 503 (không kiểm được thì không đóng)
```

- **Bước 4 do TLA+ tìm ra** ([NC-B-01](thi-nghiem/nc-b-01-tla.md)): trước đó ledger hoàn ký quỹ ngay khi nhận `project.completed`; nếu `annotation.approved` của nhãn vừa duyệt tới sau, nó gặp ký quỹ đã đóng, vào DLQ, và labeler không được trả.
- **Đóng sổ** chặn mọi thao tác làm đổi tiền sau đó: duyệt, duyệt hàng loạt, từ chối, khiếu nại, phân xử (409 `du_an_da_ket_thuc`). Các thao tác này đọc `project_terms` bằng `FOR SHARE` trong cùng transaction, nên một khiếu nại chen vào lúc đang đóng sổ hoặc đã commit trước (đóng sổ thấy nó và từ chối đóng), hoặc phải chờ và thấy dự án đã đóng.
- Nhãn **nộp tới** khi đã đóng sổ nhưng dự án chưa kết thúc hẳn vẫn được ghi nhận. Doanh nghiệp có thể chạy tiếp sau một lần đóng sổ mà project-svc chưa chốt, và `project.resumed` đi queue khác với `assignment.submitted` (TLA+ tìm ra). Chỉ khi đã nhận `project.completed` / `cancelled` thì nhãn tới sau mới là lỗi (vào DLQ).
- Nhãn từ cổng link không gắn tiền (sharer đã được trả theo lượt), nên không chặn việc đóng.
- **Ngoại lệ có kiểm soát với luật 1 mục 4:** đây là lời gọi HTTP đồng bộ đầu tiên giữa hai service nghiệp vụ. Nó không nằm trên đường nóng (đóng dự án rất hiếm), và hỏi thẳng **chủ dữ liệu** đúng như luật "quyết định tiền bạc không đọc bản sao". Endpoint nằm dưới `/internal` (gateway không định tuyến) và cần header `X-Internal-Key` (`InternalApi:Key`, bộ lọc `[InternalApi]` trong `shared/auth`).

### 3.5. Lease task 15 phút (FL-06)

**Postgres là nguồn sự thật DUY NHẤT của lease** — mọi bước là một transaction có khóa dòng:

```
POST /tasks/projects/{projectId}/next
  1. Điều kiện: đọc bản sao trong task_db (project_snapshots, project_members_cache,
     labeler_cache) — thiếu dữ liệu thì TỪ CHỐI (VD-D-04)
  2. Chọn NGẪU NHIÊN trong N task còn chỗ cũ nhất rồi khoá một dòng   (VD-T-06)
       truy vấn con: N ứng viên đầu hàng theo chỉ mục, KHÔNG khoá (N = task.lease_candidate_window, 32)
       truy vấn ngoài: ORDER BY random() trên ≤ N dòng, LIMIT 1 FOR UPDATE SKIP LOCKED
     cả N dòng đang bị giữ → rơi về ORDER BY t.id LIMIT 1 FOR UPDATE SKIP LOCKED (FIFO)
     không khóa được mà vẫn còn ứng viên → thử lại vài lần, không báo "hết task" oan
  3. INSERT assignment(state=Leased, expires_at = now + 15m) + tăng active_lease_count
     UNIQUE (project, labeler) WHERE Leased: mỗi người giữ tối đa 1 task/dự án

POST /tasks/assignments/{id}/submit
  SELECT ... FOR UPDATE assignment → Leased và chưa hết hạn? → Submitted   (VD-T-01)
  → phát assignment.submitted (annotation-svc lưu nhãn)

Reaper (30s/lần): Leased quá hạn → Expired, trả chỗ về pool (SKIP LOCKED, chạy nhiều bản được)
```

**Đo dưới tải ([NC-B-06](thi-nghiem/nc-b-06-hieu-nang.md#2-phân-phối-task)):** 46.804 lượt nộp tới 400 labeler đồng thời, 0 cấp trùng, 0 vượt redundancy. Bản đầu dùng `ORDER BY random()`: Postgres đọc và sắp xếp mọi task ứng viên mỗi lần lấy (82 ms ở dự án 20.000 mẫu). Đổi sang `ORDER BY t.id` và thêm chỉ mục `ix_assignments_labeler_active (labeler_id, task_id)` cho `NOT EXISTS` (trước đó quét tuần tự cả bảng assignments) thì thông lượng tăng khoảng 2 lần, đạt ~120 lượt/giây trên một instance. Thuần FIFO thì labeler bấm cùng lúc dễ rơi vào cùng task (dễ thông đồng), nên bước 2 chọn ngẫu nhiên trong cửa sổ N task đầu hàng: chi phí bị chặn theo N chứ không theo cỡ dự án. **Không** dùng `OFFSET` ngẫu nhiên, vì Postgres khoá cả các dòng bị OFFSET bỏ qua. Đo: 4 labeler bấm liên tiếp, FIFO cho 2 task khác nhau, cửa sổ 32 cho 3–4 task; thông lượng ngang FIFO ở 10–100 labeler.

**Vì sao không dùng Redis cho lease (khác bản thiết kế đầu):** câu UPDATE có điều kiện trên dòng đã khóa đã nguyên tử sẵn. Thêm Redis tạo **hai** nguồn sự thật phải đồng bộ — VD-T-02 chính là lỗi hai nguồn đó lệch nhau. `redis-task` để dành cho cache eligibility khi hàng đợi lên hàng triệu task (VD-T-07, P2).

### 3.6. Cổng vượt link: đường nóng phải rỗng

**Đã có (P2).** `link-svc` giữ link của người chia sẻ; `gate-svc` phục vụ trang vượt link cho khách vãng lai. Các lựa chọn đã chốt:

| # | Câu hỏi | Chọn | Lý do |
|---|---|---|---|
| G1 | Câu hỏi lấy từ đâu | **Bản sao riêng trong gate** (nghe `project.published`, `dataset.ingested`, `gold_set.updated`), nạp vào **bộ nhớ** (`GateCatalog`) | Đường nóng không gọi HTTP sang task-svc, không khoá dòng Postgres, khách bỏ ngang không để lại lease treo |
| G2 | Nhãn của khách đi đâu | `gate.solved` → annotation-svc, nguồn `linkGateway`, **không** thuộc task, **không** tính vào redundancy / đồng thuận | Kênh nhãn rẻ, luôn qua câu vàng; doanh nghiệp vẫn duyệt / loại để chọn lọc dữ liệu |
| G3 | Dự án nào lên cổng | Chỉ dự án **bật kênh cổng link**, dữ liệu ảnh / văn bản / cặp, mọi công cụ là **phân loại chọn một** hoặc **so sánh cặp**, có câu vàng **QualityCheck** | Khách có ~10 giây và không được huấn luyện (VD-L-08). Câu vàng của bài test đầu vào không đưa lên trang công khai |
| G4 | Trả người chia sẻ khi nào | **Khi qua câu vàng**, tiền **treo** như labeler. Sharer = số nhãn thật × (đơn giá − phí/nhãn); ký quỹ trừ số nhãn × (đơn giá + phí) — đúng công thức đặc tả 2.4 | Không phải chờ ai duyệt; treo để kịp giữ lại khi link vi phạm |
| G5 | Ngân sách cổng (VD-L-03) | Cổng chỉ tiêu phần ký quỹ **vượt** mức tối thiểu cho labeler (số mẫu × trần redundancy × (đơn giá + phí)). Ledger phát `gate.budget_changed` sau mỗi lượt | Cổng link **không bao giờ** ăn vào tiền đã dành cho labeler chuyên nghiệp. Doanh nghiệp muốn chạy cổng thì đặt ngân sách cao hơn mức tối thiểu |
| G6 | Datastore | Redis (phiên, jti, chống trùng IP, Redis Stream) + ClickHouse (`click_events`) + **Postgres nhỏ** cho bản sao và outbox | Outbox và chống xử lý trùng cần transaction — Redis / ClickHouse không có. Postgres chỉ ở đường nguội |

```
Bước 0  Turnstile (siteverify phía server, fail-closed)  → bot rẻ tiền chết ở đây, chưa tốn câu nào
Bước 1  GET  /g/{code}                → cần mật khẩu? đếm ngược bao lâu?  (bộ nhớ, không DB)
Bước 2  POST /g/{code}/sessions       → k câu vàng + m câu thật TRỘN LẪN, lưu phiên vào Redis
          (setting gate.gold_per_session / gate.real_per_session; mẫu đủ gate.max_labels_per_sample thì thôi phát)
Bước 3  POST /g/sessions/{id}/submit  → server kiểm đếm ngược; kiểm định dạng nhãn (Crowd.Labeling);
          DEL phiên (một request thắng); chấm CHỈ câu vàng
          → trượt: bộ câu mới, không token
          → đạt : phân loại lượt  tự vượt (token chủ link / cùng IP lúc tạo link) → không tiền
                                  trùng IP trong 24 giờ (SET NX)                → không tiền
                                  hết ngân sách cổng                            → không tiền
                                  còn lại                                       → tính tiền
                  XADD gate:events {nhãn, lượt tính tiền, dòng thống kê}       (Redis Stream)
                  SET jti EX 180 → JWT HS256 {jti, lnk, aud, exp}
Bước 4  GET  /go/{code}?t=JWT → kiểm chữ ký + đúng link → GETDEL jti (dùng một lần) → 302 link đích

Relay (worker, theo lô): Redis Stream → outbox (gate.solved, click.validated) COMMIT → ClickHouse → ACK
   chết giữa chừng: lần sau đọc lại mục chưa ACK; trùng bị chặn ở bên nhận
   (ledger theo ClickId, annotation theo id tất định (phiên, mẫu), ClickHouse ReplacingMergeTree theo click_id)
```

Doanh thu cộng bởi consumer của ledger, không nằm trong request. Đúng như đặc tả: bot không giải được câu vàng thì không sinh lượt hợp lệ, nên doanh thu tự về 0.

**Ledger chi theo lượt hay theo lô (VD-M-08).** Setting `ledger.gate_payout_mode`:

- `perClick` (mặc định): mỗi `click.validated` là một bút toán dưới khoá sổ cái toàn cục, tiền treo vào ví ngay.
- `batched`: consumer chỉ INSERT một dòng `gate_clicks` (khoá chính `click_id`, chống trùng). Cứ `ledger.gate_batch_interval` (60 giây), `GateBatchWorker` gộp tối đa `ledger.gate_batch_size` lượt: mỗi dự án **một** bút toán `clickbatch:{lô}:{dự án}`, mỗi cặp (người chia sẻ, link) một khoản treo. Lượt vượt ngân sách bị từ chối, giống chế độ theo lượt.

Đo ([NC-B-06](thi-nghiem/nc-b-06-hieu-nang.md#1-chi-tiền-cổng-link-perclick-so-với-batched)): perClick dừng ở ~30 lượt/giây và **không tăng khi thêm instance**. batched đạt ~70/s trên một instance và ~170/s với ba instance, đổi lại độ trễ tiền vào ví bằng khoảng nửa chu kỳ.

**link-svc:**
- Link mới ở `pendingScan`. Worker quét (Google Safe Browsing khi có khoá; dev dùng danh sách tên miền giả lập) rồi `active` + `link.activated`, hoặc `blocked`.
- Tên miền bị chặn (admin) chặn cả tên miền con; chặn thêm thì link đang chạy tới tên miền đó bị vô hiệu hoá ngay.
- Báo cáo từ trang vượt link: mỗi IP một lần, vượt `link.report_review_threshold` thì vào hàng đợi kiểm duyệt. Admin vô hiệu hoá kèm **giữ doanh thu** → ledger chuyển khoản đang treo của link sang `platform:withheld`.
- API key (FS-05) và Quick Link (FS-02) cho công cụ tự động. Giới thiệu (FS-08): tài khoản mới nhập mã trong `link.referral_claim_window`; ledger trả 10% từ **phần nền tảng** khi người được mời đã tự kiếm ≥ `link.referral_min_earnings_vnd` (VD-L-05).

IP không lưu thô: `sha256(muối | ip)` (muối `Privacy:IpSalt` dùng chung giữa link và gate để so "người vượt có phải người tạo link"); bản chống trùng theo ngày còn trộn thêm ngày.

### 3.7. Redundancy thích ứng — vòng lặp event giữa hai service

Đây là chỗ ranh giới service lộ ra rõ nhất: `task-svc` **sở hữu** số redundancy, `quality-svc` **quyết định** thay đổi nó.

```
annotation-svc ──annotation.submitted───► quality-svc
task-svc      ──task.redundancy_reached─►    │ đủ n bản? (hai event khác queue, cái nào tới trước cũng được)
                                             ├─ khớp / hết trần → consensus.reached ──► annotation-svc (cờ khớp/lệch — GỢI Ý)
                                             └─ tranh chấp, còn trần → redundancy.increase_requested
task-svc ◄───────────────────────────────────────┘   (+1 người, ≤ MaxRedundancy, mở lại task)
task-svc ──gold.answered─► quality-svc ──reputation.changed─► task-svc (lọc minReputation)
```

Các quyết định đã chốt khi làm P3:

| # | Câu hỏi | Chọn | Lý do |
|---|---|---|---|
| P3-1 | Ngôn ngữ | **Python + crowd-kit** (FastAPI, aio-pika, SQLAlchemy) | Dawid–Skene, Krippendorff alpha, (sau này) GLAD/MACE/ROVER/Bradley–Terry có sẵn. Envelope, outbox, consumer quorum + DLQ, `processed_events` viết lại cho khớp từng chi tiết với bản C# |
| P3-2 | Câu vàng kiểm tra | **task-svc trộn vào luồng task và tự chấm** (Crowd.Labeling), không trả tiền | Không nhân đôi luật chấm (IoU, CER…) sang Python. Response giống hệt task thật; chỉ trộn khi labeler còn task thật; mỗi câu một lần mỗi người (VD-Q-03) |
| P3-3 | Đồng thuận dùng làm gì | **Gợi ý, vẫn duyệt tay** + nút duyệt hàng loạt nhãn khớp | Tiền chỉ chi khi có người chịu trách nhiệm bấm — tránh nhiều tài khoản cùng nhãn sai tự rút tiền (VD-Q-02) |
| P3-4 | Redundancy thích ứng | **Có, ký quỹ tính theo trần** `maxRedundancy` | Lượt thêm luôn có tiền; ledger chặn chi theo trần mỗi task (VD-M-03); phần không dùng tự hoàn khi kết thúc. Trần chặn vòng lặp vô hạn (VD-Q-08) |
| P3-5 | Ai giữ điểm uy tín | **quality-svc** phát `reputation.changed` | identity-svc chưa lưu điểm này; quality có đủ bằng chứng (câu vàng + đồng thuận + DS) |

**Khi nào dừng mua thêm nhãn** (setting `quality.redundancy_policy`, hàm thuần trong `services/quality/app/redundancy.py`):

| Chính sách | Quyết định khi task đủ người |
|---|---|
| `majority` (mặc định) | Quy tắc P3: có đáp án quá bán thì chốt, không thì xin thêm một người tới trần |
| `posterior` | Tính hậu nghiệm từng đáp án, **có trọng số theo độ chính xác từng labeler** (mô hình một đồng xu; độ chính xác lấy từ Dawid–Skene nếu có, không thì từ câu vàng làm mượt). Chốt khi đáp án dẫn đầu ≥ `quality.posterior_target` (0,95) |
| `voi` | Dừng tối ưu: chỉ mua thêm nếu tồn tại m nhãn nữa (m ≤ 6 và ≤ phần còn lại tới trần) mà giá trị thông tin kỳ vọng × `quality.voi_value_ratio` vượt chi phí m nhãn |

Hai chính sách mới áp cho công cụ chọn một (phân loại một lớp, so sánh cặp); công cụ khác trong cùng tập nhãn vẫn gộp theo mục 3.15. Chạm trần mà chưa đủ tin cậy thì task là **tranh chấp**, để người duyệt xử lý. Thí nghiệm trên 5 bộ dữ liệu công khai và mô phỏng ([NC-D-01](thi-nghiem/nc-d-01-redundancy-thich-ung.md)): để đạt cùng độ chính xác với cố định n nhãn, `posterior` / `voi` cần ít hơn 29–76% nhãn. Mặc định vẫn là `majority`, vì khi mọi labeler đều mới (chưa có bằng chứng về độ chính xác) thì `posterior` đắt hơn.

**Đánh giá một task được xếp hàng theo task** (`pg_advisory_xact_lock`): hai nhãn cuối của cùng task xử lý song song thì mỗi transaction chỉ thấy nhãn của mình, cả hai đếm thiếu và task kẹt vĩnh viễn không có đồng thuận. Lỗi này lộ ra khi chạy E2E tổng thể. Sau khi sửa, khi nhiều task được quyết định song song lại lộ thêm **deadlock lúc cập nhật uy tín**: hai task của cùng hai labeler khoá dòng `reputations` theo thứ tự ngược nhau, giao lại vẫn đụng nhau, đủ 5 lần thì vào DLQ. Sửa bằng cách cập nhật theo `labeler_id` tăng dần. Phép thử ép race (`tests/e2e/ep_race_quality.py`, 40 task mỗi lần, hai người nộp đồng thời) cho 120/120 task có kết quả qua 3 lần chạy, và nằm trong bộ hồi quy của CI.

Đồng thuận tính trên **mọi công cụ** của tập nhãn trừ `freeText` (câu trả lời mở): phân loại và so sánh cặp theo đa số tuyệt đối; khung, đa giác, điểm mốc, đường, mặt nạ, đoạn thời gian, chuỗi khung theo ghép cụm; đoạn văn bản và quan hệ theo phần tử trùng hệt; bản chép, số, xếp hạng theo bản ở giữa / trung vị / Borda — xem mục 3.15. Tập nhãn chỉ có `freeText` thì task ở trạng thái `notApplicable`.

Dawid–Skene / Krippendorff alpha chạy **batch** (mặc định 15 phút, admin chạy tay được) trên các công cụ một giá trị (phân loại một lớp, so sánh cặp); labeler cần ≥ 5 nhãn trong dự án mới được chấm DS (VD-Q-07). Không chạy trong request. Mỗi lô **chỉ tính lại dự án có nhãn mới** (bảng `ds_state` giữ số nhãn đã dùng; `labels` chỉ INSERT nên số khác ⇔ có nhãn mới, kể cả nhãn đến trễ). Tính lại mọi dự án tốn ~1 giây / dự án (EM của crowd-kit): trên DB dev 107 dự án, lô đầy đủ mất 102 giây và nút "chạy ngay" của admin quá timeout gateway; lô tăng dần mất 0,04 giây. `?full=true` vẫn tính lại tất cả (sau khi đổi `quality.ds_min_labels`).

Dự án publish **trước khi** quality-svc chạy (hoặc mất `project.published`): lần đầu cần bản sao, quality-svc tự hỏi project-svc `GET /internal/projects/{id}/published-terms` (khoá nội bộ, cấu hình `QUALITY_PROJECT_URL` / `QUALITY_INTERNAL_API_KEY`) rồi ghi bản sao; project-svc không trả lời được thì consumer thử lại (không mất nhãn). Chưa cấu hình thì bỏ qua kèm cảnh báo như trước.

### 3.8. Phân quyền hai chiều — chống BOLA

RBAC (FC-07) chỉ giải quyết **một nửa** bài toán:

```
CHIỀU DỌC  — "được làm LOẠI việc này?"        CHIỀU NGANG — "được đụng BẢN GHI này?"
   Labeler  → gán nhãn            ✅              Labeler A xem nhãn của mình   ✅
   Admin    → duyệt lệnh rút      ✅              Labeler A xem nhãn của B      ❌
   ↑ RBAC làm được                               ↑ RBAC KHÔNG diễn đạt được
```

Lấy đúng `userId` từ JWT `sub` là **cần nhưng không đủ** — nó xác định *bạn là ai*, không trả lời *bạn có quyền trên bản ghi này không*. Thiếu chiều ngang là lỗ hổng **BOLA**, hạng 1 trong OWASP API Security Top 10 (API1:2023). Xem `VD-S-14`.

Năm yêu cầu trong đặc tả **không thể** thỏa bằng RBAC thuần: FP-05 (private pool), FB-23 (chặn labeler khỏi dự án), FL-04 (chỉ thấy task của mình), FB-21 (chỉ duyệt nhãn dự án mình), FP-06 (audit ai xem mẫu nào).

**Một bảng giải cả năm:**

```sql
-- project_db, thuộc sở hữu của project-svc
CREATE TABLE project_members (
    project_id UUID NOT NULL,     -- UUIDv7, cung kieu voi userId trong JWT va voi moi id trong event
    user_id    UUID NOT NULL,
    role       TEXT   NOT NULL,   -- 'owner' | 'labeler' | 'reviewer'
    state      TEXT   NOT NULL DEFAULT 'active',   -- 'active' | 'blocked'
    joined_at  TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (project_id, user_id)
);
CREATE INDEX idx_pm_user ON project_members (user_id, state);
```

Ai được thêm vào bảng này, bằng đường nào (mời, mã mời, đơn xin tham gia, bài test) và theo điều kiện gì: xem mục 3.16.

**Nhân bản sang các service thực thi** (theo luật 2, mục 4):

```
project_db.project_members            ← nguồn sự thật
   │ 📤 member.added | member.blocked | member.removed
   ├─► task_db.project_members_cache
   ├─► annotation_db.project_members_cache
   └─► media_db.project_members_cache
```

**Luật quan trọng nhất — một predicate dùng chung cho hai mục đích:**

```csharp
// trong shared/Platform.BuildingBlocks, áp lên bản sao cục bộ của từng service
private Expression<Func<Project, bool>> MemberOf(Guid userId) =>
    p => _db.ProjectMembersCache.Any(m =>
             m.ProjectId == p.Id && m.UserId == userId && m.State == "active");

Task<bool> CanAccessAsync(Guid userId, Guid labelId, CancellationToken ct);  // kiểm 1 bản ghi
IQueryable<Label> VisibleLabels(Guid userId);                                // lọc danh sách
//          ↑ cả hai PHẢI dựng từ cùng một predicate
```

Viết hai bản luật riêng thì chúng sẽ lệch nhau: danh sách hiện ra bản ghi mà bấm vào báo 403 — hoặc tệ hơn, chiều ngược lại. Đây là lỗi rất khó bắt bằng test vì mỗi bên nhìn riêng đều đúng.

### 3.9. Đa loại dữ liệu: tập nhãn là danh sách công cụ

Một nền tảng gán nhãn phải nhận được ảnh, văn bản, âm thanh, video, cặp câu trả lời (RLHF)… và mỗi loại có nhiều kiểu nhãn (phân loại, khung, đa giác, đoạn văn bản, chép lời, đoạn thời gian, so sánh cặp). Thiết kế chọn để **thêm kiểu nhãn mới không phải đổi bảng, không phải đổi hợp đồng event**:

| # | Câu hỏi | Chọn | Lý do |
|---|---|---|---|
| Q1 | Mô hình hóa "loại bài toán" | **Loại dữ liệu × công cụ**: dự án có một `modality`; tập nhãn là danh sách công cụ (ban đầu 7 loại: `classification`, `bbox`, `polygon`, `span`, `transcription`, `temporalSegment`, `pairwise`; nay 15 loại, mục 3.15) | Một màn hình thường cần nhiều công cụ cùng lúc (phân loại cảm xúc + gắn thực thể). Liệt kê từng "task type" ghép sẵn sẽ bùng nổ tổ hợp |
| Q2 | Kiểm nhãn ở đâu | **JSON Schema cho hình dạng** ([contracts/labeling](../contracts/labeling)) + **code C# cho ngữ nghĩa** (lớp có trong tập nhãn, khung nằm trong ảnh, đoạn nằm trong audio) | Schema dùng chung được cho frontend; luật phụ thuộc mẫu thì schema không diễn đạt được |
| Q3 | Nạp file lớn | **Upload thẳng lên MinIO bằng link ký sẵn + manifest + worker nền** (ffprobe đo thời lượng) | Video vài GB không đi qua service. ZIP trong request chỉ giữ cho ảnh nhỏ |
| Q4 | Lưu mẫu | `samples` thêm `modality`, `content jsonb` (text/pair), `metadata jsonb`; `storage_key` nullable; CHECK đúng một trong hai | Văn bản không cần file; metadata (kích thước, thời lượng, đoạn) là thứ server cần để kiểm nhãn |
| Q5 | Bản sao ở task / annotation | `label_schema jsonb` chép nguyên từ `project.published` | Kiểm nhãn lúc nộp và gộp kết quả không phải gọi HTTP sang project-svc (luật 1 mục 4) |
| Q6 | Media dài | **Cắt đoạn** theo `segmentSeconds`, mỗi đoạn một task, giá theo task | Labeler không phải nghe 1 giờ cho một task; tiền và redundancy giữ nguyên mô hình cũ |
| Q7 | Chấm câu vàng / gộp kết quả | Chấm trong C# theo từng công cụ: tập lớp bằng nhau, bbox IoU ≥ 0,5, polygon IoU, span F1, CER ≤ 0,1, IoU thời gian; ngưỡng đổi bằng `matchThreshold`. Gộp: mọi công cụ trừ `freeText`, thuật toán ở mục 3.15 | Chấm và gộp dùng **chung một phép đo độ giống** cho từng công cụ, nên "khớp đáp án" và "hai người vẽ cùng một vật" là một định nghĩa |

```
project.published { modality, labelSchema }          ← tập nhãn dạng chuẩn
        │
        ├─► task-svc: project_snapshots.label_schema  → kiểm nhãn lúc nộp
        └─► annotation-svc: project_terms.label_schema → gộp kết quả, xuất COCO

assignments / annotations / gold_items:  task_type = modality,
  payload jsonb = {"<tên công cụ>": <kết quả theo kind>, ...}
```

Đánh đổi: nhãn có hình dạng do dữ liệu quyết định nên database không ràng buộc được bên trong `payload`. Bù lại, **mọi đường ghi nhãn** (nộp, câu vàng, bài test, seed) đều đi qua một cửa `LabelPayload.Tao(tậpNhãn, dữLiệu, metadataMẫu)`, và đọc lại từ DB dùng `TuLuuTru` không kiểm lại (tập nhãn không đổi sau publish).

### 3.10. Setting động: admin-svc là chủ, mọi service giữ bản sao

**Bài toán.** Phí nền tảng 30 %, có tự duyệt dự án / rút tiền / nạp tiền / nhãn khớp đồng thuận hay không, hạn mức nạp – rút, thời hạn lease, hạn khiếu nại, kích thước file, chu kỳ worker… trước đây nằm rải rác trong hằng số C# và appsettings của 8 service. Đổi một con số là sửa code hoặc sửa file rồi khởi động lại. Yêu cầu: **tất cả lưu trong bảng setting, admin quản lý, đổi lúc đang chạy**.

| # | Câu hỏi | Chọn | Lý do |
|---|---|---|---|
| S1 | Lưu ở đâu | **Một bảng chung `settings` trong `admin_db`**, mỗi dòng một khóa (`key`, `value jsonb`, `version`, `updated_at`, `updated_by`) + `setting_history` (cũ → mới, ai, lý do) | Một chỗ cho admin xem và sửa; lịch sử để truy "ai hạ phí lúc nào" |
| S2 | Kiểu, mặc định, giới hạn khai ở đâu | **Catalog trong code** (`Crowd.BuildingBlocks.Settings.SettingCatalog`, 155 khóa): kiểu (`bool/int/long/double/durationSeconds/text`), mặc định, min/max, danh sách lựa chọn (`choices`, cho khoá text dạng enum), đơn vị, nhóm, mô tả, hiệu lực (`newOperations` / `restart`) | Kiểm giá trị trước khi lưu (phí 95 % → 400). Thêm khóa = thêm một dòng code + test, không cần migration. Python đọc bản xuất `shared/settings/catalog.json` (test C# canh file luôn khớp) |
| S3 | Service đọc thế nào | **Bản sao `settings_replica` trong DB của chính mỗi service** + bộ nhớ (`SettingsStore`). Nhận `setting.changed`; lúc khởi động nạp bản sao rồi xin `settings.snapshot_requested`, admin-svc phát `settings.snapshot` (cả lúc khởi động và định kỳ) | Đúng luật 1–2 mục 4: không gọi HTTP sang admin-svc trong đường nóng; admin-svc chết thì mọi service vẫn chạy với giá trị đã có. Mỗi khóa có `version` riêng — event đến sai thứ tự không ghi đè giá trị mới |
| S4 | Đổi có hiệu lực khi nào | **Chỉ cho thao tác mới.** Chu kỳ worker (giải phóng tiền treo, reaper, quét link…) đọc lại mỗi giây qua `ChoTheoSetting`, nên rút ngắn chu kỳ có hiệu lực ngay, không phải chờ hết chu kỳ cũ. Giá trị đọc lại mỗi lần dùng; thứ đã chốt thì giữ: dự án đã publish giữ phí lúc publish, lease đang chạy giữ hạn cũ, lệnh rút đã tạo giữ thuế đã tính | Không ai bị đổi luật giữa chừng. `consumers.prefetch_count` là ngoại lệ (`restart`) vì chỉ đặt được lúc mở kênh |
| S5 | Domain có đọc setting không | **Không.** Tầng Api đọc setting, dựng **đối tượng quy định** (`QuyDinhDuAn`, `QuyDinhDuLieu`, `QuyDinhBaiTest`, `QuyDinhDuyetNhan`, `QuyDinhRut`, `QuyDinhNap`, `NguongKhop`) truyền vào domain | Domain vẫn thuần, test không cần hạ tầng. Độ rộng cột DB (`CotDb.*`) vẫn là hằng số của schema — setting độ dài bị chặn trần ở đó |
| S6 | Tiến trình không có DB | Gateway giữ setting **trong bộ nhớ**, nghe qua một queue tạm (tự xóa khi tắt) | Gateway chỉ cần trần upload; không đáng một database |

```
admin  PUT /admin/settings/{key} {value, reason}
   │      kiểm theo catalog + ràng buộc cặp (min ≤ max) → settings (version+1) + setting_history
   │      outbox ──► setting.changed {key, value, settingVersion}
   ▼
 project / task / annotation / ledger / payment / identity / quality
   settings_replica (ghi nếu version mới hơn) → SettingsStore (bộ nhớ) → đọc mỗi thao tác
 gateway: SettingsStore trong bộ nhớ (queue tạm)

khởi động service: nạp settings_replica → phát settings.snapshot_requested
admin-svc: nhận → phát settings.snapshot (toàn bộ); cũng phát lúc khởi động và mỗi settings.snapshot_interval
```

**Bốn luồng tự duyệt** đều đi qua **đúng cửa** của luồng duyệt tay, chỉ khác người bấm là hệ thống:

| Setting | Mặc định | Bật thì |
|---|---|---|
| `project.auto_approve` | tắt | Consumer `escrow.reserved` duyệt luôn dự án (Chờ duyệt → Đang chạy), phát `project.published` |
| `ledger.withdraw_auto_approve` + `ledger.withdraw_auto_approve_max_vnd` | bật, ≤ 2.000.000đ | Lệnh rút đi thẳng `payout.requested`. Vượt ngưỡng hoặc tắt → **`PendingApproval`**: tiền đã giữ (trừ khả dụng), admin duyệt (`payout.requested`) hoặc từ chối (bút toán đảo, tiền về ví) |
| `payment.manual_transfer_auto_approve_max_vnd` | 0 (luôn chờ admin) | Nạp bằng **chuyển khoản thủ công**: doanh nghiệp bấm "đã chuyển" → số tiền ≤ ngưỡng thì xác nhận luôn, không thì admin đối chiếu sao kê rồi duyệt (`deposit.confirmed`). Mặc định 0 vì tự duyệt tiền chưa thấy về là rủi ro |
| `annotation.auto_approve_agreed` | tắt | Consumer `consensus.reached` duyệt luôn các nhãn **chờ duyệt khớp đồng thuận** (task `agreed`), `reviewer_id` để trống, lịch sử `auto_approved`, phát `annotation.approved` |

**Cố ý KHÔNG đưa vào setting:** chuỗi kết nối, host/tài khoản RabbitMQ – MinIO, khóa bí mật, cổng, đường dẫn ffprobe (hạ tầng); `x-delivery-limit` (đổi phải xóa queue); `Saga:BoQuaKyQuy` (cờ dev — để trong bảng thì ai có quyền admin bật được "publish không cần tiền"); `ClockSkew` của JWT (bảo mật); độ rộng cột DB; giới hạn trong JSON Schema hợp đồng nhãn; dữ liệu kịch bản seed.

---

### 3.11. KYB, khoá tài khoản và thông báo

| # | Quyết định | Chọn | Lý do / đánh đổi |
|---|---|---|---|
| K1 | KYB nằm ở đâu | **identity-svc** (bảng `business_profiles`, 1–1 với user) | identity là chủ của "ai là ai, vai trò gì". Màn hình admin vẫn gọi API duyệt của identity (`/kyb/admin`); hàng đợi duyệt chung của admin-svc (3.17) chỉ là read model dựng từ `business.kyb_submitted` / `business.verified` / `business.kyb_rejected` |
| K2 | Chặn cái gì | **Chỉ publish** | Doanh nghiệp vẫn dựng dự án, nạp dữ liệu, nạp tiền trong lúc chờ duyệt — không chặn onboarding. Tiền chỉ rời ví khi publish (ký quỹ), nên chặn đúng chỗ rủi ro |
| K3 | project-svc biết "đã xác minh" thế nào | **Claim `kyb_verified` trong access token** (identity ghi lúc cấp token) — project-svc đọc claim của chính người gọi khi publish | Không gọi đồng bộ identity lúc publish (luật dữ liệu chéo, mục 4) **và** không cần bảng bản sao: `verified` là trạng thái cuối, không thu hồi, lại là sự thật về chính người đang gọi. Trước đây là bản sao `verified_businesses` (mỗi doanh nghiệp một dòng ở project-svc) — đã bỏ, xem mục 3.14 (L8). Đánh đổi: client phải **làm mới token** sau khi được duyệt (token cũ còn mang `false` tới khi hết hạn 15 phút). Admin publish hộ không kiểm claim |
| K4 | Giấy phép | Link ký `PUT` thẳng lên bucket riêng `kyb-docs` (không công khai); nộp thì kiểm file **thật sự tồn tại** + kích thước thật; admin xem bằng link ký 5 phút | Không đi qua service (file lớn). Xin link mà không tải lên → nộp 409 `thieu_giay_phep` |
| K4b | Phiên đăng nhập | Bảng `login_sessions`: **một dòng mỗi phiên** (một thiết bị / trình duyệt), giữ SHA-256 của refresh token hiện tại; làm mới = xoay tại chỗ (`generation + 1`, gia hạn trượt). Refresh token = `base64url(mã phiên)` + `.` + 32 byte ngẫu nhiên → tìm phiên bằng khoá chính, `SELECT … FOR UPDATE`. Hash khác token hiện tại = token **cũ** của phiên bị đem ra dùng → thu hồi cả phiên. `RefreshTokenCleanupWorker` mỗi `identity.refresh_token_cleanup_interval` (2 phút) xoá phiên hết hạn / đã thu hồi quá 1 giờ, lô 1.000 `FOR UPDATE SKIP LOCKED` | Trước đây mỗi lần làm mới **thêm một dòng** (giữ token cũ để phát hiện dùng lại): đo trên dev 32.564 dòng, 86 % là token đã xoay. Mã phiên nằm trong token nên vẫn phát hiện dùng lại mà không cần giữ token cũ. Đánh đổi: không còn biết token cũ thuộc "thế hệ" nào (chỉ cần biết nó không phải token hiện tại) |
| K5 | Khoá tài khoản | Đổi trạng thái + **thu hồi mọi refresh token** trong cùng transaction + outbox `user.blocked`; task-svc nghe event và chặn nhận task | Kèm mốc thu hồi phiên (mục 3.12, B1): gateway chặn access token đang sống ngay, không phải chờ hết 15 phút. task / ledger nghe `user.blocked` / `user.unblocked` để chặn / mở nhận task và rút tiền. Không khoá được admin (409) |
| K6 | Gửi email ở đâu | Consumer **chỉ ghi dòng** `notifications` (email `Pending`); `EmailWorker` gửi theo lô `FOR UPDATE SKIP LOCKED`, lỗi thì backoff 10 s × 2ⁿ (≤ 30 phút), tối đa `notification.email_max_attempts` lần | SMTP chậm / sập không làm nghẽn consumer, không đẩy message vào DLQ; chạy nhiều instance không gửi trùng. Thông báo trong ứng dụng luôn có, kể cả khi email hỏng |
| K7 | Biết gửi cho ai | **Bản sao** địa chỉ (`user.registered`), chủ dự án (`project.publish_requested`), chủ lệnh rút (`payout.requested`) | Event tiền / dự án không mang email hay chủ sở hữu; notification không gọi ngược service khác |
| K8 | Tuỳ chọn email | Theo **loại**; 3 loại **bắt buộc** (KYB, tài khoản, rút tiền — ảnh hưởng tài khoản hoặc tiền đã rời ví) không tắt được | Loại bán nhiều (`nhan_duyet`, `tien_kha_dung`) mặc định tắt email |
| K9 | Loại bán nhiều | **Gộp**: sự kiện mới cộng dồn vào dòng cùng loại (và cùng đường dẫn) còn chưa đọc, chưa gửi email, sự kiện gần nhất trong 24 giờ; khoá dòng `FOR UPDATE` | Với sharer, **mỗi lượt click** sinh một khoản treo → không gộp thì nghìn thông báo / ngày. Khoá dòng: hai instance không cộng mất nhau; `EmailWorker` đang gửi dòng đó thì consumer chờ, Postgres kiểm lại điều kiện → dòng đã gửi không còn khớp → mở dòng mới |
| K10 | Đẩy hay hỏi | **Đẩy** thông báo mới qua WebSocket (vé dùng một lần, `pg_notify` sau commit — mục 3.17); frontend vẫn hỏi `unread-count` thưa làm dự phòng | Ban đầu chỉ hỏi mỗi 30 giây (truy vấn rẻ nhờ index một phần `read_at IS NULL`); đợt 10/2026 thêm WebSocket. Chưa có push lên thiết bị (Firebase / Web Push) |

### 3.12. Tài khoản, tranh chấp, chống gian lận, audit, bảo mật phiên

**Tài khoản (identity-svc).**

| # | Quyết định | Chọn | Lý do / đánh đổi |
|---|---|---|---|
| T1 | Mã xác thực email / đặt lại mật khẩu | Bảng `account_tokens` (chỉ lưu **SHA-256** của mã, mục đích, hạn, `used_at`); hạn `identity.email_verify_ttl` (1 ngày) / `identity.password_reset_ttl` (30 phút); link `{identity.frontend_url}/xac-thuc-email?token=` | Lộ DB không lộ mã dùng được. Dùng một lần. Gửi lại email xác thực tối thiểu cách 60 giây (409 `gui_qua_nhanh`). Quên mật khẩu **luôn 202** — không cho dò email nào đã đăng ký |
| T2 | Chưa xác thực email thì chặn gì | **Chỉ rút tiền (ledger) và publish dự án (project)** — 409 `chua_xac_thuc_email` | Như KYB (K2): không chặn onboarding, chỉ chặn chỗ tiền rời hệ thống. ledger / project đọc claim **`email_verified`** trong access token của người gọi (trước đây giữ bản sao `email_status` — đã bỏ, mục 3.14 L8). Client làm mới token sau khi xác thực email. Tài khoản có trước tính năng được migration coi là đã xác thực |
| T3 | Đổi / đặt lại mật khẩu | Thu hồi **mọi phiên** (refresh token + mốc thu hồi access token, xem B1). Đổi mật khẩu trả cặp token mới cho chính thiết bị đang dùng; đặt lại mật khẩu qua email cũng xác thực luôn email | Chiếm tài khoản rồi giữ phiên cũ là kịch bản kinh điển |
| T4 | Ảnh đại diện | Bucket riêng `avatars`, link ký `PUT` (≤ `identity.avatar_max_bytes`), xác nhận thì kiểm object có thật + kích thước + loại ảnh | Giống KYB (K4): file không đi qua service |
| T5 | Kỹ năng (FL-01) | Ba mảng `text[]` trên `users` (lĩnh vực / ngôn ngữ / loại dữ liệu), giá trị trong danh sách đóng | Đủ cho lọc và hiển thị; chưa cần bảng quan hệ |
| T6 | Cấp độ, huy hiệu, xếp hạng (FL-12) | identity đếm nhãn **đã duyệt kênh chuyên nghiệp** (`annotation.approved`) + uy tín (`reputation.changed`) vào `labeler_stats` + `labeler_daily_stats` (theo ngày giờ Việt Nam). Luật cấp là setting `identity.level_rules` (`2=50/50;3=200/65;...` = cấp=số nhãn/uy tín tối thiểu). Lên / xuống cấp → `level.changed`; huy hiệu mới → `badge.awarded` | Cấp chỉ là hàm thuần của (số nhãn, uy tín) nên test không cần DB, đổi luật không cần migration. Bảng xếp hạng all / month / week đọc thẳng bảng thống kê. Tranh chấp xử cho doanh nghiệp → trừ 1 nhãn đã duyệt |

**Bảo mật phiên và tần suất (gateway).**

| # | Quyết định | Chọn | Lý do / đánh đổi |
|---|---|---|---|
| B1 | Thu hồi access token (S-11) | **Mốc theo user** thay vì danh sách đen từng token: identity ghi `session_revocations(user_id, revoked_at)` (làm tròn xuống giây, vì `iat` của JWT tính bằng giây) + phát `user.sessions_revoked`. Gateway giữ mốc trong bộ nhớ (queue tạm), chặn token có `iat < mốc` → 401 `phien_da_bi_thu_hoi`. Gateway khởi động lại thì nạp toàn bộ mốc còn hiệu lực qua `GET /internal/session-revocations` (khoá `X-Internal-Key`), thử lại mỗi 10 giây tới khi được | Một dòng mỗi user thay vì một dòng mỗi token; không cần Redis. Mốc quá TTL access token (15 phút) thì vô nghĩa nên chỉ giữ chừng ấy. Đánh đổi: token **mới** cấp trong cùng giây với mốc vẫn bị chặn — client đăng nhập lại là xong. Gọi thẳng service (không qua gateway) thì không bị chặn — đã có S-06 (chỉ gateway public) |
| B2 | Rate limit (S-12) | `PartitionedRateLimiter` chuỗi ba tầng, cửa sổ cố định 1 phút: **chung** (`gateway.rate_limit_per_minute`, 600, theo user hoặc IP), **xác thực** (`..._auth_per_minute`, 20, theo IP: login / register / forgot / reset / verify), **tạo lệnh tiền** (`..._money_per_minute`, 30: nạp, rút). Vượt → 429 `qua_nhieu_yeu_cau` + `Retry-After` | Hạn mức là setting, đổi lúc chạy (khoá phân vùng có kèm hạn mức nên đổi là có bộ đếm mới). Giới hạn đã biết: tầng chung / tiền đọc `sub` của token **chưa kiểm chữ ký** (rate limit chạy trước xác thực) — giả `sub` lách được hạn mức chung, nhưng request đó vẫn bị 401 ở Ocelot. Đếm trong bộ nhớ từng instance gateway: chạy N instance thì hạn mức thực ≈ N lần |
| B3 | Secret (S-08) | Stack demo đọc mật khẩu DB / RabbitMQ / MinIO / khoá nội bộ từ `.env` (`deploy/tao_env.py` sinh ngẫu nhiên, không commit; thiếu biến thì compose báo lỗi). Khoá ký JWT identity tự sinh lần đầu, giữ trong volume | Không còn mật khẩu nào trong `docker-compose.demo.yml`. `appsettings.Development.json` vẫn giữ mật khẩu dev cho chế độ chạy service trên máy (`docker-compose.infra.yml`, cổng chỉ bind 127.0.0.1), nhưng ngoài Development service thấy mật khẩu / khoá dev thì **từ chối khởi động** (`ChanBiMatDev`, 3.17). Production cần secret manager (A-08) |

**Doanh nghiệp và labeler.**

| # | Quyết định | Chọn | Lý do / đánh đổi |
|---|---|---|---|
| D1 | Chi phí / ETA (FB-20) | Chi phí: ledger tổng hợp dòng sổ cái theo loại cho tài khoản ký quỹ của dự án. ETA: task-svc lấy tốc độ nộp 24 giờ gần nhất, ngoại suy phần nhãn còn thiếu | Số tiền lấy từ sổ cái (nguồn sự thật), không tự tính lại. ETA là `null` khi 24 giờ qua không ai nộp |
| D2 | Cảnh báo lệch (FB-24) | Với mỗi công cụ phân loại có ≥ `annotation.imbalance_min_labels` (20) nhãn: lớp chiếm **>** `annotation.imbalance_threshold_percent` (80 %) → `lop_ap_dao`; lớp chưa có nhãn nào → `lop_chua_co_nhan`. Trả trong `imbalanceWarnings` của kết quả; `GET projects/{id}/distribution` cho biểu đồ | Cảnh báo, không chặn — dữ liệu có thể lệch thật |
| D3 | YOLO / Pascal VOC (FB-25) | Xuất zip từ nhãn **khung (bbox)** của dự án ảnh; dự án không phải ảnh → 400 `khung_chi_cho_anh` | Hai định dạng phổ biến nhất cho detection; polygon / segment để sau |
| D4 | Nháp (FL-05) | `assignments.draft_json` (≤ 256 KB), `PUT assignments/{id}/draft`, trả lại khi lease lại cùng assignment | Gắn với lượt lease nên tự hết khi lease hết; không cần bảng riêng |
| D5 | Báo lỗi task (FL-07) | `task_reports` (broken / inappropriate / unclear / other). Người báo không bị giao lại task đó; đủ `task.report_auto_exclude` (3) người báo → task tự huỷ nếu không ai đang giữ; doanh nghiệp xem và xử lý (bỏ qua / loại task) | Không phạt labeler vì gặp dữ liệu hỏng |
| D6 | Thu nhập (FL-11) | ledger tổng hợp khoản thu của labeler / sharer theo ngày (giờ Việt Nam) hoặc theo dự án, tách theo nguồn (nhãn / vượt link / hoa hồng); bỏ khoản bị giữ lại (vi phạm) và khoản đã hoàn do thua tranh chấp | Đọc thẳng `holds` (index `ix_holds_thu_nhap`) |

**Tranh chấp (FM-06, ledger-svc).** Doanh nghiệp mở tranh chấp cho **một nhãn đã duyệt** khi khoản treo của labeler **chưa giải phóng** (đã về ví khả dụng thì không đòi lại được). Mở → khoản treo `Disputed` (worker giải phóng bỏ qua), `dispute.opened`. Admin phân xử:
- `PayLabeler` → mở băng, khoản treo chạy tiếp như cũ;
- `RefundBusiness` → bút toán `DisputeRefund` đảo dòng pending của labeler **và** phí nền tảng (lấy đúng từ bút toán gốc, không tính lại theo phí hiện tại), tiền về ký quỹ nếu ký quỹ còn mở, không thì về ví doanh nghiệp. `dispute.resolved` → annotation đánh dấu nhãn bị loại (`LoaiDoTranhChap`), identity trừ 1 nhãn đã duyệt, notification báo hai bên.

Khoá `FOR UPDATE` trên khoản treo khi mở / phân xử: không chạy song song được với worker giải phóng.

**Chống gian lận (FM-07) — gộp vào quality-svc** (không tách fraud-svc: cùng đọc luồng nộp bài). Nghe `assignment.submitted` (thêm `ClientIp` / `DeviceId`, ngoại lệ hợp đồng thứ năm), ghi `fraud_submissions`, xét 3 tín hiệu — làm quá nhanh so với trung vị dự án, một nhãn liên tục khi người khác chọn đa dạng, nhiều tài khoản chung IP công khai / mã thiết bị — ngưỡng là setting `quality.fraud_*`. **Hệ thống không tự khoá ai:** mở / cập nhật hồ sơ (mỗi khoá tối đa một hồ sơ đang mở; vừa xử lý thì chờ `fraud_reopen_cooldown` mới mở lại); admin xem bằng chứng rồi bỏ qua hoặc xác nhận — xác nhận kèm khoá → `fraud.case_confirmed` → identity khoá tài khoản (`user.blocked`). IP lấy từ `X-Forwarded-For` mà task-svc chỉ tin khi đến từ mạng proxy (`ForwardedHeaders:TrustedNetworks`).

**Audit và báo cáo (FP-06 một phần, FM-09, admin-svc).** Queue quorum `admin-svc.audit` bind `#` nhận **mọi** event. Mỗi event được **đếm** vào `stats_daily(day, event_type, count, amount_vnd, fee_vnd)` (ngày theo giờ Việt Nam) — báo cáo tổng quan đọc bảng này, một dòng cho mỗi (ngày, loại) dù có bao nhiêu event. Chỉ event **không** nằm trong `admin.audit_excluded_types` mới lưu **chi tiết** vào `audit_events` (payload > 64 KB thì cắt, `ON CONFLICT DO NOTHING` theo `event_id`). Mặc định loại các event phát sinh theo từng lượt nộp / từng click (`assignment.submitted`, `annotation.submitted` / `approved` / `rejected`, `consensus.reached`, `gold.answered`, `funds.held`, `click.validated`…): chúng đã có lịch sử ở service chủ (annotation `annotation_history`, sổ cái ledger). Thao tác quản trị, tài khoản, dự án, nạp / rút, tranh chấp vẫn lưu đủ. Event bị loại ghi `audit_counted(event_id)` trong 1 giờ để RabbitMQ giao lại không đếm hai lần. Worker xoá bản ghi quá `admin.audit_retention_days`. Tra cứu theo loại / user / khoảng thời gian. Thông báo toàn hệ thống: admin tạo → `system.announcement` → notification ghi hàng loạt theo vai trò (bản sao `contacts.roles`). **Chưa có**: chống admin sửa bảng audit (append-only ở mức quyền DB).

### 3.13. Bảo vệ dữ liệu: chặn tải bản gốc, watermark chìm

**Bài toán.** Trước đây 5 chỗ (lease của labeler, bài test đầu vào, màn duyệt nhãn, danh sách mẫu, trang vượt link) trả **link MinIO ký sẵn của bản gốc**: ai cầm link là tải được ảnh gốc của doanh nghiệp; ảnh bị lộ ra ngoài thì không biết ai làm lộ.

```
 task / annotation / gate / project ──(tự ký, không gọi HTTP)──► fileUrl = {gateway}/media/v/{token}
     token = AES-256-GCM({mẫu, khoá MinIO, người xem, chế độ, hết hạn})   ← khoá dùng chung (MediaLinks:GrantKey)

 trình duyệt <img src=fileUrl> ──► gateway ──► media-svc: giải mã token (sai 1 ký tự → 403), hết hạn? (410)
     ├── original    (chủ dự án / admin)              : bản gốc
     └── watermarked (labeler / reviewer / khách)     : ảnh mang watermark chìm = userId (hoặc mã phiên khách)

 ảnh bị lộ ──► admin POST /media/admin/watermark/decode ──► đọc ngược userId từ ảnh (không tra bảng nào)
```

| # | Quyết định | Chọn | Lý do / đánh đổi |
|---|---|---|---|
| M1 | Ngôn ngữ, trạng thái | **Python** (FastAPI), **không database**: setting nghe qua queue tạm và giữ trong bộ nhớ (như gateway) | numpy / OpenCV cho DCT và đọc / ghi ảnh. Không có gì cần lưu (xem M5) nên chạy nhiều bản sao tuỳ ý. Đánh đổi: phần đọc hợp đồng event / setting đang **chép** từ quality-svc thay vì dùng chung một gói |
| M2 | Link xem | **Token mã hoá AES-256-GCM trong URL**, sống `storage.view_link_ttl` (5 phút); service phát link tự ký bằng khoá dùng chung | Thẻ `<img>` không gửi được header `Authorization`, nên quyền xem phải nằm trong URL. Mã hoá (không chỉ ký) vì token chứa khoá MinIO. Không gọi HTTP sang media-svc lúc phát link (luật dữ liệu chéo, mục 4). Test vector cố định giữa C# (`MediaGrantTests`) và Python (`test_grant.py`) giữ hai bên khớp định dạng |
| M3 | Chặn tải bản gốc (FP-03) | Client không bao giờ thấy khoá / host MinIO; labeler, reviewer, khách chỉ nhận bản có watermark; header `Cache-Control: no-store`, `Content-Disposition: inline`, `nosniff` | **Không chặn được chụp màn hình** — không hệ thống web nào chặn được. Watermark để **truy ra** người làm lộ, không phải để ngăn |
| M4 | Watermark (FP-02) | QIM trên 5 hệ số DCT tần số trung bình của khối 8×8 kênh độ sáng; mỗi bit lặp lại trên nhiều khối theo hoán vị bí mật (`MEDIA_WATERMARK_KEY`); giải mã bỏ phiếu mềm + **CRC-32** | Đo bước 24 (mặc định), ảnh 512×512 và 300×200: PSNR ~42,5 dB (không nhìn ra), giải đúng sau nén JPEG chất lượng 50, thu nhỏ 60 % rồi phóng lại, tăng độ sáng; khoá khác / ảnh sạch → không ra ai. Nhúng ~17 ms (ảnh 512×512), ~0,8 s (12 MP). **Không chịu được cắt xén**; ảnh bị thu nhỏ phải phóng lại đúng kích thước gốc (API giải mã nhận `width` / `height`); ảnh nhỏ hơn ~130×130 không nhúng |
| M5 | Nhúng gì | **Nhúng thẳng người xem**: 1 byte loại (tài khoản / khách cổng link) + UUID 16 byte (userId / mã phiên vượt link) + CRC-32 = 168 bit. Không bảng tra | Đo cho thấy 168 bit bền **ngang** mã ngắn 48 bit + bảng tra (cùng 20/20 lần giải đúng mọi biến đổi kể cả ảnh 300×200), nên bảng tra là thừa. CRC-32: xác suất đọc nhầm ra một người ~2⁻³². Bước QIM là số nguyên 6–80: bộ giải tính DCT một lần rồi thử mọi bước (bước hiện tại trước) — đổi `media.watermark_strength` thì ảnh cũ vẫn giải được. Ảnh không có watermark phải thử hết 75 bước: ~9 giây với ảnh 12 MP (API admin, hiếm dùng) |
| M6 | Âm thanh / video | Đi qua proxy (giấu khoá, hỗ trợ `Range` để tua), **không** watermark | Watermark âm thanh / từng khung video cần pipeline riêng — để sau |
| M7 | Đã cân nhắc và **bỏ** | Che PII tự động (mặt / biển số / số CCCD) và nhật ký từng lượt xem | Che PII xung đột với dự án gán nhãn chính khuôn mặt / biển số. Nhật ký mỗi lượt xem phình theo lưu lượng (cổng link ~100× phần còn lại) mà không đủ giá trị so với watermark |

### 3.14. Lưu trữ: cái gì lớn theo người dùng, và dọn thế nào

**Bài toán.** Đo 11 database dev sau các đợt E2E (757 MB): hơn một nửa là dữ liệu **hạ tầng không ai đọc lại** — `outbox` đã gửi (task 177 MB, annotation 78 MB, quality 46 MB), `processed_events`, nhật ký audit của event tần suất cao; `refresh_tokens` 47 MB mà 86 % là token đã xoay; gate giữ mẫu của 336 dự án trong khi chỉ 27 dự án bật cổng link; mỗi nhãn chép lại nguyên nội dung mẫu. Tất cả tăng theo **số lượt nộp**, không theo lượng dữ liệu có ích.

Ba nguyên tắc:

1. Thứ gì chỉ có giá trị trong một cửa sổ thời gian thì có **hạn giữ là setting**, worker xoá **theo lô** (`DELETE … WHERE id IN (SELECT … LIMIT n FOR UPDATE SKIP LOCKED)`): không giữ khoá lâu, không tranh với đường nóng, chạy nhiều bản sao không xoá trùng.
2. Một sự thật chỉ lưu **một nơi**. Bản sao (mục 4, luật 2) chỉ giữ khi đường nóng phải **lọc bằng SQL** trên nó; sự thật một chiều về chính người gọi thì đi theo **token**.
3. Trước khi xoá dữ liệu mà màn hình còn đọc thì **chụp lại con số tổng hợp** — xoá dữ liệu thô, giữ báo cáo.

| # | Chỗ phình | Trước | Sau | Đánh đổi |
|---|---|---|---|---|
| L1 | `outbox` + `processed_events` (mọi service) | Giữ mãi | `EventStoreCleanupWorker<TDbContext>` (đăng ký sẵn trong `AddOutbox`; quality: `cleanup.py`) mỗi `outbox.cleanup_interval` (5 phút) xoá event **đã gửi** quá `outbox.retention` (7 ngày) và dấu vết chống trùng quá `consumers.processed_retention` (14 ngày), lô 2.000 × 25 | Event **chưa gửi** không bao giờ xoá. `processed_retention` phải dài hơn thời gian một message còn có thể bị giao lại: RabbitMQ giao lại trong vài giây, nhưng message nằm DLQ rồi được **đưa lại bằng tay sau hơn 14 ngày** có thể bị xử lý lần hai — vì vậy tối thiểu 1 ngày và mặc định gấp đôi `outbox.retention`. Mất khả năng soi lại event cũ trong outbox (audit giữ những event quan trọng) |
| L2 | Event `task.leased` | Mỗi lần nhận task một event, **không service nào nghe** | Bỏ (record `TaskLeased` xoá khỏi hợp đồng). Chống gian lận đo tốc độ bằng `LeasedAt` trong `assignment.submitted` | — |
| L3 | Nhật ký audit | Lưu chi tiết mọi event | Đếm mọi event vào `stats_daily`, chỉ lưu chi tiết event không bị loại (mục 3.12) | Không tra được từng lượt nộp / từng nhãn trong audit — tra ở service chủ |
| L4 | gate `gate_samples` | Giữ mẫu của mọi dự án | `project.published` **không bật cổng link**, và `project.completed` / `cancelled` → xoá mẫu của dự án | `dataset.ingested` tới trước `project.published` nên mẫu vẫn được ghi tạm rồi mới xoá |
| L5 | ClickHouse `click_events` | Giữ mãi | `TTL event_date + INTERVAL 13 MONTH` | Đủ so sánh cùng kỳ năm trước |
| L6 | quality `fraud_submissions` | Giữ mãi | Xoá quá `quality.fraud_submission_retention` (7 ngày) | Thuật toán chỉ nhìn vài chục lượt gần nhất; bằng chứng đã tổng hợp nằm trong `fraud_cases` nên không mất |
| L7 | Refresh token | Mỗi lần làm mới thêm một dòng | `login_sessions`: một dòng mỗi phiên, xoay tại chỗ (mục 3.11, K4b) | Triển khai: bảng cũ bị bỏ → **mọi người đăng nhập lại một lần** |
| L8 | Bản sao email / KYB | ledger `email_status`; project `email_status` + `verified_businesses` — mỗi user một dòng ở mỗi service, kèm 4 consumer | Claim `email_verified`, `kyb_verified` trong access token | Chỉ hợp với sự thật **một chiều** (đã xác thực thì không thu hồi) về **chính người gọi**. Thứ có thể đảo chiều (bị khoá) vẫn phải là bản sao + mốc thu hồi phiên (B1). Client làm mới token sau khi xác thực |
| L9 | Nội dung mẫu trong từng nhãn | `annotations` chép `storage_key`, `sample_content`, `sample_metadata` cho mỗi nhãn | `annotation_samples(sample_id PK, project_id, storage_key, content, metadata)` ghi **một lần** (`ON CONFLICT DO NOTHING`); nhãn chỉ giữ `sample_id` | Dev: 68.317 nhãn → 29.181 mẫu. Event `assignment.submitted` **vẫn mang** nội dung mẫu (annotation không gọi ngược project-svc; nằm trong outbox ≤ 7 ngày) |
| L10 | Dữ liệu vận hành của dự án đã đóng | Giữ mãi | Sau `project.closed_data_retention` (90 ngày; 0 = không xoá): **task-svc** chụp tiến độ cuối vào `project_snapshots.final_progress` rồi xoá `tasks`, `assignments`, `task_reports`, `gold_samples`; **quality-svc** chụp báo cáo vào `project_reports` rồi xoá `labels`, `consensus_rounds`, `task_targets`, `ds_state` | Xem dưới |

**L10 — xoá gì, giữ gì.** Dự án đóng là điểm cuối (không mở lại, không cấp task nữa), còn `tasks` + `assignments` là hai bảng lớn nhất hệ thống (mỗi mẫu × redundancy lượt, kèm bản nháp và nhãn) và chỉ phục vụ việc cấp task.

```
 dự án đóng ──► task:    project_snapshots.status = Closed (status_changed_at)
            └─► quality: projects.closed_at            (nghe project.completed / project.cancelled)

 sau project.closed_data_retention, mỗi service tự làm ba bước (dừng giữa chừng thì vòng sau làm tiếp):
   1. chụp con số tổng hợp (một transaction, khoá dòng dự án)   → API đọc bản chụp từ đây
   2. xoá theo lô
   3. đánh dấu đã dọn (data_purged_at / purged_at)
```

- **Giữ nguyên:** nhãn đã nộp / đã duyệt và kết quả (annotation-svc), sổ cái (ledger-svc), mẫu và file (project-svc, MinIO), và ở quality-svc là `label_agreements`, `gold_answers`, `ds_skills` — **uy tín labeler tính toàn cục từ ba bảng này**, xoá theo dự án là đổi điểm của người ta.
- **Sau khi dọn:** `GET /tasks/projects/{id}/progress` trả bản chụp (`frozenAt` có giá trị); `GET /quality/projects/{id}/summary` trả bản chụp (`archivedAt`), `…/labelers` giữ số của dự án còn uy tín lấy giá trị hiện tại; danh sách báo lỗi task và lượt giữ của dự án trống. Nhãn / mục tiêu đến trễ của dự án đã lưu trữ bị quality-svc bỏ qua.
- **Dọn ngay một dự án** (không đợi hết hạn): admin gọi `POST /tasks/admin/projects/{id}/purge` và `POST /quality/admin/projects/{id}/archive` — 409 `du_an_chua_dong` nếu dự án chưa đóng; gọi lại lần hai không làm gì (`purgedNow: false`).
- Chỉ mục mới `ix_assignments_task` (đầy đủ theo `task_id`): các chỉ mục cũ của `assignments` đều lọc theo `state`, xoá task theo dự án mà thiếu nó thì mỗi task là một lần quét cả bảng (khoá ngoại).

**Đo trên database dev** (11 Postgres; trước = ngay trước khi triển khai; sau = đã chạy migration + `deploy/don_du_lieu_cu.py`, hạ `outbox.retention` xuống 1 giờ cho worker dọn hết event cũ, rồi `VACUUM FULL` các bảng vừa dọn để Postgres trả chỗ):

| | Trước | Sau |
|---|---|---|
| Tổng 11 database | 757 MB | **384 MB** (−49 %) |
| `outbox` của task / annotation / quality | 154.531 / 68.646 / 49.749 dòng (169 + 75 + 44 MB) | vài trăm dòng, dưới 1 MB mỗi bảng |
| identity | 57,8 MB — `refresh_tokens` 32.564 dòng | 11,0 MB — `login_sessions` 95 dòng |
| gate | 26,9 MB — `gate_samples` 100.046 dòng | 8,7 MB — 218 dòng (chỉ dự án bật cổng link) |
| annotation | 155 MB — `annotations` 36 MB | 72 MB — `annotations` 26 MB + `annotation_samples` 5 MB |
| task | 289 MB | 122 MB (còn lại là `tasks` 72 MB + `assignments` 34 MB của dự án đang sống) |

Đọc con số cho đúng: (1) với hạn mặc định 7 ngày, `outbox` chỉ nhỏ lại khi event đủ 7 ngày tuổi — số "sau" ở trên là trạng thái với hạn 1 giờ; (2) `processed_events` (14 ngày) và `tasks` / `assignments` (dự án đóng quá 90 ngày) chưa giảm trên dev vì dữ liệu dev còn mới; (3) `DELETE` không làm file database nhỏ lại — chỗ trống được dùng lại cho dòng mới, muốn trả cho hệ điều hành phải `VACUUM FULL` (khoá bảng). Ý nghĩa thực của đợt này là các bảng đó **ngừng lớn không giới hạn**, không phải con số một lần.

**Chưa làm (ghi lại để biết giới hạn):** partition `outbox` / `assignments` theo thời gian (xoá bằng `DROP PARTITION` thay vì `DELETE`); gộp `label_agreements` + `gold_answers` thành bộ đếm theo labeler; hạn giữ cho `annotation_history`, `notifications`; xoá file MinIO của dự án đã đóng (cần doanh nghiệp đồng ý); sổ cái **không bao giờ** xoá (kiểm toán). Dự án đóng **trước** khi quality-svc có consumer `project.completed` không có `closed_at` — đã điền một lần bằng tay từ `task_db` trên dev / demo.

### 3.15. Thêm công cụ gán nhãn, và gộp nhãn cho mọi công cụ

**Bài toán.** Mục 3.9 dựng khung "loại dữ liệu × công cụ" với 7 công cụ, nhưng (1) thiếu nhiều dạng nhãn phổ biến — điểm mốc, đường, mặt nạ tô cọ, theo dõi đối tượng trong video, OCR, quan hệ giữa thực thể, xếp hạng nhiều mục, thang điểm, câu trả lời mở — và (2) chỉ phân loại / so sánh cặp được gộp tự động; với khung, đa giác… task ở trạng thái `notApplicable`: không có đồng thuận, không xin thêm người, không có bằng chứng cho uy tín, và file xuất ra là **khung của từng người chồng lên nhau**.

Sau thay đổi: 6 loại dữ liệu, 15 công cụ, mọi công cụ trừ câu trả lời mở đều chấm và gộp được.

| Công cụ | Dữ liệu | Kết quả | Độ giống hai kết quả (chấm câu vàng **và** ghép khi gộp) | Ngưỡng mặc định |
|---|---|---|---|---|
| `classification` | mọi loại | `{labelIds}` | tập lớp bằng nhau | — |
| `pairwise` | pair | `{choice}` | cùng lựa chọn | — |
| `freeText` ★ | mọi loại | `{text}` | không chấm | — |
| `number` ★ | mọi loại | `{value}` | lệch ≤ `tolerance` của công cụ | 0 |
| `transcription` | audio, video, **image** ★ | `{text}` | tỉ lệ lỗi ký tự (CER) | ≤ 0,1 |
| `bbox` | image | `[{labelId,x,y,w,h}]` | IoU | 0,5 |
| `polygon` | image | `[{labelId,points}]` | IoU ước lượng trên lưới 100 × 100 | 0,5 |
| `keypoint` ★ | image | `[{labelId,x,y}]` | 1 − khoảng cách / đường chéo ảnh | 0,98 |
| `polyline` ★ | image | `[{labelId,points}]` | 1 − độ lệch trung bình / đường chéo ảnh | 0,97 |
| `mask` ★ | image | `[{labelId,rle}]` | IoU trên RLE | 0,5 |
| `span` | text | `[{labelId,start,end}]` | trùng hệt; chấm bằng F1 | F1 1,0 |
| `relation` ★ | text | `[{labelId,from,to}]` | trùng hệt; chấm bằng F1 | F1 1,0 |
| `temporalSegment` | audio, video | `[{labelId,start,end}]` | IoU thời gian | 0,5 |
| `track` ★ | video | `[{labelId,keyframes}]` | IoU không-thời gian | 0,5 |
| `ranking` ★ | **list** ★ | `{order}` | tỉ lệ cặp mục xếp cùng chiều (Kendall) | 0,8 |

★ = thêm ở mục này. Công cụ dạng danh sách (trừ `relation`) còn cho mỗi đối tượng mang **thuộc tính** riêng (`attributes`: `choice` / `boolean` / `text` / `number`).

**Thêm công cụ: không đổi bảng, không đổi event.** Đúng như Q1–Q5 dự tính, mỗi công cụ mới là một file JSON Schema ([contracts/labeling/tools](../contracts/labeling/tools)) + một lớp `IToolKind` trong `shared/labeling/Tools` + một dòng trong bảng `ToolKinds`. Không có migration nào ở C#. Thay đổi hợp đồng duy nhất là `annotation.submitted` thêm `sampleMetadata` (quyết định C7).

| # | Câu hỏi | Chọn | Lý do / đánh đổi |
|---|---|---|---|
| C1 | Ô nhập chữ cho ảnh / văn bản / cặp | Tách **`freeText`** (câu trả lời mở: không chấm, không gộp) khỏi **`transcription`** (chép nội dung có sẵn: chấm bằng CER, gộp được; mở thêm cho ảnh) | Cùng hình dạng `{text}` nhưng khác bản chất. Dùng chung một công cụ thì mô tả ảnh sẽ bị đem ra "đồng thuận" — không bao giờ khớp, đốt hết trần redundancy |
| C2 | OCR, "xe + bị che khuất" | **Thuộc tính khai trên công cụ**, mang theo từng đối tượng; không tạo công cụ `ocr` riêng | Một cơ chế phủ OCR (khung + `text`), phân loại phụ (`choice`), cờ (`boolean`) cho cả 8 công cụ dạng danh sách. Đánh đổi: kiểu của giá trị phụ thuộc tập nhãn nên JSON Schema chỉ kiểm được "là chuỗi / số / true-false", phần còn lại kiểm trong code |
| C3 | Quan hệ trỏ tới đoạn nào | Bằng **vị trí** `[start, end)` của đoạn trong công cụ `span` mà `spanTool` chỉ tới; kiểm chéo giữa hai công cụ sau khi chuẩn hoá từng công cụ | Tham chiếu bằng số thứ tự sẽ lệch: danh sách đoạn được **sắp xếp lại** khi chuẩn hoá (để hai nhãn giống nhau ra cùng chuỗi JSON). Cái giá: `LabelPayload.Tao` có thêm một bước luật **giữa** các công cụ |
| C4 | Xếp hạng nhiều mục | Loại dữ liệu mới **`list`** (`content = {prompt, items[]}`, `metadata.itemCount`); kết quả là hoán vị các **chỉ số** mục | `pair` cố định hai mục `a` / `b`. Chỉ số gọn và kiểm được ngay bằng `itemCount`; đánh đổi: thứ tự `items` trở thành định danh, không sửa được sau khi nạp (mẫu vốn bất biến) |
| C5 | Lưu mặt nạ | **RLE không nén kiểu COCO** ngay trong `payload jsonb` (đoạn 0 / 1 xen kẽ, duyệt theo cột), bắt buộc ở dạng chuẩn; mọi phép tính (IoU, bỏ phiếu từng pixel, hình bao) đi trên **đoạn**, không bung ra pixel | Không cần kho file riêng, không đổi bảng, xuất COCO không phải chuyển đổi. Mặt nạ ảnh 4K vẫn chỉ vài nghìn đoạn. Chặn kích thước: ≤ 100.000 đoạn mỗi mặt nạ, tổng ≤ 200.000 đoạn mỗi công cụ (nhãn đi qua 3 event). Không dùng PNG / RLE nén: phải giải mã mới tính được |
| C6 | Theo dõi đối tượng | Mỗi đối tượng = chuỗi **keyframe** `{t, x, y, w, h}`, giữa hai keyframe **nội suy tuyến tính**; độ giống = IoU không-thời gian lấy mẫu 50 thời điểm trên khoảng hợp của hai chuỗi | Người gán chỉ vẽ ở vài thời điểm thay vì từng khung hình; phép đo phạt cả lệch vị trí lẫn lệch thời điểm bắt đầu / kết thúc. Chưa có ẩn / hiện lại trong một chuỗi |
| C7 | Thước đo cho điểm mốc và đường | Chuẩn hoá độ lệch theo **đường chéo ảnh** → cần kích thước ảnh ở mọi nơi chấm: `KhopDapAn(..., metadata mẫu)` (task-svc, project-svc) và `annotation.submitted.sampleMetadata` → `labels.sample_metadata` (quality-svc) | Ngưỡng theo pixel sẽ sai giữa ảnh 4K và ảnh thu nhỏ. Thêm vài chục byte vào một event, đổi lại quality-svc không phải gọi đồng bộ sang service khác (luật 1 mục 4). Mẫu cũ không có kích thước: dùng hình bao của chính các điểm đang so |
| C8 | Gộp ở đâu | **Hai nơi, một thuật toán**: `Crowd.Labeling` (C#) gộp nhãn **đã duyệt** → kết quả giao doanh nghiệp, file xuất; `quality-svc/app/gop.py` (Python) gộp nhãn **vừa nộp** → đồng thuận, xin thêm người, chấm khớp / lệch | Hai câu hỏi khác nhau trên hai tập nhãn khác nhau, ở hai service khác ngôn ngữ; gọi đồng bộ sang nhau vi phạm luật 1. Cái giá là **hai bản cài đặt** — xem "Giữ hai bản không lệch nhau" |
| C9 | Khi nào một task tranh chấp | Công cụ dạng danh sách: một người **khớp** khi F1 giữa đối tượng của họ và đối tượng đã gộp ≥ `quality.consensus_min_f1` (0,8); công cụ tranh chấp khi **không quá một nửa** số người khớp. Một nhãn khớp khi khớp mọi công cụ đã có kết quả | Đòi "ai cũng vẽ đủ mọi vật" thì một khung thừa của một người làm cả mẫu tranh chấp; F1 cho phép sót / thừa một vài trong nhiều vật. Ngưỡng là setting vì tuỳ độ khó dữ liệu |
| C10 | File xuất | COCO / YOLO / VOC / CSV ghi **đối tượng đã gộp** (kèm `support` = số người vẽ); COCO thêm `mask` | Dữ liệu huấn luyện cần một khung cho một vật. Mẫu tranh chấp không có đối tượng và được đánh dấu — cùng nguyên tắc với phân loại (FB-22) |

**Thuật toán ghép cụm** (`DanhSachToolBase.Gop`, dùng cho mọi công cụ dạng danh sách):

```text
1. GHÉP   Duyệt lần lượt từng người. Với mỗi người, ghép đối tượng của họ vào các cụm ĐÃ CÓ (do người trước
          tạo): cùng lớp, độ giống với đối tượng MỞ ĐẦU cụm ≥ ngưỡng; ưu tiên cặp giống nhất; mỗi cụm nhận
          tối đa một đối tượng của một người. Đối tượng không ghép được mở cụm mới.
2. CHỐT   Giữ cụm có HƠN MỘT NỬA số người. Mỗi cụm ra một đối tượng đại diện + thuộc tính gộp.
3. ĐỒNG Ý Mỗi người: F1 giữa đối tượng của họ và kết quả chốt ≥ ngưỡng thì khớp.
          Không quá một nửa số người khớp → tranh chấp, không có kết quả.
```

| Công cụ | Đối tượng đại diện của cụm |
|---|---|
| `bbox`, `keypoint`, `temporalSegment` | **Trung vị** từng toạ độ (không bị một người vẽ lệch kéo đi như trung bình) |
| `polygon`, `polyline`, `track` | Thành viên **"ở giữa"** — tổng độ giống tới các thành viên khác lớn nhất (số đỉnh / số keyframe mỗi người một khác, không lấy trung bình từng toạ độ được) |
| `mask` | **Bỏ phiếu từng pixel** trong cụm |
| `span`, `relation` | Ngưỡng ghép cố định "trùng hệt" → cụm = các bản y hệt nhau |
| Thuộc tính | `choice` / `boolean`: giá trị quá bán trong số người có ghi; `number`: trung vị; `text`: bản ở giữa |

Công cụ cho cả mẫu: `transcription` lấy bản chép **ở giữa** (kết quả luôn là một bản do người thật gõ, không trộn chữ của nhiều bản như ROVER); `number` lấy trung vị; `ranking` cộng điểm Borda. Cả ba dùng chung luật "không quá một nửa số người khớp kết quả gộp → tranh chấp".

Thuật toán **tất định và không có trọng số**: cùng dữ liệu cùng thứ tự luôn ra cùng kết quả (mọi chỗ hoà đều có luật phá hoà), và giải thích được cho doanh nghiệp bằng một câu. Chưa dùng độ tin cậy từng labeler làm trọng số (kiểu WBF / Dawid–Skene cho khung): cần đủ dữ liệu mới ước lượng được, và sẽ làm kết quả của cùng một mẫu thay đổi theo thời gian.

**Giữ hai bản không lệch nhau.** Ba lớp:

1. Bộ ví dụ chung [contracts/labeling/testdata/gop.vectors.json](../contracts/labeling/testdata/gop.vectors.json) — 31 ví dụ phủ mọi công cụ, kèm ca tranh chấp, thuộc tính, ảnh không có kích thước, tiếng Việt có dấu. Phần `expected` do C# sinh (`UPDATE_GOP_VECTORS=1 dotnet test shared/test/labeling-tests`); `GopVectorTests.cs` và `tests/test_gop_vectors.py` cùng chạy bộ này, lệch là test của bên đó đỏ.
2. Những chỗ hai ngôn ngữ dễ khác nhau được viết **cùng một công thức**: làm tròn (`floor(v × 1000 + 0,5) / 1000`), khoảng cách sửa trên **đơn vị UTF-16** (chuỗi Python được đổi sang UTF-16 trước khi đo), khoá sắp xếp so theo đơn vị UTF-16, thứ tự phá hoà khi ghép cặp.
3. IoU đa giác: C# quét từng hàng lưới (tính giao điểm với cạnh một lần mỗi hàng), Python tính cả lưới bằng numpy — hai cách cài đặt nhưng **cùng phép so sánh** trên cùng lưới điểm, nên ra cùng số điểm.

**Chi phí tính toán.** Ghép cụm so từng cặp đối tượng cùng lớp nên tăng theo bình phương số đối tượng; phép đo đắt nhất là IoU đa giác (lưới 100 × 100) và mặt nạ (đi trên đoạn). Trong một lần gộp, mỗi đối tượng chỉ được đọc từ JSON một lần và mỗi cặp chỉ so một lần (`BoSoSanh` / `_nho_do_giong`); đa giác và mặt nạ có bước lọc nhanh bằng hình bao / khoảng pixel trước khi tính IoU; một người gán thì không so cặp. Đo trên máy dev (một lần gộp một mẫu ảnh 1920 × 1080, 5 người, dữ liệu sinh ngẫu nhiên; hai bên cho kết quả **giống hệt** trên cả 6 trường hợp):

| Mẫu | C# (annotation-svc, kết quả / file xuất) | Python (quality-svc, đồng thuận) |
|---|---|---|
| 50 khung mỗi người | 16 ms | 65 ms |
| 300 khung mỗi người | 0,8 giây | 3,7 giây |
| 20 đa giác 12 đỉnh mỗi người | 0,15 giây | 0,5 giây |
| 100 đa giác 12 đỉnh mỗi người | 0,8 giây | 7,7 giây |
| 10 mặt nạ ~1.000 đoạn mỗi người | 45 ms | 0,5 giây |
| 40 mặt nạ ~1.000 đoạn mỗi người | 0,3 giây | 1,3 giây |

Trước khi đọc sẵn và nhớ theo cặp, C# mất 7,8 giây cho trường hợp 40 mặt nạ (đọc lại 1.000 số của mặt nạ ở mỗi lần so). quality-svc tính đồng thuận trong consumer, mỗi task một lần nên vài giây cho mẫu cực dày là chấp nhận được. `GET /annotations/projects/{id}/results` thì gộp **lại mọi mẫu ở mỗi lần gọi**: dự án hàng nghìn mẫu dày đa giác / mặt nạ sẽ chậm — chưa có cache kết quả gộp (xem Giới hạn).

**Hành vi thay đổi với dự án đang có.** (1) Dự án dùng khung / đa giác / đoạn / chép lời trước đây không có đồng thuận, nay có: task có thể được xin thêm người tới `maxRedundancy` — không vượt ngân sách, vì ký quỹ luôn tính theo trần (mục 3.7). (2) File COCO / YOLO / VOC đổi từ "mỗi khung của mỗi người" sang "khung đã gộp"; với một nhãn đã duyệt mỗi mẫu thì kết quả như cũ. (3) Nhãn nhận trước bản này không có `sample_metadata`: vẫn gộp được, điểm mốc / đường dùng thước đo dự phòng.

**Kiểm chứng.** `shared/test/labeling-tests`: 59 test (định dạng, chuẩn hoá, chấm đáp án từng công cụ + bộ ví dụ chung). `services/quality/tests`: 111 test (62 từ bộ ví dụ chung: từng công cụ và cả task). `services/annotation`: COCO / CSV / YOLO chạy từ nhãn đã duyệt tới file. E2E [e2e_cong_cu.py](../tests/e2e/e2e_cong_cu.py) (81 ca, qua API thật): nộp sai bị từ chối đúng lý do, đồng thuận ở quality-svc, tranh chấp → xin thêm người → người thứ ba phá thế hoà, kết quả gộp và file xuất, bài test đầu vào và câu vàng bằng điểm mốc, đổi ngưỡng bằng setting lúc đang chạy.

**Giới hạn.**

- IoU đa giác là ước lượng trên lưới (sai số ~1%), đủ cho ngưỡng 0,5 nhưng không dùng làm số đo chính xác.
- Trang kết quả / file xuất gộp lại toàn bộ dự án ở mỗi lần gọi (bảng chi phí ở trên). Dự án lớn với đối tượng dày cần cache kết quả gộp theo mẫu (tính lại khi mẫu có nhãn được duyệt / bị loại) — chưa làm.
- `track`: đối tượng biến mất rồi xuất hiện lại phải tách thành hai chuỗi (mất định danh); chưa có khung xoay.
- Chưa xuất định dạng riêng cho `keypoint` (COCO keypoints cần bộ khung xương cố định), `track` (MOT), `relation` (CoNLL / BRAT): lấy qua JSON / CSV.
- Dawid–Skene / Krippendorff alpha (mục 3.7) vẫn chỉ tính cho công cụ một giá trị rời rạc; chính sách `posterior` / `voi` cũng vậy.
- Cổng vượt link (mục 3.6) vẫn chỉ nhận phân loại một lớp và so sánh cặp.
- Dữ liệu seed chưa có dự án mẫu cho công cụ mới; frontend chưa có màn hình vẽ cho các công cụ này ([hợp đồng](tich-hop-frontend.md) mục 6.3, 6.6 ví dụ 11–18).

### 3.16. Vào dự án: ba chế độ, mời nhiều người, mã mời, đơn xin tham gia

Trước bản này chỉ có hai đường: dự án **công khai** thì labeler tự `join`, dự án **riêng tư** thì chủ dự án thêm **từng `userId`**. Doanh nghiệp không biết `userId` của người ta thì gần như không dùng được chế độ riêng tư. `join` cũng không kiểm cấp độ / uy tín: người chưa đủ điều kiện vẫn vào được, rồi lần nhận task nào cũng bị task-svc từ chối `chua_du_cap_do`.

**Ba chế độ hiển thị** (`visibility`, chỉ đổi được khi còn Nháp):

| Chế độ | Labeler thấy dự án? | Tự vào bằng cách nào | Chủ dự án mời / mã mời |
|---|---|---|---|
| `public` | Có | `POST /join`, hoặc làm đạt bài test đầu vào | Có |
| `approval` *(mới)* | Có | Gửi **đơn** (`POST /join-requests`) → chủ dự án duyệt. Có bài test thì làm đạt mới thành đơn, kèm điểm | Có |
| `private` | Không (404) | Không | Có — đường duy nhất |

**Bốn đường vào, một luật điều kiện.** Mọi đường đều kiểm hồ sơ labeler lấy **ngay lúc vào** từ identity-svc, cùng luật và cùng mã lỗi với task-svc khi cấp task (`EligibilityPolicy`): `tai_khoan_bi_khoa`, `khong_phai_labeler`, `chua_du_cap_do`, `chua_du_uy_tin`.

| Đường vào | Ai bấm | Kiểm điều kiện | `member.added.via` | Thông báo |
|---|---|---|---|---|
| `POST /members`, `POST /members/batch` (theo email hoặc userId) | Chủ dự án | Có, với vai trò labeler; reviewer chỉ kiểm khoá | `owner_invite` | Người được mời |
| `POST /projects/join-by-code` | Labeler | Có | `invite_code` | — |
| `POST /join`, bài test đầu vào | Labeler | Có, **trước khi** làm bài | `join`, `entrance_test` | — |
| Duyệt đơn | Chủ dự án | Có, kiểm **lại** lúc duyệt (uy tín có thể đã tụt) | `join_request` | Labeler (duyệt / từ chối + lý do); chủ dự án khi có đơn mới |

**Tra cứu người dùng: hỏi chủ dữ liệu, không giữ bản sao.** project-svc gọi `POST /internal/users/lookup` của identity-svc (khoá `X-Internal-Key`, gateway không định tuyến `/internal`) để đổi email → userId và lấy vai trò, khoá, cấp độ, uy tín. Đây là cùng kiểu với lúc đóng dự án (`DongSoClient`): thao tác hiếm, không nằm trên đường nóng, nên hỏi chủ dữ liệu tại chỗ, không nhân bản email sang project_db — trước đó hệ thống đã chủ động bỏ bản sao email KYB. identity không trả lời thì **từ chối** (503 `dich_vu_khong_san_sang`), giống task-svc "thiếu dữ liệu thì không cho qua". Riêng màn hàng đợi đơn vẫn hiện được khi identity tắt, chỉ thiếu tên / cấp độ / uy tín.

**Mời nhiều người.** `POST /members/batch` nhận `userIds` và `emails` (tổng tối đa `project.member_batch_max`, mặc định 100). Mỗi đầu vào trả một dòng kết quả theo đúng thứ tự gửi. Theo userId: `added`, `khong_tim_thay`, `trung_lap`, `da_la_thanh_vien`, `bi_chan` hoặc mã điều kiện — chủ dự án vốn đã biết người đó. Theo email: luôn `invited` (xem "Ô mời không dò được tài khoản" bên dưới). Người hợp lệ được thêm hết trong **một** transaction cùng các event `member.added`. Hai lần mời trùng người chạy cùng lúc thì khoá chính `(project_id, user_id)` chọn một, lần kia làm lại và thấy `da_la_thanh_vien`. `POST /members` (một người) dùng chung logic, nhận `userId` **hoặc** `email`, lỗi trả đúng mã như trên. Trước đây nó thêm cả `userId` không tồn tại; nay trả 404.

**Ô mời không dò được tài khoản (VD-S-16).** Nếu mời theo email trả `added` / `khong_tim_thay` / `chua_du_cap_do`, doanh nghiệp dán 100 email mỗi lần là biết email nào có tài khoản, thậm chí cấp độ của người đó. Nên mọi email đúng dạng đều ra **cùng** một dòng `invited` + `invitationId`, không `userId` / tên, và luôn tạo một dòng `project_invitations`:

| Email là của | Lời mời | Người đó thấy |
|---|---|---|
| Chưa có tài khoản | `user_id` null | Thư mời đăng ký |
| Tài khoản bình thường (kể cả chưa đủ cấp) | gắn `user_id` | Lời mời trong ứng dụng, **tự chấp nhận** (điều kiện kiểm lúc đó) |
| Thành viên / bị chặn ở dự án / bị doanh nghiệp chặn | **câm**: `user_id` null, không phát event | Không gì cả |

Đánh đổi: mời theo email không còn thêm thẳng người có tài khoản — họ phải bấm chấp nhận. Đó cũng là chỗ người dùng tự quyết có lộ mình cho doanh nghiệp hay không: chủ dự án chỉ thấy `userId` của lời mời theo email khi nó `accepted`; ngược lại lời mời theo userId không hiện email (`by_email` quyết định phía nào bị ẩn). Lời mời theo email và theo userId của cùng người là **hai dòng** (chỉ mục duy nhất `(project_id, email, by_email)`), vì dùng chung một dòng thì trùng `invitationId` sẽ nối email với tài khoản. Hạn mức riêng: `project.email_invite_daily_max` email / người / 24 giờ, đếm từ chính bảng lời mời (chỉ mục `(invited_by, created_at) WHERE by_email`) → 429 kèm `Retry-After`; vì mọi email đều tạo dòng nên đếm được cả lần dò email không tồn tại. Còn lại: thời gian phản hồi gần như không đổi (cùng một lần hỏi identity cho cả lô, cùng một lần ghi), chỉ lời mời câm bớt một dòng outbox; đăng ký tài khoản vẫn báo email đã dùng (giới hạn bằng rate limit gateway).

**Mã mời** (bảng `invite_codes`). 16 ký tự từ bảng 32 ký tự dễ đọc (bỏ I, O, 0, 1), tức 80 bit ngẫu nhiên: không dò được trong giới hạn tần suất của gateway. Mã luôn có hạn (mặc định 7 ngày, tối đa `project.invite_code_max_ttl`), có thể giới hạn số lượt và thu hồi được. Mã được lưu nguyên văn để chủ dự án xem lại và chia sẻ lại; nó chỉ mở cửa một dự án, không phải mật khẩu. Người dùng gõ chữ thường, có gạch hay khoảng trắng đều được. Mã mời là **lời mời của chủ dự án**, nên bỏ qua chế độ hiển thị (vào được dự án riêng tư, kể cả khi dự án còn Nháp) và bỏ qua bài test, nhưng **không** bỏ qua cấp độ / uy tín. Thứ tự kiểm tra: mã tồn tại → dự án chưa kết thúc → chưa là thành viên → đủ điều kiện → mã còn dùng được → ghi lượt và thêm thành viên trong một transaction. Người bị từ chối vì vậy không làm tốn lượt. Hai người dùng **lượt cuối** cùng lúc: cột `xmin` chọn một, người kia đọc lại và nhận `ma_moi_het_luot` (E2E: 6 người tranh 3 lượt → đúng 3 người vào).

**Đơn xin tham gia** (bảng `join_requests`). Mỗi người có tối đa **một** đơn `pending` trong một dự án, đảm bảo bằng chỉ mục duy nhất có điều kiện `WHERE status = 'Pending'`, nên hai request gửi cùng lúc thì DB chọn một. Đơn bị từ chối thì phải chờ `project.join_request_cooldown` (mặc định 24 giờ) mới gửi lại được: chống spam hàng đợi của chủ dự án. Nếu dự án có bài test, không gửi đơn tay được (`can_lam_test`); làm đạt bài test thì đơn tự tạo, kèm `scorePercent` và `entranceAttemptId`, và trong lúc đơn đang chờ không làm lại test được. Người đang có đơn mà vào bằng đường khác thì đơn tự đóng: chủ dự án mời thẳng → `approved`, labeler nhập mã mời → `cancelled`. Nhờ vậy hàng đợi không còn đơn thừa.

**Sửa lệch giữa hai service.** identity coi labeler chưa có thống kê là **cấp 1**. task-svc trước đây lại từ chối người chưa từng nhận `level.changed`, kể cả khi dự án chỉ đòi cấp 1. Nay task-svc cũng coi là cấp 1. Thay đổi này vẫn an toàn theo hướng từ chối: `level.changed` đến trễ chỉ làm labeler cấp cao tạm bị coi là cấp 1, không bao giờ cho người không đủ cấp vượt qua.

**Event mới.** `join_request.submitted` (→ notification: báo chủ dự án) và `join_request.decided` (→ notification: báo labeler). `member.added` thêm hai trường **không bắt buộc**: `via` và `projectName`. Bản tin cũ không có hai trường này vẫn đọc được; các service nhân bản bảng thành viên bỏ qua chúng. Hai loại thông báo mới: `thanh_vien` (mặc định có email) và `don_tham_gia` (mặc định **không** email, vì chủ dự án có thể nhận rất nhiều đơn).

**Kiểm chứng.** `Crowd.Project.Tests/ThanhVienTests.cs` (13 test thuần: chế độ, điều kiện, mã mời, đơn). E2E [e2e_thanh_vien.py](../tests/e2e/e2e_thanh_vien.py) (86 ca qua API thật): mời theo email / userId, một và nhiều người, giới hạn lô; mã mời (hết lượt, hết hạn, thu hồi, 6 người tranh 3 lượt); kiểm cấp độ / uy tín ở `join`, lúc mời, mã mời, bài test; chế độ approval (gửi, trùng, từ chối + chờ, rút, duyệt, uy tín tụt lúc duyệt, đơn tự đóng) và approval kèm bài test; thông báo cho cả hai phía; nhận task thật sau khi vào.

**Giới hạn.**

- Mời theo email chỉ áp dụng cho người **đã có tài khoản**. Chưa có lời mời chờ đăng ký (gửi email mời người chưa có tài khoản, tự vào khi đăng ký bằng email đó).
- Chủ dự án mời theo email thì biết được email đó có tài khoản hay không (`khong_tim_thay`). Rủi ro này chấp nhận được, vì chủ dự án là doanh nghiệp đã KYB; có thể giảm bằng giới hạn tần suất riêng nếu cần.
- Danh sách thành viên chưa kèm tên / email: frontend ghép bằng `GET /users/public?ids=`.
- Chưa có hạn tự đóng cho đơn `pending` mà chủ dự án bỏ quên; labeler tự rút được.

### 3.17. Đợt hoàn thiện 10/2026: những quyết định đáng nói

Đợt này lấp các chức năng đặc tả còn thiếu (trừ ml-svc, collab-svc và nhánh DevOps). Chỉ ghi những chỗ có lựa chọn thiết kế; API chi tiết ở [tích hợp frontend mục 17](tich-hop-frontend.md#17-bổ-sung-đợt-hoàn-thiện-102026).

**Xác thực hai lớp (FC-04) và xác minh danh tính (FP-05) — identity.** TOTP RFC 6238 tự viết (HMAC-SHA1, ±1 bước 30 giây), chống dùng lại mã bằng `LastUsedStep`; bí mật TOTP và số giấy tờ mã hoá AES-GCM, khoá dẫn xuất HKDF từ `IdentityData:Key` (không nằm trong DB). Đăng nhập hai bước: bước 1 trả `challengeToken` ngắn hạn thay vì token, sai mã quá `identity.two_factor_max_attempts` thì huỷ thử thách. Số giấy tờ lưu **băm có khoá** + index duy nhất có điều kiện (`WHERE status = 'Verified'`): một giấy tờ chỉ xác minh cho một tài khoản (VD-T-04) mà không lưu số rõ để so. Kết quả xác minh đi theo claim `identity_verified` / `adult` của access token — project-svc không gọi identity khi kiểm điều kiện dự án.

**Rate limit dùng chung (S-12) — gateway.** Đếm cửa sổ cố định bằng script Lua `INCR` + `PEXPIRE` trên Redis riêng của gateway (một round-trip, nguyên tử); Redis lỗi thì rơi về bộ đếm trong bộ nhớ thay vì chặn hay thả hết — thà hạn mức tạm thời lỏng hơn còn hơn sập đăng nhập.

**Cổng VNPay / MoMo (FB-03) — payment.** Mỗi cổng là một `ICongNap` (tạo URL / gọi API tạo đơn, kiểm chữ ký IPN, tra cứu). Tiền chỉ ghi từ **IPN đã kiểm chữ ký** hoặc từ **API tra cứu server-to-server** (`sync`, worker đối soát) — Return URL chỉ để biết người dùng đã quay về (S-01). Chốt lệnh bằng cập nhật có điều kiện trạng thái, IPN đến hai lần không cộng hai lần. Dev / CI không có tài khoản cổng thật nên payment-svc có **cổng giả lập** (chỉ Development) ký đúng thuật toán HMAC-SHA512 / SHA256 của từng cổng — luồng thật chạy end-to-end trong E2E.

**Hoá đơn VAT (FB-05) — ledger.** Phí nền tảng đã thu là giá gồm thuế, hoá đơn tách ngược `tiền hàng = round(phí / (1 + VAT))`. Số hoá đơn cấp từ bảng đếm theo ký hiệu + năm trong cùng transaction (không nhảy số, không trùng). Bảng `invoices` có trigger cấm `UPDATE` / `DELETE` / `TRUNCATE` như sổ cái. Xuất hai dạng: HTML in được và XML theo cấu trúc Thông tư 78 (chưa ký số, chưa gửi cơ quan thuế).

**Dự án (mục 2.9, FB-11, FB-23, FL-02) — project / task.** Đồng ý điều khoản / NDA là bản ghi riêng (`project_consents`) gắn phiên bản văn bản; đổi văn bản khi đang chạy không được (chỉ khi nháp). Ép nghỉ giải lao tính ở task-svc từ các lượt giữ của chính labeler (`PhienLamViec`), trả `429 can_nghi_giai_lao` + `Retry-After`. Mời email chưa có tài khoản tạo `project_invitations` chờ, gắn tài khoản khi `user.email_verified` (không cần identity tra ngược). Nhập nguồn ngoài (S3 / URL / Google Drive): tải qua `HttpClient` với `ConnectCallback` kiểm **IP thật sau phân giải DNS** (chặn SSRF kể cả DNS rebinding), khoá S3 lưu mã hoá và xoá sau khi nhập.

**Định dạng nhãn và xuất (FB-25) — annotation / labeling.** Khung xoay chỉ cho `bbox` có `rotatable`; IoU giữa hai khung xoay tính bằng giao đa giác (dùng chung với đa giác), gộp góc bằng trung vị. `track` thêm `visible` ở keyframe thay vì tách chuỗi. Kết quả gộp từng mẫu được **đệm** (`sample_results`) theo dấu vân tay = tập nhãn + mọi ngưỡng + danh sách nhãn đã duyệt: đổi bất cứ thứ gì thì dấu khác và tính lại, không cần xoá đệm thủ công. Vector gộp dùng chung C# / Python (`gop.vectors.json`) mở rộng cho hai dạng mới.

**Chỉ số chất lượng (FQ-02) — quality.** Krippendorff alpha viết lại tổng quát theo hàm khoảng cách (đã khớp số liệu tham chiếu của Krippendorff và crowd-kit) thay vì chỉ dùng cho dữ liệu rời rạc; với công cụ dạng danh sách, khoảng cách = 1 − F1 giữa hai bộ đối tượng, **cùng phép ghép và ngưỡng** với gộp nhãn. STAPLE chạy trên cả ảnh nhưng gom pixel theo "kiểu phiếu" (tối đa 2ⁿ kiểu cho n người) nên nhanh như chỉ xét vùng tranh cãi mà vẫn tính đúng độ đặc hiệu — bản đầu chỉ xét vùng có người tô cho độ đặc hiệu sai bằng 0, E2E bắt được. STAPLE không vào uy tín: mặt nạ ít mẫu, ước lượng EM dao động. quality-svc thiếu bản sao dự án (publish trước khi nó tồn tại) thì **tự hỏi** project-svc qua `GET /internal/projects/{id}/published-terms` (khoá nội bộ) thay vì bỏ qua đồng thuận.

**Watermark âm thanh / video (FP-02) — media.** Âm thanh: QIM trên hệ số DCT khung 2048 mẫu, dải 1–5 kHz, cùng 168 bit + CRC-32 như ảnh, xuất FLAC (không mất thông tin). Video: nhúng watermark ảnh vào **mọi khung** rồi mã hoá lại H.264 CRF 18; giải bằng cộng phiếu mềm trên tối đa 48 khung nên chịu được nén video. Mã hoá lại cả file tốn thời gian nên kết quả **đệm trên đĩa** theo (file, người xem, độ mạnh, khoá) — tua video (`Range`) không mã hoá lại; khoá theo từng bản để nhiều request cùng lúc chỉ tạo một lần. Gateway đặt `Timeout` 600 giây riêng cho route xem file.

**Hàng đợi duyệt chung (mục 5) — admin.** Không dựng khung `IRequestProcessor` dùng chung: mỗi service đã có bảng trạng thái của loại đơn mình (`business_profiles`, `identity_verifications`, `projects`, `withdrawals`, `annotations`, `disputes`, `links`, `referrals`) và API duyệt riêng — gộp chúng vào một khung chung là sửa 8 luồng đang chạy mà không thêm khả năng nào. Thay vào đó admin-svc dựng **read model** `approval_requests` từ event (cùng consumer nhận mọi event của audit, cùng transaction, chỉ khi event lần đầu thấy). Event đến lệch thứ tự được xử lý bằng `last_event_at`: event cũ hơn không ghi đè; event *đóng* đến trước *mở* thì ghi sẵn dòng đã đóng. Thêm 5 event còn thiếu để mọi luồng có đủ mở / đóng: `withdrawal.review_requested`, `link.review_requested`, `link.review_dismissed`, `appeal.resolved`, `referral.flagged` / `referral.rejected`. Bảng dựng lại được từ `audit_events` (`POST /admin/approvals/rebuild`).

**Mẫu bài toán (FM-03) — admin.** Mẫu = modality + tập nhãn + hướng dẫn + giá gợi ý; tập nhãn kiểm bằng **chính bộ đọc `LabelSchema`** của project-svc. Dự án không tham chiếu mẫu (frontend chép sang) — sửa mẫu không âm thầm đổi dự án đang chạy.

**Audit chỉ-ghi-thêm ở mức CSDL (P-03), cảnh báo DLQ (D-06 / D-12).** Trigger trên `audit_events` chặn `UPDATE` / `TRUNCATE` và chỉ cho `DELETE` dòng cũ hơn 7 ngày (cận dưới của `admin.audit_retention_days`) — kể cả tài khoản của chính admin-svc không xoá được dấu vết mới. `DlqMonitorWorker` đọc RabbitMQ management API, phát `ops.dlq_alert` khi DLQ vượt ngưỡng, nhắc lại theo chu kỳ — điều kiện bắt buộc của quyết định giữ hợp đồng payload nghiêm ngặt.

**Thông báo realtime và ngôn ngữ (FC-06, FC-08) — notification.** WebSocket qua gateway, xác thực bằng **vé dùng một lần** (trình duyệt không gửi được header khi mở WebSocket; vé lưu băm trong DB để instance cấp vé và instance nhận kết nối khác nhau vẫn chạy). Nhiều instance không cần Redis: `ThongBaoWriter` gọi `pg_notify` **trong transaction của consumer** — Postgres chỉ phát khi commit, nên không đẩy thông báo bị rollback; mỗi instance `LISTEN` và chỉ đẩy cho kết nối của mình. Vé sai → mở rồi đóng ngay với mã 4401 (từ chối lúc bắt tay thì Ocelot đổi thành 500). Ngôn ngữ chọn **lúc ghi**: mỗi nội dung có bản Việt + Anh, thông báo và email cùng một ngôn ngữ.

**Chống lạm dụng giới thiệu — link.** Không chặn người dùng (NAT, wifi chung báo nhầm): dấu hiệu nghi ngờ chỉ **hoãn** `referral.registered` — ledger chưa tính hoa hồng tới khi admin chấp nhận.

**Cổng link chỉ cho công cụ làm nhanh — project.** Khách vãng lai có vài giây, không hướng dẫn, không uy tín: vẽ đa giác / mặt nạ / chuỗi khung gần như chắc sai mà vẫn tốn ngân sách. Danh sách công cụ cho phép là setting `gate.allowed_tools`, kiểm ở checklist và lúc publish.

**Phân vùng `tasks` (T-07) — task.** `HASH(project_id)` 16 phần: câu lấy task luôn lọc theo dự án nên chỉ đụng một phân vùng. Bảng phân vùng bắt buộc khoá chính chứa cột phân vùng → `(project_id, id)` và khoá ngoại từ `assignments` thành `(project_id, task_id)`; tra theo `id` đơn lẻ (ít gặp) đi qua `ix_tasks_id`. EF không tạo partition nên migration viết SQL tay (đổi tên bảng cũ → tạo bảng phân vùng → chép dữ liệu → bỏ bảng cũ); model EF khai đúng khoá ghép để migration sau không sinh lệch. **Không** phân vùng `outbox` theo thời gian (xem VD-O-08).

**Bí mật dev (S-08).** Mật khẩu dev vẫn ở `appsettings.Development.json` cho chế độ chạy service trên máy, nhưng hệ thống **fail closed**: ngoài môi trường Development, service còn thấy mật khẩu / khoá dev trong cấu hình thì từ chối khởi động (C#: `ChanBiMatDev` đăng ký trong `AddCrowdJwtAuthentication`; Python: `config.settings()`).

## 4. Dữ liệu chéo — ba luật cứng

Đây là chỗ dự án microservice hay vỡ nhất.

```
1. KHÔNG gọi HTTP đồng bộ để lấy dữ liệu chéo trong đường nóng.
2. Service cần field của service khác → giữ BẢN SAO READ-ONLY,
   cập nhật bằng event, KHÔNG coi là nguồn sự thật.
       task_db.labeler_cache(user_id, reputation, level, verified, blocked, updated_at)
         ← identity: reputation.changed | level.changed | user.blocked
       task_db.project_members_cache(project_id, user_id, role, state)
         ← project:  member.added | member.blocked | member.removed
3. Màn hình gộp nhiều nguồn → web-bff, không nhồi vào service nghiệp vụ.
```

Nhờ luật 2, `task-svc` lọc eligibility bằng **một câu SQL trên DB của chính nó** — nhanh ngang monolith mà vẫn tách thật.

**Khi nào KHÔNG cần bản sao.** Bản sao tốn một bảng + consumer ở mỗi service cần nó. Nếu điều cần biết là sự thật **một chiều về chính người đang gọi** (email đã xác thực, doanh nghiệp đã được duyệt KYB) thì đưa vào **claim của access token** (`email_verified`, `kyb_verified`): service đọc thẳng từ token, không bảng, không event. Điều kiện: không bao giờ đảo chiều (token cũ mang `true` không được phép sai), và client làm mới token khi trạng thái đổi. Thứ có thể đảo chiều (bị khoá, bị chặn khỏi dự án) hoặc phải lọc bằng SQL trên nhiều người (uy tín, cấp độ) vẫn là bản sao.

**Ví dụ luật 3 — dashboard labeler** cần profile + lịch sử + tỉ lệ duyệt + ví:

```
web-bff  ──┬─► identity-svc    GET /users/{id}/profile
           ├─► annotation-svc  GET /labelers/{id}/stats
           ├─► task-svc        GET /labelers/{id}/active-leases
           └─► ledger-svc      GET /accounts/labeler:{id}/balance
           (4 lời gọi song song, timeout 300ms, phần nào lỗi thì degrade phần đó)
```

Ranh giới đọc/ghi: quyết định **tiền bạc** luôn hỏi lại chủ sở hữu dữ liệu, không bao giờ đọc từ bản sao. Ví dụ duy nhất hiện có của lời gọi HTTP đồng bộ giữa hai service nghiệp vụ là **đóng dự án** (mục 3.4.1): hiếm, không ở đường nóng, hỏi thẳng task-svc và annotation-svc trước khi ký quỹ được hoàn.

---

## 5. Cross-cutting

| Mối quan tâm | Giải pháp |
|---|---|
| Outbox | Bảng `outbox(id, event_type, payload, occurred_at, published_at)` ghi cùng transaction nghiệp vụ; `BackgroundService` (C#) / worker (Python, Node) đẩy sang RabbitMQ. Event **đã gửi** bị xoá sau `outbox.retention` (mục 3.14, L1) |
| Idempotency | Bảng `processed_events(event_id PK, handler, processed_at)` ở mọi consumer; dấu vết bị xoá sau `consumers.processed_retention` (mặc định 14 ngày — dài hơn mọi lần giao lại) |
| Truy vết | `correlationId` sinh ở gateway, xuyên mọi hop và mọi event; OpenTelemetry → Jaeger |
| AuthN | JWT access 15 phút + refresh rotation. Gateway verify chữ ký; service verify claim. JWKS lấy từ identity-svc |
| AuthZ — chiều dọc | RBAC (FC-07), claim trong token, kiểm ở từng service |
| AuthZ — chiều ngang | `project_members` + **một predicate dùng chung** cho kiểm-một-bản-ghi và lọc-danh-sách (mục 3.8). Chống BOLA |
| Chuyển trạng thái | Máy trạng thái khai báo (định nghĩa trong code, validate lúc khởi động). **Bắt buộc optimistic concurrency**: `UPDATE ... WHERE id=:id AND state=:expected`, kiểm `rowcount`. Hook chạy trong cùng transaction hoặc qua outbox — xem `VD-D-11` |
| Đơn phê duyệt | 8 luồng duyệt (KYB, xác minh danh tính, publish, rút, khiếu nại, tranh chấp, báo link, giới thiệu nghi lạm dụng): mỗi service giữ bảng trạng thái và API duyệt của loại đơn nó sở hữu; `admin-svc` nghe event dựng read model `approval_requests` để có MỘT màn hình duyệt (3.17). Không dựng khung `IRequestProcessor` dùng chung — xem lý do ở 3.17 |
| Nhật ký nghiệp vụ | `state_histories(entity_type, entity_id, from, to, action, actor, at)` cho mọi vòng đời — phục vụ FL-09, FM-06. `diagnostic_logs` ghi đầu vào của mỗi lần tính CPM để trả lời "sao doanh thu thấp thế" (FS-07). Lỗi hệ thống đi Serilog/OTel, **không ghi vào DB** |
| Config | **Hai tầng** (mục 3.10): biến môi trường / appsettings chỉ giữ thứ gắn với hạ tầng (chuỗi kết nối, host, khóa bí mật, cổng, topology queue); **mọi tham số nghiệp vụ và vận hành** (phí, tự duyệt, hạn mức, thời hạn, chu kỳ worker, kích thước lô…) là setting do `admin-svc` quản, phát `setting.changed` |
| Migration | EF Core (C#) · Alembic (Python) · Prisma (Node) — mỗi service tự quản, chạy lúc khởi động |
| Kiểm thử | Unit + Testcontainers cho integration; contract test dựa trên `contracts/` |
| Định nghĩa event | `eventType` và `version` khai bằng `static abstract` ngay trong payload record (`IEventPayload`), không truyền chuỗi ma thuật. Gắn nhầm eventType cho payload là **lỗi biên dịch** |
| Hợp đồng nghiêm ngặt | `UnmappedMemberHandling = Disallow` cho **cả** envelope lẫn payload: trường lạ làm message rơi vào DLQ thay vì bị bỏ qua âm thầm. Hệ quả — thêm trường vào payload là **breaking change**, phải deploy đồng loạt. Chấp nhận có chủ đích, kèm điều kiện **cảnh báo độ sâu DLQ** (xem `VD-D-12`) |
| Payload record | Ở `Crowd.Contracts`, tách khỏi `BuildingBlocks` vì nhịp thay đổi khác nhau. Quy ước: `sealed record`, mọi thuộc tính `required` hoặc nullable, tiền là `long` số nguyên đồng — `Crowd.Contracts.Tests` quét reflection ép cả bốn luật |

---

## 6. Cấu trúc thư mục

```
datn/
├─ docs/
│  └─ kien-truc-backend.md
├─ contracts/                       ← hợp đồng giữa 3 ngôn ngữ
│  ├─ labeling/                     ← JSON Schema của tập nhãn + kết quả từng loại công cụ
│  └─ events/
│     ├─ envelope.schema.json
│     ├─ CATALOG.md                 ← danh mục event đầy đủ
│     └─ <event_type>.schema.json
├─ shared/
│  ├─ building-blocks/              ← Crowd.BuildingBlocks: hạ tầng dùng chung
│  │                                  envelope, outbox, idempotency, correlation, auth
│  ├─ contracts/                    ← Crowd.Contracts: ~55 payload record
│  ├─ labeling/                     ← Crowd.Labeling: TẬP NHÃN (LabelSchema: loại dữ liệu + công cụ)
│  │                                  và NHÃN (LabelPayload) — kiểm, chấm câu vàng, gộp kết quả
│  ├─ persistence/                  ← Crowd.BuildingBlocks.Persistence: outbox, idempotency (EF Core)
│  ├─ seeding/                      ← Crowd.Seeding: kịch bản dữ liệu mẫu (chỉ Development)
│  ├─ auth/                         ← Crowd.BuildingBlocks.Auth
│  └─ test/                         ← mọi project test của shared gom một chỗ
│     ├─ building-blocks-tests/
│     ├─ contracts-tests/           ← canh quy ước cho mọi payload
│     ├─ labeling-tests/
│     ├─ persistence-tests/
│     └─ seeding-tests/
├─ Directory.Build.props             ← thuộc tính chung mọi project C#
├─ Directory.Packages.props          ← Central Package Management: phiên bản gói ở MỘT chỗ
├─ datn.slnx
├─ services/
│  ├─ gateway/  web-bff/  identity/  project/  task/  annotation/
│  ├─ ledger/   payment/  link/      gate/     media/ notification/  admin/
│  ├─ quality/  ml/       fraud/     ← Python (FastAPI; quality: app/, migrations/*.sql, tests/)
│  └─ collab/                        ← Node/TS (Fastify + ws + Yjs)
├─ docker-compose.infra.yml         ← core + 15 container Postgres (theo profile)
└─ docker-compose.yml               ← 17 service nghiệp vụ
```

Bật hạ tầng theo phase đang làm:

```bash
docker compose -f docker-compose.infra.yml up -d                # core (6 container)
docker compose -f docker-compose.infra.yml --profile p0 up -d   # core + 4 DB của P0
docker compose -f docker-compose.infra.yml --profile all up -d  # core + 15 DB
```

### 6.1. Bố cục project theo độ phức tạp

Không áp một khuôn cho cả 13 service C#. **Tiêu chí:**

> Service có **quy tắc nghiệp vụ cần kiểm thử mà không cần database** → 4 project.
> Service chủ yếu là CRUD và gọi ra ngoài → 2 project.

| Bố cục | Project | Service |
|---|---|---|
| **4 project** | `X.Api` · `X.Domain` · `X.Infrastructure` · `X.Tests` | `ledger` · `project` · `task` · `annotation` · `payment` · `gate` |
| **2 project** | `X.Api` · `X.Tests` | `gateway` · `web-bff` · `identity` · `link` · `media` · `notification` · `admin` |

Tổng: 6×4 + 7×2 = **38 project C#** (+ `Platform.BuildingBlocks`).

Lý do từng nhóm 4 project:

| Service | Logic cần test không cần DB |
|---|---|
| `ledger` | Ghi kép, bất biến `SUM = 0`, quy tắc escrow, làm tròn phần dư. Cũng là mục tiêu của `NC-B-01` (TLA+) và `NC-B-04` (DST) |
| `project` | Máy trạng thái vòng đời, công thức ký quỹ, orchestrator saga publish |
| `task` | Quy tắc eligibility, vòng đời lease, chuyển trạng thái redundancy |
| `annotation` | Validate payload đa hình theo `label_schema`, luồng duyệt/khiếu nại |
| `payment` | `Infrastructure` tách adapter VNPay/MoMo ra sau interface → **mock được cổng thanh toán trong test**, đây mới là lý do chính |
| `gate` | Chấm câu vàng và phát token — phải test được mà không cần ClickHouse lẫn Redis |

Service 2 project tổ chức nội bộ theo **lớp** kiểu controller/service/helper — không dùng Minimal API:

```
X.Api/
├─ Controllers/     ← [ApiController]: kiểm đầu vào (qua helper), gọi service, đổi kết quả thành mã HTTP
├─ Services/        ← nghiệp vụ; trả kết quả nghiệp vụ, không biết gì về HTTP
├─ Helpers/         ← hàm tĩnh dùng lại: validator, correlationId…
├─ Dtos/            ← hình dạng request/response
├─ Entities/        ← entity EF Core
├─ Settings/        ← lớp Options đọc từ appsettings
├─ Persistence/     ← DbContext, Configurations/, Migrations/
└─ Program.cs       ← composition root: đăng ký DI + AddControllers/MapControllers
```

Service 4 project cũng dùng Controllers ở tầng `Api`; nghiệp vụ nằm ở `Domain` (DDD) thay vì `Services/`.

Ba service Python (`quality`, `ml`, `fraud`) và một service Node (`collab`) có bố cục riêng, không thuộc bảng này.

**Quy ước đặt tên:** tiền tố sản phẩm `Crowd.` + tên service + vai trò → `Crowd.Ledger.Api`, `Crowd.Ledger.Domain`, …

**Một ngoại lệ bắt buộc:** `task-svc` có assembly là **`Crowd.Tasking.*`**, không phải `Crowd.Task.*`. Lý do là ràng buộc của C#: trong `namespace Crowd.Task.Api`, định danh `Task` phân giải về namespace `Crowd.Task` trước khi tới `System.Threading.Tasks.Task`, nên `async Task Foo()` không biên dịch được. Thư mục vẫn giữ `services/task/`, tên service, container và database vẫn là `task-svc` / `task_db` để khớp toàn bộ tài liệu.

**Đồ thị tham chiếu** (service 4 project):

```
Domain           ← không tham chiếu gì cả, kể cả BuildingBlocks (giữ thuần nghiệp vụ)
Infrastructure   → Domain, BuildingBlocks
Api              → Domain, Infrastructure, BuildingBlocks     (Api là composition root)
Tests            → Api, Domain
```

Service 2 project: `Api → BuildingBlocks`, `Tests → Api`.

---

## 7. Lộ trình

| Phase | Service dựng thêm | Kiểm chứng được |
|---|---|---|
| **P0** | gateway, identity, project, task, annotation | Chạy end-to-end **mọi loại dữ liệu** (ảnh, văn bản, âm thanh, video, cặp) với 15 loại công cụ nhãn trên 6 loại dữ liệu (mục 3.9, 3.15). Tạo dự án → upload → gán nhãn → xem kết quả, xuất JSON / CSV / COCO |
| **P1** | ledger, payment | Vòng tiền khép kín: nạp → ký quỹ → duyệt nhãn → hold → rút |
| **P2** | link, gate | Kênh thu thập thứ hai + chống lạm dụng (**đã có**, mục 3.6) |
| **P3** | quality | Chất lượng đo được bằng số: đồng thuận, Krippendorff alpha, Dawid–Skene, redundancy thích ứng, câu vàng kiểm tra, uy tín (**đã có**, mục 3.7) |
| **P4** | ml, fraud | Pre-labeling + active learning (SAM để sau cùng) |
| **P5** | collab | Kênh thu thập thứ ba |
| **P6** | media, notification, admin | Watermark, audit, export thêm định dạng (YOLO, Pascal VOC, CoNLL), bảng quản trị; workspace frontend cho từng công cụ. **Đã có:** admin-svc (setting), notification-svc (thông báo + email), KYB và khoá tài khoản ở identity (mục 3.11) |

P0→P3 là lõi bảo vệ được của đồ án. Nếu thời gian ép, cắt SAM (FA-03) và collab realtime (mục 2.5) trước — tốn công nhất trên mỗi điểm giá trị.

### 7.1. Mở rộng sau đồ án

Đặc tả ở [Phần III](../dac-ta-he-thong-gan-nhan-cong-dong.md#phần-iii-hướng-phát-triển-mở-rộng). Các service dưới đây là **dự kiến**: ranh giới và datastore sẽ chốt bằng bảng phương án khi bắt đầu từng phase, giống cách đã làm với các phase trên.

| Phase | Nội dung | Service dự kiến | Tái dùng |
|---|---|---|---|
| **P7** | Chợ dữ liệu cơ bản (FD-01→07): phiên bản bộ dữ liệu, thẻ dữ liệu, công khai / bán, quyền truy cập, tải có dấu vân tay | `catalog-svc` (listing, phiên bản, entitlement, đơn hàng) | ledger (tiền, escrow), payment, media (xem trước có watermark), quality (chỉ số chất lượng) |
| **P8** | Gán nhãn để mở khóa + chia doanh thu cho labeler (FD-08, FD-09); đặt thêm nhãn (FD-10) | mở rộng `catalog-svc` + ledger (tài khoản phi tiền tệ cho lượt tải) | task, annotation, quality (câu vàng chống farm lượt), fraud |
| **P9** | Code mức 1 (FN-01→03) + cuộc thi (FX-01) | `code-svc` (notebook, phiên bản, fork), `competition-svc` (bài nộp, chấm điểm, bảng xếp hạng) | MinIO, escrow cho giải thưởng |
| **P10** | Chạy code trong sandbox, cho thuê dữ liệu/code (FN-04, FN-05) | `runner` (hàng đợi job, sandbox cô lập, cụm máy tách riêng) | entitlement của P7 |
| **P11** | Compute-to-data (FN-06) | mở rộng `runner` + kiểm duyệt output | — |

Thay đổi lớn nhất về kiến trúc nằm ở P10: lần đầu hệ thống **chạy code do người dùng viết**. Cụm chạy sandbox phải tách hẳn khỏi cụm nghiệp vụ (mạng, máy, quyền truy cập datastore). Đây là một ranh giới bảo mật mới, cần ghi thêm vào [sổ vấn đề](van-de-can-giai-quyet.md) khi tới phase đó.

---

## 8. Stack

```
C#       .NET 10 (LTS) · ASP.NET Core Web API (Controllers) · EF Core (Npgsql) · MassTransit
         Ocelot · FluentValidation · Serilog · OpenTelemetry
Python   FastAPI · Arq (Redis) · numpy/scipy/scikit-learn · PyTorch (SAM)
Node     TypeScript · Fastify · ws · Yjs (+ y-redis cho multi-instance)
Store    PostgreSQL 16 · Redis 7 · RabbitMQ 3.13 · MinIO
Vận hành Docker Compose (dev) · OpenTelemetry → Jaeger · Mailpit (SMTP dev)
```
