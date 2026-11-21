# Kế hoạch: chợ dự án kiểu feed, gợi ý theo khả năng kiếm tiền, bỏ trần người làm

Ghi ngày 20/11/2026. Tổng hợp các thay đổi đã bàn, mọi điểm đều đi theo phương án được khuyến nghị. Những điểm còn cần chốt nằm ở cuối file (mục 9). Khi làm xong từng giai đoạn sẽ ghi quyết định chính thức vào `kien-truc-backend.md` (mục mới 3.38) và đặc tả.

---

## 1. Mục tiêu

| # | Vấn đề | Kết quả mong muốn |
|---|---|---|
| 1 | Labeler thấy số người đang làm / đã nộp nên nản và bỏ cuộc | Labeler chỉ thấy **số suất thưởng, tiền thưởng, hạn chót** |
| 2 | Trần người làm = suất × hệ số; đủ trần thì đóng thu bài | **Không giới hạn người làm**. Thu bài chỉ dừng khi **hết hạn** hoặc **doanh nghiệp kết thúc sớm** |
| 3 | Chợ dự án là lưới thẻ ít thông tin | **Feed lướt** kiểu Facebook / TikTok, nhiều thông tin, có mẫu xem trước, tối ưu responsive |
| 4 | Gợi ý chỉ dựa trên thẻ trùng + đơn giá | Gợi ý **theo khả năng kiếm tiền**, riêng cho từng người, học từ hành vi, nhanh khi nhiều người dùng |
| 5 | Thẻ kỹ năng sửa được bất cứ lúc nào | Chỉ chọn **lúc đăng ký**, sau đó ẩn đi, hệ thống tự học |
| 6 | "Xem như labeler" chỉ là mẫu minh hoạ | Doanh nghiệp xem **đúng như labeler**, bằng dữ liệu thật của dự án |

---

## 2. Các quyết định (theo khuyến nghị)

| Chủ đề | Chọn | Tóm tắt |
|---|---|---|
| Ai được suất thưởng | **R2** | n bài **đạt** có mức **giống đa số cao nhất**; bằng nhau thì bài nộp sớm hơn. Thay luật cũ "n bài đạt nộp sớm nhất". "Lấp suất trống" (`fill_slots_min_percent`) giữ nguyên |
| Hạn chót | **Bắt buộc** | Không có hạn thì dự án không bao giờ đóng. Thêm setting `project.max_duration_days` (đề xuất 30) |
| Bố cục chợ | **F3** | Điện thoại: mỗi dự án một màn, vuốt dọc (scroll-snap). Máy tính: feed một cột + khung chi tiết bên phải |
| Mẫu xem trước | **P1** (sửa 21/11) | Hệ thống **tự lấy 1 mẫu** thật (không phải câu vàng), doanh nghiệp chỉ bật / tắt trong thẻ "Bảo vệ nội dung & NDA" (mặc định bật). Thẻ luôn hiện toàn bộ câu hỏi + công cụ. Không hiện mẫu với dự án có NDA / nội dung nhạy cảm / riêng tư |
| Thuật toán gợi ý | **G1 → học dần** | Điểm = tiền kỳ vọng mỗi phút × mức hợp × hệ số mới. Ghi hành vi ngay từ đầu, giai đoạn sau ml-svc học trọng số |
| Đặt bộ gợi ý ở đâu | **`reco-svc` riêng ngay từ đầu** (người dùng chốt 20/11) | Service C# chuyên cho feed: Postgres `reco_db`, Redis riêng, ClickHouse database riêng. Đọc dự án qua view chủ mở, cập nhật bằng event. Chi tiết mục 5.0 |
| Học máy | **Hai loại, chọn theo lượng dữ liệu** (người dùng chốt 20/11) | Hồi quy logistic khi ít dữ liệu, LightGBM khi đủ nhiều. **Bỏ mạng nơ-ron** (cần nhiều dữ liệu và phần cứng). Chi tiết mục 8 |
| Tính feed | **Pull + cache** | Tính khi người dùng mở feed: phần dự án chung trong RAM, phần người trong Redis, danh sách đã xếp lưu Redis cho phiên lướt |
| Sở thích labeler | **Bộ đếm (a, b) + quên dần** | Thẻ chọn lúc đăng ký = số khởi đầu. Hành vi cộng dồn, chu kỳ bán rã 90 ngày |
| Ai giữ dữ liệu sở thích | **S1** | identity giữ thẻ khai báo (ghi một lần lúc đăng ký). Bộ gợi ý giữ phần học được, khởi tạo từ `user.registered` v2 |
| Phản hồi tiêu cực | **Có nút "Không quan tâm"** | Trên thẻ feed |
| Xem như labeler | **Q2** | Backend dựng "bài xem thử" giống bài thật, không ghi DB; nộp thử thì chấm câu vàng ngay |
| Dự án mới mở khi đang lướt | **B, không C** (người dùng chốt 21/11) | Tự chen vào trang kế tiếp (tối đa 2 / trang), không có thông báo "có dự án mới" ở đầu trang — tự nhiên như TikTok. Chi tiết mục 5.6 |
| Ô khám phá | **1 / 10 thẻ** (người dùng chốt 21/11) | Một thẻ "lạ" trong mỗi 10 thẻ: dự án mới chưa đủ người thấy, hoặc loại việc người này chưa từng làm — để thử sở thích và cho dự án mới được thấy |
| Phạt "thấy mà không mở" | **Bỏ** (người dùng chốt 21/11) | Lướt qua chỉ cộng nhẹ vào b của thẻ (mục 4.2), không phạt riêng từng dự án |
| Thẻ đã hiện mà dự án vừa đóng | **Giữ nguyên** (người dùng chốt 21/11) | Không làm mờ, không đẩy realtime. Bấm vào mới báo "đã ngừng nhận bài"; project-svc vẫn tự chặn khi bắt đầu làm. Thẻ CHƯA trả ra thì vẫn lọc bỏ lúc ghép trang |

