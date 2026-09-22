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
| `reputation.changed` | task | Cap nhat `task_svc.labeler_cache` phuc vu loc eligibility |
| `level.changed` | task, notification | |
| `business.kyb_submitted` | admin | Day vao hang doi duyet (FM-01) |
| `business.verified` | project, ledger | Cho phep publish du an, cho phep nap tien |

## project-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `project.publish_requested` | ledger | **Saga buoc 1** — yeu cau ky quy (2.11) |
| `project.published` | task, notification, ml | Sinh task, thong bao labeler phu hop |
| `project.paused` / `project.resumed` | task | Ngung/mo cap phat task |
| `project.cancelled` | task, ledger | Dung task + hoan tien phan chua dung |
| `project.completed` | ledger, notification | Giai phong escrow con du |
| `dataset.ingested` | task, ml | Sinh task tu sample; ml bat dau pre-label |
| `gold_set.updated` | task, gate | Cap nhat bo cau hoi vang |

## task-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `task.leased` | fraud | Do toc do lam bai (FM-07) |
| `task.lease_expired` | — | Metric |
| `task.redundancy_reached` | quality | Du n ban nhan -> tinh dong thuan |
| `task.redundancy_changed` | — | Audit cho redundancy thich ung (FQ-03) |

## annotation-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `annotation.submitted` | quality, fraud, task | Cham diem vang, do dong thuan, phat hien gian lan |
| `annotation.approved` | ledger, identity | **Chi tra** + cong diem uy tin |
| `annotation.rejected` | notification, identity | Thong bao + tru uy tin |
| `appeal.opened` | admin | Hang doi khieu nai (FL-09) |

## quality-svc  *(Python)*

| Event | Consumer | Muc dich |
|---|---|---|
| `consensus.reached` | annotation, task | Chot nhan cuoi, dong task |
| `dispute.detected` | task, admin | Day len reviewer cap cao |
| `redundancy.increase_requested` | task | **Vong lap thich ung**: 2 nguoi lech -> tang len 5 |
| `worker_confidence.updated` | identity | Dawid–Skene -> diem uy tin |
| `gold_failed` | task, fraud | Truot cau vang lien tuc -> nghi ngo |

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
| `payout.requested` | payment | Chuyen sang tich hop ngoai |
| `refund.issued` | notification | Huy du an |

## payment-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `deposit.confirmed` | ledger | Webhook VNPay/MoMo da xac thuc -> ghi so |
| `payout.completed` | ledger, notification | Chot but toan |
| `payout.failed` | ledger, admin | Hoan lai so du kha dung |
| `reconciliation.mismatch` | admin | Doi soat cuoi ngay lech |

## link-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `link.created` | (worker quet Safe Browsing) | Chua active cho toi khi quet xong |
| `link.scan_completed` | gate | Cho phep phuc vu |
| `link.disabled` | gate, ledger | Vi pham -> chan + giu doanh thu |
| `link.reported` | admin | Hang doi kiem duyet thu cong |
| `referral.registered` | ledger | Huong 10% (FS-08) |

## gate-svc

| Event | Consumer | Muc dich |
|---|---|---|
| `gate.solved` | annotation | Ghi nhan that voi `source = link_gateway` |
| `gate.failed` | fraud | Truot cau vang -> tin hieu bot |
| `click.validated` | link, ledger | Cong CPM cho sharer |

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
| `platform_config.changed` | ledger, task, gate | Phi hoa hong, don gia toi thieu, nguong rut |

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
