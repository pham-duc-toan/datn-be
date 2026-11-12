using System;
using System.Collections.Generic;

namespace Crowd.Project.Domain.Attempts
{
    /// <summary>Mot su kien trong vong doi CUA CA BAI: ai lam gi, luc nao.</summary>
    public sealed class SuKienBai
    {
        public const string DaNop = "submitted";
        public const string Dat = "accepted";
        public const string Truot = "excluded";
        public const string KhieuNai = "appeal_opened";
        public const string KhieuNaiChapNhan = "appeal_accepted";
        public const string KhieuNaiBacBo = "appeal_rejected";

        public SuKienBai(string action, Guid? actorId, string? note, DateTimeOffset at)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("Thieu hanh dong.", nameof(action));
            }

            Action = action;
            ActorId = actorId;
            Note = note;
            At = at;
        }

        /// <summary>submitted | accepted | excluded | appeal_opened | appeal_accepted | appeal_rejected</summary>
        public string Action { get; }

        /// <summary>null = he thong (cham tu dong).</summary>
        public Guid? ActorId { get; }

        public string? Note { get; }

        public DateTimeOffset At { get; }
    }

    /// <summary>
    /// Lich su bai lam DUNG LAI tu chinh cac cot cua attempts — khong co bang lich su rieng (bo attempt_history,
    /// kien truc 3.31). Moi su kien xay ra TOI DA MOT LAN tren mot bai (nop mot lan; cham mot lan — GhiKetQua giao
    /// lai khong doi; khieu nai mot lan; phan xu mot lan) va moc thoi gian / nguoi / ghi chu cua no da nam tren
    /// attempts, nen bang rieng chi la ban chep lai co the lech.
    /// </summary>
    public static class LichSuBai
    {
        public static IReadOnlyList<SuKienBai> Tu(Attempt a)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            List<SuKienBai> ds = new List<SuKienBai>();

            if (a.SubmittedAt.HasValue)
            {
                ds.Add(new SuKienBai(SuKienBai.DaNop, a.LabelerId, null, a.SubmittedAt.Value));
            }

            if (a.GradedAt.HasValue && a.Passed.HasValue)
            {
                // Phan xu khieu nai KHONG sua passed (chi doi appeal_state) → passed luon la ket qua cham goc.
                string ghiChu = (a.AgreementPercent.HasValue ? a.AgreementPercent.Value + "% giống đa số, " : string.Empty)
                                + a.GoldCorrect + "/" + a.GoldTotal + " câu vàng";
                ds.Add(new SuKienBai(a.Passed.Value ? SuKienBai.Dat : SuKienBai.Truot, null, ghiChu, a.GradedAt.Value));
            }

            if (a.AppealedAt.HasValue)
            {
                ds.Add(new SuKienBai(SuKienBai.KhieuNai, a.LabelerId, a.AppealMessage, a.AppealedAt.Value));
            }

            if (a.AppealResolvedAt.HasValue && (a.AppealState == AppealState.Accepted || a.AppealState == AppealState.Rejected))
            {
                ds.Add(new SuKienBai(
                    a.AppealState == AppealState.Accepted ? SuKienBai.KhieuNaiChapNhan : SuKienBai.KhieuNaiBacBo,
                    a.AppealResolvedBy,
                    a.AppealNote,
                    a.AppealResolvedAt.Value));
            }

            return ds;
        }
    }
}
