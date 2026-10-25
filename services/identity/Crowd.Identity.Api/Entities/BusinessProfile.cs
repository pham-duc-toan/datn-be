using System;
using System.Text.RegularExpressions;
using Crowd.Identity.Api.Exceptions;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Ho so doanh nghiep de xac minh (KYB — FB-01 / FB-02 / FM-01). Moi tai khoan business
    /// mot ho so. Giay phep kinh doanh nam trong bucket PRIVATE kyb-docs (S-07): o day chi
    /// luu khoa file, link xem cap rieng cho admin, song ngan.
    ///
    ///   Draft ──nop──► Submitted ──duyet──► Verified (khoa, khong sua nua)
    ///     ▲                │
    ///     └──sua, nop lai──┴──tu choi──► Rejected
    /// </summary>
    public sealed class BusinessProfile
    {
        public const int DoDaiTenToiDa = 200;
        public const int DoDaiDiaChiToiDa = 300;
        public const int DoDaiLyDoToiDa = 1000;

        // Ma so thue Viet Nam: 10 chu so, hoac 10 chu so + "-" + 3 chu so (chi nhanh).
        private static readonly Regex MaSoThue = new Regex(@"^\d{10}(-\d{3})?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private BusinessProfile()
        {
            CompanyName = string.Empty;
            TaxCode = string.Empty;
            Address = string.Empty;
        }

        public Guid UserId { get; private set; }

        public string CompanyName { get; private set; }

        public string TaxCode { get; private set; }

        public string Address { get; private set; }

        /// <summary>Khoa file giay phep trong bucket kyb-docs; null = chua tai len.</summary>
        public string? LicenseKey { get; private set; }

        public string? LicenseFileName { get; private set; }

        public KybStatus Status { get; private set; }

        public string? RejectReason { get; private set; }

        public DateTimeOffset? SubmittedAt { get; private set; }

        public DateTimeOffset? ReviewedAt { get; private set; }

        public Guid? ReviewedBy { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static BusinessProfile Tao(Guid userId, DateTimeOffset luc)
        {
            BusinessProfile p = new BusinessProfile();
            p.UserId = userId;
            p.Status = KybStatus.Draft;
            p.UpdatedAt = luc;
            return p;
        }

        /// <summary>Doanh nghiep seed: da xac minh san de chay kich ban demo.</summary>
        public static BusinessProfile TaoDaXacMinh(Guid userId, string ten, string maSoThue, string diaChi, DateTimeOffset luc)
        {
            BusinessProfile p = Tao(userId, luc);
            p.CompanyName = ten;
            p.TaxCode = maSoThue;
            p.Address = diaChi;
            p.Status = KybStatus.Verified;
            p.SubmittedAt = luc;
            p.ReviewedAt = luc;
            return p;
        }

        public void CapNhat(string? ten, string? maSoThue, string? diaChi, DateTimeOffset luc)
        {
            KiemSuaDuoc();

            string tenSach = (ten ?? string.Empty).Trim();
            string mstSach = (maSoThue ?? string.Empty).Trim();
            string dcSach = (diaChi ?? string.Empty).Trim();

            if (tenSach.Length == 0 || tenSach.Length > DoDaiTenToiDa)
            {
                throw new InvalidValueException("ten_doanh_nghiep_khong_hop_le", "Tên doanh nghiệp bắt buộc, tối đa " + DoDaiTenToiDa + " ký tự.");
            }

            if (!MaSoThue.IsMatch(mstSach))
            {
                throw new InvalidValueException("ma_so_thue_khong_hop_le", "Mã số thuế gồm 10 chữ số (chi nhánh: 10 chữ số-3 chữ số).");
            }

            if (dcSach.Length == 0 || dcSach.Length > DoDaiDiaChiToiDa)
            {
                throw new InvalidValueException("dia_chi_khong_hop_le", "Địa chỉ bắt buộc, tối đa " + DoDaiDiaChiToiDa + " ký tự.");
            }

            CompanyName = tenSach;
            TaxCode = mstSach;
            Address = dcSach;
            UpdatedAt = luc;
        }

        public void GanGiayPhep(string khoa, string tenFile, DateTimeOffset luc)
        {
            KiemSuaDuoc();
            LicenseKey = khoa;
            LicenseFileName = tenFile;
            UpdatedAt = luc;
        }

        public void Nop(DateTimeOffset luc)
        {
            KiemSuaDuoc();

            if (CompanyName.Length == 0 || TaxCode.Length == 0 || Address.Length == 0)
            {
                throw new RuleViolationException("ho_so_chua_du", "Điền tên, mã số thuế, địa chỉ trước khi nộp.");
            }

            if (LicenseKey == null)
            {
                throw new RuleViolationException("thieu_giay_phep", "Tải giấy phép kinh doanh lên trước khi nộp.");
            }

            Status = KybStatus.Submitted;
            SubmittedAt = luc;
            RejectReason = null;
            UpdatedAt = luc;
        }

        public void Duyet(Guid adminId, DateTimeOffset luc)
        {
            KiemDangChoDuyet();
            Status = KybStatus.Verified;
            ReviewedAt = luc;
            ReviewedBy = adminId;
            UpdatedAt = luc;
        }

        public void TuChoi(Guid adminId, string? lyDo, DateTimeOffset luc)
        {
            KiemDangChoDuyet();

            string ly = (lyDo ?? string.Empty).Trim();
            if (ly.Length == 0 || ly.Length > DoDaiLyDoToiDa)
            {
                throw new InvalidValueException("thieu_ly_do", "Từ chối phải ghi lý do (tối đa " + DoDaiLyDoToiDa + " ký tự).");
            }

            Status = KybStatus.Rejected;
            RejectReason = ly;
            ReviewedAt = luc;
            ReviewedBy = adminId;
            UpdatedAt = luc;
        }

        private void KiemSuaDuoc()
        {
            if (Status == KybStatus.Submitted)
            {
                throw new RuleViolationException("ho_so_dang_cho_duyet", "Hồ sơ đang chờ admin duyệt — chưa sửa được.");
            }

            if (Status == KybStatus.Verified)
            {
                throw new RuleViolationException("da_xac_minh", "Doanh nghiệp đã được xác minh — hồ sơ không sửa được nữa.");
            }
        }

        private void KiemDangChoDuyet()
        {
            if (Status != KybStatus.Submitted)
            {
                throw new RuleViolationException("khong_cho_duyet", "Hồ sơ không ở trạng thái chờ duyệt (đang " + Status + ").");
            }
        }
    }

    public enum KybStatus
    {
        Draft,
        Submitted,
        Verified,
        Rejected,
    }
}
