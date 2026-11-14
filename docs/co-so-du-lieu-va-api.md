# Cơ sở dữ liệu, API và luồng giao tiếp của từng service

Tài liệu này **trích từ code** (model snapshot EF Core, migration SQL của Python, controller, `shared/contracts`, `ocelot.json`, `docker-compose.demo.yml`), không chép lại từ tài liệu thiết kế. Khi code và `kien-truc-backend.md` lệch nhau thì code là đúng. Lệch quan trọng nhất: thiết kế gốc có *lease 15 phút*, *duyệt từng nhãn*, *redundancy thích ứng*. Mục 3.18 đã thay hết bằng mô hình **dự án làm trung tâm** (làm cả bài, chấm một lần), nên `quality-svc` không còn vòng xin thêm người. Sau đó task-svc và annotation-svc được **gộp vào project-svc** (cùng vòng đời dự án → cùng database, bỏ các bảng bản sao).

---

## 1. Danh sách service và datastore

| Service | Port | Ngôn ngữ | Datastore (container riêng) | Ghi chú |
|---|---|---|---|---|
| gateway | 8080 | C# (Ocelot) | `redis-gateway` | Không có DB nghiệp vụ |
| identity | 8101 | C# | Postgres `db-identity` | Phát JWKS |
| project | 8102 (REST), 9102 (gRPC nội bộ) | C# | Postgres `db-project` | Gồm cả làm bài (`/tasks/*`) và nhãn (`/annotations/*`) — trước là task-svc :8103 và annotation-svc :8104 |
| ledger | 8105 | C# | Postgres `db-ledger` | Chủ duy nhất của số dư |
| payment | 8106 | C# | Postgres `db-payment` | |
| link | 8107 | C# | Postgres `db-link` | |
| gate | 8108 | C# | **ClickHouse** `ch-gate` + Redis `redis-gate` + Postgres `db-gate` | Đường nóng: Redis + ClickHouse |
| media | 8109 | Python (FastAPI) | **không có DB** | Bộ đệm file trong volume `media-cache` |
| notification | 8110 | C# | Postgres `db-notification` | |
| admin | 8111 | C# | Postgres `db-admin` | Chủ của setting |
| quality | 8201 | Python (FastAPI) | Postgres `db-quality` | Migration SQL tay `001` (gộp). Đọc đề bài / bài nộp / nhãn của project qua **gRPC** (`project:9102`) |
| ml | 8202 | Python (FastAPI) | Postgres `db-ml` + **pgvector** | Chỉ 1 bảng |
| (hạ tầng) | | | RabbitMQ, MinIO (bucket `datasets, media, exports, kyb-docs, avatars, ml-models`), Mailpit | |

`web-bff` có thư mục mã nguồn nhưng **không** nằm trong compose demo và `ocelot.json` không trỏ tới nó.

### Bảng có mặt ở hầu hết service (chỉ giải thích một lần)

| Bảng | Cột | Ý nghĩa |
|---|---|---|
| `outbox` | `id`, `event_type`, `version`, `correlation_id`, `occurred_at`, `envelope_json` (jsonb), `published_at`, `attempt_count`, `last_error`, `next_attempt_at` | Hộp thư đi. Event được ghi **cùng transaction** với nghiệp vụ. Worker nền đọc dòng `published_at IS NULL` và `next_attempt_at <= now`, gửi RabbitMQ, đánh dấu `published_at`. Lỗi thì tăng `attempt_count`, ghi `last_error`, dời `next_attempt_at` |
| `processed_events` | `(event_id, handler)` PK, `processed_at` | Chống xử lý trùng. Consumer chèn dòng này trong cùng transaction với xử lý. Chèn trùng khoá nghĩa là đã xử lý, bỏ qua |
| (không có bản sao setting) | | Từ kiến trúc 3.32 mọi service đọc thẳng view `v_settings` (`key`, `value`, `version`) của admin_db bằng role `settings_reader` vào bộ nhớ — warm-up lúc khởi động, đọc lại mỗi `settings.reload_interval`. Đã bỏ `settings_replica` |

`outbox`, `processed_events` có ở: admin, gate, identity, ledger, link, notification, payment, project, quality. `ml` và `media` không có.

**View cho service khác đọc (kiến trúc 3.32).** Chủ dữ liệu mở view và role chỉ `SELECT` được view đó (bảng thật → `permission denied`); bên đọc nối thẳng bằng role này, không có bảng bản sao:

| View | DB chủ | Role | Ai đọc |
|---|---|---|---|
| `v_settings` (`key`, `value`, `version`) | admin_db | `settings_reader` | mọi service, gateway, quality, media (bộ nhớ) |
| `v_gate_projects`, `v_gate_samples` (kèm `question` = câu riêng của mẫu, 3.34 / 3.35), `v_gate_gold` | project_db | `gate_reader` | gate (bộ nhớ) |
| `v_gate_budgets` (`project_id`, `remaining_vnd`) | ledger_db | `gate_reader` | gate (bộ nhớ) |
| `v_gate_links` | link_db | `gate_reader` | gate (bộ nhớ) |
| `v_contacts` (`user_id`, `email`, `display_name`, `roles`) | identity_db | `notification_reader` | notification (lúc gửi email / thông báo toàn hệ thống) |
| `v_reputations` (`labeler_id`, `reputation`) | quality_db | `identity_reader` | identity (tính cấp, tra cứu cho project) |
| `v_referrals` (`referred_id`, `referrer_id`, chỉ `active`) | link_db | `ledger_reader` | ledger (lúc chi hoa hồng) |

---

## 2. Cơ sở dữ liệu từng service

Quy ước: `PK` là khoá chính. `?` là cột cho phép null. Cột `xmin` xuất hiện trong snapshot là cột hệ thống Postgres, dùng làm **token khoá lạc quan** (optimistic concurrency), không phải dữ liệu.

### 2.1 identity (`identity_db`)

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `users` | `id` PK; `email`, `username`, `display_name`, `password_hash`; `status`; `email_verified_at?`; `blocked_at?`, `blocked_reason?`; `avatar_key?` (khoá file MinIO), `bio?`; `created_at` | Tài khoản. `status` cho biết hoạt động hay bị khoá. Vai trò (Labeler/Business/Sharer/Admin) nằm trong claim của token |
| `login_sessions` | `id`; `user_id`; `token_hash` (băm refresh token, không lưu token thô); `generation`; `last_rotated_at?`; `created_at`, `expires_at`, `revoked_at?` | Phiên đăng nhập. Mỗi lần `/auth/refresh` thì xoay token, tăng `generation`. Dùng lại token cũ bị coi là trộm token |
| `session_revocations` | `user_id` PK; `revoked_at` | Mốc "thu hồi mọi phiên trước thời điểm này". Gateway nạp bảng này qua `GET /internal/session-revocations` để từ chối access token cũ |
| `account_tokens` | `id`; `user_id`; `purpose` (xác thực email, đặt lại mật khẩu…); `token_hash`; `failed_attempts`; `expires_at`; `used_at?`; `created_at` | Token một lần. `failed_attempts` giới hạn số lần đoán |
| `two_factor` | `user_id` PK; `secret_cipher` (khoá TOTP đã mã hoá); `enabled_at?`; `last_used_step` (chống dùng lại cùng một mã TOTP); `created_at` | 2FA |
| `business_profiles` | `user_id` PK; `company_name`, `tax_code`, `address`; `license_key?`, `license_file_name?` (giấy phép trong bucket `kyb-docs`); `status`; `submitted_at?`, `reviewed_at?`, `reviewed_by?`, `reject_reason?` | Hồ sơ KYB của doanh nghiệp. Duyệt xong thì sinh claim `kyb_verified` |
| `identity_verifications` | `user_id` PK; `full_name`, `date_of_birth`; `id_number_hash`, `id_number_last4` (không lưu số giấy tờ thô); `front_key?`, `back_key?`; `status`; `submitted_at?`, `reviewed_at?`, `reviewed_by?`, `reject_reason?` | Xác minh danh tính của labeler (CCCD) |
| `labeler_stats` | `user_id` PK; `level`; `updated_at` | Cấp độ (identity quyết theo `identity.level_rules`). **Uy tín không lưu ở đây** — đọc view `v_reputations` của quality (3.32). **Số câu được chấp nhận cũng không lưu** — đọc qua gRPC `LabelerStatsData` từ `project.attempts` (kiến trúc 3.24) |
| `level_dirty` | `user_id` PK; `dirty_at`; `correlation_id?` | **Hàng chờ** tính lại cấp / huy hiệu (`project.grading_applied`, khiếu nại chấp nhận, `reputation.changed`). `CapDoWorker` đọc theo lô (`FOR UPDATE SKIP LOCKED`), hỏi số câu qua gRPC, xoá dòng |
| `user_badges` | `(user_id, badge)` PK; `awarded_at` | Huy hiệu |