---

## 3. Giai đoạn 1 — Bỏ trần người làm, giấu số người, luật thưởng R2

> **Đã làm (20/11/2026)** — quyết định chính thức ở `kien-truc-backend.md` mục 3.38.

Làm trước vì đổi luật chấm và luồng tiền; các phần sau dựa vào đây.

### Backend

**project-svc**
- `CollectionRound` ([CollectionRound.cs](../services/project/Crowd.Project.Domain/Collections/CollectionRound.cs)): bỏ `MaxParticipants` và lý do đóng `du_suat`. Chỉ còn `het_han` và `ket_thuc_som`.
- `LamBaiService.BatDauAsync`: bỏ kiểm "còn chỗ làm bài".
- `GoiYService.UngVien` ([GoiYService.cs](../services/project/Crowd.Project.Api/Services/GoiYService.cs)): bỏ điều kiện đếm bài so với trần.
- Publish / sửa dự án: **hạn chót bắt buộc**, phải nằm trong `project.max_duration_days` tính từ lúc publish.
- `GET /tasks/projects/{id}/slots` (`SlotsResponse`): với labeler chỉ trả bài của chính họ và giai đoạn dự án. **Bỏ** `submitted`, `available`, `maxParticipants`. Doanh nghiệp xem số liệu qua API tiến độ của chủ (giữ nguyên).
- Khiếu nại được chấp nhận: giữ luật "chỉ được thưởng nếu còn suất".

**quality-svc**
- [cham.py](../services/quality/app/cham.py) `chon_duoc_thuong`: xếp bài đạt theo `agreement_percent` giảm dần, bằng nhau thì `submitted_at` sớm hơn. Cập nhật docstring đầu file (luật 4).

**Setting / hợp đồng**
- Bỏ `project.participant_multiplier`: sửa `SettingCatalog.cs`, `TenSetting.cs`, `shared/settings/catalog.json`. Admin tự xoá khoá không còn trong catalog.
- Thêm `project.max_duration_days`.
- `project.collection_finished`: lý do đóng không còn `du_suat`. Nếu payload đổi thì tăng version và cập nhật `contracts/payloads.snapshot.txt` + `CATALOG.md`.

**Dữ liệu:** DB chỉ có seed nên xoá migration thừa / gộp lại, xoá DB rồi seed lại. Không viết script chuyển dữ liệu.

### Frontend (`D:\learn\fe-datn`)
- `cho/[id]/page.tsx`: bỏ dòng "Chỗ làm bài", bỏ nút "Đã đủ người làm". Dòng "Suất thưởng" đổi thành "n bài đạt giống đa số nhất được thưởng".
- `config-tab.tsx`, `param-help.tsx`: bỏ "tối đa n × hệ số người làm" và "nộp sớm nhất". Thêm ô hạn chót bắt buộc.
- `bai-lam/[id]/page.tsx`: bỏ "đủ người làm" khỏi danh sách lý do đóng thu bài.
- Tour trang làm bài: điểm dừng "Thời gian còn lại" bỏ ý "suất trả cho người khác".
- Tab Tổng quan của doanh nghiệp: bỏ "x / trần người làm", giữ số bài đã nộp và đang làm.

### Kiểm thử
- Unit: `services/quality/tests/test_cham.py` (thứ tự thưởng R2), test project (bắt đầu bài không còn bị chặn trần, hạn chót bắt buộc).
- E2E: `e2e_du_an_trung_tam.py` (bỏ ca "hết suất / đủ người", thêm ca "bài nộp muộn nhưng giống đa số hơn vẫn được thưởng"), `ep_race_quality.py`.

### Rủi ro và cách đỡ
- **Thao túng đa số bằng nhiều tài khoản** (trần cũ giới hạn việc này): giữ các tín hiệu gian lận chung IP / thiết bị. Ghi vào `van-de-can-giai-quyet.md`. Cân nhắc sau: tính đa số có trọng số theo uy tín (Dawid–Skene đã có).
- **Nhiều người làm mà không được thưởng:** vẫn được cộng uy tín. Tuỳ chọn sau: hiện "bạn đứng top x %" khi đã chấm xong.

---

> **Bổ sung 21/11/2026 — kiến trúc 3.40:** bản xem trước dùng chung watermark (tạo lúc duyệt dự án, cache công khai), thích dự án + mục "Dự án đã thích" + nhắc trước hạn chót 24 giờ, phạt mềm dự án đã lướt qua (× 0,5 / × 0,25, hết sau 3 ngày), hàng đợi hành vi lưu localStorage gửi mỗi 1 phút (bỏ trùng theo mã sự kiện).

