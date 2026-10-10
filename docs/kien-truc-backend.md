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
identity_db project_db task_db  annotation_db  link_db   ClickHouse  media_db
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
| `media-svc` | 8109 | media_db | C# | signed_grants, access_audit, watermark_log, project_members_cache | CPU watermark + streaming |
| `notification-svc` | 8110 | notification_db | C# | notifications, templates, prefs | Fan-out chậm không được chặn ai |
| `admin-svc` | 8111 | admin_db | C# | **settings, setting_history** (đã có); review_queues, disputes, audit_log, requests_readmodel (P6) | Chủ của setting hệ thống (mục 3.10). Quyền cao nhất — giới hạn blast radius |
| `quality-svc` | 8201 | quality_db | Python | consensus_rounds, label_agreements, gold_answers, ds_skills, project_metrics, reputations | Ngôn ngữ khác + tính batch nặng |
| `ml-svc` | 8202 | ml_db (pgvector) | Python | models, predictions, uncertainty_scores | GPU, scale độc lập, sập vẫn chạy được hệ thống |
| `fraud-svc` | 8203 | fraud_db | Python | signals, fingerprints, farm_clusters | Batch + ML |
| `collab-svc` | 8301 | collab_db + redis-collab | Node | rooms, yjs_snapshots, op_log, contributions | **Stateful WebSocket**, sticky session, CRDT |

### 2.2. Ánh xạ sang mã chức năng đặc tả

| Service | Mã chức năng |
|---|---|
| identity | FC-01→08, FB-01, FL-01, FL-12 |
| project | FB-10→18, FB-24, FL-02, FL-03 |
| task | FL-06, FB-15, FQ-03 |
| annotation | FL-04, FL-05, FL-07, FL-08, FL-09, FB-21, FB-25 |
| ledger | FB-04, FB-05, FL-10, mục 2.11 |
| payment | FB-03, FL-11, FS-09, FM-05 |
| link | FS-01→08, mục "Kiểm duyệt link đích" |
| gate | Toàn bộ luồng "Trang vượt link", công thức CPM, chống lạm dụng |
| quality | FQ-01→04, FB-22, FA-04 |
| ml | FA-01→05 |
| collab | FR-01→09 |
| media | FP-02→04, FP-06 |
| notification | FC-06 |
| admin | FM-01→09, FB-02, FP-01, FP-05, mục 2.9 |
| fraud | FM-07, chống farm và lạm dụng referral |

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
  3. đạt cả hai → Completed / Cancelled → project.completed / project.cancelled → ledger hoàn ký quỹ
  chưa đạt → 409 chua_the_dong + closeCheck (lý do, số liệu, hạn khiếu nại cuối)
  gọi nội bộ lỗi → 503 (không kiểm được thì không đóng)
