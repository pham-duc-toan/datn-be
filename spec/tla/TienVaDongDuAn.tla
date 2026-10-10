--------------------------- MODULE TienVaDongDuAn ---------------------------
(***************************************************************************)
(* NC-B-01 — Dac ta hinh thuc cho BAT BIEN TIEN cua he thong gan nhan.      *)
(*                                                                         *)
(* Mo hinh MOT du an, vai labeler, bon service lien quan toi tien:         *)
(*   project-svc   : publish (saga ky quy), tam dung / chay tiep, DONG     *)
(*   task-svc      : cap lease, nhan nop, ban sao trang thai du an         *)
(*   annotation-svc: nhan, duyet / tu choi, khieu nai, DONG SO             *)
(*   ledger-svc    : ky quy, chi tien nhan (treo), giai phong, rut, hoan   *)
(*   payment-svc   : chuyen tien rut (thanh cong / that bai)               *)
(*                                                                         *)
(* MOI ACTION = MOT TRANSACTION trong code (khoa dong / FOR SHARE /        *)
(* FOR UPDATE / advisory lock lam no nguyen tu).                           *)
(*                                                                         *)
(* Ha tang duoc mo hinh dung nhu he thong that phai chiu:                  *)
(*   - event ghi vao outbox CUNG transaction voi thay doi trang thai;      *)
(*   - giao IT NHAT MOT LAN, co the LAP (chet sau commit truoc ACK,        *)
(*     dispatcher gui lai) va SAI THU TU giua cac queue: msgs la mot TAP,  *)
(*     consumer lay bat ky phan tu nao, bao nhieu lan cung duoc;           *)
(*   - consumer co processed_events (daXuLy) — giao lai la no-op;          *)
(*   - handler nem loi vinh vien → message vao DLQ (dlq).                  *)
(*                                                                         *)
(* Cac co BUG_* bat lai tung loi da gap / da sua de chung minh mo hinh BAT *)
(* DUOC loi (TLC in trace vi pham), khong phai "dung vi qua long".         *)
(***************************************************************************)
EXTENDS Integers, FiniteSets, TLC

CONSTANTS
    Labelers,           \* tap labeler (model value)
    Tasks,              \* tap task cua du an (model value)
    R,                  \* tran redundancy moi task (so luot chi toi da)
    Gia,                \* don gia mot nhan (phi nen tang = 0 de mo hinh gon)
    SoTienNap,          \* cac muc doanh nghiep co the nap (tap so nguyen)
    MaxTamDung,         \* so lan tam dung toi da (chan khong gian trang thai)
    MaxPublish,         \* so lan publish toi da (thieu tien → publish lai)
    KHONG,              \* model value: truong khong dung cua message
    BUG_KHONG_IDEMPOTENT,         \* ledger bo kiem "nhan da chi" + processed_events
    BUG_DONG_KHONG_DEM_NHAN,      \* dong so khong so so nhan voi so luot nop ben task
    BUG_DONG_KHONG_HAN_KHIEU_NAI, \* dong so khong cho het han khieu nai
    BUG_DONG_KHI_CON_LEASE,       \* dong khong can task-svc da tam dung / het lease
    BUG_HOAN_KY_QUY_NGAY          \* ledger hoan ky quy ngay khi nhan project.completed

Ann == Tasks \X Labelers                 \* id nhan = (task, labeler)
NganSach == Cardinality(Tasks) * R * Gia \* ky quy toi thieu = so mau x tran x don gia

VARIABLES
    \* ---- project-svc
    duAn,        \* "Nhap" | "ChoKyQuy" | "Chay" | "TamDung" | "Xong"
    soSK,        \* bo dem event cua du an (thu tu project.*)
    soTamDung, soPublish,
    dongDuyet,   \* -1 = chua goi dong so thanh cong; >= 0 = da dong so, so nhan da duyet luc dong
    \* ---- ledger-svc: so du (so cai ghi kep) va trang thai
    ngoai, dn, kyQuy, treo, khaDung, dangChuyen, daRa,
    daNap, kyQuyTT, soCanChi, soLanChi, rut,
    \* ---- task-svc
    taskTT, taskN, lease, daNopTask,
    \* ---- annotation-svc
    nhan, conHan, annDong, annDongTai, annFinal,
    \* ---- ha tang
    msgs, daXuLy, dlq