## 4. Giai đoạn 2 — Ghi hành vi và học sở thích (nền cho gợi ý)

> **Đã làm (21/11/2026)** — `kien-truc-backend.md` mục 3.39. Khác kế hoạch: Redis `aff:{u}` là **cache sống 30 phút, bị xoá sau mỗi lần cộng** (bản gốc luôn là Postgres, cộng bằng SQL) thay vì bản ghi chính sống 7 ngày — nhiều instance cùng cộng không lệch. Thêm event `attempt.started`, `attempt.abandoned`, `owner_blocklist.changed`.

### 4.1. Thẻ chọn lúc đăng ký, sau đó ẩn

> **Đã làm (20–21/11/2026)**, kể cả `user.registered` v2 (thẻ đã chuẩn hoá → reco-svc khởi tạo bộ đếm). `GET /tags` mở công khai cho trang đăng ký.
- Form đăng ký (`(public)/dang-ky/page.tsx`): khi tick vai trò người gán nhãn thì có thêm bước chọn lĩnh vực / ngôn ngữ / loại dữ liệu. Bỏ qua được.
- identity: `POST /auth/register` nhận thêm thẻ; phát `user.registered` **v2** có thẻ đã chọn.
- Bỏ mục kỹ năng ở `tai-khoan/ho-so/page.tsx` và API `PUT /me/skills` (`MeController`, `HoSoService.CapNhatKyNangAsync`). Cột `users.skill_*` chỉ ghi một lần lúc đăng ký.
- Đặc tả FL-01 đổi thành "chọn sở thích khi đăng ký, sau đó hệ thống tự học".

### 4.2. Bộ đếm sở thích
Bảng gốc ở `reco_db` (của `reco-svc`), Redis riêng của `reco-svc` là bản đọc nhanh:

```
labeler_affinity (user_id, tag_kind, tag_code, a, b, updated_at)   PK (user_id, tag_kind, tag_code)
tag_kind ∈ domain | language | modality | tool_kind
```

- **Khởi tạo** (consumer `user.registered` v2): thẻ đã chọn → a = 3, b = 1. Thẻ không chọn → không lưu, coi như a = 1, b = 1.
- **Cập nhật cộng dồn**: trước khi cộng thì quên dần, `a = a × 0,5^(ngày / 90) + w` (b tương tự).

| Hành vi | Cộng vào | w |
|---|---|---|
| Bắt đầu làm | a | 0,2 |
| Nộp bài | a | 0,5 |
| Bài đạt | a | 1 |
| Bài trượt | b | 1 |
| Bỏ dở / hết giờ | b | 0,5 |
| "Không quan tâm" | b | 1 |
| Lướt qua không mở | b | 0,05 |

- Nguồn hành vi:
  - event sẵn có: `attempt.submitted`, `project.graded`;
  - event bắt đầu / bỏ dở của project-svc: kiểm xem đã có chưa, chưa có thì ghi thẳng trong cùng service;
  - frontend gửi lượt hiện thẻ / mở / thử / không quan tâm.
- **Mức hợp** của một thẻ = a / (a + b).

### 4.3. Ghi hành vi
- ClickHouse: database `reco` **do `reco-svc` sở hữu** (user riêng, chỉ có quyền trên database này). Bảng `reco_events (event_time, user_id, project_id, kind, position, session_id, model_version, ab_group)`, `TTL` 90 ngày + bảng gộp theo ngày.
- `POST /reco/events` (gateway chuyển tới `reco-svc`, gửi theo lô vài giây một lần). Frontend dùng IntersectionObserver để biết thẻ nào thật sự hiện trên màn.

### Kiểm thử
Unit cho công thức cộng dồn và quên dần. E2E: đăng ký có thẻ thì có số khởi đầu; nộp / đạt / trượt thì số đổi đúng.

---

## 5. Giai đoạn 3 — Bộ gợi ý G1 trong `reco-svc`

> **Đã làm (21/11/2026)** — mục 3.39. Cấp độ / uy tín cho bộ lọc lấy từ claim `level` / `reputation` mới thêm vào access token. Còn lại: chart Helm + HPA (làm cùng nhánh DevOps NC-A khi dựng K8s cho cả hệ thống), load test k6 + đo bộ nhớ thật (A-12).

### 5.0. Service `reco-svc`

```
gateway ──► GET /projects/feed, POST /reco/events ──► reco-svc (C#, nhiều instance, không giữ trạng thái riêng)
                                                        │
      project_db ── v_reco_projects (role reco_reader, pool ≤ 3) ──► RAM: dự án đang chạy + đặc trưng + dữ liệu thẻ (30 s)
      RabbitMQ   ── attempt.submitted, project.graded, member.*, user.registered v2, … ──► cập nhật sở thích + tập loại trừ
      Redis-reco ── sở thích (7 ngày), tập loại trừ từng người, feed đã xếp (15 phút)
      reco_db    ── labeler_affinity (bảng gốc), model_versions
      ClickHouse ── database reco: reco_events
      MinIO      ── file mô hình (trọng số JSON / ONNX) do ml-svc huấn luyện
```

