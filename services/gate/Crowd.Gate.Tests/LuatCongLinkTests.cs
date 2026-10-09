using System;
using System.Collections.Generic;
using Crowd.Gate.Domain;
using Crowd.Labeling;

namespace Crowd.Gate.Tests
{
    public sealed class LuatCongLinkTests
    {
        private static readonly LabelSchema PhanLoaiAnh = LabelSchema.Doc(
            "{\"modality\":\"image\",\"tools\":[{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"cho\",\"meo\"]}]}");

        private static readonly Guid Vang = Guid.NewGuid();

        [Fact]
        public void Chi_ho_tro_nhan_re_va_nhanh_cho_khach_vang_lai()
        {
            Assert.Null(LuatCongLink.LyDoKhongHoTro(Modalities.Image, PhanLoaiAnh));

            LabelSchema cap = LabelSchema.Doc("{\"modality\":\"pair\",\"tools\":[{\"name\":\"tot\",\"kind\":\"pairwise\"}]}");
            Assert.Null(LuatCongLink.LyDoKhongHoTro(Modalities.Pair, cap));

            LabelSchema nhieuLop = LabelSchema.Doc(
                "{\"modality\":\"image\",\"tools\":[{\"name\":\"loai\",\"kind\":\"classification\",\"allowMultiple\":true,\"classes\":[\"a\",\"b\"]}]}");
            Assert.NotNull(LuatCongLink.LyDoKhongHoTro(Modalities.Image, nhieuLop));

            LabelSchema khung = LabelSchema.Doc("{\"modality\":\"image\",\"tools\":[{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"xe\"]}]}");
            Assert.NotNull(LuatCongLink.LyDoKhongHoTro(Modalities.Image, khung));

            LabelSchema amThanh = LabelSchema.Doc(
                "{\"modality\":\"audio\",\"tools\":[{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"a\",\"b\"]}]}");
            Assert.NotNull(LuatCongLink.LyDoKhongHoTro(Modalities.Audio, amThanh));
        }

        [Fact]
        public void Cham_chi_cau_vang_thieu_hoac_sai_la_truot()
        {
            Dictionary<Guid, LabelPayload> dapAn = new Dictionary<Guid, LabelPayload>
            {
                [Vang] = Nhan("cho"),
            };
            List<Guid> cauVang = new List<Guid> { Vang };

            Dictionary<Guid, LabelPayload> dung = new Dictionary<Guid, LabelPayload> { [Vang] = Nhan("cho"), [Guid.NewGuid()] = Nhan("meo") };
            Assert.True(LuatCongLink.DatCauVang(cauVang, dung, dapAn, PhanLoaiAnh, NguongKhop.CuaThuVien));

            Dictionary<Guid, LabelPayload> sai = new Dictionary<Guid, LabelPayload> { [Vang] = Nhan("meo") };
            Assert.False(LuatCongLink.DatCauVang(cauVang, sai, dapAn, PhanLoaiAnh, NguongKhop.CuaThuVien));

            Assert.False(LuatCongLink.DatCauVang(cauVang, new Dictionary<Guid, LabelPayload>(), dapAn, PhanLoaiAnh, NguongKhop.CuaThuVien));
        }

        [Fact]
        public void Tu_vuot_xet_truoc_trung_ip_het_ngan_sach_xet_cuoi()
        {
            Assert.Equal(KetQuaLuot.KhongCoCauHoi, LuatCongLink.PhanLoai(false, true, true, true, false));
            Assert.Equal(KetQuaLuot.TuVuot, LuatCongLink.PhanLoai(true, true, false, true, false));
            Assert.Equal(KetQuaLuot.TuVuot, LuatCongLink.PhanLoai(true, false, true, false, true));
            Assert.Equal(KetQuaLuot.TrungIp, LuatCongLink.PhanLoai(true, false, false, true, false));
            Assert.Equal(KetQuaLuot.HetNganSach, LuatCongLink.PhanLoai(true, false, false, false, false));
            Assert.Equal(KetQuaLuot.TinhTien, LuatCongLink.PhanLoai(true, false, false, false, true));
        }

        [Fact]
        public void Tien_mot_luot_theo_dac_ta()
        {
            // Dac ta 2.4: don gia 200, phi 30% = 60/nhan → sharer 140/nhan, nen tang 120/nhan.
            TienLuot t = LuatCongLink.TinhTien(2, 200, 60);
            Assert.Equal(280, t.SharerVnd);
            Assert.Equal(240, t.NenTangVnd);
            Assert.Equal(520, t.TongVnd);
            Assert.Equal(520, LuatCongLink.ChiPhiMotLuot(2, 200, 60));

            // Phi lon hon don gia (phi toi da 90% nen thuc te khong xay ra): sharer khong am.
            Assert.Equal(0, LuatCongLink.TinhTien(1, 100, 150).SharerVnd);
        }

        [Fact]
        public void Ban_sao_ngan_sach_bo_qua_event_cu()
        {
            NganSachCong n = NganSachCong.Tao(Guid.NewGuid());
            Assert.True(n.Dat(1000, 2, DateTimeOffset.UtcNow));
            Assert.False(n.Dat(5000, 1, DateTimeOffset.UtcNow));
            Assert.Equal(1000, n.RemainingVnd);
        }

        [Fact]
        public void Link_het_han_hoac_vo_hieu_khong_phuc_vu()
        {
            DateTimeOffset luc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            LinkCong l = LinkCong.Tao(Guid.NewGuid(), "abc", Guid.NewGuid(), luc);
            Assert.False(l.PhucVuDuoc(luc));

            l.KichHoat("https://example.com", null, luc.AddHours(1), null, null, luc);
            Assert.True(l.PhucVuDuoc(luc));
            Assert.False(l.PhucVuDuoc(luc.AddHours(1)));

            l.VoHieuHoa(luc);
            Assert.False(l.PhucVuDuoc(luc));
        }

        private static LabelPayload Nhan(string lop)
        {
            return LabelPayload.Tao(PhanLoaiAnh, "{\"loai\":{\"labelIds\":[\"" + lop + "\"]}}", null);
        }
    }
}
