# Danh muc Event

Nguon su that duy nhat cho giao tiep giua 13 service (task-svc va annotation-svc da gop vao project-svc). **Khong service nao duoc goi HTTP
dong bo sang service khac trong duong nong** — chi qua event, hoac doc tu ban sao read-only
cua chinh minh.

Quy uoc: `<aggregate>.<qua_khu>`. Tat ca boc trong `envelope.schema.json`.

---

## identity-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `user.registered` | (admin ghi audit) | notification KHONG nghe nua (3.32): dia chi lien lac + vai tro doc view `v_contacts` cua identity luc gui. (ledger / project KHONG con nghe: trang thai xac thuc email di theo claim `email_verified` cua access token; link hoi `POST /internal/users/lookup` lay `registeredAt` luc nhan ma gioi thieu) |
| `user.email_verification_requested` | notification | FC-01: gui email chua link `{frontend_url}/xac-thuc-email?token=...` (email giao dich, khong qua tuy chon) |
| `user.password_reset_requested` | notification | FC-03: gui link `{frontend_url}/dat-lai-mat-khau?token=...` |
| `user.email_verified` | — | FC-01. Khong con consumer: ledger (rut tien) va project (publish) doc claim `email_verified` trong access token; client lam moi token sau khi xac thuc. Event van phat de audit |
| `user.sessions_revoked` | gateway | S-11: `{userId, revokedAt, reason}` — gateway chan moi access token cua user co `iat < revokedAt` (doi / dat lai mat khau, dang xuat moi noi, bi khoa). Gateway mat event (khoi dong lai) thi nap lai qua `GET /internal/session-revocations` cua identity |
| `user.blocked` | project, notification (du kien: gate, link) | project: huy bai dang lam + chan mo bai (`labeler_cache`); ledger KHONG nghe — hoi identity ngay luc rut tien; bao + email (bat buoc) kem ly do. Phat khi admin khoa (FM-01) hoac khi admin xac nhan ho so gian lan co khoa tai khoan (FM-07) |
| `user.unblocked` | notification | Bao + email "da mo khoa". project KHONG nghe nua (3.24): mo bai hoi identity qua gRPC `IdentityData` |
| `level.changed` | notification | FL-12: identity tinh lai cap tu so cau duoc chap nhan (doc qua gRPC `LabelerStatsData` tu `project.attempts`, sau `project.grading_applied` / khieu nai chap nhan) + uy tin (`identity.level_rules`). project KHONG nghe nua (3.24, bo `labeler_cache`) |
| `badge.awarded` | notification | FL-12: huy hieu moi (moc 100 / 1000 / 10000 nhan, uy tin cao) |
| `business.kyb_submitted` | admin | Ho so cho duyet (admin ghi audit; duyet o `GET /kyb/admin` — hang doi duyet chung da bo, 3.33) |
| `business.verified` | notification | Bao + email "da xac minh". project KHONG con nghe: quyen publish doc claim `kyb_verified` trong access token (KYB chi chan publish, khong chan nap tien) |
| `business.kyb_rejected` | notification | Bao + email (bat buoc) kem ly do; doanh nghiep sua va nop lai |
| `user.two_factor_changed` | notification | FC-04: `{userId, enabled, reason: enabled\|disabled\|admin_reset}` — thong bao + email BAT BUOC (loai `bao_mat`) |
| `user.identity_verification_submitted` | notification, admin | FP-05: nop ho so xac minh danh tinh (admin ghi audit; duyet o `/users/admin/identity-verifications` — 3.33) |
| `user.identity_verified` / `user.identity_rejected` | notification, admin | Ket qua xac minh danh tinh (kem ly do khi tu choi); dong muc hang doi |
| `sensitive_document.viewed` | admin (audit) | S-07: admin lay link xem giay phep KYB / anh giay to — khong ai xu ly, chi de nhat ky audit ghi ai xem giay to cua ai |