**Dữ liệu sở hữu:** `reco_db` (Postgres riêng), Redis riêng, database ClickHouse `reco`. Không có bảng sao dữ liệu dự án.

**Đọc từ chủ khác:**
- **project_db:** view `v_reco_projects` (đúng cơ chế mục 3.32). Gồm dự án đang chạy, đặc trưng chấm điểm, dữ liệu hiển thị thẻ, `preview_sample_ids`, và **số bài đã nộp / đang làm tính sẵn trong view**. Số này chỉ dùng trong server, không bao giờ trả cho labeler.
- **identity:** cấp độ, xác minh, đủ 18 tuổi lấy từ claim của token. Thẻ đăng ký lấy từ `user.registered` v2.

**Quy tắc đường nóng: không truy vấn DB theo từng người.**
- Tập loại trừ (đã nộp, bị chặn, thành viên dự án riêng tư, danh sách chặn của doanh nghiệp) nằm trong Redis, cập nhật bằng event. Event nào còn thiếu (ví dụ thay đổi danh sách chặn của doanh nghiệp) thì thêm. Redis mất thì dựng lại bằng một truy vấn qua view, chỉ lúc thiếu.
- Link mẫu xem trước: tự ký token bằng thư viện `MediaLinks` dùng chung, không gọi media-svc.

**Vì sao tách service mà DB không quá tải:**
- Tải lên project_db từ feed **không tăng theo số lượt xem**: ~1 truy vấn / 30 giây / instance, pool bị chặn trần.
- Hiện tại `GoiYService` chạy một câu SQL nặng cho **mỗi** lượt tải feed: 300 lượt/giây là 300 truy vấn nặng/giây.
- Tách service còn cô lập CPU, luồng xử lý và pool kết nối: feed đông không làm chậm nộp bài, chấm điểm, tiền ở project-svc.
- Khi nút thắt là **ghi** vào project_db thì tách service không giúp; lúc đó tối ưu truy vấn / index, read replica, phân vùng.

**Khi `reco-svc` sập:** frontend chuyển sang `GET /projects/market` của project-svc. Endpoint này giữ lại ở dạng đơn giản: danh sách dự án mới nhất, có phân trang, không cá nhân hoá. Làm bài vẫn chạy bình thường.

**project-svc bỏ:** `GoiYService.GoiYAsync` và `GET /projects/recommended`. Thêm: view `v_reco_projects` + role `reco_reader`.

**Hạ tầng:** thêm vào `docker-compose.demo.yml` các container `reco`, `db-reco`, `redis-reco`, database ClickHouse `reco`. Thêm route gateway; mật khẩu `DB_READER_RECO_PASSWORD` trong `deploy/tao_env.py`. Nhánh DevOps: chart Helm + HPA scale theo CPU của `reco-svc`.

### 5.1. Công thức

```
tiền kỳ vọng = thưởng × P(đạt | labeler) × P(lọt suất | đạt)
điểm         = tiền kỳ vọng / phút ước tính × mức hợp × hệ số mới
```

| Thành phần | Cách tính | Ở đâu |
|---|---|---|
| P(đạt) | Từ bộ đếm loại công cụ / loại dữ liệu của labeler. Người mới thì dùng uy tín | Redis (phần người) |
| P(lọt suất) | ≈ min(1, số suất / (1 + đối thủ dự kiến đạt)), đối thủ = bài đã nộp + đang làm. **Chỉ server biết** | RAM (phần dự án) |
| Phút ước tính | Số câu × trung vị giây mỗi câu theo loại công cụ (từ bài đã nộp) | RAM |
| Mức hợp | Trung bình a/(a+b) trên các thẻ của dự án (lĩnh vực, ngôn ngữ, loại dữ liệu, loại công cụ) | Tính lúc request |
| Hệ số mới | **Bể lưu lượng**: dự án mới được hiện cho ~200 labeler phù hợp; tỉ lệ "thấy → bắt đầu" tốt thì tăng, kém thì giữ mức tối thiểu (không về 0) | RAM |

Xếp lại sau khi chấm điểm:
- tối đa 2 dự án cùng doanh nghiệp trong 10 thẻ đầu;
- xen các loại dữ liệu;
- bỏ dự án đã bấm "Không quan tâm".

### 5.2. Đường nóng (mục tiêu p95 < 50 ms)
1. Cấp độ / đã xác minh / đủ 18 tuổi lấy **từ claim của access token**. Không gọi gRPC identity mỗi request (cách cũ trong `GoiYService.LayToiAsync`).
2. Ứng viên lấy từ **RAM**: dự án đang chạy kèm đặc trưng, nạp từ `v_reco_projects` mỗi 30 giây. Phần riêng từng người (đã nộp, bị chặn, thành viên dự án riêng tư) là tập id trong Redis, cập nhật bằng event.
3. Chấm điểm + xếp lại trong bộ nhớ.
4. Lưu 200 id đã xếp vào Redis `feed:{userId}:{session}` (15 phút). Trang sau đọc theo con trỏ, không tính lại, không trùng thẻ.

