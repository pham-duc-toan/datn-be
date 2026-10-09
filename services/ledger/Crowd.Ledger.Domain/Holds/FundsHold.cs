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
    }

    /// <summary>Khoan treo sinh ra tu dau.</summary>
    public enum HoldKind
    {
        /// <summary>Nhan duoc duyet (labeler). AnnotationId = nhan.</summary>
        Annotation,

        /// <summary>Luot vuot link hop le (sharer). AnnotationId = ClickId, LinkId = link.</summary>
        GateClick,

        /// <summary>Hoa hong gioi thieu (FS-08). AnnotationId = ClickId goc, LinkId = link goc.</summary>
        Referral,
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
        /// UNIQUE (cung Kind): moi su viec sinh DUNG MOT khoan treo (VD-M-02). Voi Annotation la
        /// id nhan; voi GateClick / Referral la ClickId cua luot vuot link.
        /// </summary>
        public Guid AnnotationId { get; private set; }

        public HoldKind Kind { get; private set; }

        /// <summary>Link sinh ra khoan treo (GateClick, Referral) — de giu lai khi link vi pham.</summary>
        public Guid? LinkId { get; private set; }

        public Guid LabelerId { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>De dem so luot da chi cho moi task — chan chi vuot redundancy (VD-M-03).</summary>
        public Guid TaskId { get; private set; }

        public long AmountVnd { get; private set; }

        public DateTimeOffset HeldAt { get; private set; }

        public DateTimeOffset ReleaseAt { get; private set; }

        public HoldState State { get; private set; }

        public DateTimeOffset? ReleasedAt { get; private set; }

        public static FundsHold Tao(Guid annotationId, Guid projectId, Guid taskId, Guid labelerId, long amount, DateTimeOffset luc, TimeSpan thoiGianTreo)
        {
            FundsHold h = new FundsHold();
            h.Id = Guid.CreateVersion7();
            h.AnnotationId = annotationId;
            h.LabelerId = labelerId;
            h.ProjectId = projectId;
            h.TaskId = taskId;
            h.AmountVnd = amount;
            h.HeldAt = luc;
            h.ReleaseAt = luc + thoiGianTreo;
            h.State = HoldState.Held;
            h.Kind = HoldKind.Annotation;
            return h;
        }

        /// <summary>Doanh thu cong link cua sharer (hoac hoa hong gioi thieu) — treo nhu thu lao labeler.</summary>
        public static FundsHold TaoChoCongLink(
            HoldKind kind, Guid clickId, Guid projectId, Guid linkId, Guid userId, long amount, DateTimeOffset luc, TimeSpan thoiGianTreo)
        {
            if (kind == HoldKind.Annotation)
            {
                throw new ArgumentOutOfRangeException(nameof(kind), "Dung FundsHold.Tao cho nhan.");
            }

            FundsHold h = Tao(clickId, projectId, Guid.Empty, userId, amount, luc, thoiGianTreo);
            h.Kind = kind;
            h.LinkId = linkId;
            return h;
        }

        /// <summary>Link vi pham: giu lai khoan DANG TREO (da giai phong thi khong thu hoi duoc).</summary>
        public void GiuLai(DateTimeOffset luc)
        {
            if (State != HoldState.Held)
            {
                throw new RuleViolationException("khong_con_treo", "Khoan treo da giai phong hoac da giu lai.");
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
                throw new RuleViolationException("chua_den_han", "Khoan treo chua den han hoac da giai phong.");
            }

            State = HoldState.Released;
            ReleasedAt = luc;
        }
    }
}
