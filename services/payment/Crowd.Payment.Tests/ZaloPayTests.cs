using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Crowd.Payment.Tests
{
    public sealed class ZaloPayTests
    {
        private const string Key1 = "k1_test";
        private const string Key2 = "k2_test";

        private static ZaloPayProvider Cong()
        {
            ZaloPayOptions o = new ZaloPayOptions
            {
                AppId = "2554",
                Key1 = Key1,
                Key2 = Key2,
                ApiUrl = "https://zalopay.test",
            };
            return new ZaloPayProvider(Options.Create(o), new HttpClient(), TimeProvider.System, NullLogger<ZaloPayProvider>.Instance);
        }

        private static string Callback(Guid id, long amount, string khoa, string appId = "2554")
        {
            string data = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                { "app_id", int.Parse(appId, System.Globalization.CultureInfo.InvariantCulture) },
                { "app_trans_id", "261120_" + id.ToString("N") },
                { "app_time", 1763600000000 },
                { "app_user", "crowdlabel" },
                { "amount", amount },
                { "embed_data", "{}" },
                { "item", "[]" },
                { "zp_trans_id", 251120000012345 },
                { "server_time", 1763600010000 },
                { "channel", 38 },
            });
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                { "data", data },
                { "mac", ZaloPayKy.Hmac(khoa, data) },
                { "type", 1 },
            });
        }

        [Fact]
        public void Chuoi_ky_tao_don_dung_thu_tu_tai_lieu()
        {
            Assert.Equal(
                "2554|261120_abc|crowdlabel|50000|1763600000000|{}|[]",
                ZaloPayKy.ChuoiKyTao("2554", "261120_abc", "crowdlabel", 50000, 1763600000000, "{}", "[]"));
            Assert.Equal("2554|261120_abc|k1", ZaloPayKy.ChuoiKyTraCuu("2554", "261120_abc", "k1"));
        }

        [Fact]
        public void Ma_giao_dich_co_tien_to_ngay_gio_VN_va_tach_lai_duoc()
        {
            Guid id = Guid.NewGuid();
            YeuCauThanhToan yc = new YeuCauThanhToan
            {
                IntentId = id,
                AmountVnd = 50000,
                // 20/11 18:30 UTC = 21/11 01:30 gio VN → tien to phai la ngay 21.
                CreatedAt = new DateTimeOffset(2026, 11, 20, 18, 30, 0, TimeSpan.Zero),
                ClientIp = "127.0.0.1",
                MoTa = "Nap tien",
            };

            string ma = ZaloPayKy.MaGiaoDich(yc);
            Assert.Equal("261121_" + id.ToString("N"), ma);
            Assert.True(ma.Length <= 40);

            Guid tach;
            Assert.True(ZaloPayKy.TachIntentId(ma, out tach));
            Assert.Equal(id, tach);
            Assert.False(ZaloPayKy.TachIntentId("261121_khong-phai-guid", out tach));
        }

        [Fact]
        public void Callback_dung_mac_thi_doc_duoc_sai_thi_null()
        {
            Guid id = Guid.NewGuid();
            ThongBaoNap? tb = Cong().DocCallback(Callback(id, 50000, Key2));
            Assert.NotNull(tb);
            Assert.Equal(id, tb!.IntentId);
            Assert.True(tb.ThanhCong);
            Assert.Equal(50000, tb.AmountVnd);
            Assert.Equal("251120000012345", tb.ProviderTxnId);

            // Ky bang key1 (khoa tao don) hay khoa la deu bi tu choi — callback phai ky bang key2.
            Assert.Null(Cong().DocCallback(Callback(id, 50000, Key1)));
            Assert.Null(Cong().DocCallback(Callback(id, 50000, "khoa_ke_gian")));

            // Sua so tien trong data sau khi ky → mac lech.
            string sua = Callback(id, 50000, Key2).Replace("\\u0022amount\\u0022:50000", "\\u0022amount\\u0022:1", StringComparison.Ordinal);
            Assert.Null(Cong().DocCallback(sua));
        }

        [Fact]
        public void Callback_cua_app_khac_hoac_body_hong_thi_null()
        {
            Assert.Null(Cong().DocCallback(Callback(Guid.NewGuid(), 50000, Key2, "9999")));
            Assert.Null(Cong().DocCallback("khong phai json"));
            Assert.Null(Cong().DocCallback("{\"data\":\"\",\"mac\":\"\"}"));
        }

        [Fact]
        public void Dien_giai_ket_qua_tra_cuu()
        {
            KetQuaTraCuuNap daTra = ZaloPayProvider.DocKetQuaTraCuu(new Dictionary<string, string>
            {
                { "return_code", "1" }, { "sub_return_code", "1" }, { "amount", "50000" }, { "zp_trans_id", "251120000012345" },
            });
            Assert.Equal(TrangThaiTraCuu.DaThanhToan, daTra.TrangThai);
            Assert.Equal(50000, daTra.AmountVnd);
            Assert.Equal("251120000012345", daTra.ProviderTxnId);

            // Gia tri lay tu phan hoi THAT cua sandbox ZaloPay.
            Assert.Equal(TrangThaiTraCuu.ChuaThanhToan, ZaloPayProvider.DocKetQuaTraCuu(Ma("3", "0")).TrangThai);
            Assert.Equal(TrangThaiTraCuu.ChuaThanhToan, ZaloPayProvider.DocKetQuaTraCuu(Ma("2", "-101")).TrangThai);
            Assert.Equal(TrangThaiTraCuu.KhongRo, ZaloPayProvider.DocKetQuaTraCuu(Ma("2", "-402")).TrangThai);
            Assert.Equal(TrangThaiTraCuu.ThatBai, ZaloPayProvider.DocKetQuaTraCuu(Ma("2", "-54")).TrangThai);
        }

        private static Dictionary<string, string> Ma(string ma, string maPhu)
        {
            return new Dictionary<string, string> { { "return_code", ma }, { "sub_return_code", maPhu } };
        }
    }
}
