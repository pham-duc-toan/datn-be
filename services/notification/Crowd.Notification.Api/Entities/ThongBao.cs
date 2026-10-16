using System;

namespace Crowd.Notification.Api.Entities
{
    /// <summary>
    /// Mot thong bao gui MOT nguoi dung (FC-06). Luon co trong ung dung; email gui kem tuy loai +
    /// tuy chon cua nguoi dung, do EmailWorker gui sau (khong chan consumer).
    /// </summary>
    public sealed class ThongBao
    {
        private ThongBao()
        {
            Type = string.Empty;
            Title = string.Empty;
            Body = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid UserId { get; private set; }

        /// <summary>Ma loai (LoaiThongBao), vd "rut_tien".</summary>
        public string Type { get; private set; }

        public string Title { get; private set; }

        public string Body { get; private set; }

        /// <summary>Duong dan goi y cho frontend (vd "/projects/{id}"), co the null.</summary>
        public string? Link { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? ReadAt { get; private set; }

        public EmailState EmailState { get; private set; }

        public int EmailAttempts { get; private set; }

        public DateTimeOffset? EmailNextAt { get; private set; }

        public DateTimeOffset? EmailSentAt { get; private set; }

        public string? EmailError { get; private set; }

        /// <summary>So su kien da cong don vao thong bao nay (loai gop, xem LoaiThongBao.Gop). Mac dinh 1.</summary>
        public int MergedCount { get; private set; }

        /// <summary>Tong tien cua cac su kien da gop; null voi loai khong gop.</summary>
        public long? AmountVnd { get; private set; }

        public static ThongBao Tao(Guid userId, string loai, NoiDung noiDung, bool guiEmail, DateTimeOffset luc)
        {
            if (noiDung == null)
            {
                throw new ArgumentNullException(nameof(noiDung));
            }

            ThongBao t = new ThongBao();
            t.Id = Guid.CreateVersion7();
            t.UserId = userId;
            t.Type = loai;
            t.Title = noiDung.TieuDe;
            t.Body = noiDung.NoiDungChinh;
            t.Link = noiDung.DuongDan;
            t.CreatedAt = luc;
            t.EmailState = guiEmail ? EmailState.Pending : EmailState.Skipped;
            t.EmailNextAt = guiEmail ? luc : null;
            t.MergedCount = 1;
            return t;
        }

        /// <summary>Thong bao loai gop: ghi them so tien cua su kien dau tien.</summary>
        public static ThongBao TaoGop(Guid userId, string loai, NoiDung noiDung, long soTien, bool guiEmail, DateTimeOffset luc)
        {
            ThongBao t = Tao(userId, loai, noiDung, guiEmail, luc);
            t.AmountVnd = soTien;
            return t;
        }

        /// <summary>
        /// Cong don mot su kien nua: tang so lan, cong tien, thay noi dung (dung lai tu tong moi) va DUA LEN
        /// DAU danh sach (CreatedAt = luc su kien moi nhat). Chi goi khi chua doc va email chua gui.
        /// </summary>
        public void GopThem(long soTien, NoiDung noiDungMoi, DateTimeOffset luc)
        {
            if (noiDungMoi == null)
            {
                throw new ArgumentNullException(nameof(noiDungMoi));
            }

            if (ReadAt.HasValue || EmailState == EmailState.Sent || EmailState == EmailState.Failed)
            {
                throw new InvalidOperationException("Chi gop vao thong bao chua doc va chua gui email.");
            }

            MergedCount = MergedCount + 1;
            AmountVnd = (AmountVnd ?? 0) + soTien;
            Title = noiDungMoi.TieuDe;
            Body = noiDungMoi.NoiDungChinh;
            Link = noiDungMoi.DuongDan;
            CreatedAt = luc;
        }

        /// <summary>Da doc (idempotent). Tra ve true neu vua doi trang thai.</summary>
        public bool DanhDauDaDoc(DateTimeOffset luc)
        {
            if (ReadAt.HasValue)
            {
                return false;
            }

            ReadAt = luc;
            return true;
        }

        public void DaGuiEmail(DateTimeOffset luc)
        {
            EmailState = EmailState.Sent;
            EmailSentAt = luc;
            EmailNextAt = null;
            EmailError = null;
            EmailAttempts = EmailAttempts + 1;
        }

        /// <summary>Gui loi: hen gui lai sau (backoff), qua so lan toi da thi bo — thong bao trong ung dung van con.</summary>
        public void GuiEmailLoi(string loi, int soLanToiDa, DateTimeOffset luc)
        {
            EmailAttempts = EmailAttempts + 1;
            EmailError = loi.Length > 500 ? loi.Substring(0, 500) : loi;
            if (EmailAttempts >= soLanToiDa)
            {
                EmailState = EmailState.Failed;
                EmailNextAt = null;
            }
            else
            {
                EmailNextAt = luc + ThoiGianChoGuiLai(EmailAttempts);
            }
        }

        /// <summary>Khong co dia chi email (chua nhan user.registered) — bo email, giu thong bao.</summary>
        public void BoEmail(string lyDo)
        {
            EmailState = EmailState.Skipped;
            EmailNextAt = null;
            EmailError = lyDo;
        }

        /// <summary>Backoff luy thua: 10 giay, 20 giay, 40 giay... toi da 30 phut.</summary>
        public static TimeSpan ThoiGianChoGuiLai(int soLanDaThu)
        {
            double giay = 10 * Math.Pow(2, Math.Max(0, soLanDaThu - 1));
            return TimeSpan.FromSeconds(Math.Min(giay, 30 * 60));
        }
    }

    public enum EmailState
    {
        /// <summary>Khong gui email (nguoi dung tat, hoac khong co email).</summary>
        Skipped,

        Pending,

        Sent,

        /// <summary>Thu qua so lan toi da — bo.</summary>
        Failed,
    }

    /// <summary>
    /// Noi dung da dung san cua mot thong bao (xem NoiDungThongBao): ban tieng Viet + (tuy chon) ban tieng
    /// Anh (FC-08). ThongBaoWriter chon ban theo ngon ngu nguoi nhan LUC GHI — thong bao va email cung mot
    /// ngon ngu, doi ngon ngu chi anh huong thong bao sau.
    /// </summary>
    public sealed class NoiDung
    {
        public NoiDung(string tieuDe, string noiDungChinh, string? duongDan)
        {
            TieuDe = tieuDe;
            NoiDungChinh = noiDungChinh;
            DuongDan = duongDan;
        }

        public string TieuDe { get; }

        public string NoiDungChinh { get; }

        public string? DuongDan { get; }

        public string? TieuDeEn { get; private set; }

        public string? NoiDungChinhEn { get; private set; }

        /// <summary>Kem ban tieng Anh (tra ve chinh doi tuong, de viet lien mot dong).</summary>
        public NoiDung En(string tieuDe, string noiDungChinh)
        {
            TieuDeEn = tieuDe;
            NoiDungChinhEn = noiDungChinh;
            return this;
        }

        /// <summary>"en" va co ban tieng Anh → ban tieng Anh; con lai → tieng Viet.</summary>
        public NoiDung TheoNgonNgu(string? ngonNgu)
        {
            if (string.Equals(ngonNgu, NgonNgu.TiengAnh, StringComparison.Ordinal) && TieuDeEn != null && NoiDungChinhEn != null)
            {
                return new NoiDung(TieuDeEn, NoiDungChinhEn, DuongDan);
            }

            return this;
        }
    }

    /// <summary>Ngon ngu thong bao / email (FC-08). Mac dinh tieng Viet.</summary>
    public static class NgonNgu
    {
        public const string TiengViet = "vi";
        public const string TiengAnh = "en";

        public static bool HopLe(string? ma)
        {
            return string.Equals(ma, TiengViet, StringComparison.Ordinal) || string.Equals(ma, TiengAnh, StringComparison.Ordinal);
        }
    }

    /// <summary>Ngon ngu nguoi dung chon cho thong bao + email. Khong co dong = tieng Viet.</summary>
    public sealed class NgonNguNguoiDung
    {
        private NgonNguNguoiDung()
        {
            Language = NgonNgu.TiengViet;
        }

        public Guid UserId { get; private set; }

        public string Language { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static NgonNguNguoiDung Tao(Guid userId, string ngonNgu, DateTimeOffset luc)
        {
            NgonNguNguoiDung n = new NgonNguNguoiDung();
            n.UserId = userId;
            n.Language = ngonNgu;
            n.UpdatedAt = luc;
            return n;
        }

        public void Dat(string ngonNgu, DateTimeOffset luc)
        {
            Language = ngonNgu;
            UpdatedAt = luc;
        }
    }
}
