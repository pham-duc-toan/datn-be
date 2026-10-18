using System;
using System.Collections.Generic;
using Crowd.Tasking.Domain.Common;

namespace Crowd.Tasking.Domain.Attempts
{
    public enum AttemptState
    {
        /// <summary>Dang lam: giu mot suat, cau tra loi la ban nhap, quay lai / bo qua cau tuy y.</summary>
        InProgress,

        /// <summary>Da nop CA BAI — khong sua duoc nua; cho du an ket thuc de cham.</summary>
        Submitted,

        /// <summary>Het thoi gian giu bai (hoac du an dong thu bai) ma chua nop — mat suat.</summary>
        Expired,

        /// <summary>Labeler tu bo bai, bi chan khoi du an, hoac du an bi huy — mat suat.</summary>
        Cancelled,
    }

    public enum AppealState
    {
        None,

        /// <summary>Labeler truot da khieu nai, cho admin phan xu.</summary>
        Open,

        /// <summary>Admin doi thanh DAT — duoc tra thuong.</summary>
        Accepted,

        Rejected,
    }

    /// <summary>
    /// AGGREGATE: mot BAI LAM ca du an (docs 3.18). Labeler bat dau → giu mot suat → lam het cac cau
    /// (cau that + cau vang tron, thu tu xao rieng moi bai), duoc quay lai / bo qua, server tu luu
    /// nhap → nop CA BAI mot lan. Du an dong thu bai thi quality-svc cham; truot thi khieu nai duoc
    /// trong han.
    ///
    /// Moi thay doi xay ra khi DANG GIU KHOA DONG (SELECT ... FOR UPDATE) — service lo khoa, entity
    /// lo luat.
    /// </summary>
    public sealed class Attempt
    {
        public const int DoDaiKhieuNaiToiDa = 1000;

        private List<Guid> _questionOrder;

