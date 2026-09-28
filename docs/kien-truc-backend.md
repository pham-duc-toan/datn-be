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
| `link-svc` | 8107 | link_db | C# | links, campaigns, referrals, blacklist, reports | Bounded context của Sharer |
| `gate-svc` | 8108 | **ClickHouse** gate_db + redis-gate | C# | gate_sessions, click_events, cpm_entries | **Traffic ~100× phần còn lại**, sập độc lập |
| `media-svc` | 8109 | media_db | C# | signed_grants, access_audit, watermark_log, project_members_cache | CPU watermark + streaming |
| `notification-svc` | 8110 | notification_db | C# | notifications, templates, prefs | Fan-out chậm không được chặn ai |
| `admin-svc` | 8111 | admin_db | C# | review_queues, disputes, audit_log, platform_config, requests_readmodel | Quyền cao nhất — giới hạn blast radius |
| `quality-svc` | 8201 | quality_db | Python | consensus_state, confusion_matrix, gold_results | Ngôn ngữ khác + tính batch nặng |
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

## 3. Tám quyết định kỹ thuật cốt lõi

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
| annotation | PostgreSQL (JSONB + GIN) | 5404 | Nhãn **đa hình** theo loại bài toán |
| ledger | PostgreSQL | 5405 | ACID là toàn bộ lý do service này tồn tại |
| payment | PostgreSQL | 5406 | ACID |
| link | PostgreSQL | 5407 | CRUD quan hệ |
| **gate** | **ClickHouse** + **Redis riêng** | 8123 / 6381 | `click_events` append-only hàng triệu dòng/ngày, truy vấn 100% là aggregate theo thời gian (FS-07) |
| quality | PostgreSQL | 5421 | Ma trận tin cậy, khối lượng nhỏ |
| ml | PostgreSQL + **pgvector** | 5422 | Embedding cho active learning; model artifact để MinIO |
| fraud | PostgreSQL (`WITH RECURSIVE`) | 5423 | Bài toán đồ thị nhưng nông (2–3 hop) |
| collab | PostgreSQL (`bytea`) + **Redis riêng** | 5431 / 6382 | Yjs update là blob append-only; Redis giữ presence |
| media | PostgreSQL (partition theo tháng) | 5409 | Audit append-only, truy vấn theo `sample_id` |
| notification | PostgreSQL | 5410 | CRUD |
| admin | PostgreSQL | 5411 | Audit log + config |
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

`ledger-svc` là service duy nhất chạm số dư. Số dư là **view dẫn xuất từ bút toán**, không phải cột bị `UPDATE`:

```
Tài khoản:
  business:{id}:available   business:{id}:escrow
  labeler:{id}:pending      labeler:{id}:available
  sharer:{id}:available
  platform:fee              platform:tax_withheld     gateway:vnpay

Publish dự án      business:X:available → business:X:escrow
                   (số task × đơn giá × redundancy × (1 + phí))
Duyệt nhãn         business:X:escrow    → labeler:Y:pending
                   business:X:escrow    → platform:fee
Hết hold 3–7 ngày  labeler:Y:pending    → labeler:Y:available
Rút ≥ 2 triệu      labeler:Y:available  → gateway:vnpay
                   labeler:Y:available  → platform:tax_withheld   (10% TNCN)
Hủy dự án          business:X:escrow    → business:X:available    (phần chưa dùng)
CPM cổng link      business:X:escrow    → sharer:Z:pending
```

Bất biến kiểm được bằng một câu SQL: `SUM(amount) GROUP BY journal_entry_id = 0`. Job đối soát cuối ngày (FM-05) chạy chính câu này.

Mọi API tiền nhận header `Idempotency-Key`. Webhook VNPay/MoMo có thể đến 3 lần — `UNIQUE(provider, provider_txn_id)` chặn ghi trùng.

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
| Hủy dự án | project → task (dừng cấp) → ledger (hoàn phần chưa dùng) | — (chỉ đi tới) |
| Rút tiền | ledger (giữ) → payment (gọi ngoài) → ledger (chốt hoặc hoàn) | `payout.failed` → trả về số dư khả dụng |
| Vượt link | gate → annotation (ghi nhãn) + ledger (CPM) | Không cần: at-least-once + consumer idempotent |
| Chốt phiên collab | collab → annotation (1 bản chung) → ledger (chia theo đóng góp) | Leader chỉnh ±20% → bút toán điều chỉnh, ghi log |

### 3.5. Lease task 15 phút (FL-06)

**Postgres là nguồn sự thật DUY NHẤT của lease** — mọi bước là một transaction có khóa dòng:

```
POST /tasks/projects/{projectId}/next
  1. Điều kiện: đọc bản sao trong task_db (project_snapshots, project_members_cache,
     labeler_cache) — thiếu dữ liệu thì TỪ CHỐI (VD-D-04)
  2. SELECT ... ORDER BY random() LIMIT 1 FOR UPDATE SKIP LOCKED   (VD-T-06)
     không khóa được mà vẫn còn ứng viên → thử lại vài lần, không báo "hết task" oan
  3. INSERT assignment(state=Leased, expires_at = now + 15m) + tăng active_lease_count
     UNIQUE (project, labeler) WHERE Leased: mỗi người giữ tối đa 1 task/dự án

POST /tasks/assignments/{id}/submit
  SELECT ... FOR UPDATE assignment → Leased và chưa hết hạn? → Submitted   (VD-T-01)
  → phát assignment.submitted (annotation-svc lưu nhãn)

Reaper (30s/lần): Leased quá hạn → Expired, trả chỗ về pool (SKIP LOCKED, chạy nhiều bản được)
```

