using System;
using System.Text;
using Crowd.Identity.Api.Exceptions;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Xac minh danh tinh ca nhan (eKYC rut gon) — nguoi dung khai ho ten, ngay sinh, so giay to va
    /// tai anh hai mat giay to; admin doi chieu roi duyet. Dung cho:
    ///   - muc 2.9 / VD-P-04: du an noi dung nhay cam chi cho nguoi DA XAC MINH du tuoi;
    ///   - FP-05: du an rieng tu doi "labeler da xac minh danh tinh";
    ///   - VD-T-04: mot giay to chi gan duoc voi MOT tai khoan da xac minh (chong da tai khoan).
    ///
    /// Giong ho so KYB:
    ///   Draft ──nop──► Submitted ──duyet──► Verified (khoa)
    ///     ▲                │
    ///     └──sua, nop lai──┴──tu choi──► Rejected
    ///
    /// KHONG luu so giay to goc: chi 4 so cuoi (de admin doi chieu voi anh) va ban bam co khoa (de
    /// phat hien trung). Anh giay to nam o bucket PRIVATE kyb-docs (S-07), admin xem qua link 5 phut.
    /// </summary>
    public sealed class XacMinhDanhTinh
    {
        public const int DoDaiTenToiDa = 100;
        public const int DoDaiLyDoToiDa = 1000;
        public const int TuoiToiDa = 120;

        private XacMinhDanhTinh()
        {
            FullName = string.Empty;
            IdNumberHash = string.Empty;
            IdNumberLast4 = string.Empty;
        }

        public Guid UserId { get; private set; }

        public string FullName { get; private set; }

        public DateOnly? DateOfBirth { get; private set; }

        /// <summary>HMAC-SHA256 cua so giay to da chuan hoa (KhoaDuLieu.BamCoKhoa); rong = chua khai.</summary>
        public string IdNumberHash { get; private set; }

        public string IdNumberLast4 { get; private set; }

        /// <summary>Khoa anh mat truoc / mat sau trong bucket kyb-docs; null = chua tai.</summary>
        public string? FrontKey { get; private set; }

        public string? BackKey { get; private set; }

        public KybStatus Status { get; private set; }

        public string? RejectReason { get; private set; }

        public DateTimeOffset? SubmittedAt { get; private set; }

        public DateTimeOffset? ReviewedAt { get; private set; }

        public Guid? ReviewedBy { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static XacMinhDanhTinh Tao(Guid userId, DateTimeOffset luc)
        {
            XacMinhDanhTinh x = new XacMinhDanhTinh();
            x.UserId = userId;
            x.Status = KybStatus.Draft;
            x.UpdatedAt = luc;
            return x;
        }

        /// <summary>Tai khoan seed: da xac minh san de chay kich ban demo.</summary>
        public static XacMinhDanhTinh TaoDaXacMinh(Guid userId, string hoTen, DateOnly ngaySinh, string bamSo, string bonSoCuoi, DateTimeOffset luc)
        {
            XacMinhDanhTinh x = Tao(userId, luc);
            x.FullName = hoTen;
            x.DateOfBirth = ngaySinh;
            x.IdNumberHash = bamSo;
            x.IdNumberLast4 = bonSoCuoi;
            x.Status = KybStatus.Verified;
            x.SubmittedAt = luc;
            x.ReviewedAt = luc;
            return x;
        }

        /// <summary>
        /// Chuan hoa so giay to: bo khoang trang / gach, chu hoa. Chap nhan CCCD (12 so), CMND (9 so)
        /// hoac ho chieu (8–9 ky tu chu + so). Sai → 400.
        /// </summary>
        public static string ChuanHoaSoGiayTo(string? soGiayTo)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (soGiayTo ?? string.Empty).Trim())
            {
                if (c == ' ' || c == '-' || c == '.')
                {
                    continue;
                }

                if (!char.IsAsciiLetterOrDigit(c))
                {
                    throw new InvalidValueException("so_giay_to_khong_hop_le", "Số giấy tờ chỉ gồm chữ cái và chữ số.");
                }

                sb.Append(char.ToUpperInvariant(c));
            }

            string so = sb.ToString();
            bool toanSo = true;
            foreach (char c in so)
            {
                if (!char.IsAsciiDigit(c))
                {
                    toanSo = false;
                }
            }

            bool cccdHoacCmnd = toanSo && (so.Length == 12 || so.Length == 9);
            bool hoChieu = !toanSo && so.Length >= 8 && so.Length <= 9;
            if (!cccdHoacCmnd && !hoChieu)
            {
                throw new InvalidValueException("so_giay_to_khong_hop_le", "CCCD 12 số, CMND 9 số, hoặc hộ chiếu 8–9 ký tự.");
            }

            return so;
        }

        public void CapNhat(string? hoTen, DateOnly? ngaySinh, string soDaChuanHoa, string bamSo, DateOnly homNay, DateTimeOffset luc)
        {
            KiemSuaDuoc();

            string ten = (hoTen ?? string.Empty).Trim();
            if (ten.Length == 0 || ten.Length > DoDaiTenToiDa)
            {
                throw new InvalidValueException("ho_ten_khong_hop_le", "Họ tên bắt buộc, tối đa " + DoDaiTenToiDa + " ký tự.");
            }

            if (!ngaySinh.HasValue || ngaySinh.Value > homNay || TinhTuoi(ngaySinh.Value, homNay) > TuoiToiDa)
            {
                throw new InvalidValueException("ngay_sinh_khong_hop_le", "Ngày sinh không hợp lệ.");
            }

            FullName = ten;
            DateOfBirth = ngaySinh;
            IdNumberHash = bamSo;
            IdNumberLast4 = soDaChuanHoa.Substring(soDaChuanHoa.Length - 4);
            UpdatedAt = luc;
        }

        public void GanAnh(bool matTruoc, string khoa, DateTimeOffset luc)
        {
            KiemSuaDuoc();
            if (matTruoc)
            {
                FrontKey = khoa;
            }
            else
            {
                BackKey = khoa;
            }

            UpdatedAt = luc;
        }

        public void Nop(DateTimeOffset luc)
        {
            KiemSuaDuoc();
            if (FullName.Length == 0 || !DateOfBirth.HasValue || IdNumberHash.Length == 0)
            {
                throw new RuleViolationException("ho_so_chua_du", "Điền họ tên, ngày sinh, số giấy tờ trước khi nộp.");
            }

            if (FrontKey == null || BackKey == null)
            {
                throw new RuleViolationException("thieu_anh_giay_to", "Tải ảnh mặt trước và mặt sau của giấy tờ trước khi nộp.");
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

        /// <summary>Da xac minh VA du tuoi tinh toi homNay (sinh nhat la ngay du tuoi).</summary>
        public bool DuTuoi(int tuoiToiThieu, DateOnly homNay)
        {
            return Status == KybStatus.Verified && DateOfBirth.HasValue && TinhTuoi(DateOfBirth.Value, homNay) >= tuoiToiThieu;
        }

        public static int TinhTuoi(DateOnly ngaySinh, DateOnly homNay)
        {
            int tuoi = homNay.Year - ngaySinh.Year;
            if (homNay < ngaySinh.AddYears(tuoi))
            {
                tuoi--;
            }

            return tuoi;
        }

        private void KiemSuaDuoc()
        {
            if (Status == KybStatus.Submitted)
            {
                throw new RuleViolationException("ho_so_dang_cho_duyet", "Hồ sơ đang chờ admin duyệt — chưa sửa được.");
            }

            if (Status == KybStatus.Verified)
            {
                throw new RuleViolationException("da_xac_minh", "Danh tính đã được xác minh — hồ sơ không sửa được nữa.");
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
}