## project-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `project.publish_requested` | ledger, admin | **Saga buoc 1** — yeu cau ky quy (2.11). `projectName`: admin dat lam tieu de muc cho duyet; ledger KHONG chep nua (3.24) — hoi ten qua gRPC `LedgerData` luc xuat hoa don. admin: muc `project` trang thai `waiting` (cho ky quy) |
| `project.published` **v2** | ledger, notification, ml | Du an lam trung tam (kien truc 3.18): `RewardVnd` / `PlatformFeeVnd` MOI SUAT, `Slots`, `PassThresholdPercent` (t), `FillSlotsMinPercent` (lap suat trong, khong bat buoc), `QuestionCount`, kenh, `GateUnitPriceVnd` / `GatePlatformFeeVnd` / `GateBudgetVnd` (cong link). project-svc mo dot thu bai ngay trong transaction duyet (khong qua event); ledger: chot dieu khoan tra thuong (chan vuot so suat). gate KHONG nghe nua (3.32): doc view `v_gate_projects`. quality KHONG nghe nua (3.22): tap nhan / nguong doc qua gRPC `QualityData.GetProject` luc cham |
| ~~`project.finish_requested`~~ | — | **DA BO (gop service)**: ket thuc som dong thu bai ngay trong project-svc, phat thang `project.collection_finished` (`ket_thuc_som`) |
| `project.paused` / `project.resumed` | (admin ghi audit) | gate KHONG nghe nua (3.32): trang thai doc qua view `v_gate_projects`. Phan lam bai ghi ngay trong project-svc (`project_collections.paused_at`, chay tiep thi cong bu thoi gian cho bai dang lam) |
| `project.cancelled` **v2** | ledger, quality, notification | Chi khi CHUA co bai nop (khong con `ApprovedAnnotationCount`) — huy bai dang lam + hoan TOAN BO ky quy; bao doanh nghiep neu da ky quy. quality: moc bat dau dem `project.closed_data_retention` (don du lieu van hanh). gate tu bo du an khoi bo nho khi view khong con tra ve |
| `project.grading_applied` | identity | 3.24: project-svc DA GHI ket qua cham (`project.graded`) vao bai lam — `ApprovedLabelerIds` = labeler co bai dat. identity xep vao hang cho `level_dirty`, worker doc so cau qua gRPC roi tinh cap. Tach khoi `project.graded` vi luc quality phat event do project-svc co the chua ghi, tong doc ve se thieu du an vua cham |
| `project.completed` **v2** | ledger, quality, notification | `PassedAttemptCount` = so bai dat cuoi cung: ledger tra du so bai do roi hoan phan con lai (con thieu → `Closing`, NC-B-01). quality: moc bat dau dem `project.closed_data_retention`. gate tu bo du an khoi bo nho khi view khong con tra ve |
| `samples.ingested` v2 | media | media cat san HLS tung doan mau cua video (chi phat cho video; moi event la MOT file + cac doan cat cua no; `watermarkText` = chu dung chung in phan giua, 3.37) (ghi MinIO). gate KHONG nghe: doc mau + cau vang qua view `v_gate_samples` / `v_gate_gold` cua project (3.32) |
| ~~`gold_set.updated`~~ | — | **DA BO (3.23)**: chi gate nghe; gate keo cau vang qua gRPC luc publish (cau vang khoa tu luc roi Draft) |
| `video.prepared` (media phat) | project | media-svc cat xong HLS moi doan mau cua mot FILE video (`succeeded`, `error`). project ghi `samples.media_state`; con video dang chuan bi thi chua publish / duyet |
| `member.added` / `member.blocked` / `member.unblocked` / `member.removed` | admin (audit) | Phan lam bai / nhan doc thang `project_members` (cung database) nen khong con ban sao `*_members_cache`; event van phat de audit |
| `member.added` (them `via`, `projectName`, khong bat buoc) | notification | `via = owner_invite`: bao nguoi duoc chu du an moi. Cac duong khac (`invite_code`, `join`, `entrance_test`, `join_request`) khong bao — nguoi do tu bam, don duoc duyet bao qua `join_request.decided`. Ban tin cu khong co `via` → khong bao |
| `project.invitation_created` | notification | Loi moi vao du an: `userId` co → thong bao + email theo tuy chon; `userId` null (email chua co tai khoan) → email giao dich kem link dang ky `…/dang-ky?email=&invitation=` |
| `join_request.decided` (`reason = "het_han"`) | notification | Don xin tham gia qua `project.join_request_ttl` tu het han — bao labeler "don da het han" |
| `project.published` (`sensitiveContent`, khong bat buoc) | — | Muc 2.9: project-svc tra `sensitiveContent` trong bai lam (frontend che mau toi khi bam hien). Ep nghi giai lao da bo (3.18) |
| `join_request.submitted` | notification | Labeler gui don xin tham gia du an che do `approval` (hoac lam dat bai test, kem `scorePercent`) → bao chu du an |
| `join_request.decided` | notification | Chu du an duyet / tu choi don (kem ly do) → bao labeler. Duyet thi `member.added` (`via = join_request`) di cung transaction |