### 5.3. API
- `GET /projects/feed?cursor=` (`reco-svc`) thay cho `GET /projects/recommended`. `GET /projects/market` của project-svc chỉ còn làm danh sách dự phòng khi `reco-svc` sập.
- Mỗi thẻ có `reasons` để hiện "Hợp vì: kiếm tốt / bạn hay đạt dạng này".
- Không trả số người làm.

### 5.4. Khi hệ thống lớn lên
| Khi | Làm |
|---|---|
| Nhiều người cùng lúc | Thêm instance (bộ gợi ý không giữ trạng thái riêng); chia Redis theo `userId` |
| Hàng chục nghìn dự án | Thêm tầng lấy ứng viên: chỉ mục ngược thẻ → dự án + nhóm hot + nhóm mới |
| Hàng trăm nghìn dự án | Lấy ứng viên bằng vector (MiniLM + pgvector của ml-svc) |
| Redis mất / đổi công thức | Dựng lại từ `labeler_affinity` hoặc chạy lại từ ClickHouse |

### 5.5. Ước lượng bộ nhớ và dung lượng

Các số dưới đây tính theo kích thước dữ liệu, chưa phải số đo thật. Số thật đo khi load test ở mục kiểm thử bên dưới.

**Mỗi phần tốn bao nhiêu**

| Phần | Nằm ở | Một đơn vị tốn | Ghi chú |
|---|---|---|---|
| Đặc trưng dự án để chấm điểm (thưởng, suất, đối thủ, phút ước tính, điều kiện, 3–8 thẻ) | RAM mỗi instance `reco-svc` | ~0,5 KB / dự án | Vài chục con số + mảng thẻ, thẻ lưu bằng số nguyên nhỏ |
| Dữ liệu hiển thị thẻ (tên, đoạn mô tả, câu hỏi công cụ) | RAM (nếu giữ) | ~2,5 KB / dự án | Chuỗi trong .NET tốn 2 byte mỗi ký tự |
| Sở thích + tỉ lệ đạt của một labeler | Redis | ~1,5–2 KB / người | ~20–30 cặp `(a, b)` theo thẻ từng chạm tới + vài con số chung |
| Danh sách feed đã xếp của một phiên lướt | Redis, sống 15 phút | ~3,2 KB / phiên | 200 id × 16 byte, lưu nhị phân |
| Bảng gốc `labeler_affinity` | Postgres `reco_db` (ổ đĩa) | ~3 KB / người | ~30 dòng × ~100 byte, kể cả index |
| Hành vi (hiện thẻ, mở, thử…) | ClickHouse (ổ đĩa) | ~30 byte / sự kiện sau nén | ~100 lượt hiện thẻ / người / ngày, giữ 90 ngày |

Sở thích chỉ nằm trong Redis khi người đó **đang hoạt động**, hạn sống 7 ngày. Ai quay lại sau đó thì nạp lại từ Postgres, tốn một truy vấn.

**Theo ba mức quy mô**

| | Demo đồ án | Vừa | Lớn |
|---|---|---|---|
| Dự án đang chạy | 200 | 2.000 | 20.000 |
| Labeler (hoạt động / ngày) | 1.000 (300) | 100.000 (20.000) | 1.000.000 (200.000) |
| **RAM mỗi instance `reco-svc`** | ~0,6 MB | ~6 MB | ~60 MB (giữ cả hiển thị) / ~10 MB (chỉ phần chấm điểm) |
| **Redis: sở thích người hoạt động 7 ngày** | ~2 MB | ~100 MB | ~1 GB |
| **Redis: feed đang lướt** (~10 % người hoạt động trong 15 phút) | < 1 MB | ~6 MB | ~65 MB |
| **Tổng Redis** (× ~1,3 cho phân mảnh) | ~5 MB | ~150 MB | ~1,4 GB |
| Postgres `labeler_affinity` | ~3 MB | ~300 MB | ~3 GB |
| ClickHouse hành vi, 90 ngày | ~80 MB | ~5 GB | ~55 GB |

Cách đọc bảng:
- **RAM trong service không đáng kể.** 20.000 dự án mới tốn vài chục MB, trong khi một instance .NET lúc chạy đã dùng sẵn 100–200 MB.
- **Tải lên project_db không tăng theo số người lướt:** ~1 truy vấn / 30 giây / instance `reco-svc` (mục 5.0).
- **Redis tăng theo số người hoạt động, không theo tổng tài khoản.** 1 triệu tài khoản ≈ 1–1,5 GB, một máy Redis nhỏ là đủ.
- **ClickHouse lớn nhất nhưng là ổ đĩa**, rẻ, và có hạn giữ 90 ngày nên không phình mãi.

**Biện pháp giữ chi phí phẳng**