duAnVars == <<duAn, soSK, soTamDung, soPublish, dongDuyet>>
tienVars == <<ngoai, dn, kyQuy, treo, khaDung, dangChuyen, daRa>>
ledVars  == <<daNap, kyQuyTT, soCanChi, soLanChi, rut>>
taskVars == <<taskTT, taskN, lease, daNopTask>>
annVars  == <<nhan, conHan, annDong, annDongTai, annFinal>>
netVars  == <<msgs, daXuLy, dlq>>
vars == <<duAnVars, tienVars, ledVars, taskVars, annVars, netVars>>

Msg(loai, n, a, l, duyet) == [loai |-> loai, n |-> n, a |-> a, l |-> l, duyet |-> duyet]
SK(loai, n) == Msg(loai, n, KHONG, KHONG, 0)        \* event cua du an

RECURSIVE Tong(_, _)
Tong(f, S) == IF S = {} THEN 0 ELSE LET x == CHOOSE x \in S : TRUE IN f[x] + Tong(f, S \ {x})

ChiCuaTask(t) == Tong(soLanChi, {t} \X Labelers)
DaChi == Tong(soLanChi, Ann)
SoDuyet == Cardinality({a \in Ann : nhan[a] = "Duyet"})

Init ==
    /\ duAn = "Nhap" /\ soSK = 0 /\ soTamDung = 0 /\ soPublish = 0 /\ dongDuyet = -1
    /\ ngoai = 0 /\ dn = 0 /\ kyQuy = 0 /\ dangChuyen = 0 /\ daRa = 0
    /\ treo = [l \in Labelers |-> 0] /\ khaDung = [l \in Labelers |-> 0]
    /\ daNap = FALSE /\ kyQuyTT = "Chua" /\ soCanChi = 0
    /\ soLanChi = [a \in Ann |-> 0] /\ rut = [l \in Labelers |-> "Khong"]
    /\ taskTT = "Chua" /\ taskN = 0 /\ lease = [a \in Ann |-> FALSE] /\ daNopTask = {}
    /\ nhan = [a \in Ann |-> "Khong"] /\ conHan = [a \in Ann |-> FALSE]
    /\ annDong = FALSE /\ annDongTai = 0 /\ annFinal = FALSE
    /\ msgs = {} /\ daXuLy = {} /\ dlq = {}

\* ===================================================================== ha tang
\* Consumer c nhan message m: chua xu ly, chua vao DLQ. Giao LAP sau khi da xu ly la
\* no-op nho processed_events — tru khi BUG_KHONG_IDEMPOTENT (chi ledger, event approved).
CoTheGiao(c, m) ==
    /\ m \in msgs /\ m \notin dlq
    /\ \/ <<c, m>> \notin daXuLy
       \/ BUG_KHONG_IDEMPOTENT /\ c = "ledger" /\ m.loai = "approved"
DaXuLy(c, m) == daXuLy' = daXuLy \cup {<<c, m>>}
VaoDLQ(m) == /\ dlq' = dlq \cup {m}
             /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, annVars, msgs, daXuLy>>
Phat(S) == msgs' = msgs \cup S

\* ===================================================================== doanh nghiep / project-svc
NapTien ==
    /\ ~daNap
    /\ \E x \in SoTienNap : ngoai' = ngoai - x /\ dn' = dn + x
    /\ daNap' = TRUE
    /\ UNCHANGED <<duAnVars, kyQuy, treo, khaDung, dangChuyen, daRa, kyQuyTT, soCanChi, soLanChi, rut,
                   taskVars, annVars, netVars>>

Publish ==
    /\ duAn = "Nhap" /\ soPublish < MaxPublish
    /\ duAn' = "ChoKyQuy" /\ soPublish' = soPublish + 1 /\ soSK' = soSK + 1
    /\ Phat({SK("publish_requested", soSK + 1)})
    /\ UNCHANGED <<soTamDung, dongDuyet, tienVars, ledVars, taskVars, annVars, daXuLy, dlq>>

