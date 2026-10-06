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
    public sealed class LabelSchemaTests
    {
        private static readonly string[] ChoMeo = new string[] { "cho", "meo" };

        [Fact]
        public void Cat_khoang_trang_va_chan_lop_trung_khong_phan_biet_hoa_thuong()
        {
            LabelSchema s = LabelSchema.TaoPhanLoai(new List<string> { " cho ", "meo" }, false);
            Assert.Equal(ChoMeo, s.Classes);

            InvalidValueException ex = Assert.Throws<InvalidValueException>(() =>
                LabelSchema.TaoPhanLoai(new List<string> { "Meo", "meo" }, false));
            Assert.Equal("ten_lop_trung", ex.Code);
        }

        [Fact]
        public void Phan_loai_can_it_nhat_hai_lop()
        {
            Assert.Throws<InvalidValueException>(() => LabelSchema.TaoPhanLoai(new List<string> { "cho" }, false));
        }

        [Fact]
        public void Single_label_chi_nhan_dung_mot_nhan_co_that()
        {
            LabelSchema s = LabelSchema.TaoPhanLoai(new List<string> { "cho", "meo", "ga" }, false);

            Assert.True(s.LaBoNhanHopLe(new List<string> { "cho" }));
            Assert.False(s.LaBoNhanHopLe(new List<string>()));
            Assert.False(s.LaBoNhanHopLe(new List<string> { "cho", "meo" }));
            Assert.False(s.LaBoNhanHopLe(new List<string> { "voi" }));
        }

        [Fact]
        public void Multi_label_nhan_nhieu_nhan_nhung_khong_lap()
        {
            LabelSchema s = LabelSchema.TaoPhanLoai(new List<string> { "cho", "meo", "ga" }, true);

            Assert.True(s.LaBoNhanHopLe(new List<string> { "cho", "meo" }));
            Assert.False(s.LaBoNhanHopLe(new List<string> { "cho", "cho" }));
        }
    }

    public sealed class GoldItemTests
    {
        [Fact]
        public void Dap_an_vang_phai_hop_le_theo_tap_nhan()
        {
            LabelSchema s = LabelSchema.TaoPhanLoai(new List<string> { "cho", "meo" }, false);

            GoldItem g = GoldItem.Tao(Guid.NewGuid(), Guid.NewGuid(), LabelPayload.PhanLoai("meo"), GoldPurpose.EntranceTest, s, DateTimeOffset.UtcNow);
            Assert.Equal(LabelPayload.PhanLoai("meo"), g.ExpectedPayload);

            Assert.Throws<InvalidValueException>(() =>
                GoldItem.Tao(Guid.NewGuid(), Guid.NewGuid(), LabelPayload.PhanLoai("voi"), GoldPurpose.EntranceTest, s, DateTimeOffset.UtcNow));
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

        private static Dictionary<Guid, LabelPayload> DapAn()
        {
            Dictionary<Guid, LabelPayload> d = new Dictionary<Guid, LabelPayload>();
            d[Cau1] = LabelPayload.PhanLoai("cho");
            d[Cau2] = LabelPayload.PhanLoai("meo", "ga");
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
            traLoi[Cau1] = LabelPayload.PhanLoai("cho");
            traLoi[Cau2] = LabelPayload.PhanLoai("ga", "meo");

            bool dau = a.Nop(traLoi, DapAn(), 100, Luc.AddMinutes(5));

            Assert.True(dau);
            Assert.Equal(100, a.ScorePercent);
        }

        [Fact]
        public void Multi_label_thieu_mot_nhan_la_sai()
        {
            EntranceAttempt a = LanMoi();
            Dictionary<Guid, LabelPayload> traLoi = new Dictionary<Guid, LabelPayload>();
            traLoi[Cau1] = LabelPayload.PhanLoai("cho");
            traLoi[Cau2] = LabelPayload.PhanLoai("meo");

            bool dau = a.Nop(traLoi, DapAn(), 80, Luc.AddMinutes(5));

            Assert.False(dau);
            Assert.Equal(50, a.ScorePercent);
        }

        [Fact]
        public void Nop_tre_la_truot_voi_0_diem()
        {
            EntranceAttempt a = LanMoi();
            Dictionary<Guid, LabelPayload> traLoi = new Dictionary<Guid, LabelPayload>();
            traLoi[Cau1] = LabelPayload.PhanLoai("cho");
            traLoi[Cau2] = LabelPayload.PhanLoai("meo", "ga");

            bool dau = a.Nop(traLoi, DapAn(), 50, Luc + EntranceAttempt.ThoiGianLamBai);

            Assert.False(dau);
            Assert.Equal(0, a.ScorePercent);
        }

        [Fact]
        public void Khong_nop_duoc_hai_lan()
        {
            EntranceAttempt a = LanMoi();
            a.Nop(new Dictionary<Guid, LabelPayload>(), DapAn(), 50, Luc);

            Assert.Throws<RuleViolationException>(() =>
                a.Nop(new Dictionary<Guid, LabelPayload>(), DapAn(), 50, Luc));
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
