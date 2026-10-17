# Danh muc Event

Nguon su that duy nhat cho giao tiep giua 15 service. **Khong service nao duoc goi HTTP
dong bo sang service khac trong duong nong** — chi qua event, hoac doc tu ban sao read-only
cua chinh minh.

Quy uoc: `<aggregate>.<qua_khu>`. Tat ca boc trong `envelope.schema.json`.

---

## identity-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `user.registered` | notification, link | notification: ban sao lien lac + vai tro (`contacts.roles`, dung cho thong bao toan he thong). (ledger / project KHONG con nghe: trang thai xac thuc email di theo claim `email_verified` cua access token) |
| `user.email_verification_requested` | notification | FC-01: gui email chua link `{frontend_url}/xac-thuc-email?token=...` (email giao dich, khong qua tuy chon) |
| `user.password_reset_requested` | notification | FC-03: gui link `{frontend_url}/dat-lai-mat-khau?token=...` |
| `user.email_verified` | — | FC-01. Khong con consumer: ledger (rut tien) va project (publish) doc claim `email_verified` trong access token; client lam moi token sau khi xac thuc. Event van phat de audit |
| `user.sessions_revoked` | gateway | S-11: `{userId, revokedAt, reason}` — gateway chan moi access token cua user co `iat < revokedAt` (doi / dat lai mat khau, dang xuat moi noi, bi khoa). Gateway mat event (khoi dong lai) thi nap lai qua `GET /internal/session-revocations` cua identity |
| `user.blocked` | task, ledger, notification (du kien: gate, link) | Chan nhan task, khoa rut tien, bao + email (bat buoc) kem ly do. Phat khi admin khoa (FM-01) hoac khi admin xac nhan ho so gian lan co khoa tai khoan (FM-07) |
| `user.unblocked` | notification, task, ledger | Bao + email "da mo khoa"; task / ledger mo lai nhan task / rut tien |
| `level.changed` | task, notification | FL-12: identity tinh lai cap tu so nhan da duyet + uy tin (`identity.level_rules`) |
| `badge.awarded` | notification | FL-12: huy hieu moi (moc 100 / 1000 / 10000 nhan, uy tin cao) |
| `business.kyb_submitted` | admin | Ho so cho duyet → muc `kyb` trong hang doi duyet chung (`GET /admin/approvals`) |
| `business.verified` | notification | Bao + email "da xac minh". project KHONG con nghe: quyen publish doc claim `kyb_verified` trong access token (KYB chi chan publish, khong chan nap tien) |
| `business.kyb_rejected` | notification | Bao + email (bat buoc) kem ly do; doanh nghiep sua va nop lai |
| `user.two_factor_changed` | notification | FC-04: `{userId, enabled, reason: enabled\|disabled\|admin_reset}` — thong bao + email BAT BUOC (loai `bao_mat`) |
| `user.identity_verification_submitted` | notification, admin | FP-05: nop ho so xac minh danh tinh → muc `identity` trong hang doi duyet chung; bao nguoi nop "da nhan" |
| `user.identity_verified` / `user.identity_rejected` | notification, admin | Ket qua xac minh danh tinh (kem ly do khi tu choi); dong muc hang doi |
| `sensitive_document.viewed` | admin (audit) | S-07: admin lay link xem giay phep KYB / anh giay to — khong ai xu ly, chi de nhat ky audit ghi ai xem giay to cua ai |

