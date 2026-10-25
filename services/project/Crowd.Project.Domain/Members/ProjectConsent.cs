using System;
using System.Security.Cryptography;
using System.Text;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Domain.Members
{
    /// <summary>
    /// Labeler DONG Y dieu khoan rieng cua mot du an: canh bao noi dung nhay cam (muc 2.9) va / hoac
    /// ky NDA dien tu (FP-01). Moi (du an, nguoi) mot dong, ghi de khi ky lai phien ban moi.
    ///
    /// Chu ky NDA dien tu gom: ho ten nguoi ky TU GO, thoi diem, IP, trinh duyet, va BAN BAM SHA-256 cua
    /// dung van ban da hien — sau nay chu du an sua van ban thi van chung minh duoc nguoi do da ky
    /// noi dung nao (doi chieu bam voi ban luu).
    /// </summary>
    public sealed class ProjectConsent
    {
        public const int DoDaiTenToiDa = 100;

        private ProjectConsent()
        {
        }

        public Guid ProjectId { get; private set; }

        public Guid UserId { get; private set; }

        /// <summary>Luc dong y canh bao noi dung; null = chua (hoac du an khong nhay cam).</summary>
        public DateTimeOffset? ContentWarningAcceptedAt { get; private set; }

        /// <summary>Phien ban NDA da ky; null = chua ky.</summary>
        public int? NdaVersion { get; private set; }

        /// <summary>SHA-256 (hex) cua van ban NDA luc ky.</summary>
        public string? NdaTextHash { get; private set; }

        public string? NdaSignedName { get; private set; }

        public DateTimeOffset? NdaSignedAt { get; private set; }

        public string? Ip { get; private set; }

        public string? UserAgent { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static ProjectConsent Tao(Guid projectId, Guid userId, DateTimeOffset luc)
        {
            ProjectConsent c = new ProjectConsent();
            c.ProjectId = projectId;
            c.UserId = userId;
            c.UpdatedAt = luc;
            return c;
        }

        /// <summary>
        /// Ghi dong y. Du an nhay cam bat buoc dongYCanhBao; du an co NDA bat buoc dongYNda + ho ten.
        /// </summary>
        public void DongY(LabelingProject duAn, bool dongYCanhBao, bool dongYNda, string? hoTen, string? ip, string? trinhDuyet, DateTimeOffset luc)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (!duAn.CanDongYCaNhan())
            {
                throw new RuleViolationException("khong_can_dong_y", "Dự án không có cảnh báo nội dung hay NDA.");
            }

            if (duAn.SensitiveContent && !dongYCanhBao)
            {
                throw new InvalidValueException("chua_dong_y_canh_bao", "Phải đồng ý cảnh báo nội dung nhạy cảm.");
            }

            if (duAn.NdaRequired)
            {
                string ten = (hoTen ?? string.Empty).Trim();
                if (!dongYNda)
                {
                    throw new InvalidValueException("chua_dong_y_nda", "Phải đồng ý thoả thuận bảo mật (NDA).");
                }

                if (ten.Length < 2 || ten.Length > DoDaiTenToiDa)
                {
                    throw new InvalidValueException("thieu_ho_ten_ky", "Gõ họ tên đầy đủ để ký NDA (2–" + DoDaiTenToiDa + " ký tự).");
                }

                NdaVersion = duAn.NdaVersion;
                NdaTextHash = BamVanBan(duAn.NdaText ?? string.Empty);
                NdaSignedName = ten;
                NdaSignedAt = luc;
            }

            if (duAn.SensitiveContent)
            {
                ContentWarningAcceptedAt = luc;
            }

            Ip = CatNgan(ip, 64);
            UserAgent = CatNgan(trinhDuyet, 300);
            UpdatedAt = luc;
        }

        /// <summary>Dong y nay con hieu luc voi cau hinh HIEN TAI cua du an (dung phien ban NDA, da nhan canh bao).</summary>
        public bool ConHieuLuc(LabelingProject duAn)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (duAn.SensitiveContent && !ContentWarningAcceptedAt.HasValue)
            {
                return false;
            }

            if (duAn.NdaRequired && (NdaVersion != duAn.NdaVersion || NdaTextHash != BamVanBan(duAn.NdaText ?? string.Empty)))
            {
                return false;
            }

            return true;
        }

        public static string BamVanBan(string vanBan)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(vanBan)));
        }

        private static string? CatNgan(string? s, int toiDa)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }

            string t = s.Trim();
            return t.Length > toiDa ? t.Substring(0, toiDa) : t;
        }
    }
}
