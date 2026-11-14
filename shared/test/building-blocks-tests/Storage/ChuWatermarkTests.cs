using System;
using System.Text.Json;
using Crowd.BuildingBlocks.Storage;

namespace Crowd.BuildingBlocks.Tests.Storage
{
    /// <summary>
    /// Watermark noi anh / video: MOT dinh dang chu "nguồn: &lt;nguoi xem&gt; - &lt;doanh nghiep&gt; - &lt;domain&gt;" va cac truong
    /// tuy chon cua token (wt, ws, a, b) — media-svc chi ve chu nhan duoc.
    /// </summary>
    public sealed class ChuWatermarkTests
    {
        private static readonly Guid Labeler = Guid.Parse("0192f3a4-7c1e-7a2b-9d10-5e6f7a8b9c0d");

        [Fact]
        public void Chu_ca_nhan_co_nguoi_xem_doanh_nghiep_domain()
        {
            Assert.Equal(
                "nguồn: 0192f3a4-7c1e-7a2b-9d10-5e6f7a8b9c0d - Công ty TNHH Văn Phong - gannhan.vn",
                ChuWatermark.CaNhan(NguoiXem.Labeler(Labeler), "Công ty TNHH Văn Phong", "gannhan.vn"));
        }

        [Fact]
        public void Khach_cong_link_dung_ma_phien_thieu_doanh_nghiep_thi_bo_phan_do()
        {
            Guid phien = Guid.Parse("11111111-2222-3333-4444-555555555555");
            Assert.Equal(
                "nguồn: 11111111-2222-3333-4444-555555555555 - gannhan.vn",
                ChuWatermark.CaNhan(NguoiXem.Khach(phien), null, "gannhan.vn"));
            Assert.Equal("nguồn: Công ty A - gannhan.vn", ChuWatermark.DungChung("Công ty A", "gannhan.vn"));
            Assert.Equal("nguồn: gannhan.vn", ChuWatermark.DungChung("  ", "gannhan.vn"));
        }

        [Theory]
        [InlineData("http://localhost:5173", "localhost:5173")]
        [InlineData("https://gannhan.vn", "gannhan.vn")]
        [InlineData("https://gannhan.vn/", "gannhan.vn")]
        [InlineData("https://app.gannhan.vn:8443/x", "app.gannhan.vn:8443")]
        [InlineData("gannhan.vn/", "gannhan.vn")]
        public void Domain_lay_tu_dia_chi_trang(string diaChi, string domain)
        {
            Assert.Equal(domain, ChuWatermark.DomainTu(diaChi));
        }

        [Fact]
        public void Token_ghi_chu_va_doan_video_khi_co_bo_khi_khong()
        {
            MauCanXem video = new MauCanXem
            {
                SampleId = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                StorageKey = "p/a.mp4",
                Modality = "video",
                BatDau = 120,
                KetThuc = 240.5,
            };
            byte[] coChu = MediaGrant.TaoNoiDung(video, NguoiXem.Labeler(Labeler), CheDoXem.Watermark, Guid.NewGuid(),
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5), "nguồn: x - A - d", "nguồn: A - d");
            using (JsonDocument doc = JsonDocument.Parse(coChu))
            {
                Assert.Equal("nguồn: x - A - d", doc.RootElement.GetProperty("wt").GetString());
                Assert.Equal("nguồn: A - d", doc.RootElement.GetProperty("ws").GetString());
                Assert.Equal(120, doc.RootElement.GetProperty("a").GetDouble());
                Assert.Equal(240.5, doc.RootElement.GetProperty("b").GetDouble());
            }

            MauCanXem anh = new MauCanXem { SampleId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), StorageKey = "p/x.png", Modality = "image" };
            byte[] khongChu = MediaGrant.TaoNoiDung(anh, NguoiXem.Labeler(Labeler), CheDoXem.Goc, Guid.NewGuid(),
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5));
            using (JsonDocument doc = JsonDocument.Parse(khongChu))
            {
                Assert.False(doc.RootElement.TryGetProperty("wt", out _));
                Assert.False(doc.RootElement.TryGetProperty("ws", out _));
                Assert.False(doc.RootElement.TryGetProperty("a", out _));
            }
        }
    }
}
