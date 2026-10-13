using System;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Disputes
{
    public enum DisputeState
    {
        Open,

        /// <summary>Doanh nghiep thang: tien tra lai, nhan bi loai.</summary>
        RefundedBusiness,

        /// <summary>Labeler thang: khoan treo chay tiep.</summary>
        PaidLabeler,
    }

    /// <summary>
    /// Tranh chap (FM-06): doanh nghiep phan doi mot nhan DA DUYET trong luc tien con treo (phat hien
    /// sai / gian lan sau khi duyet). Khoan treo bi dong bang toi khi admin phan xu. Moi nhan toi da
    /// mot tranh chap (UNIQUE annotation_id).
    /// </summary>
    public sealed class Dispute
    {
        public const int DoDaiLyDoToiDa = 1000;

        private Dispute()
        {
            Reason = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid HoldId { get; private set; }

        public Guid AnnotationId { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid LabelerId { get; private set; }

        public Guid BusinessId { get; private set; }

        public long AmountVnd { get; private set; }

        public string Reason { get; private set; }

        public DisputeState State { get; private set; }

        public DateTimeOffset OpenedAt { get; private set; }

        public DateTimeOffset? ResolvedAt { get; private set; }

        public Guid? ResolvedBy { get; private set; }

        public string? ResolutionNote { get; private set; }

        public static Dispute Mo(
            Guid holdId, Guid annotationId, Guid projectId, Guid labelerId, Guid businessId, long amount, string lyDo, DateTimeOffset luc)
        {
            string ly = (lyDo ?? string.Empty).Trim();
            if (ly.Length == 0 || ly.Length > DoDaiLyDoToiDa)
            {
                throw new InvalidValueException("thieu_ly_do", "Tranh chap phai ghi ly do (toi da " + DoDaiLyDoToiDa + " ky tu).");
            }

            Dispute d = new Dispute();
            d.Id = Guid.CreateVersion7();
            d.HoldId = holdId;
            d.AnnotationId = annotationId;
            d.ProjectId = projectId;
            d.LabelerId = labelerId;
            d.BusinessId = businessId;
            d.AmountVnd = amount;
            d.Reason = ly;
            d.State = DisputeState.Open;
            d.OpenedAt = luc;
            return d;
        }

        public void PhanXu(bool traDoanhNghiep, string ghiChu, Guid adminId, DateTimeOffset luc)
        {
            if (State != DisputeState.Open)
            {
                throw new RuleViolationException("tranh_chap_da_xu", "Tranh chap da duoc phan xu.");
            }

            string note = (ghiChu ?? string.Empty).Trim();
            if (note.Length == 0 || note.Length > DoDaiLyDoToiDa)
            {
                throw new InvalidValueException("thieu_ghi_chu", "Phan xu phai ghi ly do (toi da " + DoDaiLyDoToiDa + " ky tu).");
            }

            State = traDoanhNghiep ? DisputeState.RefundedBusiness : DisputeState.PaidLabeler;
            ResolutionNote = note;
            ResolvedBy = adminId;
            ResolvedAt = luc;
        }
    }
}