### 2.2 project (`project_db`)

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `projects` | `id` PK; `owner_id`; `name`, `description`, `modality` (image/text/audio/video…); `status` (`Draft, PendingEscrow, PendingApproval, Running, Paused, Grading, Completed, Cancelled`); `status_reason?`; `visibility` (`Public, Private, Approval`) | Dự án. `status` đi theo máy trạng thái, mọi lần chuyển dùng `xmin` để chống ghi chồng |
| (tiếp) | `label_schema` jsonb: tập công cụ gán nhãn, mỗi công cụ một `tools[].question` (câu hỏi, bắt buộc trước khi publish — 3.35; câu hỏi chung và `hint` đã bỏ) | Cấu hình gán nhãn. Hướng dẫn Markdown (`guideline`) đã bỏ — kiến trúc 3.34 |
| (tiếp) | `slots` (số suất thưởng S); `reward_vnd` (thưởng mỗi suất R); `platform_fee_percent`; `pass_threshold_percent` (ngưỡng t); `question_count` (số câu của một bài); `fill_slots_min_percent?` (lấp suất trống) | Cấu hình kinh tế. Ký quỹ = `S × (R + phí) + ngân sách cổng link` |
| (tiếp) | `allow_collaborative`, `allow_professional`, `allow_link_gateway`; `gate_unit_price_vnd?`, `gate_budget_vnd?` | Kênh làm việc: cộng đồng, chuyên nghiệp, cổng link (sharer) và giá/ngân sách cổng |
| (tiếp) | `min_level?`, `min_reputation?`, `require_verified_identity`, `require_entrance_test`, `entrance_question_count`, `entrance_pass_percent` | Điều kiện tham gia |
| (tiếp) | `nda_required`, `nda_text?`, `nda_version`, `sensitive_content`, `content_warning?`, `delete_files_after_close`, `files_purged_at?`, `hidden_by_owner_at?` | Bảo vệ dữ liệu nhạy cảm, NDA, xoá file sau khi đóng; lúc doanh nghiệp "xoá" dự án đã kết thúc (chỉ ẩn với chủ dự án — kiến trúc 3.36) |
| (tiếp) | `deadline?`, `submitted_for_approval_at?`, `published_at?`, `finish_requested_at?`, `grading_started_at?`, `closed_at?`, `was_escrowed`, `created_at`, `updated_at` | Mốc thời gian vòng đời. `was_escrowed` cho biết đã từng giữ ký quỹ, để biết có cần hoàn khi huỷ không |
| `samples` | `(project_id, id)` PK; `modality`; `storage_key?` (file MinIO) hoặc `content?` (jsonb cho text/pair/list, nén lz4); `original_name`, `content_type?`, `size_bytes?`, `sha256?` (NULL tới khi đọc xong file); `metadata` jsonb (lz4; kích thước ảnh, thời lượng…); `ingest_state` (`queued`, `processing`, `ready`, `failed`), `ingest_error?`, `ingest_source?` (link tải của nhập nguồn ngoài, xoá khi xong); `media_state?`, `media_error?` (chuẩn hoá video do media-svc); `excluded_at?`, `excluded_reason?` (`report_accepted`, `auto_reports`); `question?` (câu hỏi RIÊNG, ≤ 1000 ký tự — NULL = dùng câu hỏi chung của dự án, 3.34) | Một mẫu cần gán nhãn — cũng chính là **câu hỏi** của bài (không còn bảng `tasks` riêng). **Không có bảng "lô nạp" (`datasets`)**: tiến trình nạp nằm ngay trên từng mẫu (`ingest_state`); mọi truy vấn nghiệp vụ chỉ thấy mẫu `ready` (bộ lọc toàn cục của EF), chủ dự án xem cả mẫu đang chờ / lỗi. **Phân vùng HASH(`project_id`) 64 phần** (`samples_p00..p63`). UNIQUE `(project_id, sha256, question) NULLS NOT DISTINCT WHERE sha256 IS NOT NULL` chống trùng: cùng nội dung + cùng câu hỏi là trùng, cùng nội dung khác câu hỏi là hai mẫu |
| `gold_items` | `id`; `project_id`; `sample_id` (FK `samples (project_id, id)`); `purpose` (câu kiểm tra hoặc bài test đầu vào); `expected_payload` jsonb, `expected_task_type`, `expected_schema_version` | Câu vàng và đáp án đúng; chấm ngay lúc nộp bài |
| `project_members` | `(project_id, user_id)` PK; `role` (`Owner, Labeler, Reviewer`); `state` (`Active, Blocked`); `blocked_by_owner_list`; `joined_at`, `updated_at` | Thành viên. Là cơ sở của phân quyền ngang (chống BOLA) |
| `project_invitations` | `id`; `project_id`; `email`; `user_id?`; `by_email`; `role`; `status`; `invited_by`; `expires_at`; `decided_at?` | Lời mời. Có thể mời theo email chưa có tài khoản |
| `invite_codes` | `id`; `project_id`; `code`; `max_uses?`, `used_count`; `expires_at?`, `revoked_at?`; `note?`; `created_by` | Mã mời. `xmin` chống vượt `max_uses` khi nhiều người nhập cùng lúc |
| `join_requests` | `id`; `project_id`; `user_id`; `status`; `message?`; `entrance_attempt_id?`, `score_percent?`; `decided_by?`, `decided_at?`, `reject_reason?` | Đơn xin tham gia dự án chế độ duyệt |
| `entrance_attempts` | `id`; `project_id`; `user_id`; `started_at`, `expires_at`, `submitted_at?`; `score_percent?`, `passed?` | Lượt làm bài test đầu vào |
| `project_consents` | `(project_id, user_id)` PK; `nda_signed_at?`, `nda_signed_name?`, `nda_version?`, `nda_text_hash?`; `content_warning_accepted_at?`; `ip?`, `user_agent?` | Bằng chứng đã đồng ý NDA và cảnh báo nội dung |
| `owner_labeler_lists` | `(owner_id, labeler_id)` PK; `kind` (ưu tiên hoặc chặn); `note?` | Danh sách riêng của doanh nghiệp. Chặn thì `blocked_by_owner_list` được đặt ở `project_members` |
| `labeler_ratings` | `(project_id, labeler_id)` PK; `owner_id`; `stars` (1–5); `comment?` | Doanh nghiệp đánh giá labeler |

#### Phần làm bài và nhãn (trước là task-svc và annotation-svc, nay cùng `project_db`)