## project-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `project.publish_requested` | ledger, notification, admin | **Saga buoc 1** — yeu cau ky quy (2.11). Them `projectName` (ledger in len hoa don). notification: ban sao chu du an (`project_owners`). admin: muc `project` trang thai `waiting` (cho ky quy) |
| `project.published` | task, annotation, ledger, quality, notification, ml | Sinh task, chot dieu khoan, tran chi tra (`MaxRedundancy`), ban sao tap nhan cho quality |
| `project.paused` / `project.resumed` | task | Ngung/mo cap phat task |
| `project.cancelled` | task, ledger, quality, gate, notification | Dung task + hoan tien phan chua dung; bao doanh nghiep neu da ky quy. quality / task: moc bat dau dem `project.closed_data_retention` (don du lieu van hanh); gate: xoa mau cua du an |
| `project.completed` | task, ledger, quality, gate, notification | Giai phong escrow con du. quality / task: moc bat dau dem `project.closed_data_retention`; gate: xoa mau cua du an |
| `dataset.ingested` | task, ml | Sinh task tu sample; ml bat dau pre-label |
| `gold_set.updated` | task, gate | Cap nhat bo cau hoi vang |
| `member.added` / `member.blocked` / `member.unblocked` / `member.removed` | task, annotation, media | Nhan ban `project_members` sang `*_members_cache` — nen cua phan quyen chieu ngang (VD-S-14) |
| `member.added` (them `via`, `projectName`, khong bat buoc) | notification | `via = owner_invite`: bao nguoi duoc chu du an moi. Cac duong khac (`invite_code`, `join`, `entrance_test`, `join_request`) khong bao — nguoi do tu bam, don duoc duyet bao qua `join_request.decided`. Ban tin cu khong co `via` → khong bao |
| `project.invitation_created` | notification | Loi moi vao du an: `userId` co → thong bao + email theo tuy chon; `userId` null (email chua co tai khoan) → email giao dich kem link dang ky `…/dang-ky?email=&invitation=` |
| `join_request.decided` (`reason = "het_han"`) | notification | Don xin tham gia qua `project.join_request_ttl` tu het han — bao labeler "don da het han" |
| `project.published` (them `sensitiveContent`, khong bat buoc) | task, quality | Muc 2.9: task-svc tra `sensitiveContent` trong lease va ep nghi giai lao (`welfare.*`) |
| `project.published` (them `requireReview`, khong bat buoc — thieu = `true`) | annotation | Du an khong can duyet tung nhan: consumer `consensus.reached` tu duyet nhan khop (va task `notApplicable`). Hop dong nghiem ngat (D-12): moi consumer cua `project.published` phai deploy cung dot |
| `join_request.submitted` | notification | Labeler gui don xin tham gia du an che do `approval` (hoac lam dat bai test, kem `scorePercent`) → bao chu du an |
| `join_request.decided` | notification | Chu du an duyet / tu choi don (kem ly do) → bao labeler. Duyet thi `member.added` (`via = join_request`) di cung transaction |

## task-svc

| Event | Consumer | Muc dich |
|---|---|---|
| ~~`task.leased`~~ | — | **DA BO (2026-10-13)**: moi lan nhan task mot event ma khong service nao nghe. Toc do lam bai (FM-07) do bang `LeasedAt` trong `assignment.submitted` |
| `assignment.submitted` | annotation, quality | quality: dau vao chong gian lan (FM-07: lam qua nhanh, mot nhan lien tuc, nhieu tai khoan mot IP / thiet bi). Labeler nop nhan cho mot luot lease HOP LE — annotation-svc luu nhan roi phat `annotation.submitted`. Nop di qua task-svc vi kiem lease va danh dau da nop phai nguyen tu tren du lieu cua task-svc (VD-T-01) |
| `task.lease_expired` | — | Metric |
| `task.redundancy_reached` | quality | Du n ban nhan -> tinh dong thuan |
| `task.redundancy_changed` | — | Audit cho redundancy thich ung (FQ-03): task vua doi redundancy theo yeu cau cua quality |
| `gold.answered` | quality | Labeler tra loi mot cau vang kiem tra tron trong luong task; task-svc tu cham (Crowd.Labeling), mang `Correct` |

## annotation-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `annotation.submitted` | quality, fraud | Dau vao dong thuan (gop nhan moi cong cu — kien truc 3.15) va Dawid-Skene; phat hien gian lan. Mang `sampleMetadata` (kich thuoc anh, do dai...): quality-svc can de so do lech diem moc / duong gap khuc theo duong cheo anh |
| `annotation.approved` | ledger, notification, identity | **Chi tra**; notification: chi nhan kenh chuyen nghiep, thong bao GOP theo du an; identity: dem nhan da duyet (cap do, huy hieu, bang xep hang) |
| `annotation.rejected` | notification | Thong bao |
| `appeal.opened` | admin | Hang doi khieu nai (FL-09) → muc `appeal` trong hang doi duyet chung |
| `appeal.resolved` | admin | Admin phan xu khieu nai xong (`accepted`). Tien / trang thai nhan van di bang `annotation.approved` / `annotation.rejected`; event nay de dong muc `appeal` trong hang doi duyet chung |

