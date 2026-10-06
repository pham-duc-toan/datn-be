using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.EntranceTests;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Datasets;

namespace Crowd.Project.Tests
{
    public sealed class GoldItemTests
    {
        [Fact]
        public void Dap_an_vang_kiem_theo_tap_nhan_truoc_khi_tao()
        {
            LabelSchema s = LabelingProjectTests.PhanLoaiAnh("cho", "meo");

            LabelPayload dapAn = LabelPayload.Tao(s, "{\"label\":{\"labelIds\":[\"meo\"]}}", null);
            GoldItem g = GoldItem.Tao(Guid.NewGuid(), Guid.NewGuid(), dapAn, GoldPurpose.EntranceTest, DateTimeOffset.UtcNow);
            Assert.Equal(dapAn, g.ExpectedPayload);
            Assert.Equal(Modalities.Image, g.ExpectedPayload.TaskType);

            // Lop khong co trong tap nhan → khong tao duoc dap an.
            Assert.Throws<LabelFormatException>(() =>
                LabelPayload.Tao(s, "{\"label\":{\"labelIds\":[\"voi\"]}}", null));
        }
    }

    public sealed class CatDoanTests
    {
        [Fact]
        public void Khong_khai_segmentSeconds_hoac_file_ngan_thi_khong_cat()
        {
            Assert.Single(Crowd.Project.Api.Services.DatasetIngestor.CatDoan(25, null));
            Assert.Single(Crowd.Project.Api.Services.DatasetIngestor.CatDoan(8, 10));
        }

        [Fact]
        public void Cat_deu_va_doan_cuoi_ngan_hon()
        {
            List<(double BatDau, double KetThuc)> d = Crowd.Project.Api.Services.DatasetIngestor.CatDoan(25, 10);

            Assert.Equal(3, d.Count);
            Assert.Equal((0d, 10d), d[0]);
            Assert.Equal((10d, 20d), d[1]);
            Assert.Equal((20d, 25d), d[2]);
        }

        [Fact]
        public void Doan_cuoi_duoi_mot_giay_gop_vao_doan_truoc()
        {
            List<(double BatDau, double KetThuc)> d = Crowd.Project.Api.Services.DatasetIngestor.CatDoan(20.4, 10);

            Assert.Equal(2, d.Count);
            Assert.Equal((10d, 20.4d), d[1]);
        }
    }

    public sealed class ProjectMemberTests
    {
        [Fact]
        public void Khong_chan_khong_xoa_duoc_chu_so_huu()
        {
            ProjectMember chu = ProjectMember.TaoChuSoHuu(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

            Assert.Throws<RuleViolationException>(() => chu.Chan(DateTimeOffset.UtcNow));
            Assert.Throws<RuleViolationException>(() => chu.KiemTraCoTheXoa());
        }

        [Fact]
        public void Khong_them_duoc_owner_thu_hai()
        {
            Assert.Throws<InvalidValueException>(() =>
                ProjectMember.TaoThanhVien(Guid.NewGuid(), Guid.NewGuid(), MemberRole.Owner, DateTimeOffset.UtcNow));
        }

        [Fact]
        public void Chan_roi_bo_chan()
        {
            ProjectMember m = ProjectMember.TaoThanhVien(Guid.NewGuid(), Guid.NewGuid(), MemberRole.Labeler, DateTimeOffset.UtcNow);

            m.Chan(DateTimeOffset.UtcNow);
            Assert.False(m.LaHoatDong());
            Assert.Throws<RuleViolationException>(() => m.Chan(DateTimeOffset.UtcNow));

            m.BoChan(DateTimeOffset.UtcNow);
            Assert.True(m.LaHoatDong());
        }
    }

    public sealed class EntranceAttemptTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Cau1 = Guid.NewGuid();
        private static readonly Guid Cau2 = Guid.NewGuid();

        private static readonly LabelSchema TapNhan = LabelSchema.Doc(
            "{\"modality\":\"image\",\"tools\":[{\"name\":\"label\",\"kind\":\"classification\","
            + "\"classes\":[\"cho\",\"meo\",\"ga\"],\"allowMultiple\":true}]}");