Bài làm, nhãn và mẫu có **cùng vòng đời với dự án** nên nằm chung database: không còn bản sao dự án / mẫu / câu vàng / thành viên ở service khác, mở bài đọc thẳng `projects`, `samples`, `gold_items`, `project_members`.

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `project_collections` | `project_id` PK (FK `projects`); `max_participants`; `paused_at?`; `finish_reason?` (`du_suat, het_han, ket_thuc_som`); `graded_at?`, `appeal_deadline?`, `settled_at?`, `passed_count?`; `final_progress?` jsonb (ảnh chụp tiến độ lúc đóng); `data_purged_at?`; `created_at` | **Đợt thu bài** của dự án, mở lúc admin duyệt. Mọi thao tác đổi trạng thái thu bài (nộp, tạm dừng, kết thúc sớm, huỷ) khoá dòng này trước (`FOR UPDATE`) — thứ tự khoá cố định `project_collections → attempts → samples` |
| `attempts` | `id` PK; `project_id`; `labeler_id`; `state` (`InProgress, Submitted, Expired, Cancelled`); `started_at`, `expires_at` (hạn làm = min(+48h, hạn chót dự án)), `submitted_at?`, `ended_at?`; `question_order` uuid[] (thứ tự câu đã trộn); `answered_count?`; `gold_total`, `gold_correct`, `gold_answers?` jsonb (câu trả lời câu vàng, chấm lúc nộp); `client_ip?`, `device_id?`; `comparable_count?`, `matched_count?`, `agreement_percent?`, `passed?`, `rewarded?`, `graded_at?` (kết quả chấm do quality trả về); `appeal_state` (`None, Open, Accepted, Rejected`), `appealed_at?`, `appeal_message?`, `appeal_note?`, `appeal_resolved_at?`, `appeal_resolved_by?` | Một bài = mọi câu của dự án. UNIQUE có điều kiện `ux_attempts_project_labeler_active` (`state IN ('InProgress','Submitted')`): mỗi người một bài đang làm hoặc đã nộp mỗi dự án. `xmin` khoá lạc quan |
| `annotations` | `(project_id, attempt_id, sample_id)` PK; `labeler_id?` (null = khách cổng link); `source` (`Professional`, `LinkGateway`, `Collaborative`); `payload` jsonb (nén **lz4**, không GIN) — **chỉ 6 cột** | Câu trả lời đã nộp: người nào (`labeler_id`, bài `attempt_id`) chọn gì (`payload`) cho câu nào (`sample_id`) — **bảng lớn nhất** về lâu dài. **Phân vùng HASH(`project_id`) 64 phần**. Không lưu những gì suy ra được: `status` (Submitted / Accepted / Excluded) tính từ `attempts.graded_at`, `passed`, `appeal_state` (cổng link luôn Accepted); thời điểm nộp / chốt lấy từ `attempts`; `task_type` = `projects.modality`, `schema_version` = 1. Chấm bài và phân xử khiếu nại chỉ sửa **một dòng `attempts`**, không UPDATE nhãn. Nhãn chỉ INSERT lúc nộp / `gate.solved` |
| `sample_results` | `(project_id, sample_id)` PK; `tools` jsonb (kết quả gộp từng công cụ: `method, final, agrees, disputed…`); `fingerprint` (băm đầu vào); `computed_at` | **Bộ nhớ đệm** kết quả gộp. `fingerprint` đổi thì mới tính lại — mẫu cần tính được gửi sang quality-svc gộp qua gRPC `LabelAggregation` (kiến trúc 3.24, thuật toán gộp chỉ còn ở Python) |
| (không có bảng lịch sử bài) | | Lịch sử một bài (nộp, chấm đạt / trượt, khiếu nại, phân xử) dựng lại từ chính các cột của `attempts` (`GET /annotations/attempts/{id}/history`, kiến trúc 3.31). Đã bỏ `attempt_history` |
| `sample_reports` | `id`; `attempt_id`; `project_id`; `sample_id` (FK `samples (project_id, id)`); `labeler_id`; `reason`; `note?`; `state` (`open, dismissed, excluded`); `created_at`, `resolved_by?`, `resolved_at?` | Labeler báo mẫu lỗi. Chủ dự án chấp nhận (hoặc đủ nhiều người báo) thì **mẫu** bị đánh dấu `samples.excluded_at` và loại khỏi chấm |

### 2.5 quality (`quality_db`, Python, migration `001`)

**Không có bản sao dữ liệu của project-svc** (kiến trúc 3.22). Đề bài, bài nộp, nhãn, IP / thiết bị lúc nộp là dữ liệu của project-svc; quality đọc thẳng qua gRPC `QualityData` (`contracts/grpc/project/v1/quality_data.proto`) lúc chấm, lúc chạy Dawid–Skene (một lần sau khi chấm) và lúc xét gian lận. Ở đây chỉ còn kết quả quality tự tính ra và trạng thái công việc của chính nó.

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `pending_attempts` | `attempt_id` PK; `project_id`; `queued_at`; `fraud_checked_at?`; `freetext_done_at?` | **Hàng chờ** (không phải bản sao): `attempt.submitted` chỉ ghi một dòng, worker đọc bài qua gRPC để xét gian lận và nhúng câu trả lời mở. Xong cả hai thì xoá dòng |
| `project_gradings` | `project_id` PK; `requested_at`; `correlation_id`; `graded_at?`; `majority_used?` (false nếu < 3 bài); `freetext_state?` (`pending`/`done`), `freetext_method?`; `ds_at?`; `reputation_at?` | Chuỗi xử lý của dự án đã đóng thu bài: `requested_at` → `graded_at` → `ds_at` → `reputation_at`. `graded_at` null = chưa chấm xong (project-svc tạm không trả lời, hoặc chờ ml-svc) → worker chấm lại. `ds_at` = đã chạy Dawid–Skene + alpha / kappa / STAPLE **một lần, sau khi chấm** (kiến trúc 3.30). `reputation_at` = đã xếp labeler vào `reputation_dirty`, **sau khi** có `ds_at` và project-svc ghi xong `project.graded` (gRPC thấy mọi bài `graded`) |
| `reputation_dirty` | `labeler_id` PK; `dirty_at`; `correlation_id?` | **Hàng chờ** tính lại uy tín (dự án vừa áp dụng, hoặc admin tính lại DS). Worker đọc bằng chứng cộng dồn qua gRPC `GetLabelerEvidence`, ghi `reputations`, xoá dòng |
| `sample_majorities` | `(project_id, sample_id)` PK; `status`; `final` jsonb; `label_count` | Kết quả đa số của từng câu |
| `freetext_vectors` | `(attempt_id, sample_id, tool)` PK; `project_id`; `model?`; `vector?` real[]; `created_at` | Embedding câu trả lời mở lấy từ ml-svc |
| `ds_skills` | `(labeler_id, project_id, tool)` PK; `skill` (0–1); `label_count`; `computed_at` | Kỹ năng Dawid–Skene (bỏ câu bị loại; labeler < `quality.ds_min_labels` nhãn thì không có dòng) |
| `project_metrics` | `(project_id, tool)` PK; `alpha?` (Krippendorff), `kappa?` (Fleiss), `metric?` (thang đo: nominal/interval/cer/kendall/set_f1); `item_count`, `label_count`; `computed_at` | Chỉ số đồng thuận theo công cụ, hiển thị ở tab Chất lượng |
| `staple_skills` | `(labeler_id, project_id, tool)` PK; `sensitivity`, `specificity`; `sample_count`; `computed_at` | STAPLE cho mask. Chỉ tham khảo, không vào uy tín |
| `reputations` | `labeler_id` PK; `reputation` (0–100); `gold_total`, `gold_correct`; `agree_total`, `agree_count`; `ds_skill?`; `updated_at` | Uy tín tổng hợp — quality là chủ. Các cột tổng là ảnh chụp bằng chứng lúc tính (nguồn: `project.attempts`). Thay đổi thì phát `reputation.changed` |
| `fraud_cases` | `id` PK; `type`; `case_key`; `project_id?`; `user_ids` uuid[]; `evidence` jsonb; `status`; `created_at`, `updated_at`; `resolved_at?`, `resolved_by?`; `note?`; `blocked` | Hồ sơ nghi gian lận. Admin xác nhận thì phát `fraud.case_confirmed` |
| `project_archives` | `project_id` PK; `closed_at` (nhận `project.completed` / `cancelled`); `archived_at?`; `purged_at?` | Đồng hồ lưu trữ của quality (không phải bản sao trạng thái dự án) |
| `project_reports` | `project_id` PK; `summary` jsonb; `labelers` jsonb; `created_at` | Báo cáo chốt khi lưu trữ dự án (sau đó xoá kết quả trung gian) |

Đã bỏ (3.22): `projects`, `attempts`, `labels` (bản sao từ `project.published` / `attempt.submitted` — bảng `labels` cùng cỡ với bảng lớn nhất hệ thống), `project_collections` (danh sách bài / câu bị loại chép từ event), `fraud_submissions` (thời gian, IP, thiết bị của từng bài), `reputation_baseline` (di sản mô hình cũ, rỗng trên dữ liệu seed), `attempt_grades` (chép lại số câu so được / giống / câu vàng đã có trên `project.attempts`).