## project-svc — phan lam bai (truoc la task-svc)

| Event | Consumer | Muc dich |
|---|---|---|
| `attempt.submitted` **v2** | quality | 3.18: labeler nop CA BAI; nhan `submitted` da ghi cung transaction trong project-svc. **v2 (3.22): chi la TIN HIEU** — bo `Answers` (cau tra loi + noi dung mau), them `AnsweredCount`; cau vang chi mang `GoldCorrect` / `GoldTotal`. quality: xep bai vao `pending_attempts`, worker doc bai + nhan qua gRPC de chong gian lan (FM-07: giay / cau, chuoi cung lop, chung IP / thiet bi) va nhung cau tra loi mo; danh dau du an can tinh lai DS |
| `project.collection_finished` | quality | 3.18: dong thu bai (`du_suat` / `het_han` / `ket_thuc_som`), danh sach bai phai cham + cau bi loai (bao loi duoc chap nhan). Running → Grading cung transaction. quality (3.22): ghi yeu cau cham roi doc de bai / bai nop / nhan qua gRPC — moi bai da commit truoc event nay; project-svc khong tra loi thi worker cham lai |
| `attempt.appeal_opened` | admin | 3.18: labeler truot khieu nai CA BAI (admin ghi audit; phan xu o `GET /tasks/admin/appeals` — 3.33) |
| `attempt.appeal_resolved` | ledger, identity, notification, admin | Admin phan xu. `Accepted`: nhan cua bai vao ket qua (ngay trong project-svc), tra thuong, cong cap do (`AnsweredCount`); dong muc hang doi |
| ~~`project.settled`~~ | — | **DA BO (gop service)**: het han khieu nai → project-svc chot va chuyen Completed cung transaction, phat thang `project.completed` |
| ~~`assignment.submitted`~~, ~~`task.leased`~~, ~~`task.lease_expired`~~, ~~`task.redundancy_reached`~~, ~~`task.redundancy_changed`~~, ~~`gold.answered`~~ | — | **DA BO (3.18)**: khong con lease / redundancy / cau vang tron trong luong task |

## project-svc — phan nhan (truoc la annotation-svc)

| Event | Consumer | Muc dich |
|---|---|---|
| ~~`annotation.submitted`~~, ~~`annotation.approved`~~, ~~`annotation.rejected`~~, ~~`appeal.opened`~~, ~~`appeal.resolved`~~ | — | **DA BO (3.18)**: khong con duyet / khieu nai tung nhan. Nhan duoc LUU luc nop bai va CHOT vao ket qua theo `project.graded` / phan xu khieu nai; khong phat event nghiep vu nao |

## quality-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `project.graded` **v2** | project, ledger, notification | 3.18: cham MOT lan ca du an. v2 them `OwnerId` (notification bao chu du an ma khong can ban sao). Moi bai: `ComparableCount`, `MatchedCount`, `AgreementPercent`, cau vang, `Passed` (= % >= t VA dung het cau vang), `AnsweredCount`; `MajorityUsed = false` khi < 3 bai (chi xet cau vang). ledger: tra thuong bai dat; project: ghi ket qua bai + nhan Accepted / Excluded |
| ~~`consensus.reached`~~, ~~`redundancy.increase_requested`~~ | — | **DA BO (3.18)**: khong con dong thuan theo task / redundancy thich ung |
| `reputation.changed` | identity | Uy tin 0-100 = cau vang + ti le cau giong da so (cong don qua cac bai da cham) / do tin cay Dawid-Skene. Tu 3.32 identity chi dung lam TIN HIEU tinh lai cap — diem doc view `v_reputations` cua quality |
| `dispute.detected` | project, admin | *(chua lam)* Day len reviewer cap cao |
| `gold_failed` | project, quality | *(chua lam)* Truot cau vang lien tuc -> nghi ngo |
| `fraud.case_confirmed` | identity | FM-07: admin xac nhan ho so gian lan; `BlockAccounts = true` → identity khoa cac tai khoan (phat tiep `user.blocked`) |

