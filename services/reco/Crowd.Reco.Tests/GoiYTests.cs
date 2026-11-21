using System;
using System.Collections.Generic;
using System.Linq;
using Crowd.Reco.Api.Helpers;
using Crowd.Reco.Api.Services;
using Crowd.Reco.Domain;
using Crowd.Reco.Infrastructure.Source;

namespace Crowd.Reco.Tests
{
    /// <summary>Du lieu dung chung cho test bo goi y.</summary>
    internal static class Mau
    {
        public static readonly DateTimeOffset BayGio = new DateTimeOffset(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);

        public static DuAnGoiY DuAn(
            Guid? id = null,
            Guid? owner = null,
            string modality = "image",
            long thuong = 30000,
            int suat = 5,
            int daNop = 0,
            int soCau = 10,
            long daThay = 500,
            bool rieng = false,
            int? capToiThieu = null,
            bool nhayCam = false,
            bool dangNhan = true,
            DateTimeOffset? moLuc = null,
            string[]? congCu = null)
        {
            return new DuAnGoiY
            {
                Id = id ?? Guid.NewGuid(),
                OwnerId = owner ?? Guid.NewGuid(),
                Name = "Du an",
                Description = "Mo ta",
                Modality = modality,
                Domains = new string[] { "y-te" },
                Languages = new string[] { "vi" },
                ToolKinds = congCu ?? new string[] { "classification" },
                RewardVnd = thuong,
                Slots = suat,
                QuestionCount = soCau,
                Deadline = BayGio.AddDays(10),
                PublishedAt = BayGio.AddDays(-1),
                IsPrivate = rieng,
                MinLevel = capToiThieu,
                MinReputation = null,
                RequireEntranceTest = false,
                RequireVerifiedIdentity = false,
                SensitiveContent = nhayCam,
                NdaRequired = false,
                Accepting = dangNhan,
                SubmittedCount = daNop,
                InProgressCount = 0,
                StartedCount = 50,
                MedianSecondsPerQuestion = 30,
                Tools = Array.Empty<CongCuGoiY>(),
                SeenCount = daThay,
                OpenedAt = moLuc ?? BayGio.AddDays(-1),
            };
        }

        public static LabelerGoiY Nguoi(SoThich? st = null, int cap = 1, bool duTuoi = false, IEnumerable<Guid>? loaiTru = null,
            IEnumerable<Guid>? thanhVien = null, IEnumerable<Guid>? dnChan = null, Guid? id = null)
        {
            return new LabelerGoiY
            {
                UserId = id ?? Guid.NewGuid(),
                Level = cap,
                Reputation = 80,
                IdentityVerified = duTuoi,
                Adult = duTuoi,
                SoThich = st ?? new SoThich(),
                LoaiTru = new HashSet<Guid>(loaiTru ?? Array.Empty<Guid>()),
                ThanhVien = new HashSet<Guid>(thanhVien ?? Array.Empty<Guid>()),
                DoanhNghiepChan = new HashSet<Guid>(dnChan ?? Array.Empty<Guid>()),
            };
        }

        public static readonly ThongSoGoiY ThongSo = new ThongSoGoiY
        {
            ChuKyQuenNgay = 90,
            NguongDuAnMoi = 200,
            KhamPhaMoi = 10,
            ToiDaMotDoanhNghiep = 2,
            ChenMoiMoiTrang = 2,
            KichThuocDanhSach = 200,
        };

        public static DuAnDaCham Cham(DuAnGoiY p, LabelerGoiY nguoi)
        {
            return ChamDiem.Cham(p, nguoi, MoHinhG1.Instance, ThongKeCho.Tinh(new DuAnGoiY[] { p }), ThongSo, BayGio);
        }
    }

    public sealed class BoDemSoThichTests
    {
        private static readonly string[] YTe = new string[] { "y-te" };
        private static readonly string[] Anh = new string[] { "image" };
        private static readonly string[] Bbox = new string[] { "bbox" };

