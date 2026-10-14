using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crowd.BuildingBlocks.Storage;

namespace Crowd.BuildingBlocks.Tests.Storage
{
    /// <summary>
    /// Token link xem mau (FP-03): C# ma hoa, media-svc (Python) giai ma. VECTOR co dinh duoi day duoc
    /// tinh bang thu vien cryptography cua Python — C# phai ra DUNG chuoi do, va test Python
    /// (services/media/tests/test_grant.py) giai dung chuoi do. Lech la hai ben khong noi chuyen duoc.
    /// </summary>
    public sealed class MediaGrantTests
    {
        private const string KhoaVector = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

        private const string TokenVector =
            "ZGVmZ2hpamtsbW5vMzmoREPYerxZQGXK61RbzHPzNzumXcJDlvydecqSiHml2HHtfSOqNczCSQ7Mc_v-OXe6lAJwYxrIw57k3bOXpzq-6Tz7eQk3uULfcSO4HNRWSi0Glfe_9O3DBOZXMT5ztFOhTVh-i02AyfP2kT6LxREYsyzsSooHgVSggumHO_QBrKubWgFgB-sidD-r8C7t_KKeMiNk5_BSvgZjkH_C2LwTSjOy5sOJBjnGks5Ygbu0y--dA4dm-0w1JWX2tCv65xsy3pw4QDV2S0ZXcQ7GPHR8ojazbDif3jplp87ff54iS47Z0KcRHHbNPrTEMPLzfPRqicnmmqqzlWgGQ0yR_hNRpqIUSOD0hznhssiWLiCYu30Gh1oIFft5d2VZSnRD48clWwVWNSZmfok_xbtAJyrh4mYG";

        private static readonly byte[] NonceVector = new byte[] { 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111 };

        private static MauCanXem MauVector()
        {
            return new MauCanXem
            {
                SampleId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ProjectId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                StorageKey = "projects/abc/x.png",
                Modality = "image",
            };
        }

        [Fact]
        public void Ma_hoa_ra_dung_vector_Python()
        {
            byte[] noiDung = MediaGrant.TaoNoiDung(
                MauVector(),
                NguoiXem.Labeler(Guid.Parse("44444444-4444-4444-4444-444444444444")),
                CheDoXem.Watermark,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                DateTimeOffset.FromUnixTimeSeconds(1760000000),
                DateTimeOffset.FromUnixTimeSeconds(1760000300));

            Assert.Equal(TokenVector, MediaGrant.MaHoa(MediaGrant.DocKhoa(KhoaVector), noiDung, NonceVector));
        }

        [Fact]
        public void Khach_cong_link_khong_co_user_chi_co_phien()
        {
            Guid phien = Guid.NewGuid();
            byte[] noiDung = MediaGrant.TaoNoiDung(MauVector(), NguoiXem.Khach(phien), CheDoXem.Watermark,
                Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5));

            using (JsonDocument doc = JsonDocument.Parse(noiDung))
            {
                Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("u").ValueKind);
                Assert.Equal(phien, doc.RootElement.GetProperty("gs").GetGuid());
                Assert.Equal("guest", doc.RootElement.GetProperty("r").GetString());
            }
        }

        [Fact]
        public void Token_bi_sua_mot_ky_tu_thi_khong_giai_duoc()
        {
            byte[] khoa = MediaGrant.DocKhoa(KhoaVector);
            char[] sua = TokenVector.ToCharArray();
            sua[40] = sua[40] == 'A' ? 'B' : 'A';

            Assert.Equal(
                "projects/abc/x.png",
                JsonDocument.Parse(MediaGrant.GiaiMa(khoa, TokenVector)).RootElement.GetProperty("k").GetString());
            Assert.ThrowsAny<CryptographicException>(() => MediaGrant.GiaiMa(khoa, new string(sua)));
        }

        [Fact]
        public void Khoa_khac_thi_khong_giai_duoc()
        {
            byte[] khoaKhac = new byte[32];
            Assert.ThrowsAny<CryptographicException>(() => MediaGrant.GiaiMa(khoaKhac, TokenVector));
        }

        [Fact]
        public void Token_khong_lo_khoa_MinIO()
        {
            string b64 = TokenVector.Replace('-', '+').Replace('_', '/');
            b64 = b64 + new string('=', (4 - (b64.Length % 4)) % 4);
            Assert.DoesNotContain("projects", Encoding.ASCII.GetString(Convert.FromBase64String(b64)), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("")]
        [InlineData("khong-phai-base64!")]
        [InlineData("AAECAwQ=")]
        public void Khoa_sai_thi_loi_ngay(string khoa)
        {
            Assert.Throws<InvalidOperationException>(() => MediaGrant.DocKhoa(khoa));
        }
    }
}