Chong gian lan (FM-07) gop vao quality-svc thay cho fraud-svc rieng: cung doc luong nop bai, tranh them mot service.

quality-svc viet bang Python: hop dong o [services/quality/app/contracts.py](../../services/quality/app/contracts.py)
phai khop tung truong voi record C# (anh chup `payloads.snapshot.txt`); payload la → DLQ.

Du lieu quality DOC DONG BO tu project-svc (de bai, bai nop, nhan, IP / thiet bi, bang chung uy tin) khong di
qua event ma qua gRPC noi bo: hop dong o [contracts/grpc/project/v1/quality_data.proto](../grpc/project/v1/quality_data.proto)
(kien truc 3.22). gate KHONG dung gRPC nua: doc thang view `v_gate_*` cua project / ledger / link de nap bo nho
(3.32, thay ban sao + `gate_data.proto` cua 3.23). Kien truc 3.24 them: identity phuc vu
[identity/v1/identity_data.proto](../grpc/identity/v1/identity_data.proto) (project tra cuu nguoi dung, dieu kien mo bai);
project phuc vu [project/v1/labeler_stats.proto](../grpc/project/v1/labeler_stats.proto) (identity: so cau duoc chap nhan,
bang xep hang) va [project/v1/ledger_data.proto](../grpc/project/v1/ledger_data.proto) (ledger: ten du an in hoa don);
quality phuc vu [quality/v1/label_aggregation.proto](../grpc/quality/v1/label_aggregation.proto) (project: gop nhan giao
doanh nghiep — thuat toan gop chi con o quality). Stub Python sinh san o `services/quality/app/grpc_gen` (`sinh_grpc.py`), C# sinh luc build.

## ml-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `prelabel.ready` | project | Gan goi y vao cau hoi (FA-01) |
| `activelearning.ranked` | project | Doi thu tu uu tien cau hoi (FA-02) |
| ~~`guideline.issues_found`~~ | — | Da bo cung huong dan Markdown cua du an (kien truc 3.34) — chua tung duoc phat |

## ledger-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `escrow.reserved` | project | **Saga buoc 2** — du tien, cho phep publish |
| `escrow.rejected` **v2** | project, notification | Thieu tien -> tra ve Nhap; bao "Du an chua publish duoc". v2 them `OwnerId` (notification khong can ban sao chu du an) |
| `escrow.released` | notification | Bao doanh nghiep phan ky quy con du da ve vi |
| `funds.held` | notification, admin (dem) | Mot khoan thu cua nguoi lam: thuong bai dat (`ledger.reward_hold` = 0 → giai phong ngay), doanh thu cong link, hoa hong. `AnnotationId` = id bai lam / luot click |
| `hold.expired` | notification | Da rut duoc. Thong bao GOP (sharer: moi luot click mot khoan treo) |
| `payout.requested` | payment | Lenh rut DA DUYET (tu duyet theo nguong hoac admin duyet) -> chuyen sang tich hop ngoai. Lenh cho duyet KHONG phat |
| `withdrawal.rejected` | notification | Admin tu choi lenh rut cho duyet; tien da ve vi kha dung. Bao + email (bat buoc) kem ly do |
| `withdrawal.review_requested` | admin | Lenh rut cho admin duyet (khong tu duyet) — trang rut tien; dong boi `payout.requested` / `withdrawal.rejected` |
| `refund.issued` | notification | Hoan ky quy (huy / dong du an) |
| ~~`dispute.opened`~~, ~~`dispute.resolved`~~ | — | **DA BO (3.18)**: khong con tranh chap nhan da duyet (khong con duyet tung nhan) |

## payment-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `deposit.confirmed` | ledger, notification | Webhook cong da xac thuc, hoac admin duyet chuyen khoan thu cong (`provider = manual_transfer`) -> ghi so |
| `payout.completed` **v2** | ledger, notification | Chot but toan. v2 them `LabelerId`, `NetAmountVnd` (notification bao nguoi rut khong can ban sao) |
| `payout.failed` **v2** | ledger, notification (du kien: admin) | Hoan lai so du kha dung. v2 them `LabelerId`, `NetAmountVnd` |
| `reconciliation.mismatch` | admin | Doi soat cuoi ngay lech |

