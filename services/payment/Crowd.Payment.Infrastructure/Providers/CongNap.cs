using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Crowd.Payment.Infrastructure.Providers
{
    /// <summary>
    /// Mot cong NHAN tien nap (FB-03): sandbox, VNPay, MoMo. Tach khoi IPaymentProvider (cong CHI
    /// tien ra) vi hai chieu khac han nhau: VNPay / MoMo cho doanh nghiep tra tien vao, con chi tien
    /// cho labeler di qua cong khac (ngan hang / ung dung chi ho).
    ///
    /// Moi cong co ba viec:
    ///   1. TaoLinkThanhToanAsync — dung link trang thanh toan (VNPay: tu ky URL; MoMo: goi API tao don);
    ///   2. Doc thong bao ket qua (IPN / webhook) — dinh dang rieng tung cong nen moi adapter tu
    ///      doc + KIEM CHU KY roi tra ThongBaoNap chung (xem VnPayProvider.DocIpn, MoMoProvider.DocIpn);
    ///   3. TraCuuNapAsync — HOI NGUOC cong "lenh nay da tra chua, bao nhieu tien" (VD-S-02, VD-M-09).
    /// </summary>
    public interface ICongNap
    {
        /// <summary>Ten cong luu vao payment_intents.provider: sandbox | vnpay | momo.</summary>
        string Name { get; }

        Task<string> TaoLinkThanhToanAsync(YeuCauThanhToan yeuCau, CancellationToken ct);

        Task<KetQuaTraCuuNap> TraCuuNapAsync(YeuCauThanhToan yeuCau, CancellationToken ct);
    }

    /// <summary>Thong tin mot lenh nap gui sang cong.</summary>
    public sealed class YeuCauThanhToan
    {
        public required Guid IntentId { get; init; }

        public required long AmountVnd { get; init; }

        /// <summary>Luc tao lenh — VNPay dung lam vnp_CreateDate va de tra cuu (vnp_TransactionDate).</summary>
        public required DateTimeOffset CreatedAt { get; init; }

        /// <summary>IP cua nguoi tra tien (VNPay bat buoc vnp_IpAddr).</summary>
        public required string ClientIp { get; init; }

        public required string MoTa { get; init; }

        /// <summary>Ma don gui cong: 32 ky tu hex cua intentId (khong dau gach — VNPay chi nhan chu + so).</summary>
        public string MaDon
        {
            get { return IntentId.ToString("N"); }
        }
    }

    /// <summary>Ket qua thanh toan cong bao ve, DA kiem chu ky.</summary>
    public sealed class ThongBaoNap
    {
        public required Guid IntentId { get; init; }

        public required long AmountVnd { get; init; }

        public required bool ThanhCong { get; init; }

        /// <summary>Ma giao dich phia cong; rong khi that bai.</summary>
        public required string ProviderTxnId { get; init; }

        /// <summary>Ma ket qua goc cua cong (VNPay vnp_ResponseCode, MoMo resultCode) — ghi log, doi soat.</summary>
        public required string MaKetQua { get; init; }
    }

    public enum TrangThaiTraCuu
    {
        /// <summary>Cong xac nhan da thu tien.</summary>
        DaThanhToan,

        /// <summary>Cong xac nhan giao dich that bai / bi huy.</summary>
        ThatBai,

        /// <summary>Nguoi dung chua tra xong (con trong han).</summary>
        ChuaThanhToan,

        /// <summary>Khong hoi duoc (loi mang, cong khong ho tro tra cuu) — KHONG duoc doan, de lan sau.</summary>
        KhongRo,
    }

    public sealed class KetQuaTraCuuNap
    {
        public KetQuaTraCuuNap(TrangThaiTraCuu trangThai, long amountVnd, string providerTxnId, string maKetQua)
        {
            TrangThai = trangThai;
            AmountVnd = amountVnd;
            ProviderTxnId = providerTxnId;
            MaKetQua = maKetQua;
        }

        public TrangThaiTraCuu TrangThai { get; }

        public long AmountVnd { get; }

        public string ProviderTxnId { get; }

        public string MaKetQua { get; }

        public static KetQuaTraCuuNap KhongRo(string lyDo)
        {
            return new KetQuaTraCuuNap(TrangThaiTraCuu.KhongRo, 0, string.Empty, lyDo);
        }
    }

    /// <summary>Danh ba cac cong nap da cau hinh (co du khoa trong appsettings / bien moi truong).</summary>
    public sealed class DanhBaCongNap
    {
        private readonly Dictionary<string, ICongNap> _theoTen;

        public DanhBaCongNap(IEnumerable<ICongNap> cacCong)
        {
            if (cacCong == null)
            {
                throw new ArgumentNullException(nameof(cacCong));
            }

            _theoTen = new Dictionary<string, ICongNap>(StringComparer.OrdinalIgnoreCase);
            foreach (ICongNap c in cacCong)
            {
                _theoTen[c.Name] = c;
            }
        }

        public IReadOnlyCollection<string> TenCacCong
        {
            get { return _theoTen.Keys; }
        }

        public ICongNap? Tim(string? ten)
        {
            ICongNap? c;
            return ten != null && _theoTen.TryGetValue(ten, out c) ? c : null;
        }
    }

    /// <summary>Gio Viet Nam (GMT+7) dang yyyyMMddHHmmss — dinh dang ngay gio cua VNPay.</summary>
    public static class GioVietNam
    {
        private static readonly TimeSpan Lech = TimeSpan.FromHours(7);

        public static string Dinh(DateTimeOffset luc)
        {
            return luc.ToOffset(Lech).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }
    }
}