## quality-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `consensus.reached` | annotation | Ket qua dong thuan cua MOT task (agreed / disputed / notApplicable) + tung nhan khop hay lech. `final` = ket qua gop theo TUNG cong cu (cung hinh dang voi nhan nop; null = cong cu tranh chap) — voi mat na / chuoi khung co the dai vai chuc KB. **Chi la goi y** cho nguoi duyet — khong tu duyet, khong tu chi tien |
| `redundancy.increase_requested` | task | **Vong lap thich ung**: tranh chap ma chua cham tran → xin them MOT nguoi (`NewRedundancy` tuyet doi) |
| `reputation.changed` | task, identity | Uy tin 0-100 = cau vang + muc khop dong thuan / do tin cay Dawid-Skene |
| `dispute.detected` | task, admin | *(chua lam)* Day len reviewer cap cao |
| `gold_failed` | task, fraud | *(chua lam)* Truot cau vang lien tuc -> nghi ngo |
| `fraud.case_confirmed` | identity | FM-07: admin xac nhan ho so gian lan; `BlockAccounts = true` → identity khoa cac tai khoan (phat tiep `user.blocked`) |

Chong gian lan (FM-07) gop vao quality-svc thay cho fraud-svc rieng: cung doc luong nop bai, tranh them mot service.

quality-svc viet bang Python: hop dong o [services/quality/app/contracts.py](../../services/quality/app/contracts.py)
phai khop tung truong voi record C# (anh chup `payloads.snapshot.txt`); payload la → DLQ.

## ml-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `prelabel.ready` | task | Gan goi y vao task (FA-01) |
| `activelearning.ranked` | task | Doi thu tu uu tien task (FA-02) |
| `guideline.issues_found` | project, notification | LLM phat hien huong dan mo ho (FA-05) |

## ledger-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `escrow.reserved` | project | **Saga buoc 2** — du tien, cho phep publish |
| `escrow.rejected` | project, notification | Thieu tien -> tra ve Nhap; bao "Du an chua publish duoc" |
| `escrow.released` | notification | Bao doanh nghiep phan ky quy con du da ve vi |
| `funds.held` | notification | Vao so du cho doi soat (3–7 ngay) |
| `hold.expired` | notification | Da rut duoc. Thong bao GOP (sharer: moi luot click mot khoan treo) |
| `payout.requested` | payment, notification | Lenh rut DA DUYET (tu duyet theo nguong hoac admin duyet) -> chuyen sang tich hop ngoai. Lenh cho duyet KHONG phat. notification: ban sao chu lenh rut (`withdrawal_owners`) |
| `withdrawal.rejected` | notification | Admin tu choi lenh rut cho duyet; tien da ve vi kha dung. Bao + email (bat buoc) kem ly do |
| `withdrawal.review_requested` | admin | Lenh rut vao hang doi duyet (khong tu duyet) → muc `withdrawal`; dong boi `payout.requested` / `withdrawal.rejected` |
| `refund.issued` | notification | Hoan ky quy (huy / dong du an) |
| `dispute.opened` | notification | FM-06: doanh nghiep tranh chap mot nhan da duyet — khoan treo cua labeler bi dong bang; bao labeler |
| `dispute.resolved` | annotation, identity, notification | FM-06: admin phan xu. `RefundBusiness`: hoan tien cho doanh nghiep, annotation danh dau nhan bi loai, identity tru 1 nhan da duyet. `PayLabeler`: mo bang khoan treo |
| `gate.budget_changed` | gate | Ngan sach cong link con lai cua du an (phan ky quy vuot muc danh cho labeler); `sequence` tang dan |

## payment-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `deposit.confirmed` | ledger, notification | Webhook cong da xac thuc, hoac admin duyet chuyen khoan thu cong (`provider = manual_transfer`) -> ghi so |
| `payout.completed` | ledger, notification | Chot but toan |
| `payout.failed` | ledger, notification (du kien: admin) | Hoan lai so du kha dung |
| `reconciliation.mismatch` | admin | Doi soat cuoi ngay lech |