### 2.6 ledger (`ledger_db`)

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `accounts` | `code` PK (vd `labeler:{id}:available`, `escrow:project:{id}`); `balance` (VND, số nguyên); `created_at` | Tài khoản sổ cái. `balance` là bộ đệm, nguồn sự thật là `journal_entries` |
| `journal_entries` | `id` PK; `seq` (số thứ tự toàn cục); `type`; `reference` (UNIQUE cùng `type`, chống ghi trùng); `description`; `prev_hash`, `hash` (chuỗi băm SHA-256); `created_at` | Bút toán bất biến. Trigger chặn `UPDATE/DELETE`. Các dòng bút toán (số tiền có dấu theo từng tài khoản) nằm trong cột con của entity này |
| `project_escrows` | `project_id` PK; `owner_id`; `state` (`Active, Closing, Closed`); `slots`; `reward_vnd`; `platform_fee_vnd`; `reserved_vnd`, `reserved_at`; `allow_link_gateway`; `gate_budget_vnd`, `gate_spent_vnd`, `gate_budget_sequence`; `expected_paid_attempts?`; `closing_is_cancel`; `closed_at?` | Ký quỹ theo dự án. `Closing` là trạng thái chờ trả nốt cho bài đạt rồi mới hoàn tiền dư. Tên dự án in hoá đơn hỏi project-svc qua gRPC `LedgerData` lúc xuất (không chép vào đây — 3.24) |
| `holds` | `id` PK; `kind` (`GateClick, Referral, ProjectReward`); `state` (`Held, Released, Withheld`); `source_id` (id nguồn: bài làm với `ProjectReward`, ClickId với `GateClick` / `Referral` — UNIQUE cùng `kind`); `project_id`, `link_id?`, `labeler_id`; `amount_vnd`; `held_at`, `release_at?`, `released_at?` | Khoản tiền đang treo trước khi vào số dư khả dụng |
| `withdrawals` | `id`; `labeler_id`; `amount_vnd`, `tax_vnd` (10% TNCN nếu ≥ 2 triệu/lần), `net_vnd`; `bank_account`; `state` (`Requested, PendingApproval, Completed, Failed, Rejected`); `idempotency_key`; `reviewed_by?`, `reviewed_at?`; `failure_reason?`; `completed_at?` | Lệnh rút tiền |
| `gate_clicks` | `click_id` PK; `link_id`, `project_id`, `sharer_id`; `sharer_amount_vnd`, `platform_amount_vnd`; `state`; `batch_id?`; `received_at`, `validated_at?`, `settled_at?` | Lượt vượt link đã được gate xác nhận, chờ trả cho sharer |
| (không có bảng giới thiệu) | | Quan hệ giới thiệu đọc view `v_referrals` của link lúc chi hoa hồng (3.32). Đã bỏ `ledger.referrals` |
| `invoices` | `id`; `business_id`; `number`, `serial`, `period` (yyyy-MM); `seller_*`, `buyer_*` (tên, mã số thuế, địa chỉ, email); `total_net_vnd`, `vat_percent`, `total_vat_vnd`, `total_vnd`, `pass_through_vnd`; `items` jsonb, `pass_through` jsonb; `amount_in_words`; `issued_at` | Hoá đơn VAT cho doanh nghiệp |
| `invoice_counters` | `year` PK; `last_number` | Bộ đếm số hoá đơn liên tục theo năm |

### 2.7 payment (`payment_db`)

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `payment_intents` | `id` PK; `business_id`; `amount_vnd`; `provider` (sandbox, vnpay, momo, chuyển khoản tay); `status`; `idempotency_key`; `provider_txn_id?`; `checkout_url?`; `transfer_code?`, `transferred_at?`; `failure_code?`, `reject_reason?`; `reviewed_by?`; `last_checked_at?`; `created_at`, `completed_at?` | Ý định nạp tiền. Xong thì phát `deposit.confirmed` |
| `payouts` | `id` PK; `labeler_id`; `net_vnd`; `bank_account`; `status` (`Requested, Sending, Succeeded, Failed`); `attempts`, `last_error?`; `provider_txn_id?`; `sent_at?`, `completed_at?` | Lệnh chi tiền ra ngân hàng. Chạy 3 pha (ghi ý định, commit, gọi cổng, ghi kết quả) |

### 2.8 link (`link_db`)

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `links` | `id`; `owner_id?` (null nếu tạo ẩn danh); `code` (mã rút gọn); `destination_url`, `domain`; `campaign_id?`; `password_hash?`; `expires_at?`; `status`, `status_reason?`; `needs_review`; `report_count`; `creator_ip_hash?` | Link rút gọn |
| `campaigns` | `id`; `owner_id`; `name` | Chiến dịch gom link |
| `api_keys` | `user_id` PK; `prefix`, `key_hash` | Khoá gọi API rút gọn, chỉ lưu băm |
| `blocked_domains` | `domain` PK; `reason`; `created_by` | Tên miền cấm làm link đích |
| `link_reports` | `id`; `link_id`; `reason`; `ip_hash` | Báo cáo link xấu |
| `referral_codes` | `user_id` PK; `code` | Mã giới thiệu |
| `referrals` | `referred_id` PK; `referrer_id`; `status`; `flag_reasons?`; `claim_ip_hash?`; `decided_by?`, `decided_at?` | Quan hệ giới thiệu. Nghi gian lận thì chờ admin duyệt. Ledger đọc qua view `v_referrals` (chỉ `active`) |

### 2.9 gate (Postgres `gate_db` + Redis + ClickHouse)

| Kho | Đối tượng | Ý nghĩa |
|---|---|---|
| ClickHouse | `click_events` (ReplacingMergeTree theo `click_id`, partition theo tháng, TTL 13 tháng): `click_id`, `event_time`, `kind`, `outcome`, `link_id`, `owner_id`, `campaign_id?`, `project_id?`, `label_count`, `revenue_vnd`, `ip_hash_day`, `referrer_host` | Mọi lượt truy cập. Truy vấn toàn là aggregate (thống kê sharer). `ReplacingMergeTree` làm bản giao trùng không đếm đôi |
| Redis (`redis-gate`) | phiên vượt link, `jti` (chống dùng lại token), chống trùng IP, stream | Đường nóng, không chạm Postgres |
| Postgres | chỉ `outbox`, `processed_events` | Không còn bản sao (kiến trúc 3.32): dự án, mẫu, câu vàng, ngân sách, link đọc thẳng view `v_gate_*` của project / ledger / link vào bộ nhớ `GateCatalog` — warm-up trước khi mở cổng HTTP, đọc lại mỗi `gate.cache_refresh_interval`; mẫu + câu vàng mỗi dự án đọc một lần |

### 2.10 notification (`notification_db`)

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `notifications` | `id`; `user_id`; `type`; `title`, `body`, `link?`; `amount_vnd?`; `merged_count` (gộp thông báo cùng loại); `read_at?`; `email_state`, `email_attempts`, `email_next_at?`, `email_sent_at?`, `email_error?` | Thông báo trong ứng dụng, kèm trạng thái gửi email |
| `email_preferences` | `(user_id, type)` PK; `email_enabled` | Tuỳ chọn nhận email theo loại |
| `language_preferences` | `user_id` PK; `language` | Ngôn ngữ thông báo |
| `transactional_emails` | `id`; `user_id?`; `to_email`, `display_name`; `subject`, `body`; `state`; `attempts`; `next_at?`, `sent_at?`, `error?` | Email giao dịch (xác thực, đặt lại mật khẩu) có thử lại |
| `realtime_tickets` | `token_hash` PK; `user_id`; `expires_at` | Vé dùng một lần để mở WebSocket (trình duyệt không gửi được header Authorization) |
| (không có bảng liên lạc) | | Địa chỉ liên lạc (email, tên, vai trò) đọc view `v_contacts` của identity lúc gửi (3.32) — luôn là giá trị mới nhất. Người nhận của từng event (chủ dự án, người rút) đi **trong payload** (`ownerId`, `labelerId`) |

### 2.11 admin (`admin_db`)

| Bảng | Cột chính | Ý nghĩa |
|---|---|---|
| `settings` | `key` PK; `value` jsonb; `version`; `updated_by`, `updated_at` | Setting hệ thống. Đổi thì `version` tăng và phát `setting.changed` |
| `setting_history` | `id`; `key`; `old_value?`, `new_value`; `version`; `changed_by`, `changed_at`; `reason?` | Lịch sử đổi setting |
| `manual_actions` | `action_id` PK; `kind`; `title`, `detail`; `project_id?`, `user_id?`, `amount_vnd?`; `status` (`pending`, `resolved`); `note?`; `opened_at`, `resolved_at?`, `resolved_by?` | **Việc cần xử lý tay** (kiến trúc 3.29) dựng từ `manual_action.required` — dữ liệu riêng của admin. Hàng đợi duyệt chung `approval_requests` đã bỏ (3.33): mỗi loại đơn duyệt ở trang riêng, đọc API của service chủ |
| `audit_events` | `event_id` PK; `event_type`; `producer`; `actor_id?`, `actor_role?`; `correlation_id`, `causation_id?`; `occurred_at`, `received_at`; `payload` jsonb | Nhật ký chỉ-ghi-thêm của mọi event |
| `audit_counted` | `event_id` PK; `seen_at` | Đánh dấu event đã đưa vào thống kê |
| `stats_daily` | `(day, event_type)` PK; `count`; `amount_vnd`; `fee_vnd` | Thống kê theo ngày cho `GET /admin/stats/overview` |
| `announcements` | `id`; `title`, `body`; `audience`; `send_email`; `created_by`, `created_at` | Thông báo toàn hệ thống |
| `workspace_templates` | `id`; `code`, `name`, `description`; `modality`; `label_schema` jsonb (kèm câu hỏi chung); `suggested_redundancy?`, `suggested_unit_price_vnd?`; `active`; `version`; `updated_by`, `updated_at` | Mẫu bài toán dựng sẵn để doanh nghiệp tạo dự án nhanh |
| `tags` | `id`; `code`, `name`; `kind` (lĩnh vực hoặc ngôn ngữ); `hidden`; `merged_into?` | Danh mục thẻ. Gộp thẻ thì phát `tag.merged` |