        private static LabelPayload Nhan(params string[] lop)
        {
            return LabelPayload.Tao(
                TapNhan,
                "{\"label\":{\"labelIds\":[\"" + string.Join("\",\"", lop) + "\"]}}",
                null);
        }

        private static Dictionary<Guid, LabelPayload> DapAn()
        {
            Dictionary<Guid, LabelPayload> d = new Dictionary<Guid, LabelPayload>();
            d[Cau1] = Nhan("cho");
            d[Cau2] = Nhan("meo", "ga");
            return d;
        }

        private static EntranceAttempt LanMoi()
        {
            return EntranceAttempt.BatDau(Guid.NewGuid(), Guid.NewGuid(), new List<Guid> { Cau1, Cau2 }, 0, false, Luc);
        }

        [Fact]
        public void Cham_theo_tap_nhan_trung_khop_khong_tinh_thu_tu()
        {
            EntranceAttempt a = LanMoi();
            Dictionary<Guid, LabelPayload> traLoi = new Dictionary<Guid, LabelPayload>();
            traLoi[Cau1] = Nhan("cho");
            traLoi[Cau2] = Nhan("ga", "meo");

            bool dau = a.Nop(traLoi, DapAn(), TapNhan, 100, Luc.AddMinutes(5));

            Assert.True(dau);
            Assert.Equal(100, a.ScorePercent);
        }

        [Fact]
        public void Multi_label_thieu_mot_nhan_la_sai()
        {
            EntranceAttempt a = LanMoi();
            Dictionary<Guid, LabelPayload> traLoi = new Dictionary<Guid, LabelPayload>();
            traLoi[Cau1] = Nhan("cho");
            traLoi[Cau2] = Nhan("meo");

            bool dau = a.Nop(traLoi, DapAn(), TapNhan, 80, Luc.AddMinutes(5));

            Assert.False(dau);
            Assert.Equal(50, a.ScorePercent);
        }

        [Fact]
        public void Nop_tre_la_truot_voi_0_diem()
        {
            EntranceAttempt a = LanMoi();
            Dictionary<Guid, LabelPayload> traLoi = new Dictionary<Guid, LabelPayload>();
            traLoi[Cau1] = Nhan("cho");
            traLoi[Cau2] = Nhan("meo", "ga");

            bool dau = a.Nop(traLoi, DapAn(), TapNhan, 50, Luc + EntranceAttempt.ThoiGianLamBai);

            Assert.False(dau);
            Assert.Equal(0, a.ScorePercent);
        }

        [Fact]
        public void Khong_nop_duoc_hai_lan()
        {
            EntranceAttempt a = LanMoi();
            a.Nop(new Dictionary<Guid, LabelPayload>(), DapAn(), TapNhan, 50, Luc);

            Assert.Throws<RuleViolationException>(() =>
                a.Nop(new Dictionary<Guid, LabelPayload>(), DapAn(), TapNhan, 50, Luc));
        }

        [Fact]
        public void Toi_da_ba_lan_va_khong_mo_hai_lan_cung_luc()
        {
            List<Guid> cau = new List<Guid> { Cau1 };

            Assert.Throws<RuleViolationException>(() =>
                EntranceAttempt.BatDau(Guid.NewGuid(), Guid.NewGuid(), cau, EntranceAttempt.SoLanToiDa, false, Luc));

            Assert.Throws<RuleViolationException>(() =>
                EntranceAttempt.BatDau(Guid.NewGuid(), Guid.NewGuid(), cau, 0, true, Luc));
        }
    }

    public sealed class ZipImageReaderTests
    {
        [Fact]
        public void Magic_bytes_quyet_dinh_chu_khong_phai_duoi_file()
        {
            byte[] jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
            byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
            byte[] exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };

            Assert.True(ZipImageReader.DungMagicBytes(jpeg, ".jpg"));
            Assert.True(ZipImageReader.DungMagicBytes(png, ".png"));

            // "virus.exe" doi ten thanh "anh.jpg" van bi loai.
            Assert.False(ZipImageReader.DungMagicBytes(exe, ".jpg"));

            // PNG doi duoi thanh .jpg cung bi loai: duoi va noi dung phai khop.
            Assert.False(ZipImageReader.DungMagicBytes(png, ".jpg"));
        }
    }
}
