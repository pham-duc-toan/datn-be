using System;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Members
{
    public enum JoinRequestStatus
    {
        Pending,
        Approved,
        Rejected,

        /// <summary>Labeler tu rut don.</summary>
        Cancelled,

        /// <summary>Chu du an bo quen qua project.join_request_ttl — tu dong.</summary>
        Expired,
    }

    /// <summary>
    /// DON XIN THAM GIA du an che do Approval. Labeler gui (kem loi nhan), hoac tu sinh khi lam
    /// dat bai test dau vao (kem diem); chu du an duyet → thanh thanh vien, hoac tu choi (kem ly
    /// do). Moi nguoi toi da MOT don dang cho moi du an (chi muc duy nhat co dieu kien trong DB).
    /// </summary>
    public sealed class JoinRequest
    {
        public const int DoDaiLoiNhanToiDa = 500;

        private JoinRequest()
        {
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid UserId { get; private set; }

        public string? Message { get; private set; }

        public JoinRequestStatus Status { get; private set; }

        /// <summary>Don sinh tu bai test dau vao: lan lam bai va diem dat.</summary>
        public Guid? EntranceAttemptId { get; private set; }

        public int? ScorePercent { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? DecidedAt { get; private set; }

        public Guid? DecidedBy { get; private set; }

        public string? RejectReason { get; private set; }

        public static JoinRequest Tao(
            Guid projectId,
            Guid userId,
            string? message,
            Guid? entranceAttemptId,
            int? scorePercent,
            DateTimeOffset luc)
        {
            if (projectId == Guid.Empty || userId == Guid.Empty)
            {
                throw new InvalidValueException("id_rong", "Thiếu dự án hoặc người dùng.");
            }

            JoinRequest d = new JoinRequest();
            d.Id = Guid.CreateVersion7();
            d.ProjectId = projectId;
            d.UserId = userId;
            d.Message = ChuanHoaChu(message, "loi_nhan_qua_dai", "Loi nhan");
            d.Status = JoinRequestStatus.Pending;
            d.EntranceAttemptId = entranceAttemptId;
            d.ScorePercent = scorePercent;
            d.CreatedAt = luc;
            return d;
        }

        public void Duyet(Guid nguoiDuyet, DateTimeOffset luc)
        {
            ChiKhiDangCho();
            Status = JoinRequestStatus.Approved;
            DecidedBy = nguoiDuyet;
            DecidedAt = luc;
        }

        public void TuChoi(Guid nguoiDuyet, string? lyDo, DateTimeOffset luc)
        {
            ChiKhiDangCho();
            Status = JoinRequestStatus.Rejected;
            DecidedBy = nguoiDuyet;
            DecidedAt = luc;
            RejectReason = ChuanHoaChu(lyDo, "ly_do_qua_dai", "Ly do");
        }

        public void Huy(DateTimeOffset luc)
        {
            ChiKhiDangCho();
            Status = JoinRequestStatus.Cancelled;
            DecidedBy = UserId;
            DecidedAt = luc;
        }

        /// <summary>Qua han khong ai duyet (worker). Labeler gui lai duoc ngay.</summary>
        public void HetHan(DateTimeOffset luc)
        {
            ChiKhiDangCho();
            Status = JoinRequestStatus.Expired;
            DecidedAt = luc;
        }

        /// <summary>Bi tu choi thi phai cho het khoang nay moi gui don moi (chong spam chu du an).</summary>
        public DateTimeOffset? DuocXinLaiTu(TimeSpan cho)
        {
            if (Status != JoinRequestStatus.Rejected || !DecidedAt.HasValue)
            {
                return null;
            }

            return DecidedAt.Value + cho;
        }

        private void ChiKhiDangCho()
        {
            if (Status != JoinRequestStatus.Pending)
            {
                throw new RuleViolationException("don_da_xu_ly", "Đơn xin tham gia đã được xử lý (" + Status + ").");
            }
        }

        private static string? ChuanHoaChu(string? chu, string maLoi, string ten)
        {
            if (string.IsNullOrWhiteSpace(chu))
            {
                return null;
            }

            string s = chu.Trim();
            if (s.Length > DoDaiLoiNhanToiDa)
            {
                throw new InvalidValueException(maLoi, ten + " tối đa " + DoDaiLoiNhanToiDa + " ký tự.");
            }

            return s;
        }
    }
}
