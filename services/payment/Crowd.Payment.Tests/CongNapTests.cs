using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Crowd.Payment.Tests
{
    public sealed class VnPayTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.Zero);

        private static VnPayProvider Cong()
        {
            VnPayOptions o = new VnPayOptions
            {
                TmnCode = "TESTCODE",
                HashSecret = "bi_mat_vnpay",
                PayUrl = "https://cong.test/pay",
                ReturnUrl = "https://fe.test/ket-qua",
            };
            return new VnPayProvider(Options.Create(o), new HttpClient(), TimeProvider.System, NullLogger<VnPayProvider>.Instance);
        }

        private static Dictionary<string, string> TachQuery(string url)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string cap in url.Substring(url.IndexOf('?', StringComparison.Ordinal) + 1).Split('&'))
            {
                string[] kv = cap.Split('=', 2);
                d[Uri.UnescapeDataString(kv[0])] = System.Net.WebUtility.UrlDecode(kv[1]);
            }

            return d;
        }

        [Fact]
        public void Chuoi_ky_sap_xep_theo_ten_bo_rong_va_bo_chu_ky()
        {
            Dictionary<string, string> p = new Dictionary<string, string>
            {
                { "vnp_TxnRef", "abc" },
                { "vnp_Amount", "1000000" },
                { "vnp_OrderInfo", "Nap tien" },
                { "vnp_BankCode", string.Empty },
                { "vnp_SecureHash", "x" },
                { "khac", "y" },
            };

            // Ordinal: chu hoa dung truoc chu thuong; dau cach → '+'.
            Assert.Equal("vnp_Amount=1000000&vnp_OrderInfo=Nap+tien&vnp_TxnRef=abc", VnPayKy.ChuoiKy(p));
        }

        [Fact]
        public async Task Link_thanh_toan_ky_dung_va_sua_so_tien_thi_sai_chu_ky()
        {
            VnPayProvider c = Cong();
            Guid id = Guid.NewGuid();
            string url = await c.TaoLinkThanhToanAsync(new YeuCauThanhToan
            {
                IntentId = id,
                AmountVnd = 50000,
                CreatedAt = T,
                ClientIp = "1.2.3.4",
                MoTa = "Nap tien vi",
            }, default);

            Dictionary<string, string> q = TachQuery(url);
            Assert.Equal("5000000", q["vnp_Amount"]);
            Assert.Equal(id.ToString("N"), q["vnp_TxnRef"]);
            Assert.Equal("20261014080203", q["vnp_CreateDate"]);
            Assert.Equal("20261014081703", q["vnp_ExpireDate"]);
            Assert.True(VnPayKy.KhopChuKy(VnPayKy.Hmac("bi_mat_vnpay", VnPayKy.ChuoiKy(q)), q["vnp_SecureHash"]));

            q["vnp_Amount"] = "100";
            Assert.False(VnPayKy.KhopChuKy(VnPayKy.Hmac("bi_mat_vnpay", VnPayKy.ChuoiKy(q)), q["vnp_SecureHash"]));
        }

        private static Dictionary<string, string> Ipn(Guid id, string ma, string trangThai, long amount, string khoa)
        {
            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "vnp_Amount", (amount * 100).ToString(CultureInfo.InvariantCulture) },
                { "vnp_ResponseCode", ma },
                { "vnp_TransactionStatus", trangThai },
                { "vnp_TransactionNo", "14000001" },
                { "vnp_TxnRef", id.ToString("N") },
                { "vnp_TmnCode", "TESTCODE" },
                { "vnp_OrderInfo", "Nap tien vi" },
            };
            p["vnp_SecureHashType"] = "HmacSHA512";
            p["vnp_SecureHash"] = VnPayKy.Hmac(khoa, VnPayKy.ChuoiKy(p)).ToUpperInvariant();
            return p;
        }

        [Fact]
        public void Ipn_dung_chu_ky_doc_ra_so_tien_dong()
        {
            Guid id = Guid.NewGuid();
            ThongBaoNap? tb = Cong().DocIpn(Ipn(id, "00", "00", 50000, "bi_mat_vnpay"));

            Assert.NotNull(tb);
            Assert.Equal(id, tb!.IntentId);
            Assert.Equal(50000, tb.AmountVnd);
            Assert.True(tb.ThanhCong);
            Assert.Equal("14000001", tb.ProviderTxnId);
        }

        [Fact]
        public void Ipn_sai_khoa_hoac_bi_sua_thi_null()
        {
            Guid id = Guid.NewGuid();
            Assert.Null(Cong().DocIpn(Ipn(id, "00", "00", 50000, "khoa_ke_gian")));

            Dictionary<string, string> p = Ipn(id, "00", "00", 50000, "bi_mat_vnpay");
            p["vnp_Amount"] = "500000000";
            Assert.Null(Cong().DocIpn(p));
        }

        [Fact]
        public void Ipn_ma_loi_la_that_bai()
        {
            ThongBaoNap? tb = Cong().DocIpn(Ipn(Guid.NewGuid(), "24", "02", 50000, "bi_mat_vnpay"));
            Assert.NotNull(tb);
            Assert.False(tb!.ThanhCong);
            Assert.Equal(string.Empty, tb.ProviderTxnId);
        }

        [Fact]
        public void Gio_viet_nam_dinh_dang_vnpay()
        {
            Assert.Equal("20261014080203", GioVietNam.Dinh(T));
        }
    }

    public sealed class MoMoTests
    {
        private static MoMoProvider Cong()
        {
            MoMoOptions o = new MoMoOptions
            {
                PartnerCode = "MOMOTEST",
                AccessKey = "ak",
                SecretKey = "sk",
                ApiUrl = "https://momo.test",
            };
            return new MoMoProvider(Options.Create(o), new HttpClient(), NullLogger<MoMoProvider>.Instance);
        }

        private static Dictionary<string, string> Ipn(Guid id, string resultCode, string khoa)
        {
            Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "partnerCode", "MOMOTEST" },
                { "orderId", id.ToString("N") },
                { "requestId", "r1" },
                { "amount", "75000" },
                { "orderInfo", "Nap tien" },
                { "orderType", "momo_wallet" },
                { "transId", "4088878653" },
                { "resultCode", resultCode },
                { "message", "Successful." },
                { "payType", "qr" },
                { "responseTime", "1721720663942" },
                { "extraData", string.Empty },
            };
            p["signature"] = MoMoKy.Hmac(khoa, MoMoKy.ChuoiKyIpn("ak", p));
            return p;
        }

        [Fact]
        public void Chuoi_ky_ipn_dung_thu_tu_tai_lieu()
        {
            Dictionary<string, string> p = Ipn(Guid.Empty, "0", "sk");
            Assert.Equal(
                "accessKey=ak&amount=75000&extraData=&message=Successful.&orderId=00000000000000000000000000000000&orderInfo=Nap tien"
                + "&orderType=momo_wallet&partnerCode=MOMOTEST&payType=qr&requestId=r1&responseTime=1721720663942&resultCode=0&transId=4088878653",
                MoMoKy.ChuoiKyIpn("ak", p));
        }

        [Fact]
        public void Ipn_dung_chu_ky_thi_doc_duoc_sai_thi_null()
        {
            Guid id = Guid.NewGuid();
            ThongBaoNap? tb = Cong().DocIpn(Ipn(id, "0", "sk"));
            Assert.NotNull(tb);
            Assert.True(tb!.ThanhCong);
            Assert.Equal(75000, tb.AmountVnd);
            Assert.Equal("4088878653", tb.ProviderTxnId);

            Assert.Null(Cong().DocIpn(Ipn(id, "0", "khoa_ke_gian")));

            Dictionary<string, string> sua = Ipn(id, "0", "sk");
            sua["amount"] = "1";
            Assert.Null(Cong().DocIpn(sua));
        }

        [Fact]
        public void Ipn_ma_khac_0_la_that_bai()
        {
            ThongBaoNap? tb = Cong().DocIpn(Ipn(Guid.NewGuid(), "1006", "sk"));
            Assert.NotNull(tb);
            Assert.False(tb!.ThanhCong);
        }
    }
}
