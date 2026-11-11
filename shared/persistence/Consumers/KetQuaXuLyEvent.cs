using System;
using System.Collections.Generic;
using System.Text;

namespace Crowd.BuildingBlocks.Persistence.Consumers
{
    /// <summary>
    /// LOAI 1 (kien truc 3.29): event toi khi doi tuong da doi trang thai / da bi xoa — ca BINH THUONG, khong phai
    /// loi. Handler nem de EventConsumer ack ngay, ghi log muc Information, KHONG thu lai, KHONG vao DLQ.
    /// Moi thay doi handler da lam trong transaction bi quay lui.
    /// </summary>
    public sealed class BoQuaEventException : Exception
    {
        public BoQuaEventException(string lyDo)
            : base(lyDo)
        {
        }
    }

    /// <summary>
    /// LOAI 2: khong phai loi he thong nhung de lai hau qua nghiep vu can NGUOI xu ly (vd tien thuong khong chi
    /// duoc). EventConsumer quay lui thay doi cua handler, phat manual_action.required (mot lan cho moi event) roi
    /// ack — admin thay mot muc "Viec can xu ly tay" trong hang doi duyet. KHONG thu lai, KHONG vao DLQ.
    /// </summary>
    public sealed class CanXuLyTayException : Exception
    {
        public CanXuLyTayException(ViecCanXuLyTay viec)
            : base(viec == null ? "Can xu ly tay" : viec.TieuDe)
        {
            if (viec == null)
            {
                throw new ArgumentNullException(nameof(viec));
            }

            Viec = viec;
        }

        public ViecCanXuLyTay Viec { get; }
    }

    /// <summary>Noi dung muc hang doi duyet — viet bang loi cho admin (khong ten service, khong ma event).</summary>
    public sealed class ViecCanXuLyTay
    {
        /// <summary>Ma loai viec on dinh, vd "thuong_khong_chi_duoc".</summary>
        public required string Loai { get; init; }

        public required string TieuDe { get; init; }

        public required string MoTa { get; init; }

        public Guid? ProjectId { get; init; }

        public Guid? UserId { get; init; }

        public long? AmountVnd { get; init; }

        /// <summary>"500.000 đ" — so tien trong mo ta cho admin.</summary>
        public static string Tien(long vnd)
        {
            return vnd.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ";
        }
    }

    /// <summary>
    /// LOAI 3 — loi ky thuat: thu lai co gian cach, lan cuoi ghi log Error (message sap vao DLQ). Ham thuan, dung
    /// chung cho EventConsumer va AuditConsumer.
    /// </summary>
    public static class ThuLaiEvent
    {
        /// <summary>
        /// So lan giao THAT BAI truoc lan nay — header x-delivery-count cua quorum queue (khong co = lan dau).
        /// </summary>
        public static long SoLanDaThatBai(IDictionary<string, object?>? headers)
        {
            object? v;
            if (headers == null || !headers.TryGetValue("x-delivery-count", out v) || v == null)
            {
                return 0;
            }

            if (v is long l)
            {
                return l;
            }

            if (v is int i)
            {
                return i;
            }

            if (v is byte[] b)
            {
                long so;
                return long.TryParse(Encoding.UTF8.GetString(b), out so) ? so : 0;
            }

            long kq;
            return long.TryParse(v.ToString(), out kq) ? kq : 0;
        }

        /// <summary>
        /// Lan nay hong nua thi broker chuyen message vao DLQ: quorum queue dead-letter khi so lan tra lai VUOT
        /// x-delivery-limit, tuc lan giao thu (limit + 1) la lan cuoi.
        /// </summary>
        public static bool LaLanCuoi(long soLanDaThatBai, int gioiHan)
        {
            return soLanDaThatBai >= gioiHan;
        }

        /// <summary>
        /// Cho truoc khi tra message lai hang doi: 1 giay, 3 giay, 9 giay, 27 giay... (khong qua toiDa). Loi tam
        /// thoi (CSDL khoi dong lai vai giay) khong con day message vao DLQ chi vi thu lai qua nhanh.
        /// </summary>
        public static TimeSpan ChoTruocKhiThuLai(long soLanDaThatBai, TimeSpan toiDa)
        {
            double giay = Math.Pow(3, Math.Min(soLanDaThatBai, 20));
            TimeSpan cho = TimeSpan.FromSeconds(giay);
            return cho > toiDa ? toiDa : cho;
        }
    }
}
