using System;

namespace Crowd.Project.Domain.Attempts
{
    /// <summary>
    /// Mot dong nhat ky vong doi CUA CA BAI (docs muc 5: state_histories): ai lam gi, luc nao.
    ///
    /// Thay cho lich su theo TUNG NHAN truoc day: moi su kien (nop, cham dat / truot, khieu nai, phan xu) deu
    /// tac dong len ca bai cung luc — bai 100 cau thi truoc day ghi 100 dong giong nhau, nay ghi MOT.
    /// Bang chi them (khong sua, khong xoa le).
    /// </summary>
    public sealed class AttemptHistoryEntry
    {
        public const int DoDaiGhiChuToiDa = 1000;

        public const string DaNop = "submitted";
        public const string Dat = "accepted";
        public const string Truot = "excluded";
        public const string KhieuNai = "appeal_opened";
        public const string KhieuNaiChapNhan = "appeal_accepted";
        public const string KhieuNaiBacBo = "appeal_rejected";

        private AttemptHistoryEntry()
        {
            Action = string.Empty;
        }

        public long Id { get; private set; }

        public Guid AttemptId { get; private set; }

        public Guid ProjectId { get; private set; }

        /// <summary>submitted | accepted | excluded | appeal_opened | appeal_accepted | appeal_rejected</summary>
        public string Action { get; private set; }

        /// <summary>null = he thong (cham tu dong).</summary>
        public Guid? ActorId { get; private set; }

        public string? Note { get; private set; }

        public DateTimeOffset At { get; private set; }

        public static AttemptHistoryEntry Tao(Guid attemptId, Guid projectId, string action, Guid? actorId, string? note, DateTimeOffset at)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("Thieu hanh dong.", nameof(action));
            }

            AttemptHistoryEntry h = new AttemptHistoryEntry();
            h.AttemptId = attemptId;
            h.ProjectId = projectId;
            h.Action = action;
            h.ActorId = actorId;
            h.Note = note != null && note.Length > DoDaiGhiChuToiDa ? note.Substring(0, DoDaiGhiChuToiDa) : note;
            h.At = at;
            return h;
        }
    }
}