        [Fact]
        public void Quen_dan_sau_mot_chu_ky_con_mot_nua()
        {
            BoDem d = new BoDem(4, 2, Mau.BayGio);
            BoDem sau = d.QuenToi(Mau.BayGio.AddDays(90), 90);
            Assert.Equal(2, sau.A, 6);
            Assert.Equal(1, sau.B, 6);
        }

        [Fact]
        public void Quen_roi_moi_cong()
        {
            BoDem d = new BoDem(4, 0, Mau.BayGio).Cong(1, 0, Mau.BayGio.AddDays(90), 90);
            Assert.Equal(3, d.A, 6);
        }

        [Fact]
        public void The_chua_co_tin_hieu_la_nua_nua_the_chon_luc_dang_ky_la_ba_phan_tu()
        {
            SoThich st = new SoThich();
            foreach (ThayDoiBoDem x in HanhVi.TuDangKy(YTe, null, Anh))
            {
                st.Dat(x.Loai, x.Ma, new BoDem(x.WA, x.WB, Mau.BayGio));
            }

            Assert.Equal(0.5, st.MucHop(LoaiThe.LinhVuc, "tai-chinh", Mau.BayGio, 90), 6);
            Assert.Equal(0.75, st.MucHop(LoaiThe.LinhVuc, "y-te", Mau.BayGio, 90), 6);
            Assert.Equal(0.75, st.MucHop(LoaiThe.LoaiDuLieu, "image", Mau.BayGio, 90), 6);
        }

        [Fact]
        public void Ma_hoa_redis_doc_lai_duoc()
        {
            BoDem d = new BoDem(1.25, 0.05, Mau.BayGio);
            BoDem? lai = BoDem.GiaiMa(d.MaHoa());
            Assert.NotNull(lai);
            Assert.Equal(d.A, lai!.A, 9);
            Assert.Equal(d.B, lai.B, 9);
            Assert.Equal(d.CapNhatLuc.ToUnixTimeSeconds(), lai.CapNhatLuc.ToUnixTimeSeconds());
            Assert.Null(BoDem.GiaiMa("rac"));
        }

        [Fact]
        public void Bai_dat_cong_ca_so_thich_lan_bo_dem_dat_theo_cong_cu()
        {
            DuAnGoiY p = Mau.DuAn(congCu: Bbox);
            List<ThayDoiBoDem> dat = HanhVi.ThayDoi(HanhVi.Dat, p);
            Assert.Contains(dat, x => x.Loai == LoaiThe.CongCu && x.Ma == "bbox" && x.WA == 1);
            Assert.Contains(dat, x => x.Loai == LoaiThe.DatTheoCongCu && x.Ma == "bbox" && x.WA == 1 && x.WB == 0);

            List<ThayDoiBoDem> truot = HanhVi.ThayDoi(HanhVi.Truot, p);
            Assert.Contains(truot, x => x.Loai == LoaiThe.DatTheoCongCu && x.WB == 1 && x.WA == 0);
            Assert.Empty(HanhVi.ThayDoi(HanhVi.MoThe, p));
        }
    }

    public sealed class DieuKienGoiYTests
    {
        [Fact]
        public void Loc_dung_luat_cho_du_an()
        {
            Guid chuChan = Guid.NewGuid();
            DuAnGoiY rieng = Mau.DuAn(rieng: true);
            DuAnGoiY daNop = Mau.DuAn();
            LabelerGoiY nguoi = Mau.Nguoi(loaiTru: new Guid[] { daNop.Id }, dnChan: new Guid[] { chuChan });

            Assert.True(DieuKienGoiY.LamDuoc(Mau.DuAn(), nguoi));
            Assert.False(DieuKienGoiY.LamDuoc(Mau.DuAn(dangNhan: false), nguoi));
            Assert.False(DieuKienGoiY.LamDuoc(rieng, nguoi));
            Assert.True(DieuKienGoiY.LamDuoc(rieng, Mau.Nguoi(thanhVien: new Guid[] { rieng.Id })));
            Assert.False(DieuKienGoiY.LamDuoc(daNop, nguoi));
            Assert.False(DieuKienGoiY.LamDuoc(Mau.DuAn(owner: chuChan), nguoi));
            Assert.False(DieuKienGoiY.LamDuoc(Mau.DuAn(capToiThieu: 3), nguoi));
            Assert.False(DieuKienGoiY.LamDuoc(Mau.DuAn(nhayCam: true), nguoi));
            Assert.True(DieuKienGoiY.LamDuoc(Mau.DuAn(nhayCam: true), Mau.Nguoi(duTuoi: true)));
        }