ProjectNhanKyQuy(m) ==   \* escrow.reserved / escrow.rejected
    /\ m.loai \in {"escrow_reserved", "escrow_rejected"}
    /\ IF duAn = "ChoKyQuy"
          THEN IF m.loai = "escrow_reserved"
                  THEN /\ duAn' = "Chay" /\ soSK' = soSK + 1
                       /\ Phat({SK("published", soSK + 1)})
                  ELSE /\ duAn' = "Nhap" /\ UNCHANGED <<soSK, msgs>>
          ELSE UNCHANGED <<duAn, soSK, msgs>>
    /\ DaXuLy("project", m)
    /\ UNCHANGED <<soTamDung, soPublish, dongDuyet, tienVars, ledVars, taskVars, annVars, dlq>>

TamDung ==
    /\ duAn = "Chay" /\ soTamDung < MaxTamDung
    /\ duAn' = "TamDung" /\ soTamDung' = soTamDung + 1 /\ soSK' = soSK + 1
    /\ Phat({SK("paused", soSK + 1)})
    /\ UNCHANGED <<soPublish, dongDuyet, tienVars, ledVars, taskVars, annVars, daXuLy, dlq>>

ChayTiep ==
    /\ duAn = "TamDung"
    /\ duAn' = "Chay" /\ dongDuyet' = -1 /\ soSK' = soSK + 1
    /\ Phat({SK("resumed", soSK + 1)})
    /\ UNCHANGED <<soTamDung, soPublish, tienVars, ledVars, taskVars, annVars, daXuLy, dlq>>

\* Buoc 1-2 cua POST /complete: hoi task-svc (da tam dung? con lease?) roi goi annotation-svc
\* DONG SO trong MOT transaction (FOR UPDATE project_terms). Giua hai loi goi khong luot nao
\* nop them duoc: task-svc da tam dung (khong cap lease) va khong con lease.
DieuKienTask ==
    BUG_DONG_KHI_CON_LEASE \/ (taskTT = "TamDung" /\ taskN = soSK /\ \A a \in Ann : ~lease[a])