        private Attempt()
        {
            _questionOrder = new List<Guid>();
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid LabelerId { get; private set; }

        public AttemptState State { get; private set; }

        public DateTimeOffset StartedAt { get; private set; }

        /// <summary>Het han giu bai (khong qua han chot du an). Tam dung du an thi duoc cong bu.</summary>
        public DateTimeOffset ExpiresAt { get; private set; }

        public DateTimeOffset? SubmittedAt { get; private set; }

        /// <summary>Luc ket thuc KHONG nop (het han, bo, bi huy).</summary>
        public DateTimeOffset? EndedAt { get; private set; }

        /// <summary>Thu tu cau hoi cua bai nay (mau that + mau vang kiem tra, da xao).</summary>
        public IReadOnlyList<Guid> QuestionOrder
        {
            get { return _questionOrder; }
        }

        /// <summary>So cau vang trong bai / so cau vang tra loi dung (cham luc nop).</summary>
        public int GoldTotal { get; private set; }

        public int GoldCorrect { get; private set; }

        public string? ClientIp { get; private set; }

        public string? DeviceId { get; private set; }

        // ---- Ket qua cham (project.graded) ----

        public int? ComparableCount { get; private set; }

        public int? MatchedCount { get; private set; }

        public int? AgreementPercent { get; private set; }

        /// <summary>null = chua cham.</summary>
        public bool? Passed { get; private set; }

        public DateTimeOffset? GradedAt { get; private set; }

        // ---- Khieu nai ca bai ----

        public AppealState AppealState { get; private set; }

        public string? AppealMessage { get; private set; }

        public DateTimeOffset? AppealedAt { get; private set; }

        public string? AppealNote { get; private set; }

        public Guid? AppealResolvedBy { get; private set; }

        public DateTimeOffset? AppealResolvedAt { get; private set; }

        public bool DangLam
        {
            get { return State == AttemptState.InProgress; }
        }

        /// <summary>Ket qua cuoi (sau khieu nai): dat thi duoc tra thuong.</summary>
        public bool DatCuoiCung
        {
            get { return Passed == true || AppealState == AppealState.Accepted; }
        }

        /// <param name="cauHoi">Mau that + mau vang kiem tra, DA XAO theo thu tu rieng cua bai nay.</param>
        /// <param name="hetHan">Luc het han giu bai (service da cat theo han chot du an).</param>
        public static Attempt BatDau(Guid projectId, Guid labelerId, IReadOnlyList<Guid> cauHoi, DateTimeOffset luc, DateTimeOffset hetHan)
        {
            if (cauHoi == null)
            {
                throw new ArgumentNullException(nameof(cauHoi));
            }

            if (cauHoi.Count == 0)
            {
                throw new RuleViolationException("du_an_chua_co_cau_hoi", "Du an chua co cau hoi nao.");
            }

            if (hetHan <= luc)
            {
                throw new RuleViolationException("da_qua_deadline", "Du an da qua han chot.");
            }

            Attempt a = new Attempt();
            a.Id = Guid.CreateVersion7();
            a.ProjectId = projectId;
            a.LabelerId = labelerId;
            a.State = AttemptState.InProgress;
            a.StartedAt = luc;
            a.ExpiresAt = hetHan;
            a._questionOrder = new List<Guid>(cauHoi);
            a.AppealState = AppealState.None;
            return a;
        }

        public bool CoCauHoi(Guid sampleId)
        {
            return _questionOrder.Contains(sampleId);
        }

        /// <summary>Con lam (luu nhap / nop) duoc khong — dang lam va chua het han.</summary>
        public void KiemConLamDuoc(DateTimeOffset luc)
        {
            if (State == AttemptState.Submitted)
            {
                throw new RuleViolationException("bai_da_nop", "Bai da nop, khong sua duoc nua.");
            }

            if (State != AttemptState.InProgress)
            {
                throw new RuleViolationException("bai_da_ket_thuc", "Bai lam da ket thuc (" + State + ").");
            }

            if (luc >= ExpiresAt)
            {
                throw new RuleViolationException("bai_da_het_han", "Da het thoi gian giu bai.");
            }
        }

        /// <summary>Nop CA BAI. Cau vang da cham boi service (Crowd.Labeling, cung luat bai test dau vao).</summary>
        public void Nop(int soCauVang, int soCauVangDung, string? ip, string? thietBi, DateTimeOffset luc)
        {
            KiemConLamDuoc(luc);

            if (soCauVang < 0 || soCauVangDung < 0 || soCauVangDung > soCauVang)
            {
                throw new ArgumentOutOfRangeException(nameof(soCauVangDung));
            }

            State = AttemptState.Submitted;
            SubmittedAt = luc;
            GoldTotal = soCauVang;
            GoldCorrect = soCauVangDung;
            ClientIp = ip;
            DeviceId = thietBi;
        }

        /// <summary>Het han giu bai / du an dong thu bai khi chua nop. Bai da ket thuc thi khong lam gi.</summary>
        public bool HetHan(DateTimeOffset luc)
        {
            if (State != AttemptState.InProgress)
            {
                return false;
            }

            State = AttemptState.Expired;
            EndedAt = luc;
            return true;
        }

        /// <summary>Tu bo bai, bi chan khoi du an, du an bi huy. Bai da nop thi giu nguyen.</summary>
        public bool Huy(DateTimeOffset luc)
        {
            if (State != AttemptState.InProgress)
            {
                return false;
            }

            State = AttemptState.Cancelled;
            EndedAt = luc;
            return true;
        }

        /// <summary>Du an tiep tuc sau tam dung: cong bu thoi gian tam dung (khong qua han chot).</summary>
        public void CongBuThoiGian(TimeSpan them, DateTimeOffset hanChot)
        {
            if (State != AttemptState.InProgress || them <= TimeSpan.Zero)
            {
                return;
            }

            DateTimeOffset moi = ExpiresAt + them;
            ExpiresAt = moi < hanChot ? moi : hanChot;
        }

        /// <summary>project.graded: ghi ket qua. Giao lai cung ket qua → khong doi.</summary>
        public void GhiKetQua(int soSoDuoc, int soGiong, int phanTram, bool dat, DateTimeOffset luc)
        {
            if (State != AttemptState.Submitted)
            {
                throw new RuleViolationException("bai_chua_nop", "Chi cham bai da nop.");
            }

            if (GradedAt.HasValue)
            {
                return;
            }

            ComparableCount = soSoDuoc;
            MatchedCount = soGiong;
            AgreementPercent = phanTram;
            Passed = dat;
            GradedAt = luc;
        }

        /// <summary>Khieu nai ket qua CA BAI: chi bai TRUOT, mot lan, truoc han.</summary>
        public void KhieuNai(string? noiDung, DateTimeOffset hanKhieuNai, DateTimeOffset luc)
        {
            if (Passed == null)
            {
                throw new RuleViolationException("chua_cham", "Bai chua duoc cham.");
            }

            if (Passed == true)
            {
                throw new RuleViolationException("bai_da_dat", "Bai da dat, khong can khieu nai.");
            }

            if (AppealState != AppealState.None)
            {
                throw new RuleViolationException("da_khieu_nai", "Moi bai chi khieu nai mot lan.");
            }

            if (luc > hanKhieuNai)
            {
                throw new RuleViolationException("het_han_khieu_nai", "Da het han khieu nai.");
            }

            string nd = (noiDung ?? string.Empty).Trim();
            if (nd.Length == 0 || nd.Length > DoDaiKhieuNaiToiDa)
            {
                throw new InvalidValueException("noi_dung_khieu_nai_khong_hop_le", "Noi dung khieu nai tu 1 den " + DoDaiKhieuNaiToiDa + " ky tu.");
            }

            AppealState = AppealState.Open;
            AppealMessage = nd;
            AppealedAt = luc;
        }

        public void PhanXu(bool chapNhan, string? ghiChu, Guid nguoiXu, DateTimeOffset luc)
        {
            if (AppealState != AppealState.Open)
            {
                throw new RuleViolationException("khong_co_khieu_nai_mo", "Bai nay khong co khieu nai dang cho phan xu.");
            }

            string? gc = string.IsNullOrWhiteSpace(ghiChu) ? null : ghiChu.Trim();
            if (gc != null && gc.Length > DoDaiKhieuNaiToiDa)
            {
                throw new InvalidValueException("ghi_chu_qua_dai", "Ghi chu toi da " + DoDaiKhieuNaiToiDa + " ky tu.");
            }

            AppealState = chapNhan ? AppealState.Accepted : AppealState.Rejected;
            AppealNote = gc;
            AppealResolvedBy = nguoiXu;
            AppealResolvedAt = luc;
        }
    }
}