| Biện pháp | Tác dụng |
|---|---|
| RAM trong service **chỉ giữ phần chấm điểm**. Dữ liệu hiển thị của 20 thẻ / trang đọc một lần từ DB theo danh sách id, hoặc cache dùng chung trong Redis | Mỗi instance không phải giữ một bản sao dữ liệu hiển thị. Ở mức lớn: 60 MB → ~10 MB / instance |
| Sở thích ở Redis có **hạn sống 7 ngày**, mất thì nạp lại từ Postgres | Redis tỉ lệ với người hoạt động, không tỉ lệ với tổng tài khoản |
| **Redis riêng cho gợi ý**, `maxmemory` + `allkeys-lru` | Mọi thứ trong đó đều dựng lại được, nên đầy thì xoá bớt khoá ít dùng là an toàn. Tách khỏi Redis của gateway / gate (nguyên tắc Redis tách theo service) |
| Danh sách feed lưu **nhị phân** (16 byte / id), không lưu chuỗi UUID 36 ký tự | Giảm hơn 2 lần |
| "Hiện thẻ" chỉ ghi **lần đầu mỗi dự án trong một phiên**. Có bảng gộp theo ngày, chi tiết giữ 90 ngày | ClickHouse giảm ~3 lần: mức lớn còn ~18 GB |
| Nếu `labeler_affinity` quá lớn: gom thành **một dòng / người** (một cột JSON hoặc nhị phân) | ~3 GB → ~1,5 GB. Đổi lại phải đọc ra rồi ghi lại cả dòng, không cộng thẳng bằng SQL được |
| Theo dõi `INFO memory` của Redis, heap .NET, dung lượng ClickHouse, có cảnh báo vượt ngưỡng | Biết sớm khi nào cần mở rộng (gắn với nhánh DevOps A-06) |

**Khi nào mới cần đổi kiến trúc**
- **Hơn ~100.000 dự án đang chạy:** RAM vẫn dưới 100 MB, nhưng chấm điểm toàn bộ mỗi lượt bắt đầu chậm. Lúc đó thêm tầng lấy ứng viên (mục 5.4) để chỉ chấm vài trăm dự án.
- **Hàng chục triệu người dùng:** chia Redis thành nhiều máy theo `userId`.

### 5.6. Luồng phiên lướt (chốt 21/11)

**Bốn nhịp chạy**

| Nhịp | Việc |
|---|---|
| Mỗi đêm | ml-svc huấn luyện; tốt hơn bản đang chạy thì lưu file mô hình; `reco-svc` tải vào RAM |
| Mỗi 30 s + event dự án | Mỗi instance `reco-svc` tự nạp dự án đang mở vào RAM của chính nó; event mở / đóng thu bài sửa RAM ngay |
| Mỗi hành vi | Cập nhật (a, b) + tập loại trừ của người đó trong Redis |
| Mỗi request | Trang đầu: lọc → chấm → xếp lại → lưu 200 id. Trang sau: đọc tiếp danh sách |

**Khoá Redis của `reco-svc`** (Redis riêng `redis-reco`, `allkeys-lru`, mọi khoá dựng lại được)

| Khoá | Kiểu | Hạn | Chứa |
|---|---|---|---|
| `aff:{userId}` | Hash | 7 ngày | (a, b) theo thẻ; nguồn gốc `labeler_affinity` |
| `excl:{userId}` | Set | 7 ngày | Đã nộp, bị chặn, "không quan tâm" |
| `priv:{userId}` | Set | 7 ngày | Dự án riêng tư người này là thành viên |
| `feed:{userId}:{phiên}` | String nhị phân | 15 phút | 200 id đã xếp + lúc tạo. **Không bao giờ sửa** |
| `served:{userId}:{phiên}` | Set | 15 phút | Id đã trả trong phiên (kể cả dự án mới được chèn) |

**Ghép một trang** (con trỏ = vị trí trong danh sách, chỉ tăng):
1. Lấy id tiếp theo từ con trỏ, bỏ id đã có trong `served`, dự án đã đóng (tra RAM), id trong `excl`.
2. Dự án mở **sau** lúc tạo danh sách mà người này làm được và chưa trả: chấm điểm, chèn tối đa 2 dự án lên đầu trang nếu điểm cao hơn thẻ cuối của trang hoặc còn trong bể dự án mới.
3. Mỗi 10 thẻ có 1 ô khám phá: dự án còn trong bể dự án mới (chưa đủ ~200 labeler phù hợp thật sự thấy) hoặc khác loại việc người này hay làm; chọn ngẫu nhiên có trọng số.
4. Con trỏ tăng đúng số thẻ lấy từ danh sách; ghi mọi id đã trả vào `served`.
5. Hết danh sách → xếp hạng lại toàn bộ trừ `served`; không còn gì thì báo đã xem hết.

Không lặp vì danh sách không đổi + con trỏ chỉ đi tới + `served` chặn trùng; không mất dự án mới vì dự án mới trộn lúc ghép trang chứ không chen vào danh sách.

### Kiểm thử
- Unit cho từng thành phần điểm.
- E2E: dự án đông người tụt hạng; dự án mới vẫn xuất hiện; không lộ số người.
- Load test k6: đo p95 của `/projects/feed`. Gắn với nhánh DevOps A-12.
- Đo bộ nhớ thật lúc load test (`MEMORY USAGE` từng loại khoá Redis, heap .NET, dung lượng bảng ClickHouse). So với bảng ước lượng 5.5 rồi sửa lại số trong file này.

---

## 6. Giai đoạn 4 — Feed F3 và mẫu xem trước P1