        [Fact]
        public void Khong_goi_y_du_an_cua_chinh_minh()
        {
            Guid toi = Guid.NewGuid();
            Assert.False(DieuKienGoiY.LamDuoc(Mau.DuAn(owner: toi), Mau.Nguoi(id: toi)));
        }
    }

    public sealed class ChamDiemTests
    {
        [Fact]
        public void Du_an_dong_doi_thu_tut_diem_so_voi_du_an_vang()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            DuAnDaCham vang = Mau.Cham(Mau.DuAn(suat: 5, daNop: 0), nguoi);
            DuAnDaCham dong = Mau.Cham(Mau.DuAn(suat: 5, daNop: 100), nguoi);
            Assert.True(vang.Diem > dong.Diem);
            Assert.True(vang.DacTrung[DacTrung.PSlot] > dong.DacTrung[DacTrung.PSlot]);
        }

        [Fact]
        public void Thuong_cao_hon_moi_phut_thi_diem_cao_hon()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            Assert.True(Mau.Cham(Mau.DuAn(thuong: 60000), nguoi).Diem > Mau.Cham(Mau.DuAn(thuong: 20000), nguoi).Diem);
            Assert.True(Mau.Cham(Mau.DuAn(soCau: 5), nguoi).Diem > Mau.Cham(Mau.DuAn(soCau: 50), nguoi).Diem);
        }

        [Fact]
        public void Du_an_moi_duoc_uu_tien_va_co_ly_do_moi()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            DuAnDaCham moi = Mau.Cham(Mau.DuAn(daThay: 10), nguoi);
            DuAnDaCham cu = Mau.Cham(Mau.DuAn(daThay: 5000), nguoi);
            Assert.Contains(ChamDiem.LyDoMoi, moi.LyDo);
            Assert.DoesNotContain(ChamDiem.LyDoMoi, cu.LyDo);
            Assert.True(moi.Diem > cu.Diem);
        }

        [Fact]
        public void So_thich_hop_hon_thi_diem_cao_hon()
        {
            SoThich thich = new SoThich();
            thich.Dat(LoaiThe.LoaiDuLieu, "audio", new BoDem(10, 0, Mau.BayGio));
            LabelerGoiY nguoi = Mau.Nguoi(thich);
            Assert.True(Mau.Cham(Mau.DuAn(modality: "audio"), nguoi).Diem > Mau.Cham(Mau.DuAn(modality: "image"), nguoi).Diem);
        }
    }

    public sealed class XepLaiTests
    {
        private static readonly string[] PhienAm = new string[] { "transcription" };

        [Fact]
        public void Moi_khung_muoi_the_toi_da_hai_du_an_cung_doanh_nghiep()
        {
            Guid dn = Guid.NewGuid();
            LabelerGoiY nguoi = Mau.Nguoi();
            List<DuAnDaCham> ds = new List<DuAnDaCham>();
            for (int i = 0; i < 8; i++)
            {
                ds.Add(Mau.Cham(Mau.DuAn(owner: dn, thuong: 90000 + i), nguoi));
            }

            for (int i = 0; i < 20; i++)
            {
                ds.Add(Mau.Cham(Mau.DuAn(thuong: 10000 + i, modality: i % 2 == 0 ? "text" : "audio"), nguoi));
            }

            ThongSoGoiY khongKhamPha = new ThongSoGoiY { KhamPhaMoi = 0, ToiDaMotDoanhNghiep = 2, KichThuocDanhSach = 200 };
            List<DuAnDaCham> xep = XepLai.Xep(ds, nguoi, khongKhamPha, new Random(1));
            Assert.Equal(28, xep.Count);
            Assert.True(xep.Take(10).Count(x => x.DuAn.OwnerId == dn) <= 2);
            Assert.True(xep.Skip(10).Take(10).Count(x => x.DuAn.OwnerId == dn) <= 2);
        }

        [Fact]
        public void Khong_de_ba_the_lien_tiep_cung_loai_du_lieu_khi_con_loai_khac()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            List<DuAnDaCham> ds = new List<DuAnDaCham>();
            for (int i = 0; i < 6; i++)
            {
                ds.Add(Mau.Cham(Mau.DuAn(modality: "image", thuong: 80000 + i), nguoi));
            }

            ds.Add(Mau.Cham(Mau.DuAn(modality: "text", thuong: 1000), nguoi));
            ds.Add(Mau.Cham(Mau.DuAn(modality: "audio", thuong: 1000), nguoi));
            ThongSoGoiY ts = new ThongSoGoiY { KhamPhaMoi = 0, ToiDaMotDoanhNghiep = 10, KichThuocDanhSach = 200 };
            List<DuAnDaCham> xep = XepLai.Xep(ds, nguoi, ts, new Random(1));
            Assert.NotEqual("image", xep[2].DuAn.Modality);
        }

        [Fact]
        public void O_thu_muoi_la_the_kham_pha_du_an_moi_hoac_chua_tung_lam()
        {
            SoThich st = new SoThich();
            st.Dat(LoaiThe.LoaiDuLieu, "image", new BoDem(5, 0, Mau.BayGio));
            st.Dat(LoaiThe.LoaiDuLieu, "text", new BoDem(5, 0, Mau.BayGio));
            st.Dat(LoaiThe.CongCu, "classification", new BoDem(5, 0, Mau.BayGio));
            LabelerGoiY nguoi = Mau.Nguoi(st);
            List<DuAnDaCham> ds = new List<DuAnDaCham>();
            for (int i = 0; i < 15; i++)
            {
                // Xen hai loai labeler DA tung lam — luat "khong 3 the cung loai" khong keo the la len som.
                ds.Add(Mau.Cham(Mau.DuAn(thuong: 50000 + i, daThay: 5000, modality: i % 2 == 0 ? "image" : "text"), nguoi));
            }

            DuAnGoiY la = Mau.DuAn(modality: "audio", congCu: PhienAm, thuong: 1000, daThay: 5000);
            ds.Add(Mau.Cham(la, nguoi));
            ThongSoGoiY ts = new ThongSoGoiY { KhamPhaMoi = 10, ToiDaMotDoanhNghiep = 10, KichThuocDanhSach = 200, NguongDuAnMoi = 200 };
            List<DuAnDaCham> xep = XepLai.Xep(ds, nguoi, ts, new Random(7));
            Assert.Equal(la.Id, xep[9].DuAn.Id);
            Assert.True(xep[9].KhamPha);
            Assert.Contains(ChamDiem.LyDoKhamPha, xep[9].LyDo);
        }
    }

    public sealed class GhepTrangTests
    {
        private static DanhSachLuot TaoDanhSach(List<DuAnGoiY> duAn, LabelerGoiY nguoi)
        {
            List<DuAnDaCham> cham = duAn.Select(p => Mau.Cham(p, nguoi)).OrderByDescending(x => x.Diem).ToList();
            return DanhSachLuot.Tu(cham, Mau.BayGio);
        }

        [Fact]
        public void Danh_sach_ma_hoa_nhi_phan_doc_lai_duoc()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            DanhSachLuot ds = TaoDanhSach(Enumerable.Range(0, 5).Select(i => Mau.DuAn()).ToList(), nguoi);
            DanhSachLuot? lai = DanhSachLuot.GiaiMa(ds.MaHoa());
            Assert.NotNull(lai);
            Assert.Equal(ds.Muc.Select(x => x.ProjectId), lai!.Muc.Select(x => x.ProjectId));
            Assert.Equal(ds.TaoLuc.ToUnixTimeMilliseconds(), lai.TaoLuc.ToUnixTimeMilliseconds());
            Assert.Equal(8 + 5 * 21, ds.MaHoa().Length);
        }

        [Fact]
        public void Hai_trang_lien_tiep_khong_lap_the_nao_va_khong_mat_the_nao()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            List<DuAnGoiY> duAn = Enumerable.Range(0, 25).Select(i => Mau.DuAn(thuong: 10000 + i * 1000)).ToList();
            Dictionary<Guid, DuAnGoiY> theoId = duAn.ToDictionary(x => x.Id);
            DanhSachLuot ds = TaoDanhSach(duAn, nguoi);
            HashSet<Guid> daTra = new HashSet<Guid>();

            KetQuaTrang t1 = GhepTrang.Ghep(ds, 0, 10, daTra, id => theoId[id], p => true, new List<DuAnDaCham>(), Mau.ThongSo, 0);
            foreach (TheTrongTrang x in t1.The)
            {
                daTra.Add(x.DuAn.Id);
            }

            KetQuaTrang t2 = GhepTrang.Ghep(ds, t1.ConTro, 10, daTra, id => theoId[id], p => true, new List<DuAnDaCham>(), Mau.ThongSo, 10);
            foreach (TheTrongTrang x in t2.The)
            {
                daTra.Add(x.DuAn.Id);
            }

            KetQuaTrang t3 = GhepTrang.Ghep(ds, t2.ConTro, 10, daTra, id => theoId[id], p => true, new List<DuAnDaCham>(), Mau.ThongSo, 20);
            List<Guid> tatCa = t1.The.Concat(t2.The).Concat(t3.The).Select(x => x.DuAn.Id).ToList();
            Assert.Equal(25, tatCa.Count);
            Assert.Equal(25, tatCa.Distinct().Count());
            Assert.Equal(ds.Muc.Select(x => x.ProjectId), tatCa);
        }

        [Fact]
        public void Du_an_moi_diem_cao_len_dau_trang_ke_va_the_bi_day_ra_hien_o_trang_sau()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            List<DuAnGoiY> duAn = Enumerable.Range(0, 30).Select(i => Mau.DuAn(thuong: 10000 + i * 1000)).ToList();
            Dictionary<Guid, DuAnGoiY> theoId = duAn.ToDictionary(x => x.Id);
            DanhSachLuot ds = TaoDanhSach(duAn, nguoi);
            HashSet<Guid> daTra = new HashSet<Guid>();

            KetQuaTrang t1 = GhepTrang.Ghep(ds, 0, 10, daTra, id => theoId[id], p => true, new List<DuAnDaCham>(), Mau.ThongSo, 0);
            foreach (TheTrongTrang x in t1.The)
            {
                daTra.Add(x.DuAn.Id);
            }

            // M mo SAU luc tao danh sach, thuong cao nhat (diem nhu the so 1).
            DuAnGoiY m = Mau.DuAn(thuong: 500000, moLuc: Mau.BayGio.AddMinutes(3));
            theoId[m.Id] = m;
            KetQuaTrang t2 = GhepTrang.Ghep(ds, t1.ConTro, 10, daTra, id => theoId[id], p => true,
                new List<DuAnDaCham> { Mau.Cham(m, nguoi) }, Mau.ThongSo, 10);
            Assert.Equal(m.Id, t2.The[0].DuAn.Id);
            Assert.True(t2.The[0].ChenMoi);
            Assert.Equal(10, t2.The.Count);
            Assert.DoesNotContain(t2.The, x => t1.The.Any(y => y.DuAn.Id == x.DuAn.Id));

            // The thu 20 cua danh sach bi day ra khoi trang 2 → la the DAU cua phan danh sach o trang 3.
            foreach (TheTrongTrang x in t2.The)
            {
                daTra.Add(x.DuAn.Id);
            }

            KetQuaTrang t3 = GhepTrang.Ghep(ds, t2.ConTro, 10, daTra, id => theoId[id], p => true, new List<DuAnDaCham>(), Mau.ThongSo, 20);
            Assert.Equal(ds.Muc[19].ProjectId, t3.The[0].DuAn.Id);
            List<Guid> tatCa = t1.The.Concat(t2.The).Concat(t3.The).Select(x => x.DuAn.Id).ToList();
            Assert.Equal(tatCa.Count, tatCa.Distinct().Count());
            Assert.Equal(30, tatCa.Count);
        }

        [Fact]
        public void Du_an_vua_dong_chua_tra_ra_thi_bi_bo_va_lay_the_sau_bu()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            List<DuAnGoiY> duAn = Enumerable.Range(0, 12).Select(i => Mau.DuAn(thuong: 10000 + i * 1000)).ToList();
            Dictionary<Guid, DuAnGoiY> theoId = duAn.ToDictionary(x => x.Id);
            DanhSachLuot ds = TaoDanhSach(duAn, nguoi);
            Guid dong = ds.Muc[3].ProjectId;

            KetQuaTrang t1 = GhepTrang.Ghep(ds, 0, 10, new HashSet<Guid>(), id => id == dong ? null : theoId[id], p => true,
                new List<DuAnDaCham>(), Mau.ThongSo, 0);
            Assert.Equal(10, t1.The.Count);
            Assert.DoesNotContain(t1.The, x => x.DuAn.Id == dong);
            Assert.Equal(ds.Muc[10].ProjectId, t1.The[9].DuAn.Id);
        }

        [Fact]
        public void Het_danh_sach_thi_bao_de_xep_lai()
        {
            LabelerGoiY nguoi = Mau.Nguoi();
            List<DuAnGoiY> duAn = Enumerable.Range(0, 4).Select(i => Mau.DuAn()).ToList();
            Dictionary<Guid, DuAnGoiY> theoId = duAn.ToDictionary(x => x.Id);
            KetQuaTrang t = GhepTrang.Ghep(TaoDanhSach(duAn, nguoi), 0, 10, new HashSet<Guid>(), id => theoId[id], p => true,
                new List<DuAnDaCham>(), Mau.ThongSo, 0);
            Assert.Equal(4, t.The.Count);
            Assert.True(t.HetDanhSach);
        }
    }

    public sealed class MoHinhHocMayTests
    {
        private static string DanhSachTen()
        {
            return "[" + string.Join(",", DacTrung.Ten.Select(x => "\"" + x + "\"")) + "]";
        }

        [Fact]
        public void Doc_logistic_va_tinh_sigmoid()
        {
            string w = "[" + string.Join(",", DacTrung.Ten.Select((x, i) => i == DacTrung.Match ? "2" : "0")) + "]";
            string json = "{\"version\":\"lr-1\",\"kind\":\"logistic\",\"features\":" + DanhSachTen()
                          + ",\"start\":{\"intercept\":-1,\"coef\":" + w + "},\"pass\":{\"intercept\":0,\"coef\":" + w + "}}";
            MoHinhHocMay m = MoHinhHocMay.Doc(json);
            double[] x = new double[DacTrung.SoChieu];
            x[DacTrung.Match] = 0.5;
            Assert.Equal("lr-1", m.PhienBan);
            Assert.Equal(0.5, m.PBatDau(x), 6);
            Assert.Equal(MoHinhHocMay.Sigmoid(1), m.PDat(x), 6);
        }

        [Fact]
        public void Doc_cay_lightgbm_va_duyet_dung_nhanh()
        {
            // Mot cay: log_reward <= 3 → -1, nguoc lai → +1. Dac trung thieu → default_left.
            string cay = "{\"split_feature\":0,\"threshold\":3,\"decision_type\":\"<=\",\"default_left\":true,\"missing_type\":\"NaN\","
                         + "\"left_child\":{\"leaf_value\":-1},\"right_child\":{\"leaf_value\":1}}";
            string json = "{\"version\":\"gbm-1\",\"kind\":\"lightgbm\",\"features\":[\"log_reward\"],"
                          + "\"start\":{\"trees\":[" + cay + "]},\"pass\":{\"trees\":[" + cay + "," + cay + "]}}";
            MoHinhHocMay m = MoHinhHocMay.Doc(json);
            double[] thap = new double[DacTrung.SoChieu];
            thap[DacTrung.LogReward] = 2;
            double[] cao = new double[DacTrung.SoChieu];
            cao[DacTrung.LogReward] = 5;
            Assert.Equal(MoHinhHocMay.Sigmoid(-1), m.PBatDau(thap), 6);
            Assert.Equal(MoHinhHocMay.Sigmoid(1), m.PBatDau(cao), 6);
            Assert.Equal(MoHinhHocMay.Sigmoid(2), m.PDat(cao), 6);
        }

        [Fact]
        public void File_mo_hinh_sai_thi_bao_loi_dinh_dang()
        {
            Assert.Throws<FormatException>(() => MoHinhHocMay.Doc("{\"kind\":\"logistic\"}"));
            Assert.Throws<FormatException>(() => MoHinhHocMay.Doc("{\"version\":\"x\",\"kind\":\"mlp\",\"features\":[],\"start\":{},\"pass\":{}}"));
        }
    }

    public sealed class NhomAbVaConTroTests
    {
        [Fact]
        public void Nhom_ab_co_dinh_theo_nguoi_va_chia_gan_dung_ti_le()
        {
            Guid u = Guid.NewGuid();
            Assert.Equal(NhomAb.Chon(u, 50), NhomAb.Chon(u, 50));
            Assert.Equal(NhomAb.DangChay, NhomAb.Chon(u, 0));
            Assert.Equal(NhomAb.ThuNghiem, NhomAb.Chon(u, 100));

            int thu = Enumerable.Range(0, 4000).Count(i => NhomAb.Chon(Guid.NewGuid(), 30) == NhomAb.ThuNghiem);
            Assert.InRange(thu, 1000, 1400);
        }

        [Fact]
        public void Con_tro_viet_doc_lai_duoc_va_tu_choi_rac()
        {
            ConTro c = new ConTro { Phien = Guid.NewGuid(), ViTri = 37, ViTriThe = 30 };
            ConTro? lai = ConTro.Doc(c.Viet());
            Assert.NotNull(lai);
            Assert.Equal(c.Phien, lai!.Phien);
            Assert.Equal(37, lai.ViTri);
            Assert.Equal(30, lai.ViTriThe);
            Assert.Null(ConTro.Doc("@@@"));
            Assert.Null(ConTro.Doc(null));
        }

        [Fact]
        public void Con_tro_moi_nhat_doc_lai_dung_va_khong_lan_voi_con_tro_goi_y()
        {
            ConTroMoiNhat c = new ConTroMoiNhat { Phien = Guid.NewGuid(), PublishedAt = Mau.BayGio, Id = Guid.NewGuid(), ViTriThe = 20 };
            ConTroMoiNhat? doc = ConTroMoiNhat.Doc(c.Viet());
            Assert.NotNull(doc);
            Assert.Equal(c.Phien, doc!.Phien);
            Assert.Equal(c.PublishedAt, doc.PublishedAt);
            Assert.Equal(c.Id, doc.Id);
            Assert.Equal(20, doc.ViTriThe);

            string goiY = new ConTro { Phien = Guid.NewGuid(), ViTri = 3, ViTriThe = 10 }.Viet();
            Assert.Null(ConTroMoiNhat.Doc(goiY));
            Assert.Null(ConTro.Doc(c.Viet()));
            Assert.True(FeedService.LaPhienMoiNhat("moi_abc"));
            Assert.False(FeedService.LaPhienMoiNhat("abc"));
        }

        [Fact]
        public void Doc_cong_cu_tu_tap_nhan_bo_qua_muc_hong()
        {
            const string Json = "[{\"name\":\"loai\",\"kind\":\"classification\",\"question\":\" Anh co meo? \",\"classes\":[\"co\",\"khong\"]},"
                + "{\"name\":\"khung\",\"kind\":\"bbox\"},{\"kind\":\"span\"},7]";
            IReadOnlyList<CongCuGoiY> ds = NguonDuAn.DocCongCu(Json);
            Assert.Equal(2, ds.Count);
            Assert.Equal("Anh co meo?", ds[0].Question);
            Assert.Equal(2, ds[0].Classes.Count);
            Assert.Equal("khong", ds[0].Classes[1]);
            Assert.Null(ds[1].Question);
            Assert.Empty(ds[1].Classes);
            Assert.Empty(NguonDuAn.DocCongCu("khong phai json"));
        }

        [Fact]
        public void Phat_luot_qua_mem_va_hoi_phuc_sau_ba_ngay()
        {
            LuotQua mot = new LuotQua { SoLan = 1, LanCuoi = Mau.BayGio };
            LuotQua hai = new LuotQua { SoLan = 3, LanCuoi = Mau.BayGio };
            Assert.Equal(1, PhatLuotQua.HeSo(null, Mau.BayGio, 3));
            Assert.Equal(0.5, PhatLuotQua.HeSo(mot, Mau.BayGio, 3), 6);
            Assert.Equal(0.25, PhatLuotQua.HeSo(hai, Mau.BayGio, 3), 6);
            // Hoi phuc tuyen tinh: sau 1,5 ngay giua chung; sau 3 ngay het phat.
            Assert.Equal(0.75, PhatLuotQua.HeSo(mot, Mau.BayGio.AddDays(1.5), 3), 6);
            Assert.Equal(1, PhatLuotQua.HeSo(hai, Mau.BayGio.AddDays(4), 3), 6);
        }

        [Fact]
        public void Du_an_da_luot_qua_bi_day_xuong_nhung_khong_bi_an()
        {
            DuAnGoiY p = Mau.DuAn();
            LabelerGoiY goc = Mau.Nguoi();
            LabelerGoiY daLuot = new LabelerGoiY
            {
                UserId = goc.UserId,
                Level = goc.Level,
                Reputation = goc.Reputation,
                IdentityVerified = goc.IdentityVerified,
                Adult = goc.Adult,
                SoThich = goc.SoThich,
                LoaiTru = goc.LoaiTru,
                ThanhVien = goc.ThanhVien,
                DoanhNghiepChan = goc.DoanhNghiepChan,
                DaLuotQua = new Dictionary<Guid, LuotQua> { { p.Id, new LuotQua { SoLan = 1, LanCuoi = Mau.BayGio } } },
            };
            double diemGoc = Mau.Cham(p, goc).Diem;
            Assert.Equal(diemGoc * 0.5, Mau.Cham(p, daLuot).Diem, 6);
            Assert.True(DieuKienGoiY.LamDuoc(p, daLuot));
        }

        [Fact]
        public void Thich_cong_a_bo_thich_cong_b_va_chi_feed_goi_y_tinh_luot_qua()
        {
            DuAnGoiY p = Mau.DuAn();
            Assert.All(HanhVi.ThayDoi(HanhVi.Thich, p), x => Assert.Equal(0.3, x.WA, 6));
            Assert.All(HanhVi.ThayDoi(HanhVi.BoThich, p), x => Assert.Equal(0.3, x.WB, 6));
            Assert.True(FeedService.TinhLuotQua("0193abc"));
            Assert.False(FeedService.TinhLuotQua("moi_0193abc"));
            Assert.False(FeedService.TinhLuotQua("thich_0193abc"));
            Assert.False(FeedService.TinhLuotQua(null));
            Assert.Contains(HanhVi.Thich, HanhVi.XoaLuotQua);
        }
    }
}