### 2.12 ml (`ml_db`, pgvector)

| Bảng | Cột | Ý nghĩa |
|---|---|---|
| `embedding_cache` | `text_hash` bytea PK; `model`; `dim`; `embedding` vector; `created_at`; `last_used_at` (index để dọn) | Cache embedding theo băm văn bản, tránh nhúng lại cùng một câu |

### 2.13 media (không DB) và gateway

- **media**: không có bảng. Token xem file là chuỗi **ký bằng khoá `MEDIA_GRANT_KEY`** (đã mã hoá, tự hết hạn), nên không cần lưu. File đã gắn watermark được cache vào volume `media-cache`.
- **gateway**: Redis `redis-gateway` chỉ chứa bộ đếm rate limit; mốc thu hồi phiên nạp từ identity lúc khởi động.

---

## 3. API HTTP

Mọi API client đi qua **gateway** (`:8080`, ở demo là `:18080`). Gateway kiểm JWT (Bearer) cho các route cần auth, rồi chuyển tiếp. `X-Internal-Key` bảo vệ `/internal/*`, và `/internal/*` **không** được gateway định tuyến ra ngoài.

### 3.1 Bảng định tuyến gateway

| Đường dẫn ngoài | Service | Auth ở gateway |
|---|---|---|
| `/auth/*` | identity | không |
| `/me`, `/me/*`, `/kyb/*`, `/users/*` | identity | Bearer |
| `/projects`, `/projects/*` | project | Bearer |
| `/tasks/*` | project (phần làm bài) | Bearer |
| `/annotations/*` | project (phần nhãn) | Bearer |
| `/ledger/*` | ledger | Bearer |
| `/payments/webhooks/*`, `/payments/sandbox/*` | payment | không (webhook ký HMAC, trang giả lập) |
| `/payments/*` | payment | Bearer |
| `/links`, `/links/*` | link | không ở gateway (tạo ẩn danh được); từng action tự kiểm quyền |
| `/g/*`, `/go/*` | gate | không (trang vượt link công khai) |
| `/gate/*` | gate | Bearer |
| `/media/v/*` | media | không (token nằm trong URL, vì `<img>` không gửi được header; timeout 600 s) |
| `/media/admin/*` | media | Bearer |
| `/notifications/realtime` | notification | không (xác thực bằng vé một lần) |
| `/notifications/*` | notification | Bearer |
| `/admin/*`, `/templates`, `/tags` | admin | Bearer |
| `/quality/*` | quality | Bearer |

`ml` không có route ngoài. Nó chỉ nhận gọi nội bộ từ quality.

### 3.2 API theo service (đường dẫn trên gateway)

**identity** — đăng ký và phiên: `POST /auth/register`, `/auth/login`, `/auth/2fa/verify`, `/auth/refresh`, `/auth/logout`, `/auth/verify-email`, `/auth/forgot-password`, `/auth/reset-password`. Cá nhân: `GET /me`, `GET|PUT /me/profile`, `PUT /me/skills`, `POST /me/avatar/upload-url`, `PUT /me/avatar`, `POST /me/password`, `POST /me/logout-all`, `GET /me/level`, `GET|POST /me/2fa/*`, `GET|PUT /me/identity-verification` (+ `/upload-url`, `/submit`). KYB: `GET|PUT /kyb/me`, `POST /kyb/me/license`, `POST /kyb/me/submit`; admin `GET /kyb/admin`, `POST /kyb/admin/{id}/approve|reject`. Người dùng: `GET /users/public`, `/users/suggest`, `/users/leaderboard`; admin `GET /users/admin`, `POST /users/admin/{id}/block|unblock`, `/2fa/reset`, duyệt `/identity-verifications/{id}/approve|reject`. Nội bộ: `GET /internal/session-revocations`, `POST /internal/users/lookup`, `GET /internal/businesses/{id}`. Khác: `GET /.well-known/openid-configuration`, `GET /.well-known/jwks.json`.

**project** — vòng đời: `POST /projects`, `GET /projects`, `GET /projects/{id}`, `GET /projects/{id}/readiness`, `PATCH /projects/{id}` (sửa cấu hình theo nhóm `info|labelSchema|pricing|channels|eligibility|protection|tags|retention`; câu hỏi chung nằm trong `labelSchema.question`), `POST .../publish|pause|resume|finish|cancel`; admin `GET /projects/pending-approval`, `POST /projects/{id}/approve|reject`. Dữ liệu: `POST .../uploads` (xin link upload thẳng MinIO), `POST .../samples` (dòng JSON / file đã upload), `POST .../samples/zip` (ZIP ảnh), `POST .../samples/import` (nguồn ngoài), `GET .../samples` (+ `?trangThai=dang_xu_ly|loi|san_sang`, `?ids=`), `GET .../samples/status`, `PUT .../samples/{sampleId}/question` (câu hỏi riêng của mẫu, chỉ khi nháp), `DELETE .../samples/failed`, `GET|POST|DELETE .../gold-items`. Thành viên: `GET|POST .../members`, `.../members/batch`, `POST .../members/{u}/block|unblock`, `DELETE .../members/{u}`, `POST .../join`, `.../leave`; mã mời: `POST|GET|DELETE .../invite-codes`, `POST /projects/join-by-code` (+ `/preview`); xin vào: `POST .../join-requests`, `GET .../join-requests`, `POST .../join-requests/{r}/approve|reject`; lời mời: `GET /projects/invitations/mine`, `POST .../invitations/{i}/accept|decline`; test đầu vào: `POST .../entrance-test/attempts` (+ `/{a}/submit`); NDA: `GET .../terms`, `POST .../consent`; danh sách riêng: `GET|PUT|DELETE /projects/labeler-lists/*`; đánh giá: `PUT .../ratings/{labelerId}`; chợ dự án của labeler: `GET /projects/market` (chỉ dự án làm được ngay: còn nhận bài, chưa quá hạn chót, còn chỗ, chưa nộp, không bị chặn, đủ cấp độ / uy tín / xác minh); gợi ý: `GET /projects/recommended` (cùng bộ lọc, chỉ dự án chưa tham gia). Nội bộ: gRPC `QualityData` ở cổng 9102 cho quality (mục 4.1).

**project — làm bài** (giữ tiền tố `/tasks` cũ) — `POST /tasks/projects/{p}/attempt` (bắt đầu bài), `GET /tasks/attempts/{a}`, `POST .../submit` (body `{answers:[{sampleId, data}]}` — cả bài một lần; **nháp nằm ở localStorage của frontend**, backend không lưu), `.../abandon`, `.../appeal`, `.../reports`; `GET /tasks/me/attempts`, `GET /tasks/projects/{p}/slots|progress|attempts|reports`, `POST /tasks/reports/{r}/resolve`. Admin: `GET /tasks/admin/appeals`, `POST /tasks/admin/attempts/{a}/appeal/resolve`, `POST /tasks/admin/projects/{p}/purge` (xoá báo lỗi). Không còn `/internal/projects/{id}/close-check` (đóng dự án kiểm tại chỗ, cùng transaction).

**project — nhãn** (giữ tiền tố `/annotations` cũ) — `GET /annotations/mine`, `GET /annotations/projects/{p}`, `.../results` (kết quả gộp), `.../distribution`, `.../export` (COCO/YOLO/VOC), `GET /annotations/attempts/{attemptId}/history` (lịch sử theo bài — nhãn không có id riêng).