## link-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `link.activated` | (admin ghi audit) | Link qua kiem duyet (blacklist + quet URL). gate KHONG nghe nua (3.32): link dang chay doc qua view `v_gate_links` |
| `link.disabled` | ledger | Chu link xoa / admin vo hieu hoa. `withholdRevenue = true` (vi pham) → ledger giu doanh thu dang treo cua link |
| `referral.registered` | (admin ghi audit) | Quan he gioi thieu (FS-08). Tu 3.32 ledger KHONG nghe: doc view `v_referrals` cua link (chi quan he active) luc chi hoa hong tu phan nen tang |
| `referral.flagged` | admin | Gioi thieu co dau hieu lam dung (`reasons`: trung_ip_nguoi_gioi_thieu, nhieu_tai_khoan_cung_ip, dot_bien) — CHUA phat `referral.registered` (chua tinh hoa hong) toi khi admin chap nhan |
| `referral.rejected` | admin | Admin bac gioi thieu nghi ngo — khong bao gio tinh hoa hong |
| `link.review_requested` / `link.review_dismissed` | admin | Link vuot nguong bao cao (`link.report_review_threshold`) vao hang doi kiem duyet / admin bo qua bao cao |

## gate-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `gate.solved` | project | Nhan cau THAT cua khach da qua cau vang, nguon `linkGateway`, khong thuoc bai lam nao |
| `click.validated` | ledger | Luot vuot hop le: ky quy → sharer (treo) + nen tang |

## collab-svc  *(Node)*

| Event | Consumer | Muc dich |
|---|---|---|
| `room.opened` / `room.closed` | — | Metric |
| `session.finalized` | project | 1 ban nhan chung cho ca phong (FR-09) |
| `contribution.split` | ledger | Chia tien theo cong thuc 0.6/0.4 |
| `split.adjusted` | admin | Leader chinh +-20% -> ghi log de khieu nai |

## media-svc  *(Python)*

Khong phat event nghiep vu: chi phat file mau qua token (FP-03) va gan watermark (FP-02: anh / video chu noi, video
HLS theo doan mau; am thanh watermark chim — 3.37). Doc view `v_settings` cua admin_db (bat / tat watermark) nhu moi
service (3.32). Cac service phat
link (project, gate) tu ky token bang khoa dung chung — khong goi media-svc.

*Da bo (2026-10-13):* che PII tu dong (`pii.scrubbed`) — xung dot voi du an gan nhan chinh khuon mat / bien
so; nhat ky tung luot xem (`media.accessed`) — phinh theo luu luong ma khong du gia tri.

## admin-svc

*Khong lam (thiet ke cu):* `project.approved` / `project.rejected`, `business.kyb_approved`, `link.banned` — admin duyet
bang API cua chinh service chu (project, identity, link); service chu phat event ket qua (`project.published`,
`business.verified`, `link.disabled`...).

admin-svc nghe **moi** event (routing key `#`, queue `admin-svc.audit`): ghi nhat ky audit, dem thong ke ngay, va dung
bang **viec can xu ly tay** (`manual_actions`) tu `manual_action.required` / `.resolved`. Hang doi duyet chung (`approval_requests`) da bo (3.33).

| Event | Consumer | Muc dich |
|---|---|---|
| `system.announcement` | notification | FM-09: thong bao toan he thong theo doi tuong (`all` / `labeler` / `business`), tuy chon gui email |
| `manual_action.required` | admin | Kien truc 3.29 (loai 2): EventConsumer dung chung phat thay handler nem `CanXuLyTayException` — `{actionId, kind, title, description, projectId?, userId?, amountVnd?, sourceEventType, sourceEventId}`; admin dua vao hang doi duyet kind `manual`. Producer = service cua consumer |
| `manual_action.resolved` | admin (audit) | Admin bam "Da xu ly" (`POST /admin/manual-actions/{id}/resolve`) — `{actionId, note}`, actor = admin |
| `ops.dlq_alert` | notification | VD-D-06 / D-12, kien truc 3.29 (loai 3): DLQ vuot `ops.dlq_alert_threshold` (admin-svc doc RabbitMQ management API) — email toi `ops.alert_email` (doi ky thuat). Nay DLQ chi con loi ky thuat |
| `setting.changed` | (admin ghi audit) | Admin doi mot setting: `{key, value, settingVersion, changedBy, changedAt}`. Tu 3.32 khong ai dong bo theo event nay: moi service doc view `v_settings` moi `settings.reload_interval` |
| ~~`settings.snapshot`~~ | — | **DA BO (3.32)**: moi service doc thang view `v_settings` cua admin_db vao bo nho |
| ~~`settings.snapshot_requested`~~ | — | **DA BO (3.32)** |