DieuKienDongSo ==
    /\ BUG_DONG_KHONG_DEM_NHAN \/ Cardinality(daNopTask) = Cardinality({a \in Ann : nhan[a] # "Khong"})
    /\ \A a \in Ann : nhan[a] \notin {"Cho", "KhieuNai"}
    /\ BUG_DONG_KHONG_HAN_KHIEU_NAI \/ \A a \in Ann : ~(nhan[a] = "TuChoi" /\ conHan[a])
GoiDongSo ==
    /\ duAn = "TamDung" /\ dongDuyet = -1
    /\ DieuKienTask /\ DieuKienDongSo
    /\ annDong' = TRUE /\ annDongTai' = soSK
    /\ dongDuyet' = SoDuyet
    /\ UNCHANGED <<duAn, soSK, soTamDung, soPublish, tienVars, ledVars, taskVars, nhan, conHan, annFinal, netVars>>

\* Buoc 3: luu Completed + project.completed (mang so nhan da duyet luc dong so).
ChotHoanThanh ==
    /\ duAn = "TamDung" /\ dongDuyet >= 0
    /\ duAn' = "Xong" /\ soSK' = soSK + 1
    /\ Phat({Msg("completed", soSK + 1, KHONG, KHONG, dongDuyet)})
    /\ UNCHANGED <<soTamDung, soPublish, dongDuyet, tienVars, ledVars, taskVars, annVars, daXuLy, dlq>>

\* ===================================================================== task-svc
TaskNhanSK(m) ==   \* published / paused / resumed / completed — chi ap dung event MOI hon
    /\ m.loai \in {"published", "paused", "resumed", "completed"}
    /\ IF m.n > taskN
          THEN /\ taskN' = m.n
               /\ taskTT' = CASE m.loai = "published" -> "Chay"
                              [] m.loai = "paused"    -> "TamDung"
                              [] m.loai = "resumed"   -> "Chay"
                              [] OTHER                -> "Dong"
          ELSE UNCHANGED <<taskN, taskTT>>
    /\ DaXuLy("task", m)
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, lease, daNopTask, annVars, msgs, dlq>>

Lease(a) ==
    /\ taskTT = "Chay" /\ ~lease[a] /\ a \notin daNopTask
    /\ Cardinality({b \in {a[1]} \X Labelers : lease[b] \/ b \in daNopTask}) < R
    /\ lease' = [lease EXCEPT ![a] = TRUE]
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskTT, taskN, daNopTask, annVars, netVars>>

HetLease(a) ==
    /\ lease[a]
    /\ lease' = [lease EXCEPT ![a] = FALSE]
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskTT, taskN, daNopTask, annVars, netVars>>

Nop(a) ==   \* nop duoc ca khi du an dang tam dung (luot dang giu khong bi thu hoi)
    /\ lease[a]
    /\ lease' = [lease EXCEPT ![a] = FALSE] /\ daNopTask' = daNopTask \cup {a}
    /\ Phat({Msg("submitted", 0, a, KHONG, 0)})
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskTT, taskN, annVars, daXuLy, dlq>>

\* ===================================================================== annotation-svc
AnnNhanNop(m) ==
    /\ m.loai = "submitted"
    /\ IF nhan[m.a] # "Khong" THEN /\ nhan' = nhan /\ DaXuLy("annotation", m) /\ UNCHANGED dlq   \* giao lai
       ELSE IF annFinal
          THEN /\ dlq' = dlq \cup {m} /\ UNCHANGED <<nhan, daXuLy>>   \* nop toi sau khi du an KET THUC → sai bat bien, DLQ
          \* Dong so nhung CHUA ket thuc (project chua chot / doanh nghiep chay tiep): van ghi nhan nhan.
          \* project.resumed (mo so) va assignment.submitted di hai queue khac nhau — nop co the toi
          \* truoc. Nhan moi o "Cho" tu chan lan dong tiep theo, khong lam doi tien.
          ELSE /\ nhan' = [nhan EXCEPT ![m.a] = "Cho"] /\ DaXuLy("annotation", m) /\ UNCHANGED dlq
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, conHan, annDong, annDongTai, annFinal, msgs>>

AnnNhanSK(m) ==   \* resumed: mo lai so (neu moi hon luc dong, chua ket thuc); completed: dong vinh vien
    /\ m.loai \in {"resumed", "completed"}
    /\ IF m.loai = "completed"
          THEN /\ annFinal' = TRUE /\ annDong' = TRUE
               /\ annDongTai' = IF annDong THEN annDongTai ELSE m.n
          ELSE /\ annDong' = (annDong /\ (annFinal \/ m.n <= annDongTai))
               /\ UNCHANGED <<annFinal, annDongTai>>
    /\ DaXuLy("annotation", m)
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, nhan, conHan, msgs, dlq>>

Duyet(a) ==
    /\ nhan[a] = "Cho" /\ ~annDong
    /\ nhan' = [nhan EXCEPT ![a] = "Duyet"]
    /\ Phat({Msg("approved", 0, a, KHONG, 0)})
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, conHan, annDong, annDongTai, annFinal, daXuLy, dlq>>

TuChoi(a) ==
    /\ nhan[a] = "Cho" /\ ~annDong
    /\ nhan' = [nhan EXCEPT ![a] = "TuChoi"] /\ conHan' = [conHan EXCEPT ![a] = TRUE]
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, annDong, annDongTai, annFinal, netVars>>

HetHanKhieuNai(a) ==   \* thoi gian troi qua annotation.appeal_window
    /\ nhan[a] = "TuChoi" /\ conHan[a]
    /\ conHan' = [conHan EXCEPT ![a] = FALSE]
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, nhan, annDong, annDongTai, annFinal, netVars>>

KhieuNai(a) ==
    /\ nhan[a] = "TuChoi" /\ conHan[a] /\ ~annDong
    /\ nhan' = [nhan EXCEPT ![a] = "KhieuNai"]
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, conHan, annDong, annDongTai, annFinal, netVars>>

PhanXu(a) ==
    /\ nhan[a] = "KhieuNai" /\ ~annDong
    /\ \/ /\ nhan' = [nhan EXCEPT ![a] = "Duyet"]          \* chap nhan → tra tien
          /\ Phat({Msg("approved", 0, a, KHONG, 0)})
       \/ /\ nhan' = [nhan EXCEPT ![a] = "TuChoiHan"]      \* bac → tu choi vinh vien
          /\ UNCHANGED msgs
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, conHan, annDong, annDongTai, annFinal, daXuLy, dlq>>

\* ===================================================================== ledger-svc
TraHet == /\ dn' = dn + kyQuy /\ kyQuy' = 0 /\ kyQuyTT' = "Dong"

LedgerKyQuy(m) ==
    /\ m.loai = "publish_requested"
    /\ IF kyQuyTT # "Chua" THEN UNCHANGED <<dn, kyQuy, kyQuyTT, msgs>>          \* da ky quy (giao lai)
       ELSE IF dn >= NganSach
          THEN /\ dn' = dn - NganSach /\ kyQuy' = kyQuy + NganSach /\ kyQuyTT' = "Giu"
               /\ Phat({SK("escrow_reserved", m.n)})
          ELSE /\ Phat({SK("escrow_rejected", m.n)}) /\ UNCHANGED <<dn, kyQuy, kyQuyTT>>
    /\ DaXuLy("ledger", m)
    /\ UNCHANGED <<duAnVars, ngoai, treo, khaDung, dangChuyen, daRa, daNap, soCanChi, soLanChi, rut,
                   taskVars, annVars, dlq>>

\* annotation.approved → ky quy chi Gia, treo cho labeler (ChiTraNhanAsync).
LedgerChiNhan(m) ==
    /\ m.loai = "approved"
    /\ LET a == m.a  t == m.a[1]  l == m.a[2] IN
       IF soLanChi[a] >= 1 /\ ~BUG_KHONG_IDEMPOTENT
          THEN /\ DaXuLy("ledger", m)                                           \* da chi (VD-M-02)
               /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, annVars, msgs, dlq>>
       ELSE IF kyQuyTT \in {"Chua", "Dong"} \/ ChiCuaTask(t) >= R \/ kyQuy < Gia
          THEN VaoDLQ(m)                                                        \* ky_quy_da_dong / vuot_redundancy
       ELSE /\ treo' = [treo EXCEPT ![l] = @ + Gia]
            /\ soLanChi' = [soLanChi EXCEPT ![a] = @ + 1]
            \* Ban sua: ky quy DANG DONG cho chi not nhan da duyet → chi du thi hoan phan con lai.
            /\ IF kyQuyTT = "DangDong" /\ DaChi + 1 >= soCanChi
                  THEN /\ dn' = dn + (kyQuy - Gia) /\ kyQuy' = 0 /\ kyQuyTT' = "Dong"
                  ELSE /\ kyQuy' = kyQuy - Gia /\ UNCHANGED <<dn, kyQuyTT>>
            /\ DaXuLy("ledger", m)
            /\ UNCHANGED <<duAnVars, ngoai, khaDung, dangChuyen, daRa, daNap, soCanChi, rut, taskVars, annVars, msgs, dlq>>

\* project.completed → hoan ky quy con lai (TraKyQuyAsync).
LedgerHoanKyQuy(m) ==
    /\ m.loai = "completed"
    /\ IF kyQuyTT # "Giu" THEN UNCHANGED <<dn, kyQuy, kyQuyTT, soCanChi>>        \* giao lai / da dong
       ELSE IF BUG_HOAN_KY_QUY_NGAY \/ DaChi >= m.duyet
          THEN TraHet /\ UNCHANGED soCanChi
          ELSE /\ kyQuyTT' = "DangDong" /\ soCanChi' = m.duyet                 \* con nhan da duyet chua chi
               /\ UNCHANGED <<dn, kyQuy>>
    /\ DaXuLy("ledger", m)
    /\ UNCHANGED <<duAnVars, ngoai, treo, khaDung, dangChuyen, daRa, daNap, soLanChi, rut, taskVars, annVars, msgs, dlq>>

GiaiPhongTreo(l) ==   \* HoldReleaseWorker: khoan treo den han
    /\ treo[l] >= Gia
    /\ treo' = [treo EXCEPT ![l] = @ - Gia] /\ khaDung' = [khaDung EXCEPT ![l] = @ + Gia]
    /\ UNCHANGED <<duAnVars, ngoai, dn, kyQuy, dangChuyen, daRa, ledVars, taskVars, annVars, netVars>>

RutTien(l) ==   \* tu duyet (duoi nguong) hoac cho admin duyet
    /\ rut[l] = "Khong" /\ khaDung[l] >= Gia
    /\ khaDung' = [khaDung EXCEPT ![l] = @ - Gia] /\ dangChuyen' = dangChuyen + Gia
    /\ \/ /\ rut' = [rut EXCEPT ![l] = "Gui"] /\ Phat({Msg("payout_requested", 0, KHONG, l, 0)})
       \/ /\ rut' = [rut EXCEPT ![l] = "ChoDuyet"] /\ UNCHANGED msgs
    /\ UNCHANGED <<duAnVars, ngoai, dn, kyQuy, treo, daRa, daNap, kyQuyTT, soCanChi, soLanChi, taskVars, annVars, daXuLy, dlq>>

AdminDuyetRut(l) ==
    /\ rut[l] = "ChoDuyet"
    /\ rut' = [rut EXCEPT ![l] = "Gui"] /\ Phat({Msg("payout_requested", 0, KHONG, l, 0)})
    /\ UNCHANGED <<duAnVars, tienVars, daNap, kyQuyTT, soCanChi, soLanChi, taskVars, annVars, daXuLy, dlq>>

AdminTuChoiRut(l) ==   \* but toan dao: tien ve vi
    /\ rut[l] = "ChoDuyet"
    /\ rut' = [rut EXCEPT ![l] = "TuChoi"]
    /\ dangChuyen' = dangChuyen - Gia /\ khaDung' = [khaDung EXCEPT ![l] = @ + Gia]
    /\ UNCHANGED <<duAnVars, ngoai, dn, kyQuy, treo, daRa, daNap, kyQuyTT, soCanChi, soLanChi, taskVars, annVars, netVars>>

PaymentChuyen(m) ==   \* payment-svc goi cong ngoai: thanh cong hoac that bai
    /\ m.loai = "payout_requested"
    /\ \E kq \in {"payout_completed", "payout_failed"} : Phat({Msg(kq, 0, KHONG, m.l, 0)})
    /\ DaXuLy("payment", m)
    /\ UNCHANGED <<duAnVars, tienVars, ledVars, taskVars, annVars, dlq>>

LedgerKetQuaRut(m) ==
    /\ m.loai \in {"payout_completed", "payout_failed"}
    /\ IF rut[m.l] # "Gui" THEN UNCHANGED <<rut, dangChuyen, daRa, khaDung>>     \* giao lai
       ELSE IF m.loai = "payout_completed"
          THEN /\ rut' = [rut EXCEPT ![m.l] = "Xong"]
               /\ dangChuyen' = dangChuyen - Gia /\ daRa' = daRa + Gia /\ UNCHANGED khaDung
          ELSE /\ rut' = [rut EXCEPT ![m.l] = "ThatBai"]
               /\ dangChuyen' = dangChuyen - Gia /\ khaDung' = [khaDung EXCEPT ![m.l] = @ + Gia]
               /\ UNCHANGED daRa
    /\ DaXuLy("ledger", m)
    /\ UNCHANGED <<duAnVars, ngoai, dn, kyQuy, treo, daNap, kyQuyTT, soCanChi, soLanChi, taskVars, annVars, msgs, dlq>>

\* ===================================================================== tong hop
Giao ==
    \E m \in msgs :
        \/ CoTheGiao("project", m) /\ ProjectNhanKyQuy(m)
        \/ CoTheGiao("task", m) /\ TaskNhanSK(m)
        \/ CoTheGiao("annotation", m) /\ (AnnNhanNop(m) \/ AnnNhanSK(m))
        \/ CoTheGiao("ledger", m) /\ (LedgerKyQuy(m) \/ LedgerChiNhan(m) \/ LedgerHoanKyQuy(m) \/ LedgerKetQuaRut(m))
        \/ CoTheGiao("payment", m) /\ PaymentChuyen(m)

Next ==
    \/ NapTien \/ Publish \/ TamDung \/ ChayTiep \/ GoiDongSo \/ ChotHoanThanh
    \/ \E a \in Ann : Lease(a) \/ HetLease(a) \/ Nop(a)
    \/ \E a \in Ann : Duyet(a) \/ TuChoi(a) \/ HetHanKhieuNai(a) \/ KhieuNai(a) \/ PhanXu(a)
    \/ \E l \in Labelers : GiaiPhongTreo(l) \/ RutTien(l) \/ AdminDuyetRut(l) \/ AdminTuChoiRut(l)
    \/ Giao

Spec == Init /\ [][Next]_vars

\* Hai labeler bat ky hoan doi duoc cho nhau → TLC gop trang thai doi xung.
DoiXungLabeler == Permutations(Labelers)

\* ===================================================================== BAT BIEN
\* So cai ghi kep: tong moi tai khoan luon bang 0 (tien khong tu sinh ra / mat di).
BaoToanTien ==
    ngoai + dn + kyQuy + Tong(treo, Labelers) + Tong(khaDung, Labelers) + dangChuyen + daRa = 0

KhongSoDuAm ==
    /\ dn >= 0 /\ kyQuy >= 0 /\ dangChuyen >= 0
    /\ \A l \in Labelers : treo[l] >= 0 /\ khaDung[l] >= 0

\* Moi nhan chi duoc tra MOT lan, du event approved bi giao lap (VD-M-02).
ChiMotLan == \A a \in Ann : soLanChi[a] <= 1

\* Khong task nao duoc chi qua tran redundancy (VD-M-03).
KhongVuotRedundancy == \A t \in Tasks : ChiCuaTask(t) <= R

\* Khong event nao lam doi tien roi vao DLQ (moi luong tien deu chay het).
KhongDLQ == dlq = {}

\* Ky quy da tra ve doanh nghiep thi moi nhan da duyet deu da duoc tra: labeler khong lam khong cong.
KhongNoLabeler == kyQuyTT = "Dong" => \A a \in Ann : nhan[a] = "Duyet" => soLanChi[a] = 1

\* Dong so roi thi khong nhan bi tu choi nao con dang trong han khieu nai (khong tuoc quyen khieu nai).
KhongTuocQuyenKhieuNai == annDong => \A a \in Ann : ~(nhan[a] = "TuChoi" /\ conHan[a])

\* Du an da KET THUC thi moi luot nop ben task-svc deu da thanh nhan ben annotation-svc
\* (khong luot nop nao bi bo roi). Trong luc dong so chua chot, nop dang tren duong la binh thuong.
KhongBoSotLuotNop == annFinal => \A a \in daNopTask : nhan[a] # "Khong"

\* Ky quy chi duoc dong (tra ve) sau khi du an da Xong.
DongKyQuySauKhiXong == kyQuyTT \in {"DangDong", "Dong"} => duAn = "Xong"

TypeOK ==
    /\ duAn \in {"Nhap", "ChoKyQuy", "Chay", "TamDung", "Xong"}
    /\ kyQuyTT \in {"Chua", "Giu", "DangDong", "Dong"}
    /\ taskTT \in {"Chua", "Chay", "TamDung", "Dong"}
    /\ nhan \in [Ann -> {"Khong", "Cho", "Duyet", "TuChoi", "KhieuNai", "TuChoiHan"}]
    /\ rut \in [Labelers -> {"Khong", "Gui", "ChoDuyet", "Xong", "TuChoi", "ThatBai"}]
=============================================================================