## link-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `link.activated` | gate | Link qua kiem duyet (blacklist + quet URL) — gate bat dau phuc vu `/g/{code}`. Phat lai khi doi tuy chon (mat khau, het han, chien dich) |
| `link.disabled` | gate, ledger | Chu link xoa / admin vo hieu hoa. `withholdRevenue = true` (vi pham) → ledger giu doanh thu dang treo cua link |
| `referral.registered` | ledger | Quan he gioi thieu (FS-08) — ledger tra hoa hong tu phan nen tang |
| `referral.flagged` | admin | Gioi thieu co dau hieu lam dung (`reasons`: trung_ip_nguoi_gioi_thieu, nhieu_tai_khoan_cung_ip, dot_bien) — CHUA phat `referral.registered` (chua tinh hoa hong) toi khi admin chap nhan |
| `referral.rejected` | admin | Admin bac gioi thieu nghi ngo — khong bao gio tinh hoa hong |
| `link.review_requested` / `link.review_dismissed` | admin | Link vuot nguong bao cao (`link.report_review_threshold`) vao hang doi kiem duyet / admin bo qua bao cao |

## gate-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `gate.solved` | annotation | Nhan cau THAT cua khach da qua cau vang, nguon `linkGateway`, khong thuoc task |
| `click.validated` | ledger | Luot vuot hop le: ky quy → sharer (treo) + nen tang |

## collab-svc  *(Node)*

| Event | Consumer | Muc dich |
|---|---|---|
| `room.opened` / `room.closed` | — | Metric |
| `session.finalized` | annotation | 1 ban nhan chung cho ca phong (FR-09) |
| `contribution.split` | ledger | Chia tien theo cong thuc 0.6/0.4 |
| `split.adjusted` | admin | Leader chinh +-20% -> ghi log de khieu nai |

## media-svc  *(Python)*

Khong phat event nghiep vu: chi phat file mau qua token (FP-03) va gan watermark chim (FP-02). Nghe
`setting.changed` / `settings.snapshot` (bat / tat, do manh watermark) nhu moi service. Cac service phat
link (project, task, annotation, gate) tu ky token bang khoa dung chung — khong goi media-svc.

*Da bo (2026-10-13):* che PII tu dong (`pii.scrubbed`) — xung dot voi du an gan nhan chinh khuon mat / bien
so; nhat ky tung luot xem (`media.accessed`) — phinh theo luu luong ma khong du gia tri.

## admin-svc

*Khong lam (thiet ke cu):* `project.approved` / `project.rejected`, `business.kyb_approved`, `link.banned` — admin duyet
bang API cua chinh service chu (project, identity, link); service chu phat event ket qua (`project.published`,
`business.verified`, `link.disabled`...).

admin-svc nghe **moi** event (routing key `#`, queue `admin-svc.audit`): ghi nhat ky audit, dem thong ke ngay, va dung
**hang doi duyet chung** (`approval_requests`) tu cac event mo / dong don cua 8 luong duyet (xem `HangDoiDuyet`).

| Event | Consumer | Muc dich |
|---|---|---|
| `system.announcement` | notification | FM-09: thong bao toan he thong theo doi tuong (`all` / `labeler` / `business`), tuy chon gui email |
| `ops.dlq_alert` | notification | VD-D-06 / D-12: DLQ vuot `ops.dlq_alert_threshold` (admin-svc doc RabbitMQ management API) — email toi `ops.alert_email` |
| `setting.changed` | moi service (+ gateway, quality) | Admin doi mot setting: `{key, value, settingVersion, changedBy, changedAt}`. Ban sao chi ghi khi version MOI hon |
| `settings.snapshot` | moi service | Toan bo setting (`items[]`). Phat luc khoi dong, dinh ky, va khi co service xin |
| `settings.snapshot_requested` *(nguoc chieu: moi service -> admin)* | admin | Service vua khoi dong xin phat lai toan bo |

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
   `LabelSchema`; `dataset.ingested` (`IngestedSample`) them `Modality`, `Content`,
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
(truong `LabelSchema`), task-svc va annotation-svc chep nguyen khoi:

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
`{"text"}` / `{"prompt"?, "a", "b"}`), khong bao gio ca hai.

Them loai cong cu moi (keypoint, cuboid...) = them mot file `contracts/labeling/tools/*.result.schema.json`
va mot lop `IToolKind` trong `shared/labeling/Tools` — **khong doi hop dong event, khong doi bang**.