```

- **Đóng sổ** chặn mọi thao tác làm đổi tiền sau đó: duyệt, duyệt hàng loạt, từ chối, khiếu nại, phân xử (409 `du_an_da_ket_thuc`). Các thao tác này đọc `project_terms` bằng `FOR SHARE` trong cùng transaction, nên một khiếu nại chen vào lúc đang đóng sổ hoặc đã commit trước (đóng sổ thấy nó và từ chối đóng), hoặc phải chờ và thấy dự án đã đóng.
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

Hai chính sách mới áp cho công cụ chọn một (phân loại một lớp, so sánh cặp). Chạm trần mà chưa đủ tin cậy thì task là **tranh chấp**, để người duyệt xử lý. Thí nghiệm trên 5 bộ dữ liệu công khai và mô phỏng ([NC-D-01](thi-nghiem/nc-d-01-redundancy-thich-ung.md)): để đạt cùng độ chính xác với cố định n nhãn, `posterior` / `voi` cần ít hơn 29–76% nhãn. Mặc định vẫn là `majority`, vì khi mọi labeler đều mới (chưa có bằng chứng về độ chính xác) thì `posterior` đắt hơn.

**Đánh giá một task được xếp hàng theo task** (`pg_advisory_xact_lock`): hai nhãn cuối của cùng task xử lý song song thì mỗi transaction chỉ thấy nhãn của mình, cả hai đếm thiếu và task kẹt vĩnh viễn không có đồng thuận. Lỗi này lộ ra khi chạy E2E tổng thể; phép thử ép race (40 task, hai người nộp đồng thời) cho 40/40 task có kết quả.

Gộp tự động hiện chỉ cho `classification` và `pairwise` (đa số tuyệt đối). Công cụ khác (khung, chép lời…) cho trạng thái `notApplicable` — gộp chúng (WBF, ROVER) là việc tiếp theo.

Dawid–Skene / Krippendorff alpha chạy **batch** (mặc định 15 phút, admin chạy tay được) trên các công cụ một giá trị (phân loại một lớp, so sánh cặp); labeler cần ≥ 5 nhãn trong dự án mới được chấm DS (VD-Q-07). Không chạy trong request.

Giới hạn hiện tại: quality-svc chỉ biết các dự án publish **sau khi** nó chạy (cộng 5 dự án seed). Dự án cũ hơn được bỏ qua kèm cảnh báo, không vào DLQ.

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
| Q1 | Mô hình hóa "loại bài toán" | **Loại dữ liệu × công cụ**: dự án có một `modality`; tập nhãn là danh sách công cụ (`classification`, `bbox`, `polygon`, `span`, `transcription`, `temporalSegment`, `pairwise`) | Một màn hình thường cần nhiều công cụ cùng lúc (phân loại cảm xúc + gắn thực thể). Liệt kê từng "task type" ghép sẵn sẽ bùng nổ tổ hợp |
| Q2 | Kiểm nhãn ở đâu | **JSON Schema cho hình dạng** ([contracts/labeling](../contracts/labeling)) + **code C# cho ngữ nghĩa** (lớp có trong tập nhãn, khung nằm trong ảnh, đoạn nằm trong audio) | Schema dùng chung được cho frontend; luật phụ thuộc mẫu thì schema không diễn đạt được |
| Q3 | Nạp file lớn | **Upload thẳng lên MinIO bằng link ký sẵn + manifest + worker nền** (ffprobe đo thời lượng) | Video vài GB không đi qua service. ZIP trong request chỉ giữ cho ảnh nhỏ |
| Q4 | Lưu mẫu | `samples` thêm `modality`, `content jsonb` (text/pair), `metadata jsonb`; `storage_key` nullable; CHECK đúng một trong hai | Văn bản không cần file; metadata (kích thước, thời lượng, đoạn) là thứ server cần để kiểm nhãn |
| Q5 | Bản sao ở task / annotation | `label_schema jsonb` chép nguyên từ `project.published` | Kiểm nhãn lúc nộp và gộp kết quả không phải gọi HTTP sang project-svc (luật 1 mục 4) |
| Q6 | Media dài | **Cắt đoạn** theo `segmentSeconds`, mỗi đoạn một task, giá theo task | Labeler không phải nghe 1 giờ cho một task; tiền và redundancy giữ nguyên mô hình cũ |
| Q7 | Chấm câu vàng / gộp kết quả | Chấm trong C# theo từng công cụ: tập lớp bằng nhau, bbox IoU ≥ 0,5, polygon IoU, span F1, CER ≤ 0,1, IoU thời gian; ngưỡng đổi bằng `matchThreshold`. Gộp: đa số cho phân loại / cặp; công cụ khác **chưa gộp tự động**, kết quả là các nhãn đã duyệt | Gộp khung / bản chép (WBF, ROVER, Dawid–Skene) là việc của quality-svc (P3) |

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
| S2 | Kiểu, mặc định, giới hạn khai ở đâu | **Catalog trong code** (`Crowd.BuildingBlocks.Settings.SettingCatalog`, 108 khóa): kiểu (`bool/int/long/double/durationSeconds/text`), mặc định, min/max, danh sách lựa chọn (`choices`, cho khoá text dạng enum), đơn vị, nhóm, mô tả, hiệu lực (`newOperations` / `restart`) | Kiểm giá trị trước khi lưu (phí 95 % → 400). Thêm khóa = thêm một dòng code + test, không cần migration. Python đọc bản xuất `shared/settings/catalog.json` (test C# canh file luôn khớp) |
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
| Outbox | Bảng `outbox(id, event_type, payload, occurred_at, published_at)` ghi cùng transaction nghiệp vụ; `BackgroundService` (C#) / worker (Python, Node) đẩy sang RabbitMQ |
| Idempotency | Bảng `processed_events(event_id PK, handler, processed_at)` ở mọi consumer |
| Truy vết | `correlationId` sinh ở gateway, xuyên mọi hop và mọi event; OpenTelemetry → Jaeger |
| AuthN | JWT access 15 phút + refresh rotation. Gateway verify chữ ký; service verify claim. JWKS lấy từ identity-svc |
| AuthZ — chiều dọc | RBAC (FC-07), claim trong token, kiểm ở từng service |
| AuthZ — chiều ngang | `project_members` + **một predicate dùng chung** cho kiểm-một-bản-ghi và lọc-danh-sách (mục 3.8). Chống BOLA |
| Chuyển trạng thái | Máy trạng thái khai báo (định nghĩa trong code, validate lúc khởi động). **Bắt buộc optimistic concurrency**: `UPDATE ... WHERE id=:id AND state=:expected`, kiểm `rowcount`. Hook chạy trong cùng transaction hoặc qua outbox — xem `VD-D-11` |
| Đơn phê duyệt | 7 luồng duyệt (KYB, nạp, rút, publish, khiếu nại, tranh chấp, báo link) dùng chung khung `IRequestProcessor` trong `shared/`. **Mỗi service một bảng `requests` riêng** cho loại đơn nó sở hữu; `admin-svc` nghe event dựng read model để có MỘT màn hình duyệt thống nhất |
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
| **P0** | gateway, identity, project, task, annotation | Chạy end-to-end **mọi loại dữ liệu** (ảnh, văn bản, âm thanh, video, cặp) với 7 loại công cụ nhãn (mục 3.9). Tạo dự án → upload → gán nhãn → xem kết quả, xuất JSON / CSV / COCO |
| **P1** | ledger, payment | Vòng tiền khép kín: nạp → ký quỹ → duyệt nhãn → hold → rút |
| **P2** | link, gate | Kênh thu thập thứ hai + chống lạm dụng (**đã có**, mục 3.6) |
| **P3** | quality | Chất lượng đo được bằng số: đồng thuận, Krippendorff alpha, Dawid–Skene, redundancy thích ứng, câu vàng kiểm tra, uy tín (**đã có**, mục 3.7) |
| **P4** | ml, fraud | Pre-labeling + active learning (SAM để sau cùng) |
| **P5** | collab | Kênh thu thập thứ ba |
| **P6** | media, notification, admin | Watermark, audit, export thêm định dạng (YOLO, Pascal VOC, CoNLL), bảng quản trị; workspace frontend cho từng công cụ |

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