> **Đã làm (21/11/2026)** — cả hai tab "Dành cho bạn" và "Mới nhất" lướt kiểu TikTok, mỗi dự án một màn (khác nhau chỉ ở dữ liệu; "Mới nhất" = `GET /projects/feed?order=newest`, reco lỗi thì rơi về `/projects/market`). Thẻ: khung sáng 1 mẫu + toàn bộ câu hỏi, công cụ. **Đổi so với bản đầu (người dùng chốt 21/11):** bỏ chọn 1–3 mẫu tay → hệ thống tự lấy 1 mẫu, doanh nghiệp chỉ bật / tắt (`projects.preview_enabled`, `PUT /projects/{id}/preview`) trong thẻ "Bảo vệ nội dung & NDA". Không có thông báo "có dự án mới" và không làm mờ thẻ đã đóng.

### Backend
- Thêm `projects.preview_sample_ids uuid[]` (tối đa 3) + API cho chủ chọn / đổi / tắt. Lúc publish, hệ thống gợi ý sẵn 3 mẫu ngẫu nhiên không phải câu vàng.
- Chặn bật xem trước khi dự án có NDA, `sensitive_content` hoặc là `Private`.
- Feed trả link xem mẫu xem trước qua media-svc, có chữ nổi như bài thật.

### Frontend
Thẻ dự án dùng chung, gồm:
- doanh nghiệp (đã xác minh), tên dự án, thưởng / bài đạt, số suất, đếm ngược hạn chót;
- phút ước tính, số câu, dạng công cụ;
- ảnh xem trước vuốt ngang, câu hỏi của công cụ;
- "Hợp vì…";
- các nút "Thử 1 câu" (gán trên mẫu xem trước, không lưu, không chấm), "Làm ngay", "Không quan tâm".

Bố cục:
- **Điện thoại:** mỗi thẻ cao bằng màn hình, `scroll-snap` dọc.
- **Máy tính:** feed một cột + khung chi tiết dự án bên phải.

Tải thêm khi cuộn tới cuối (theo con trỏ). Bộ lọc loại dữ liệu / mức thưởng.

Tab Cấu hình của doanh nghiệp: chọn mẫu xem trước.

### Kiểm thử
E2E: dự án NDA không bật được xem trước; câu vàng không bao giờ lọt vào xem trước. Kiểm giao diện ở 3 cỡ màn (~400 px, ~768 px, ≥ 1280 px).

---

## 7. Giai đoạn 5 — "Xem như labeler" bằng dữ liệu thật (Q2)

> **Đã làm (21/11/2026)** — `GET /projects/{id}/trial`, `POST /projects/{id}/trial/check`; màn "Xem như labeler" có tab "Bài xem thử" (mẫu thật, chấm câu kiểm tra) và "Thẻ ở chợ dự án" (đúng thẻ feed, kèm mẫu xem trước). Chưa có mẫu / tập nhãn thì rơi về mẫu minh hoạ.

### Backend
- `GET /projects/{id}/preview-attempt` (chỉ chủ dự án): trả đúng dạng `Attempt`, gồm mẫu thật đã xáo, câu vàng chèn như thật, câu hỏi riêng từng mẫu, chữ watermark kèm "XEM THỬ". **Không ghi DB.**
- `POST /projects/{id}/preview-attempt/check`:
  - kiểm hợp lệ như lúc nộp thật;
  - **chấm câu vàng ngay**, để doanh nghiệp biết đáp án câu vàng có sai hoặc quá khó không.

### Frontend
- Trang làm bài thêm chế độ xem thử (biểu ngữ "Bạn đang xem thử", nộp không lưu).
- `labeler-preview.tsx`:
  - tab "Thẻ trên feed" dùng thẻ thật kèm mẫu xem trước;
  - tab "Một câu trong bài" dùng mẫu thật;
  - nút "Mở bài xem thử đầy đủ".
- `demo-samples.ts` chỉ còn dùng khi dự án chưa có mẫu.

### Kiểm thử
E2E: bài xem thử không tạo dòng `attempts` / `annotations`; người khác ngoài chủ bị 403.

---

## 8. Giai đoạn 6 (sau) — Học máy cho gợi ý và A/B

> **Đã làm khung chạy (21/11/2026)** — ml-svc học mỗi đêm (19 UTC) + `POST /internal/reco/train`; logistic / LightGBM chọn theo lượng dữ liệu; candidate → A/B → production qua manifest MinIO. **Khác kế hoạch:** file mô hình là **JSON** (trọng số logistic hoặc cây `dump_model()` của LightGBM), reco-svc tự duyệt cây bằng C# — **không dùng ONNX Runtime** (khỏi thư viện native trong container, kết quả khớp LightGBM đã có test). Mô hình chỉ thật sự học khi đã tích luỹ đủ hành vi (tối thiểu 200 mẫu có cả hai nhãn); trước đó chạy G1.

Cần dữ liệu hành vi tích luỹ từ giai đoạn 2. Trước khi đủ dữ liệu, `reco-svc` dùng công thức tay của mục 5.1.

### 8.1. Hai mô hình, chọn theo lượng dữ liệu (bỏ mạng nơ-ron)

Học hai mô hình:
- **P(bắt đầu | thấy thẻ)**;
- **P(đạt | đã bắt đầu)**.

Ghép với thưởng và phút ước tính ra tiền kỳ vọng mỗi phút để xếp feed.