**quality** — `GET /quality/projects/{p}/summary`, `GET /quality/projects/{p}/labelers`, `GET /quality/me`; admin `POST /quality/admin/projects/{p}/archive`, `POST /quality/admin/dawid-skene/run`, `GET /quality/admin/fraud-cases[/{id}]`, `POST .../fraud-cases/{id}/resolve`.

**ledger** — `GET /ledger/me/balance|transactions|earnings`, `POST /ledger/withdrawals`, `GET /ledger/withdrawals/mine`, `GET /ledger/projects/{p}/cost`; hoá đơn `GET|POST /ledger/invoices`, `/preview`, `/{id}`, `/{id}/html`, `/{id}/xml`; admin `GET /ledger/admin/reconciliation`, `/admin/withdrawals`, `POST .../withdrawals/{id}/approve|reject`, `GET /ledger/invoices/admin`.

**payment** — `GET /payments/deposits/providers`, `POST /payments/deposits` (+ `/manual`, `/{id}/transferred`, `/{id}/sync`), `GET /payments/deposits/{id}`; admin `GET /payments/admin/deposits`, `POST .../{id}/approve|reject`; webhook `POST /payments/webhooks/sandbox|momo`, `GET /payments/webhooks/vnpay`; giả lập cổng `/payments/sandbox/*`.

**link** — công khai: `POST /links`, `/links/bulk`, `GET /links/quick`, `/links/api-shorten`, `POST /links/r/{code}/report`; sharer: `GET|PUT|DELETE /links[/{id}]`, `POST|GET /links/campaigns`, `POST|GET /links/api-key`; giới thiệu: `GET /links/referrals/me`, `POST /links/referrals/claim`; admin: `GET /links/admin/review-queue`, `POST /links/admin/{id}/disable|dismiss-reports`, tên miền cấm `GET|POST|DELETE /links/admin/blocked-domains`, giới thiệu nghi ngờ `GET /links/admin/referrals/flagged`, `POST .../{id}/approve|reject`.

**gate** — công khai: `GET /g/{code}` (trang vượt link), `POST /g/{code}/sessions`, `POST /g/sessions/{id}/submit`, `GET /go/{code}`. Thống kê: `GET /gate/stats/me/daily|top-links|sources|outcomes`.

**notification** — `GET /notifications/me`, `/unread-count`, `POST /{id}/read`, `/read-all`, `GET|PUT /preferences`, `GET|PUT /language`, `POST /notifications/me/realtime-ticket`, `GET /notifications/realtime` (WebSocket).

**admin** — setting `GET|PUT /admin/settings[/{key}]`, `/{key}/history`; thẻ `GET|POST|PUT /admin/tags`, `/hide|show|merge`, `GET /tags`; mẫu `GET|POST|PUT /admin/templates`, `/activate|deactivate`, `GET /templates[/{id}]`; việc cần xử lý tay `GET /admin/manual-actions`, `POST /admin/manual-actions/{id}/resolve` (hàng đợi duyệt chung đã bỏ — 3.33); vận hành `GET /admin/audit`, `/admin/stats/overview`, `GET /admin/system-health` (tình trạng hệ thống — số lỗi kỹ thuật trong DLQ), thông báo `POST|GET /admin/announcements`.

**media** — `GET /media/v/{token}` (xem file đã watermark), `POST /media/admin/watermark/decode` (giải watermark), `GET /health`. **ml** (nội bộ) — `GET /internal/model`, `POST /internal/embeddings`.

---

## 4. Giao tiếp giữa service

### 4.1 HTTP đồng bộ giữa service (rất ít)

| Người gọi | Người nhận | API | Mục đích |
|---|---|---|---|
| gateway | identity | `GET /internal/session-revocations` | Nạp mốc thu hồi phiên lúc khởi động (cộng thêm nghe `user.sessions_revoked`) |
| project | identity | **gRPC** `IdentityData.LookupUsers` (`identity:9101`, cùng khoá) | Tra username/email khi mời thành viên, kiểm điều kiện lúc vào dự án **và lúc mở bài** (khoá, cấp, uy tín — thay bảng `labeler_cache`, 3.24). Identity lỗi → 503, không cho vào / mở bài |
| project | quality | **gRPC** `LabelAggregation.AggregateSamples` (`quality:9201`, cùng khoá) | Gộp nhãn đã chốt cho kết quả / file xuất của doanh nghiệp — thuật toán gộp chỉ còn ở quality (3.24). Chỉ mẫu chưa có trong `sample_results`; quality lỗi → 503 |
| identity | project | **gRPC** `LabelerStatsData.GetApprovedCounts` / `GetLeaderboard` (`project:9102`) | Số câu được chấp nhận (tính cấp / huy hiệu, "cấp độ của tôi") và bảng xếp hạng — thay `approved_count` / `labeler_daily_stats` (3.24). Có cache trong identity (`identity.stats_cache_ttl`) |
| ledger | project | **gRPC** `LedgerData.GetProjectNames` (`project:9102`) | Tên dự án in lên hoá đơn VAT lúc xuất (thay `project_escrows.name`, 3.24) |
| ledger | identity | `POST /internal/users/lookup` | Kiểm tài khoản bị khoá **ngay lúc rút tiền** (thay bảng `blocked_users`). Identity lỗi → 503, không cho rút |
| link | identity | `POST /internal/users/lookup` | Lấy `registeredAt` khi nhận mã giới thiệu (thay bảng `known_users`) |
| ledger | identity | `GET /internal/businesses/{id}` | Lấy thông tin doanh nghiệp để lập hoá đơn |
| quality | project | **gRPC** `QualityData` (`project:9102`, HTTP/2, metadata `x-internal-key`): `GetProject`, `ListSubmittedAttempts`, `StreamLabels` (stream), `FindSharedSubmitters`, `GetLabelerSubmissionStats`, `GetLabelerEvidence` | Đọc đề bài, bài nộp, nhãn lúc chấm / chạy DS sau khi chấm / xét gian lận, bằng chứng uy tín cộng dồn — thay các bảng bản sao (3.22). Chỉ worker và lần chấm gọi, không phải đường nóng; project-svc lỗi thì việc nằm trong hàng chờ, vòng sau làm tiếp. Báo cáo chất lượng gọi lúc chủ dự án mở (lỗi → 503) |
| gate | project, ledger, link | **đọc view** `v_gate_*` (role `gate_reader`) | Nạp bộ nhớ cổng link lúc khởi động (warm-up) và mỗi `gate.cache_refresh_interval` (3.32, thay gRPC `GateData`) |
| quality | ml | `POST /internal/embeddings` | Nhúng câu trả lời mở để chấm theo nghĩa. Lỗi thì worker thử lại vòng sau, không làm hỏng việc chấm |
| gate, link | (ngoài) | Turnstile, kiểm URL | Chống bot và kiểm link đích |
| payment | (ngoài) | MoMo, VNPay | Cổng thanh toán thật hoặc giả lập |

### 4.2 Event qua RabbitMQ

Exchange `datn.events` (topic). Mỗi consumer có queue riêng, tên dạng `<service>.<sự-kiện>`, vd `quality-svc.attempt-submitted`. Danh sách dưới trích tự động từ code (`IEventProcessor<T>`, `AddEventConsumer`, `Consumer(...)`, và lệnh phát event).

