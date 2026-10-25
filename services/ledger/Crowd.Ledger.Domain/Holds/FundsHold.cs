using System;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Holds
{
    public enum HoldState
    {
        Held,
        Released,

        /// <summary>Giu lai vi vi pham (link bi vo hieu hoa) — tien sang platform:withheld, khong tra.</summary>
        Withheld,

        /// <summary>Du lieu cu: tranh chap nhan da duyet (da bo cung duyet tung nhan, docs 3.18).</summary>
        Disputed,

        /// <summary>Du lieu cu: tranh chap xu doanh nghiep thang.</summary>
        Refunded,
    }

    /// <summary>Khoan treo sinh ra tu dau.</summary>
    public enum HoldKind
    {
        /// <summary>Du lieu cu: nhan duoc duyet (truoc du an lam trung tam). AnnotationId = nhan.</summary>
        Annotation,

        /// <summary>Luot vuot link hop le (sharer). AnnotationId = ClickId, LinkId = link.</summary>
        GateClick,

        /// <summary>Hoa hong gioi thieu (FS-08). AnnotationId = ClickId goc, LinkId = link goc.</summary>
        Referral,

        /// <summary>Thuong cho MOT bai dat cua du an (docs 3.18). AnnotationId = id bai lam.</summary>
        ProjectReward,
    }

    /// <summary>
    /// Mot khoan thu lao dang TREO (dac ta 2.11: 3-7 ngay truoc khi rut duoc).
    /// Thoi gian treo cho phep xu tranh chap / gian lan truoc khi tien roi he thong.
    /// </summary>
    public sealed class FundsHold
    {
        private FundsHold()
        {
        }

        public Guid Id { get; private set; }

        /// <summary>
        /// UNIQUE (cung Kind): moi su viec sinh DUNG MOT khoan treo (VD-M-02). Voi ProjectReward la id bai
        /// lam; voi GateClick / Referral la ClickId cua luot vuot link; du lieu cu (Annotation) la id nhan.
        /// </summary>
        public Guid AnnotationId { get; private set; }

        public HoldKind Kind { get; private set; }

        /// <summary>Link sinh ra khoan treo (GateClick, Referral) — de giu lai khi link vi pham.</summary>
        public Guid? LinkId { get; private set; }

        public Guid LabelerId { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>Du lieu cu (chi theo task). Khoan moi: Guid.Empty.</summary>
        public Guid TaskId { get; private set; }

        public long AmountVnd { get; private set; }

        public DateTimeOffset HeldAt { get; private set; }

        public DateTimeOffset ReleaseAt { get; private set; }

        public HoldState State { get; private set; }

        public DateTimeOffset? ReleasedAt { get; private set; }

        /// <summary>Thuong mot bai dat — treo ledger.reward_hold (0 = noi goi giai phong ngay).</summary>
        public static FundsHold TaoChoBaiDat(Guid attemptId, Guid projectId, Guid labelerId, long amount, DateTimeOffset luc, TimeSpan thoiGianTreo)
        {
            FundsHold h = TaoCoBan(attemptId, projectId, labelerId, amount, luc, thoiGianTreo);
            h.Kind = HoldKind.ProjectReward;
            return h;
        }

        private static FundsHold TaoCoBan(Guid nguon, Guid projectId, Guid userId, long amount, DateTimeOffset luc, TimeSpan thoiGianTreo)
        {
            if (thoiGianTreo < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(thoiGianTreo), "Thoi gian treo khong duoc am.");
            }

            FundsHold h = new FundsHold();
            h.Id = Guid.CreateVersion7();
            h.AnnotationId = nguon;
            h.LabelerId = userId;
            h.ProjectId = projectId;
            h.TaskId = Guid.Empty;
            h.AmountVnd = amount;
            h.HeldAt = luc;
            h.ReleaseAt = luc + thoiGianTreo;
            h.State = HoldState.Held;
            return h;
        }

        /// <summary>Doanh thu cong link cua sharer (hoac hoa hong gioi thieu) — treo nhu thu lao labeler.</summary>
        public static FundsHold TaoChoCongLink(
            HoldKind kind, Guid clickId, Guid projectId, Guid linkId, Guid userId, long amount, DateTimeOffset luc, TimeSpan thoiGianTreo)
        {
            if (kind != HoldKind.GateClick && kind != HoldKind.Referral)
            {
                throw new ArgumentOutOfRangeException(nameof(kind), "Cong link chi co GateClick / Referral.");
            }

            FundsHold h = TaoCoBan(clickId, projectId, userId, amount, luc, thoiGianTreo);
            h.Kind = kind;
            h.LinkId = linkId;
            return h;
        }

        /// <summary>Link vi pham: giu lai khoan DANG TREO (da giai phong thi khong thu hoi duoc).</summary>
        public void GiuLai(DateTimeOffset luc)
        {
            if (State != HoldState.Held)
            {
                throw new RuleViolationException("khong_con_treo", "Khoản treo đã giải phóng hoặc đã giữ lại.");
            }

            State = HoldState.Withheld;
            ReleasedAt = luc;
        }

        public bool DenHan(DateTimeOffset luc)
        {
            return State == HoldState.Held && luc >= ReleaseAt;
        }

        public void GiaiPhong(DateTimeOffset luc)
        {
            if (!DenHan(luc))
            {
                throw new RuleViolationException("chua_den_han", "Khoản treo chưa đến hạn hoặc đã giải phóng.");
            }

            State = HoldState.Released;
            ReleasedAt = luc;
        }
    }
}
