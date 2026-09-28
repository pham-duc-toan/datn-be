using System;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Domain.Projects;

namespace Crowd.Tasking.Domain.Eligibility
{
    /// <summary>
    /// "Labeler nay co duoc nhan task cua du an nay khong" — MOT ham thuan, test
    /// duoc khong can database.
    ///
    /// FAIL-SAFE VE PHIA TU CHOI (VD-D-04): ban sao co the TRE hoac THIEU. Thieu
    /// du lieu thi TU CHOI, khong bao gio "khong biet thi cho qua":
    ///   - chua co ban sao du an      → tu choi
    ///   - khong co dong thanh vien   → tu choi
    ///   - du an doi cap do ma chua biet cap do cua labeler → tu choi
    /// Tu choi nham chi lam labeler cho vai giay toi khi event toi; cho qua nham
    /// la tra tien cho nguoi khong duoc phep lam.
    /// </summary>
    public static class EligibilityPolicy
    {
        /// <summary>Tra ve MA ly do tu choi, hoac null neu duoc phep.</summary>
        public static string? LyDoTuChoi(
            ProjectSnapshot? duAn,
            MemberCache? thanhVien,
            LabelerProfile? labeler,
            DateTimeOffset luc)
        {
            if (duAn == null || !duAn.IsConfigured)
            {
                return "du_an_chua_san_sang";
            }

            if (duAn.Status != SnapshotStatus.Running)
            {
                return "du_an_khong_chay";
            }

            if (duAn.Deadline.HasValue && duAn.Deadline.Value <= luc)
            {
                return "da_qua_deadline";
            }

            if (!duAn.AllowProfessional)
            {
                return "khong_mo_kenh_chuyen_nghiep";
            }

            if (thanhVien == null || thanhVien.State == CachedMemberState.Removed)
            {
                return "khong_phai_thanh_vien";
            }

            if (thanhVien.State == CachedMemberState.Blocked)
            {
                return "bi_chan_khoi_du_an";
            }

            if (thanhVien.Role != CachedMemberRole.Labeler)
            {
                return "khong_phai_labeler";
            }

            if (labeler != null && labeler.Blocked)
            {
                return "tai_khoan_bi_khoa";
            }

            if (duAn.MinLevel.HasValue)
            {
                if (labeler == null || labeler.Level == null || labeler.Level.Value < duAn.MinLevel.Value)
                {
                    return "chua_du_cap_do";
                }
            }

            if (duAn.MinReputation.HasValue)
            {
                if (labeler == null || labeler.Reputation == null || labeler.Reputation.Value < duAn.MinReputation.Value)
                {
                    return "chua_du_uy_tin";
                }
            }

            return null;
        }
    }
}