**Vì sao không dùng Redis cho lease (khác bản thiết kế đầu):** câu UPDATE có điều kiện trên dòng đã khóa đã nguyên tử sẵn. Thêm Redis tạo **hai** nguồn sự thật phải đồng bộ — VD-T-02 chính là lỗi hai nguồn đó lệch nhau. `redis-task` để dành cho cache eligibility khi hàng đợi lên hàng triệu task (VD-T-07, P2).

### 3.6. Cổng vượt link: đường nóng phải rỗng

`gate-svc` chịu tải cao nhất, request redirect **không chạm Postgres**:

```
Bước 0  Cloudflare Turnstile (ở gateway)   → bot rẻ tiền chết ở đây, chưa tốn task nào
Bước 1  Đếm ngược 5–10s + banner
Bước 2  GET  /gate/{code}/questions
          → k câu vàng + m câu thật; cache Redis 60s theo projectId
          → đáp án câu vàng KHÔNG BAO GIỜ ra client
Bước 3  POST /gate/{code}/submit
          → chấm chỉ câu vàng, server-side
          → trượt: cấp bộ mới, không token, không doanh thu
          → đạt : JWT{jti, linkId, aud=domain, exp=180s}
                  SET jti EX 180          (Redis, chống replay)
                  publish gate.solved + click.validated   (ASYNC — không chặn response)
Bước 4  GET  /go/{code}?t=JWT
          → Lua: DEL jti (dùng một lần) → 302 tới link đích

Chống trùng IP:  SETNX click:{linkId}:{sha256(ip+salt+day)} EX 86400
```

Doanh thu CPM cộng bởi consumer, không nằm trong request path. Đúng như đặc tả: bot không giải được nhãn → không sinh nhãn hợp lệ → CPM tự về 0, không cần tầng chống bot riêng.

### 3.7. Redundancy thích ứng — vòng lặp event giữa hai service

Đây là chỗ ranh giới service lộ ra rõ nhất: `task-svc` **sở hữu** số redundancy, `quality-svc` **quyết định** thay đổi nó.

```
annotation-svc ──annotation.submitted───► quality-svc
task-svc      ──task.redundancy_reached─►    │
                                             │ đủ n bản?
                                             ├─ khớp       → consensus.reached ───┐
                                             └─ tranh chấp → redundancy.increase_requested
                                                              (n → 5, hoặc reviewer cấp cao)
task-svc ◄────────────────────────────────────────────────────────────────────────┘
annotation-svc ◄──consensus.reached── chốt nhãn → annotation.approved → ledger-svc chi trả
```

Dawid–Skene / MACE chạy **batch** (cron 15 phút + lúc kết dự án) để cập nhật ma trận tin cậy từng labeler, không chạy trong request.

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

Ranh giới đọc/ghi: quyết định **tiền bạc** luôn hỏi lại chủ sở hữu dữ liệu, không bao giờ đọc từ bản sao.

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
| Config | Biến môi trường + `admin-svc` phát `platform_config.changed` cho tham số nghiệp vụ (phí, đơn giá tối thiểu, ngưỡng rút) |
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
│  └─ events/
│     ├─ envelope.schema.json
│     ├─ CATALOG.md                 ← danh mục event đầy đủ
│     └─ <event_type>.schema.json
├─ shared/
│  ├─ building-blocks/              ← Crowd.BuildingBlocks: hạ tầng dùng chung
│  │                                  envelope, outbox, idempotency, correlation, auth
│  ├─ contracts/                    ← Crowd.Contracts: ~55 payload record
│  ├─ persistence/                  ← Crowd.BuildingBlocks.Persistence: outbox, idempotency (EF Core)
│  ├─ auth/                         ← Crowd.BuildingBlocks.Auth
│  └─ test/                         ← mọi project test của shared gom một chỗ
│     ├─ building-blocks-tests/
│     ├─ contracts-tests/           ← canh quy ước cho mọi payload
│     └─ persistence-tests/
├─ Directory.Build.props             ← thuộc tính chung mọi project C#
├─ Directory.Packages.props          ← Central Package Management: phiên bản gói ở MỘT chỗ
├─ datn.slnx
├─ services/
│  ├─ gateway/  web-bff/  identity/  project/  task/  annotation/
│  ├─ ledger/   payment/  link/      gate/     media/ notification/  admin/
│  ├─ quality/  ml/       fraud/     ← Python (FastAPI + Arq worker)
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
| **P0** | gateway, identity, project, task, annotation | Chạy end-to-end **một loại bài toán: phân loại ảnh**. Tạo dự án → upload → gán nhãn → xem kết quả |
| **P1** | ledger, payment | Vòng tiền khép kín: nạp → ký quỹ → duyệt nhãn → hold → rút |
| **P2** | link, gate | Kênh thu thập thứ hai + chống lạm dụng |
| **P3** | quality | Chất lượng đo được bằng số: kappa, Dawid–Skene, redundancy thích ứng |
| **P4** | ml, fraud | Pre-labeling + active learning (SAM để sau cùng) |
| **P5** | collab | Kênh thu thập thứ ba |
| **P6** | media, notification, admin | Watermark, audit, export đa định dạng, bảng quản trị; workspace bounding box + NER |

P0→P3 là lõi bảo vệ được của đồ án. Nếu thời gian ép, cắt SAM (FA-03) và collab realtime (mục 2.5) trước — tốn công nhất trên mỗi điểm giá trị.

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