## fraud-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `fraud.flagged` | admin, task, gate | *(khong lam — da gop vao quality-svc, xem `fraud.case_confirmed`)* |
| `farm.detected` | admin | *(khong lam — quality-svc mo ho so `shared_ip` / `shared_device`)* |

**Audit (FP-06):** admin-svc co queue `admin-svc.audit` bind `#` — DEM moi event vao `stats_daily`
(ngay gio Viet Nam, loai, so luong, tien) cho bao cao; chi LUU CHI TIET event khong nam trong
`admin.audit_excluded_types` vao `audit_events`, giu `admin.audit_retention_days` ngay. Mac dinh loai cac
event theo tung luot nop / tung click (`assignment.submitted`, `annotation.*` tan suat cao,
`consensus.reached`, `gold.answered`, `funds.held`, `click.validated`...).

**Don dep:** moi service xoa event DA GUI trong `outbox` sau `outbox.retention` (7 ngay) va dau vet
`processed_events` sau `consumers.processed_retention` (14 ngay). Message nam trong DLQ lau hon thoi han
do roi moi dua lai co the bi xu ly lan hai — dua lai som, hoac tang `consumers.processed_retention`.

---

## Luat cung

1. **Outbox bat buoc.** Event ghi vao bang `outbox` trong CUNG transaction voi du lieu
   nghiep vu. Mot dispatcher nen day sang RabbitMQ. Khong bao gio `SaveChanges()` roi
   `Publish()` — service chet o giua la mat event.
2. **Consumer phai idempotent.** Bang `processed_events(event_id)` unique. RabbitMQ la
   at-least-once, event SE den 2 lan.
3. **Ban sao read-only khong phai nguon su that.** `task_svc.labeler_cache` chi de loc;
   quyet dinh tien bac luon hoi lai chu so huu.
4. **Doi schema = tang `version`,** phat song song 2 version, xoa version cu khi consumer
   cuoi cung da chuyen. Khong bao gio sua schema tai cho.

   *Ngoai le da ghi nhan (2026-10-06):* `assignment.submitted`, `annotation.submitted`,
   `gold_set.updated` doi `Labels`/`ExpectedLabels` (danh sach chuoi) sang `LabelPayload`
   **ngay tren v1**. Ly do: chua co production, chua co consumer nao ngoai ba service deploy
   dong loat — giu song song v1/v2 la ganh hai phien ban khong ai dung. Ke tu khi co
   production, luat 4 ap dung khong ngoai le.

   *Ngoai le thu hai (2026-10-06, cung ly do):* ho tro nhieu loai du lieu —
   `project.published` bo `TaskType`/`LabelClasses`/`AllowMultipleLabels`, them `Modality` +
   `LabelSchema`; `dataset.ingested` (nay la `samples.ingested`; `IngestedSample`) them `Modality`, `Content`,
   `Metadata`, `StorageKey` thanh nullable; `assignment.submitted` them `SampleContent`,
   `SampleMetadata`, `StorageKey` nullable; `LabelPayload.taskType` doi tu
   `imageClassification` sang loai du lieu (`image`...). Du lieu cu duoc chuyen bang
   migration `NhieuLoaiDuLieu` cua project / task / annotation.

   *Ngoai le thu ba (2026-10-07, cung ly do):* `project.published` them `MaxRedundancy`
   (tran redundancy thich ung) va `GoldCheckPercent` ngay tren v1. Du lieu cu: migration
   `KiemSoatChatLuong` dat tran = redundancy, ti le cau vang = 10%.

   *Ngoai le thu tu (2026-10-10, cung ly do):* `project.completed` va `project.cancelled` them
   `ApprovedAnnotationCount` (`int?`) ngay tren v1 — so nhan da duyet luc dong so, de ledger
   chi hoan ky quy khi da chi du (annotation.approved di queue khac, co the toi sau; TLA+
   `NC-B-01` tim ra). `null` = event cu: ledger hoan ngay nhu truoc.

   *Ngoai le thu nam (2026-10-12, cung ly do):* `assignment.submitted` them `ClientIp` va
   `DeviceId` (`string?`) ngay tren v1 — dau vao chong gian lan cua quality-svc (FM-07).
   `null` = event cu hoac client khong gui header `X-Device-Id`: bo qua tieu chi IP / thiet bi.