| Event | Phát | Nhận | Ý nghĩa |
|---|---|---|---|
| `user.registered` | identity | (admin ghi audit) | Đăng ký mới. notification không nghe nữa — đọc view `v_contacts` (3.32) |
| `user.email_verification_requested`, `user.password_reset_requested` | identity | notification | Gửi email xác thực / đặt lại mật khẩu |
| `user.email_verified` | identity | project | Cho phép tạo/ publish dự án |
| `user.blocked` | identity | notification, project | Khoá: project huỷ bài đang làm (mở bài mới thì project hỏi identity qua gRPC — 3.24; ledger hỏi identity lúc rút) |
| `user.unblocked` | identity | notification | Mở khoá |
| `user.sessions_revoked` | identity | (gateway nghe riêng) | Thu hồi phiên |
| `user.identity_verification_submitted`, `user.identity_verified`, `user.identity_rejected`, `user.two_factor_changed` | identity | notification | Xác minh danh tính, 2FA |
| `business.kyb_submitted`, `business.verified`, `business.kyb_rejected` | identity | notification (và admin ghi audit) | KYB |
| `badge.awarded`, `level.changed` | identity | notification | Huy hiệu, cấp độ |
| `project.publish_requested` | project | ledger, admin | Xin giữ ký quỹ. `projectName` cho tiêu đề hàng chờ duyệt của admin; ledger không chép tên (hỏi qua gRPC lúc xuất hoá đơn) |
| `project.grading_applied` | project | identity | project-svc **đã ghi** kết quả chấm vào bài làm: identity xếp labeler có bài đạt vào `level_dirty` (3.24) |
| `escrow.reserved` | ledger | project | Đủ tiền → `PendingApproval` |
| `escrow.rejected` (v2, có `ownerId`) | ledger | project, notification | Thiếu tiền → về `Draft` |
| `project.published` | project | ledger, notification | Dự án chạy: ledger chốt điều khoản tiền, notification báo (phần làm bài mở đợt thu bài ngay trong transaction duyệt). quality (3.22) và gate (3.32) không nghe nữa |
| `samples.ingested` | project | media | Video vừa nạp xong: chuẩn hoá sẵn bản chuẩn (chỉ phát cho video; gate không nghe — đọc view `v_gate_samples`, 3.32) |
| `video.prepared` | media | project | Video đã chuẩn hoá, cho phép publish/duyệt |
| `member.added/blocked/unblocked/removed` | project | notification (`added`), admin (audit) | Thông báo / nhật ký (phần làm bài đọc thẳng `project_members`) |
| `join_request.submitted/decided`, `project.invitation_created` | project | notification | Xin vào / lời mời |
| `project.paused`, `project.resumed` | project | (admin ghi audit) | Tạm dừng / tiếp tục. gate đọc trạng thái qua view (3.32) |
| `attempt.submitted` (v2) | project | quality | Nộp cả bài (nhãn đã ghi cùng transaction). v2 chỉ là **tín hiệu** — không mang câu trả lời; quality xếp bài vào hàng chờ, đọc nhãn qua gRPC |
| `project.collection_finished` | project | quality | Đóng thu bài → quality ghi yêu cầu chấm, đọc bài + nhãn qua gRPC (mọi bài đã commit trước event này) |
| `project.graded` (v2, có `ownerId`) | quality | project, ledger, notification | Kết quả chấm từng bài, ledger trả thưởng bài đạt (identity nghe `project.grading_applied` thay vì event này) |
| `reputation.changed` | quality | identity | Tín hiệu uy tín đổi: identity xếp labeler vào hàng tính lại cấp (điểm đọc view `v_reputations`, 3.32) |
| `fraud.case_confirmed` | quality | identity | Khoá/hạ uy tín do gian lận |
| `attempt.appeal_opened` | project | (admin ghi audit) | Khiếu nại cả bài |
| `attempt.appeal_resolved` | project | identity, ledger, notification | Admin phân xử, ledger trả thưởng nếu thắng (nhãn được nhận vào kết quả ngay trong project) |
| `project.completed`, `project.cancelled` | project | quality, ledger, notification | Đóng dự án, ledger hoàn ký quỹ còn dư |
| `deposit.confirmed` | payment | ledger, notification | Nạp tiền thành công → cộng ví doanh nghiệp |
| `payout.requested` | ledger | payment | Lệnh chi sau khi duyệt rút |
| `payout.completed`, `payout.failed` (v2, có `labelerId`, `netAmountVnd`) | payment | ledger, notification | Chi xong / lỗi (lỗi thì ledger đảo bút toán trả tiền) |
| `withdrawal.review_requested`, `withdrawal.rejected` | ledger | notification | Rút tiền cần admin duyệt / bị từ chối |
| `funds.held`, `hold.expired`, `refund.issued`, `escrow.released` | ledger | notification (một số) | Treo tiền, hết hạn treo, hoàn tiền, nhả ký quỹ |
| ~~`gate.budget_changed`~~ | — | — | **Đã bỏ (3.32)**: gate đọc view `v_gate_budgets` của ledger |
| `gate.solved` | gate | project | Khách vượt link đã gán nhãn |
| `click.validated` | gate | ledger | Lượt click hợp lệ → treo/trả tiền sharer |
| `link.activated`, `link.disabled` | link | ledger (`disabled`), admin ghi audit | Bật/tắt link. gate đọc link đang chạy qua view `v_gate_links` (3.32) |
| `link.review_requested/dismissed` | link | (admin ghi audit) | Link nghi ngờ |
| `referral.registered` | link | (admin ghi audit) | Quan hệ giới thiệu. ledger đọc view `v_referrals` lúc chi hoa hồng (3.32) |
| `referral.flagged/rejected` | link | (admin ghi audit) | Giới thiệu nghi gian lận |
| `setting.changed` | admin | (admin ghi audit) | Admin đổi setting. Không ai đồng bộ theo event nữa — mọi service đọc view `v_settings` (3.32) |
| ~~`settings.snapshot`, `settings.snapshot_requested`~~ | — | — | **Đã bỏ (3.32)** |
| `tag.merged` | admin | project, identity | Gộp thẻ |
| `system.announcement` | admin | notification | Thông báo toàn hệ thống |
| `manual_action.required` | mọi service (EventConsumer dùng chung) | admin | Việc cần xử lý tay (3.29) → bảng `manual_actions`, trang `/admin/viec-tay` (3.33) |
| `manual_action.resolved` | admin | admin (audit) | Admin đánh dấu đã xử lý việc tay |
| `ops.dlq_alert` | admin | notification | Lỗi kỹ thuật trong DLQ vượt ngưỡng → email đội kỹ thuật (3.29) |
| `sensitive_document.viewed` | identity | (admin ghi audit) | Admin xem giấy tờ nhạy cảm |

**Lưu ý:** cột "Nhận" của `setting.changed` chỉ liệt kê quality vì các service C# đăng ký consumer setting bằng cơ chế dùng chung (`shared/settings`) mà quét tự động không bắt được. Thực tế mọi service đều nhận. `admin` còn có `AuditConsumer` bind routing key `#` (mọi event) để ghi `audit_events`, `stats_daily` và dựng `approval_requests`.

### 4.3 Bảo đảm của kênh event

- **Outbox**: ghi cùng transaction, nên không mất event dù broker đang lỗi.
- **At-least-once** nên có `processed_events` ở phía nhận để chống trùng.
- **Hợp đồng nghiêm ngặt**: trường lạ trong payload làm message vào DLQ. Message hỏng vào DLQ để lại log `Error` đi Elasticsearch (Kibana) — không còn màn hình / cảnh báo DLQ trên admin (3.27).
- Các event của cùng một dự án đi **nhiều queue khác nhau**, nên có thể đến lộn thứ tự. Vì vậy quality dùng `pg_advisory_xact_lock` theo `project_id` khi chấm, và các bản sao có cột `*_at` / `version` / `sequence` để bỏ qua dữ liệu cũ. Vòng đời thu bài (tạm dừng, kết thúc, huỷ, nộp) không còn qua event nữa — cùng database với dự án nên khoá dòng `project_collections` là đủ.

---

## 5. Luồng nghiệp vụ chính

### 5.1 Đăng ký và đăng nhập
```
Client → POST /auth/register ──► identity: tạo users, account_tokens, ghi outbox
         identity ──user.registered──► (admin audit); notification đọc view v_contacts lúc gửi (3.32)
         identity ──user.email_verification_requested──► notification: gửi email
Client → POST /auth/verify-email ──► identity: users.email_verified_at; ──user.email_verified──► project
Client → POST /auth/login ──► (2FA? POST /auth/2fa/verify) ──► trả access JWT 15 phút + refresh token
Gateway: kiểm chữ ký JWT bằng JWKS của identity; chặn token phát trước mốc trong session_revocations
Client → POST /auth/refresh: xoay refresh token (generation+1); dùng lại token cũ thì thu hồi phiên
```

### 5.2 Nạp tiền
```
Doanh nghiệp → POST /payments/deposits ──► payment: tạo payment_intents, trả checkout_url
→ thanh toán ở cổng (MoMo / VNPay / sandbox) → cổng gọi webhook /payments/webhooks/*
payment: xác minh HMAC, cập nhật intent ──deposit.confirmed──► ledger: gateway:… → business:{id}:available
                                                           └──► notification
```

