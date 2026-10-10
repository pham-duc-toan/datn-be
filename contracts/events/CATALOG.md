# Danh muc Event

Nguon su that duy nhat cho giao tiep giua 15 service. **Khong service nao duoc goi HTTP
dong bo sang service khac trong duong nong** — chi qua event, hoac doc tu ban sao read-only
cua chinh minh.

Quy uoc: `<aggregate>.<qua_khu>`. Tat ca boc trong `envelope.schema.json`.

---

## identity-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `user.registered` | notification | Gui email xac thuc |
| `user.email_verified` | notification | Kich hoat tai khoan |
| `user.blocked` | task, gate, link, ledger | Thu hoi lease, chan tao link, khoa rut tien |
| `reputation.changed` | task | Cap nhat `task_svc.labeler_cache` phuc vu loc eligibility. **Hien do quality-svc phat** (identity-svc chua luu diem uy tin) |
| `level.changed` | task, notification | |
| `business.kyb_submitted` | admin | Day vao hang doi duyet (FM-01) |
| `business.verified` | project, ledger | Cho phep publish du an, cho phep nap tien |

## project-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `project.publish_requested` | ledger | **Saga buoc 1** — yeu cau ky quy (2.11) |
| `project.published` | task, annotation, ledger, quality, notification, ml | Sinh task, chot dieu khoan, tran chi tra (`MaxRedundancy`), ban sao tap nhan cho quality |
| `project.paused` / `project.resumed` | task | Ngung/mo cap phat task |
| `project.cancelled` | task, ledger | Dung task + hoan tien phan chua dung |
| `project.completed` | task, ledger, notification | Giai phong escrow con du |
| `dataset.ingested` | task, ml | Sinh task tu sample; ml bat dau pre-label |
| `gold_set.updated` | task, gate | Cap nhat bo cau hoi vang |
| `member.added` / `member.blocked` / `member.unblocked` / `member.removed` | task, annotation, media | Nhan ban `project_members` sang `*_members_cache` — nen cua phan quyen chieu ngang (VD-S-14) |

## task-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `task.leased` | fraud | Do toc do lam bai (FM-07) |
| `assignment.submitted` | annotation | Labeler nop nhan cho mot luot lease HOP LE — annotation-svc luu nhan roi phat `annotation.submitted`. Nop di qua task-svc vi kiem lease va danh dau da nop phai nguyen tu tren du lieu cua task-svc (VD-T-01) |
| `task.lease_expired` | — | Metric |
| `task.redundancy_reached` | quality | Du n ban nhan -> tinh dong thuan |
| `task.redundancy_changed` | — | Audit cho redundancy thich ung (FQ-03): task vua doi redundancy theo yeu cau cua quality |
| `gold.answered` | quality | Labeler tra loi mot cau vang kiem tra tron trong luong task; task-svc tu cham (Crowd.Labeling), mang `Correct` |

## annotation-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `annotation.submitted` | quality, fraud | Dau vao dong thuan va Dawid-Skene; phat hien gian lan |
| `annotation.approved` | ledger, identity | **Chi tra** + cong diem uy tin |
| `annotation.rejected` | notification, identity | Thong bao + tru uy tin |
| `appeal.opened` | admin | Hang doi khieu nai (FL-09) |

## quality-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `consensus.reached` | annotation | Ket qua dong thuan cua MOT task (agreed / disputed / notApplicable) + tung nhan khop hay lech. **Chi la goi y** cho nguoi duyet — khong tu duyet, khong tu chi tien |
| `redundancy.increase_requested` | task | **Vong lap thich ung**: tranh chap ma chua cham tran → xin them MOT nguoi (`NewRedundancy` tuyet doi) |
| `reputation.changed` | task | Uy tin 0-100 = cau vang + muc khop dong thuan / do tin cay Dawid-Skene |
| `dispute.detected` | task, admin | *(chua lam)* Day len reviewer cap cao |
| `gold_failed` | task, fraud | *(chua lam)* Truot cau vang lien tuc -> nghi ngo |

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
| `escrow.rejected` | project, notification | Thieu tien -> tra ve Nhap |
| `escrow.released` | — | Audit |
| `funds.held` | notification | Vao so du cho doi soat (3–7 ngay) |
| `hold.expired` | notification | Da rut duoc |
| `payout.requested` | payment | Lenh rut DA DUYET (tu duyet theo nguong hoac admin duyet) -> chuyen sang tich hop ngoai. Lenh cho duyet KHONG phat |
| `refund.issued` | notification | Huy du an |
| `gate.budget_changed` | gate | Ngan sach cong link con lai cua du an (phan ky quy vuot muc danh cho labeler); `sequence` tang dan |

## payment-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `deposit.confirmed` | ledger | Webhook cong da xac thuc, hoac admin duyet chuyen khoan thu cong (`provider = manual_transfer`) -> ghi so |
| `payout.completed` | ledger, notification | Chot but toan |
| `payout.failed` | ledger, admin | Hoan lai so du kha dung |
| `reconciliation.mismatch` | admin | Doi soat cuoi ngay lech |

## link-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `link.activated` | gate | Link qua kiem duyet (blacklist + quet URL) — gate bat dau phuc vu `/g/{code}`. Phat lai khi doi tuy chon (mat khau, het han, chien dich) |
| `link.disabled` | gate, ledger | Chu link xoa / admin vo hieu hoa. `withholdRevenue = true` (vi pham) → ledger giu doanh thu dang treo cua link |
| `referral.registered` | ledger | Quan he gioi thieu (FS-08) — ledger tra hoa hong tu phan nen tang |

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

## media-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `media.accessed` | admin | Audit log FP-06: ai xem mau nao, luc nao, bao lau |
| `pii.scrubbed` | project | Ban derived da che mat/bien so san sang (FP-04) |

## admin-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `project.approved` / `project.rejected` | project | FM-02 |
| `business.kyb_approved` / `.rejected` | identity | FM-01 |
| `link.banned` | link, gate, ledger | FM-08 |
| `dispute.resolved` | ledger, annotation | FM-06 |
| `setting.changed` | moi service (+ gateway, quality) | Admin doi mot setting: `{key, value, settingVersion, changedBy, changedAt}`. Ban sao chi ghi khi version MOI hon |
| `settings.snapshot` | moi service | Toan bo setting (`items[]`). Phat luc khoi dong, dinh ky, va khi co service xin |
| `settings.snapshot_requested` *(nguoc chieu: moi service -> admin)* | admin | Service vua khoi dong xin phat lai toan bo |

## fraud-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `fraud.flagged` | admin, task, gate | Tam khoa cap task / chan gate |
| `farm.detected` | admin | Nhieu tai khoan cung thiet bi / cung phuong thuc rut |

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