| Trường hợp | Mô hình | Khi nào dùng (ngưỡng khởi điểm, chỉnh lại khi đo) | Học được gì | Chạy trong `reco-svc` |
|---|---|---|---|---|
| **Ít dữ liệu** | **Hồi quy logistic** (scikit-learn) | < ~200.000 lượt hiện thẻ trong cửa sổ huấn luyện, **hoặc** < ~5.000 lượt "bắt đầu" | Mỗi đặc trưng một trọng số, ảnh hưởng tuyến tính | Vài chục trọng số (JSON), tính thẳng bằng C#, < 1 µs / dự án |
| **Đủ dữ liệu** | **LightGBM** (cây quyết định tăng cường) | Trên các ngưỡng trên **và** thắng logistic trên dữ liệu kiểm | Thêm quan hệ phi tuyến và kết hợp đặc trưng (vd "thưởng cao chỉ hấp dẫn khi dưới 10 phút") | File **ONNX**, chạy bằng ONNX Runtime cho .NET, không qua mạng |
| ~~Rất nhiều dữ liệu~~ | ~~Mạng nơ-ron~~ | **Bỏ** | — | — |

ml-svc tự chọn mỗi lần huấn luyện. Dưới ngưỡng thì chỉ học logistic. Trên ngưỡng thì học cả hai, lấy mô hình có log-loss / AUC tốt hơn trên 20 % dữ liệu kiểm.

### 8.2. Quy trình huấn luyện (ml-svc, mỗi đêm)

```
ClickHouse reco.reco_events ──► ghép đặc trưng TẠI THỜI ĐIỂM hiện thẻ ──► chia 80 % học / 20 % kiểm (theo thời gian)
                                                                                  │
                                         huấn luyện logistic (+ LightGBM nếu đủ dữ liệu) ──► đo AUC, log-loss
                                                                                  │ tốt hơn bản đang chạy?
                                                                                  ▼ có
                         lưu file mô hình + phiên bản (MinIO + reco_db.model_versions) ──► A/B với bản cũ
```

- **Đặc trưng:**
  - của dự án: thưởng, số suất, phút ước tính, loại công cụ;
  - của labeler: mức hợp `(a, b)` theo từng loại thẻ, uy tín;
  - của lượt hiện: giờ trong ngày, vị trí thẻ trên feed.
- **Bẫy:** chỉ dùng thông tin có **trước** lúc hiện thẻ. Không dùng kết quả cuối của dự án (ví dụ tỉ lệ đạt của dự án), vì lúc hiện thẻ chưa có.
- ml-svc chỉ huấn luyện, không nằm trên đường xử lý request, không quyết định chấm / tiền. `reco-svc` đọc mô hình mới mỗi giờ. ml-svc sập thì `reco-svc` vẫn chạy bằng mô hình đang có.

### 8.3. A/B
- Chia labeler cố định theo `băm(userId) % 100` (một người luôn ở cùng một nhóm). Mỗi sự kiện ghi `model_version` + `ab_group`.
- Chạy ~2 tuần. So:
  - tỉ lệ "thấy → bắt đầu";
  - tiền kiếm được / giờ của labeler;
  - tỉ lệ dự án có đủ bài đạt trước hạn.

  Bản mới phải thắng có ý nghĩa thống kê mới bật 100 %.
- Hạn chế: hai nhóm tranh cùng một tập dự án nên kết quả hơi lệch. Chấp nhận ở quy mô đồ án.

---

## 9. Còn cần chốt

| # | Câu hỏi | Đang để mặc định |
|---|---|---|
| 1 | Hạn chót tối đa | 30 ngày (`project.max_duration_days`) |
| 2 | Có hiện "bạn đứng top x %" sau khi chấm không | Chưa làm |
| 3 | Chu kỳ quên sở thích | 90 ngày |
| 4 | Số thẻ chọn tối đa lúc đăng ký | 5 lĩnh vực, 3 ngôn ngữ, loại dữ liệu không giới hạn |
| 5 | ClickHouse của `reco-svc` | Dùng chung server ClickHouse của gate nhưng **database `reco` + user riêng** (chỉ có quyền trên database của mình). Tách container riêng nếu muốn đúng tuyệt đối "mỗi service một kho" |
| ~~6~~ | ~~Tách `reco-svc` khi nào~~ | **Đã chốt 20/11:** có `reco-svc` ngay từ giai đoạn 3 |
| 6 | Ngưỡng chuyển từ logistic sang LightGBM | ~200.000 lượt hiện thẻ / ~5.000 lượt bắt đầu (mục 8.1), chỉnh khi có số đo |

## 10. Thứ tự và phụ thuộc

```
GĐ1 bỏ trần + R2 ──► GĐ2 hành vi + sở thích ──► GĐ3 bộ gợi ý ──► GĐ4 feed + xem trước ──► GĐ5 xem như labeler
                                                                     ▲
                                              (GĐ5 chỉ cần GĐ1; có thể làm song song GĐ2–4)
GĐ6 sau khi GĐ2 đã ghi hành vi đủ lâu
```

Mỗi giai đoạn xong:
- chạy unit test + E2E liên quan;
- cập nhật `kien-truc-backend.md`, `co-so-du-lieu-va-api.md`, `tich-hop-frontend.md`, `CATALOG.md`;
- gợi ý commit message (người dùng tự commit).
