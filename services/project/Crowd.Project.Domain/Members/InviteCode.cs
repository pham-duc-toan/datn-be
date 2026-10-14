using System;
using System.Security.Cryptography;
using System.Text;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Members
{
    /// <summary>
    /// MA MOI cua chu du an: ai co ma deu vao du an voi vai tro labeler (POST /projects/join-by-code)
    /// — khong can chu du an biet userId tung nguoi. Luon co han dung; co the gioi han so luot va
    /// thu hoi bat ky luc nao.
    ///
    /// Ma moi la LOI MOI cua chu du an nen bo qua che do hien thi (vao duoc du an rieng tu) va bai
    /// test dau vao, nhung KHONG bo qua dieu kien cap do / uy tin (task-svc van ap khi cap task).
    ///
    /// Ma 16 ky tu tu bang 32 ky tu de doc (khong co I, O, 0, 1) = 80 bit ngau nhien: khong do
    /// duoc trong gioi han tan suat cua gateway. Luu nguyen van de chu du an xem lai va chia se lai
    /// — ma chi mo cua mot du an, thu hoi duoc, khong phai mat khau tai khoan.
    /// </summary>
    public sealed class InviteCode
    {
        public const int DoDaiMa = 16;
        public const int DoDaiGhiChuToiDa = 200;
        public const int SoLuotToiDa = 100000;

        private const string BangKyTu = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private InviteCode()
        {
            Code = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public string Code { get; private set; }

        /// <summary>Ghi chu cua chu du an ("nhom sinh vien K66"...).</summary>
        public string? Note { get; private set; }

        /// <summary>null = khong gioi han luot (van co han dung).</summary>
        public int? MaxUses { get; private set; }

        public int UsedCount { get; private set; }

        public DateTimeOffset ExpiresAt { get; private set; }

        public Guid CreatedBy { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? RevokedAt { get; private set; }

        public static InviteCode Tao(
            Guid projectId,
            Guid createdBy,
            int? maxUses,
            TimeSpan hieuLuc,
            string? note,
            DateTimeOffset luc,
            QuyDinhThanhVien quyDinh)
        {
            if (quyDinh == null)
            {
                throw new ArgumentNullException(nameof(quyDinh));
            }

            if (projectId == Guid.Empty || createdBy == Guid.Empty)
            {
                throw new InvalidValueException("id_rong", "Thieu du an hoac nguoi tao ma.");
            }

            if (maxUses.HasValue && (maxUses.Value < 1 || maxUses.Value > SoLuotToiDa))
            {
                throw new InvalidValueException("so_luot_khong_hop_le", "So luot dung phai tu 1 den " + SoLuotToiDa + ".");
            }

            if (hieuLuc < TimeSpan.FromHours(1) || hieuLuc > quyDinh.HanMaMoiToiDa)
            {
                throw new InvalidValueException(
                    "han_ma_moi_khong_hop_le",
                    "Han dung cua ma moi phai tu 1 gio den " + quyDinh.HanMaMoiToiDa.TotalHours + " gio.");
            }

            string? ghiChu = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (ghiChu != null && ghiChu.Length > DoDaiGhiChuToiDa)
            {
                throw new InvalidValueException("ghi_chu_qua_dai", "Ghi chu toi da " + DoDaiGhiChuToiDa + " ky tu.");
            }

            InviteCode m = new InviteCode();
            m.Id = Guid.CreateVersion7();
            m.ProjectId = projectId;
            m.Code = SinhMa();
            m.Note = ghiChu;
            m.MaxUses = maxUses;
            m.UsedCount = 0;
            m.ExpiresAt = luc + hieuLuc;
            m.CreatedBy = createdBy;
            m.CreatedAt = luc;
            return m;
        }

        /// <summary>Ma loi neu ma khong dung duoc luc nay, null = dung duoc.</summary>
        public string? LyDoKhongDung(DateTimeOffset luc)
        {
            if (RevokedAt.HasValue)
            {
                return "ma_moi_da_thu_hoi";
            }

            if (luc >= ExpiresAt)
            {
                return "ma_moi_het_han";
            }

            if (MaxUses.HasValue && UsedCount >= MaxUses.Value)
            {
                return "ma_moi_het_luot";
            }

            return null;
        }

        /// <summary>active | revoked | expired | exhausted — cho man hinh quan ly ma.</summary>
        public string TrangThai(DateTimeOffset luc)
        {
            string? lyDo = LyDoKhongDung(luc);
            if (lyDo == null)
            {
                return "active";
            }

            if (lyDo == "ma_moi_da_thu_hoi")
            {
                return "revoked";
            }

            return lyDo == "ma_moi_het_han" ? "expired" : "exhausted";
        }

        /// <summary>Ghi nhan mot luot dung. Hai nguoi cung dung luot cuoi: xmin chon mot (xem cau hinh EF).</summary>
        public void Dung(DateTimeOffset luc)
        {
            string? lyDo = LyDoKhongDung(luc);
            if (lyDo != null)
            {
                throw new RuleViolationException(lyDo, GiaiThich(lyDo));
            }

            UsedCount = UsedCount + 1;
        }

        public void ThuHoi(DateTimeOffset luc)
        {
            if (RevokedAt.HasValue)
            {
                throw new RuleViolationException("ma_moi_da_thu_hoi", "Ma moi da bi thu hoi tu truoc.");
            }

            RevokedAt = luc;
        }

        /// <summary>Nguoi dung go tay: bo khoang trang / dau gach, khong phan biet hoa thuong.</summary>
        public static string ChuanHoa(string? ma)
        {
            if (ma == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(ma.Length);
            foreach (char c in ma)
            {
                if (c != '-' && !char.IsWhiteSpace(c))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }

            return sb.ToString();
        }

        public static string GiaiThich(string lyDo)
        {
            switch (lyDo)
            {
                case "ma_moi_da_thu_hoi":
                    return "Ma moi da bi chu du an thu hoi.";
                case "ma_moi_het_han":
                    return "Ma moi da het han.";
                case "ma_moi_het_luot":
                    return "Ma moi da dung het so luot.";
                default:
                    return "Ma moi khong dung duoc.";
            }
        }

        private static string SinhMa()
        {
            char[] kq = new char[DoDaiMa];
            for (int i = 0; i < DoDaiMa; i++)
            {
                kq[i] = BangKyTu[RandomNumberGenerator.GetInt32(BangKyTu.Length)];
            }

            return new string(kq);
        }
    }
}