### 5.3 Publish dự án (saga)
```
POST /projects → PATCH labelSchema (kèm câu hỏi chung) / pricing / channels / eligibility
→ POST /projects/{id}/samples/zip (ảnh) | /uploads → PUT MinIO → POST /samples (mỗi file một mẫu `queued`; worker xử lý → `ready`/`failed`) | /samples/import (nguồn ngoài)
→ (tuỳ chọn) dòng JSON kèm "question" hoặc PUT /projects/{id}/samples/{sampleId}/question: ngữ cảnh / câu hỏi riêng của mẫu, tuỳ chọn (3.34)
→ POST /projects/{id}/publish: Draft → PendingEscrow, phát project.publish_requested
ledger: giữ ký quỹ S×(R+phí) + ngân sách cổng, idempotent theo projectId
   đủ tiền → escrow.reserved ──► project: PendingApproval
   thiếu   → escrow.rejected ──► project: về Draft, ghi status_reason; notification báo
admin → GET /projects/pending-approval → POST /projects/{id}/approve
project: Running + mở project_collections (cùng transaction)
         ──project.published──► ledger, notification   (gate đọc view v_gate_projects, 3.32)
Quá 72 giờ không duyệt → project.cancelled → ledger hoàn ký quỹ
```

### 5.4 Labeler tham gia và làm bài
```
GET /projects/market (hoặc /projects/recommended) → chọn dự án
Điều kiện: cấp độ, uy tín, xác minh danh tính, NDA (POST .../consent), test đầu vào, vào theo chế độ:
   công khai: POST .../join         mã mời: POST /projects/join-by-code       cần duyệt: POST .../join-requests
   project ghi project_members (phát member.added cho notification / audit)
POST /tasks/projects/{p}/attempt: project đọc projects + project_collections + project_members, hỏi identity qua gRPC
   (khoá / cấp / uy tín — cùng hàm điều kiện với lúc vào dự án; identity lỗi → 503),
   khoá project_collections, kiểm còn suất → tạo attempts (hạn 48h, question_order = mẫu chưa loại + câu vàng)
   trộn câu vàng lẫn câu thật, labeler không phân biệt được
Frontend lưu nháp từng câu vào localStorage (backend không biết, không gọi API)
POST /tasks/attempts/{a}/submit {answers:[{sampleId, data}]}: một transaction (khoá project_collections → attempts)
     ├ kiểm body: trùng câu / câu không thuộc bài / data không phải object → 400; vượt task.attempt_max_bytes → 400
     ├ chấm câu vàng ngay (gold_correct / gold_total, gold_answers)
     ├ INSERT annotations (project, bài, câu, labeler, source, payload) cho các câu thật
     ├ đánh dấu Submitted (xmin chống nộp đôi)
     ├ đủ max_participants bài nộp → đóng thu bài ngay (du_suat)
     └ outbox: attempt.submitted (tín hiệu) ──► quality (pending_attempts → worker xét gian lận qua gRPC)
POST /tasks/attempts/{a}/abandon: trả suất lại
```

### 5.5 Đóng thu bài, chấm, trả thưởng
```
Điều kiện đóng: đủ S bài nộp (lúc nộp)  |  hết deadline (BaiLamWorker)  |  POST /projects/{id}/finish (đóng ngay)
project, MỘT transaction: các bài InProgress → Expired; Running → Grading;
   phát project.collection_finished {bài cần chấm, câu bị loại}
quality: ghi project_gradings rồi chấm ngay — đề bài, bài nộp, câu bị loại, nhãn đọc qua gRPC (khoá advisory theo dự án);
   project-svc không trả lời → yêu cầu nằm chờ, worker viec_cho chấm lại (không DLQ)
   cham_du_an: gộp đa số từng câu (xem phần thuật toán), tính matched/comparable → agreementPercent,
               đạt khi % ≥ pass_threshold_percent VÀ đúng hết câu vàng (< 3 bài: chỉ xét câu vàng)
   có freetext: gọi ml-svc lấy embedding
   phát project.graded {từng bài: %, đạt/trượt}
   uy tín: worker thấy project-svc đã ghi kết quả (gRPC: mọi bài graded) → reputation_dirty →
           GetLabelerEvidence (cộng dồn trên project.attempts) → reputations, phát reputation.changed
       ├─► ledger: trả thưởng R cho bài đạt (kind ProjectReward, vào số dư khả dụng); chặn vượt S suất
       ├─► project: ghi attempts.passed, agreement_percent (nhãn tự thành Accepted / Excluded — suy ra từ bài);
       │            mở hạn khiếu nại (3 ngày)
       │            phát project.grading_applied ──► identity: level_dirty → CapDoWorker hỏi số câu qua gRPC
       │                                                  (LabelerStatsData) → cấp / huy hiệu
       └─► notification
```

### 5.6 Khiếu nại và chốt
```
Labeler trượt → POST /tasks/attempts/{a}/appeal (trong 3 ngày, một lần): project phát attempt.appeal_opened
admin → GET /tasks/admin/appeals → POST /tasks/admin/attempts/{a}/appeal/resolve
   chấp nhận: attempts.appeal_state = Accepted (nhãn của bài tự thành Accepted); attempt.appeal_resolved ─► ledger trả thưởng, identity, notification
   từ chối  : attempt.appeal_resolved ─► notification
Hết hạn khiếu nại và không còn đơn mở (BaiLamWorker) → MỘT transaction:
   project_collections.settled_at, Grading → Completed, phát project.completed {PassedAttemptCount}
ledger: đã trả đủ số bài đạt?  có → hoàn phần ký quỹ còn lại, escrow Closed
                                chưa → escrow Closing, trả nốt khi event đến rồi tự hoàn
```

### 5.7 Rút tiền
```
Labeler → POST /ledger/withdrawals (Idempotency-Key): ledger hỏi identity (khoá?), kiểm số dư, tính thuế 10% nếu ≥ 2 triệu
   Withdrawal luôn khởi tạo ở PendingApproval; ledger tự duyệt (Duyet(null) = hệ thống) hoặc phát
   withdrawal.review_requested để admin duyệt (POST /ledger/admin/withdrawals/{id}/approve|reject)
   khi duyệt: Requested, chuyển available → payout:in_flight (+ tax_withheld) ──payout.requested──► payment
payment: ba pha (ghi payouts → commit → gọi cổng → ghi kết quả)
   thành công: payout.completed ─► ledger: in_flight → gateway;  notification
   thất bại  : payout.failed    ─► ledger: bút toán ĐẢO, trả lại toàn bộ kể cả thuế
```

### 5.8 Cổng vượt link (sharer)
```
Sharer → POST /links (hoặc /links/bulk) ──► link: kiểm domain bị cấm, quét URL; link Active ──► gate thấy qua view v_gate_links (≤ gate.cache_refresh_interval, 3.32)
Khách → GET /g/{code}: gate lấy link từ Redis, kiểm mật khẩu / hết hạn
Khách → POST /g/{code}/sessions: tạo phiên trong Redis (chống trùng IP trong ngày)
Khách gán một vài nhãn (câu thật + câu vàng) → POST /g/sessions/{id}/submit
gate: kiểm câu vàng, ghi click_events vào ClickHouse
   ├ gate.solved ─► project (lưu annotations nguồn cổng link)
   └ click.validated ─► ledger: treo tiền sharer (GateClick), trừ ngân sách cổng (gate đọc view v_gate_budgets, 3.32)
Khách → GET /go/{code}: chuyển tới link đích
Sharer → GET /gate/stats/me/*: thống kê từ ClickHouse
Link vi phạm: link.disabled ─► gate dừng, ledger dừng trả
```

### 5.9 Xem file mẫu có watermark
```
Frontend cần hiển thị ảnh / video / âm thanh của một mẫu
→ backend cấp URL dạng /media/v/{token} (token ký bằng MEDIA_GRANT_KEY, gắn người xem, hết hạn)
→ trình duyệt <img src=…> GET /media/v/{token} (qua gateway, không cần header Authorization)
media: kiểm token, lấy file gốc từ MinIO, nhúng watermark chìm (ảnh / âm thanh / chỉ đầu-cuối video), cache vào media-cache, trả về
media dùng để truy ngược rò rỉ: POST /media/admin/watermark/decode
Video mới nạp: media nghe samples.ingested → chuẩn hoá → phát video.prepared ─► project (không cho publish khi chưa xong)
```

### 5.10 Cập nhật setting (đồng bộ cấu hình)
```
admin → PUT /admin/settings/{key}: ghi settings + setting_history, version+1, phát setting.changed
mọi service (kiến trúc 3.32): warm-up đọc view v_settings của admin_db (role settings_reader) vào bộ nhớ trước khi nhận việc,
  đọc lại mỗi settings.reload_interval — không bản sao, không event đồng bộ (setting.changed chỉ còn để audit)
```