## Dinh dang nhan: tap nhan (`LabelSchema`) + nhan (`LabelPayload`)

Dinh nghia trong `shared/labeling` (Crowd.Labeling). Hinh dang JSON nam trong
`contracts/labeling/*.schema.json` (JSON Schema 2020-12) — nguon su that cho ca backend
(kiem bang JsonSchema.Net) va frontend sau nay.

**Tap nhan** cua du an = loai du lieu + danh sach cong cu. Mang trong `project.published`
(truong `LabelSchema`), gate-svc chep nguyen khoi; project-svc doc thang cot `label_schema`, quality-svc doc qua gRPC:

```json
{ "modality": "audio", "segmentSeconds": 10,
  "tools": [ { "name": "loi_noi", "kind": "transcription" },
             { "name": "doan", "kind": "temporalSegment", "classes": ["noi", "nhac"], "required": false } ] }
```

- `modality` — `image` | `text` | `audio` | `video` | `pair`.
- `segmentSeconds` — audio / video: file dai hon thi cat thanh nhieu mau (moi doan mot task).
- `tools[].kind` — `classification` | `bbox` | `polygon` | `span` | `transcription` |
  `temporalSegment` | `pairwise`. Cong cu nao dung voi loai du lieu nao: `ToolKinds.HopVoi`.
- Tuy chon cua cong cu: `classes`, `allowMultiple`, `required` (mac dinh true), `minItems`,
  `maxItems`, `maxLength`, `allowTie`, `matchThreshold` (nguong cham cau vang).

**Nhan** — moi event mang noi dung nhan deu dung chung mot hinh dang:

```json
{ "taskType": "image", "schemaVersion": 1,
  "data": { "loai": { "labelIds": ["xe"] }, "vat": [ { "labelId": "xe", "x": 10, "y": 20, "w": 50, "h": 40 } ] } }
```

- `taskType` — loai du lieu (`modality`) cua du an.
- `schemaVersion` — phien ban hinh dang cua `data` (hien la 1).
- `data` — JSON long nhau, khoa = ten cong cu. Hinh dang ket qua tung loai cong cu:

| kind | ket qua | kiem them theo mau |
|---|---|---|
| `classification` | `{ "labelIds": ["lop", ...] }` | lop co trong tap nhan; single-label dung mot lop |
| `bbox` | `[ { "labelId", "x", "y", "w", "h" } ]` | khung nam trong anh (`metadata.width/height`) |
| `polygon` | `[ { "labelId", "points": [[x, y], ...] } ]` — 3..500 dinh | dinh nam trong anh |
| `span` | `[ { "labelId", "start", "end" } ]` — chi so ky tu | `end <=` do dai van ban |
| `transcription` | `{ "text": "..." }` | dai `<= maxLength` |
| `temporalSegment` | `[ { "labelId", "start", "end" } ]` — giay, tinh tu dau doan | `end <=` thoi luong doan |
| `pairwise` | `{ "choice": "a" \| "b" \| "tie" }` | `tie` chi khi `allowTie` |

**Metadata mau** (`IngestedSample.Metadata`, `AssignmentSubmitted.SampleMetadata`):
`width`, `height`, `durationSec`, `length`, `segmentStart`, `segmentEnd`, `sourceDurationSec`
— truong nao khong co thi bo. Mau la file (`StorageKey`) hoac noi dung (`Content`:
`{"text"}` / `{"a", "b"}` / `{"items"}`; cau hoi rieng cua mau o samples.question, khong nam trong content — 3.34), khong bao gio ca hai.

Them loai cong cu moi (keypoint, cuboid...) = them mot file `contracts/labeling/tools/*.result.schema.json`
va mot lop `IToolKind` trong `shared/labeling/Tools` — **khong doi hop dong event, khong doi bang**.
