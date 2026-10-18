using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Annotation.Tests
{
    /// <summary>Vong doi nhan trong du an lam trung tam (docs 3.18): nop ca bai → cham → chot.</summary>
    public sealed class VongDoiNhanTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Labeler = Guid.NewGuid();
        private static readonly Guid Admin = Guid.NewGuid();
        private static readonly string[] NopRoiTruotRoiKhieuNai = new string[] { "submitted", "excluded", "appeal_accepted" };

        private static LabelAnnotation NhanMoi()
        {
            return LabelAnnotation.TaoTuBaiNop(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Labeler,
                LabelPayload.Tao(ResultAggregatorTests.TapNhan, "{\"loai\":{\"labelIds\":[\"cho\"]}}", null),
                Luc);
        }

        [Fact]
        public void Nhan_moi_nop_cho_cham_khong_vao_ket_qua()
        {
            LabelAnnotation a = NhanMoi();

            Assert.Equal(AnnotationStatus.Submitted, a.Status);
            Assert.Equal(LabelSource.Professional, a.Source);
            Assert.Null(a.DecidedAt);
        }

        [Fact]
        public void Bai_dat_thi_nhan_vao_ket_qua_va_chi_chot_mot_lan()
        {
            LabelAnnotation a = NhanMoi();

            Assert.True(a.ChotTheoKetQua(true, Luc.AddDays(1)));
            Assert.Equal(AnnotationStatus.Accepted, a.Status);
            Assert.Equal(Luc.AddDays(1), a.DecidedAt);

            // project.graded giao lai (hoac ket qua khac toi tre) khong doi nhan da chot.
            Assert.False(a.ChotTheoKetQua(false, Luc.AddDays(2)));
            Assert.Equal(AnnotationStatus.Accepted, a.Status);
        }

        [Fact]
        public void Bai_truot_bi_loai_khieu_nai_duoc_chap_nhan_thi_vao_lai_ket_qua()
        {
            LabelAnnotation a = NhanMoi();
            a.ChotTheoKetQua(false, Luc.AddDays(1));
            Assert.Equal(AnnotationStatus.Excluded, a.Status);

            Assert.True(a.ChapNhanSauKhieuNai(Admin, "Dong y", Luc.AddDays(2)));
            Assert.Equal(AnnotationStatus.Accepted, a.Status);
            Assert.Equal(NopRoiTruotRoiKhieuNai, a.History.Select(h => h.Action).ToArray());

            // Chap nhan giao lai: khong ghi them nhat ky.
            Assert.False(a.ChapNhanSauKhieuNai(Admin, "Dong y", Luc.AddDays(3)));
            Assert.Equal(3, a.History.Count);
        }

        [Fact]
        public void Khieu_nai_chi_ap_dung_cho_nhan_bi_loai()
        {
            LabelAnnotation a = NhanMoi();

            Assert.False(a.ChapNhanSauKhieuNai(Admin, null, Luc.AddDays(1)));
            Assert.Equal(AnnotationStatus.Submitted, a.Status);
        }

        [Fact]
        public void Nhan_cong_link_vao_ket_qua_ngay_khong_co_labeler()
        {
            LabelAnnotation a = LabelAnnotation.TaoTuCongLink(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                LabelPayload.Tao(ResultAggregatorTests.TapNhan, "{\"loai\":{\"labelIds\":[\"cho\"]}}", null),
                Luc);

            Assert.Equal(AnnotationStatus.Accepted, a.Status);
            Assert.Equal(LabelSource.LinkGateway, a.Source);
            Assert.Null(a.LabelerId);
            Assert.False(a.ChotTheoKetQua(false, Luc.AddDays(1)));
        }

        [Fact]
        public void Nhan_rong_bi_tu_choi()
        {
            InvalidValueException ex = Assert.Throws<InvalidValueException>(() =>
                LabelAnnotation.TaoTuBaiNop(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Labeler, null!, Luc));
            Assert.Equal("nhan_rong", ex.Code);
        }
    }

    public sealed class ResultAggregatorTests
    {
        /// <summary>Anh: mot cong cu phan loai nhieu lop + mot cong cu khung.</summary>
        internal static readonly LabelSchema TapNhan = LabelSchema.Doc(
            "{\"modality\":\"image\",\"tools\":["
            + "{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"cho\",\"meo\",\"ga\"],\"allowMultiple\":true},"
            + "{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"mat\"],\"required\":false}]}");

        private static readonly Guid Mau = Guid.NewGuid();
        private static readonly string[] ChiCho = new string[] { "cho" };
        private static readonly SampleMetadata Anh = new SampleMetadata { Width = 100, Height = 100 };

        private static LabelAnnotation Nhan(string duLieu)
        {
            LabelAnnotation a = LabelAnnotation.TaoTuBaiNop(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Mau,
                Guid.NewGuid(),
                LabelPayload.Tao(TapNhan, duLieu, Anh),
                DateTimeOffset.UtcNow);
            a.ChotTheoKetQua(true, DateTimeOffset.UtcNow);
            return a;
        }

        private static LabelAnnotation Lop(params string[] lop)
        {
            return Nhan("{\"loai\":{\"labelIds\":[\"" + string.Join("\",\"", lop) + "\"]}}");
        }

        private static string[] Chot(SampleResult r)
        {
            JsonNode? final = r.Tools["loai"]!["final"];
            if (final == null)
            {
                return Array.Empty<string>();
            }

            return ((JsonArray)final["labelIds"]!).Select(n => n!.GetValue<string>()).ToArray();
        }

        [Fact]
        public void Da_so_tuyet_doi_thang()
        {
            SampleResult r = ResultAggregator.Chot(TapNhan, new[] { Lop("cho"), Lop("cho"), Lop("meo") }).Single();

            Assert.Equal(ChiCho, Chot(r));
            Assert.Equal(2, r.Tools["loai"]!["votes"]!["cho"]!.GetValue<int>());
            Assert.False(r.Disputed);
        }

        [Fact]
        public void Hoa_phieu_la_tranh_chap_FB_22()
        {
            SampleResult r = ResultAggregator.Chot(TapNhan, new[] { Lop("cho"), Lop("meo") }).Single();

            Assert.True(r.Disputed);
            Assert.Empty(Chot(r));
        }

        [Fact]
        public void Multi_label_xet_tung_lop_doc_lap()
        {
            SampleResult r = ResultAggregator.Chot(TapNhan, new[] { Lop("cho", "meo"), Lop("cho"), Lop("cho", "ga") }).Single();

            Assert.Equal(ChiCho, Chot(r));
        }

        [Fact]
        public void Khung_duoc_gop_theo_cum_va_van_dem_phan_bo()
        {
            LabelAnnotation a = Nhan(
                "{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"mat\",\"x\":1,\"y\":1,\"w\":10,\"h\":10},"
                + "{\"labelId\":\"mat\",\"x\":50,\"y\":50,\"w\":10,\"h\":10}]}");
            LabelAnnotation b = Lop("cho");   // khong lam cong cu khung (tuy chon) — khong tinh vao so nguoi cua cong cu do
            LabelAnnotation c = Nhan(
                "{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"mat\",\"x\":2,\"y\":1,\"w\":10,\"h\":10},"
                + "{\"labelId\":\"mat\",\"x\":50,\"y\":51,\"w\":10,\"h\":10},{\"labelId\":\"mat\",\"x\":80,\"y\":80,\"w\":5,\"h\":5}]}");

            IReadOnlyList<SampleResult> kq = ResultAggregator.Chot(TapNhan, new[] { a, b, c });
            SampleResult r = kq.Single();

            // Hai khung ca hai nguoi cung ve duoc giu (trung vi toa do); khung thu ba chi mot nguoi ve bi bo.
            Assert.Equal("cluster", r.Tools["vat"]!["method"]!.GetValue<string>());
            JsonArray khung = (JsonArray)r.Tools["vat"]!["final"]!;
            Assert.Equal(2, khung.Count);
            Assert.Equal(1.5, khung[0]!["x"]!.GetValue<double>());
            Assert.Equal(50.5, khung[1]!["y"]!.GetValue<double>());
            Assert.Equal("[2,2]", r.Tools["vat"]!["supports"]!.ToJsonString());
            // Nguoi ve thua mot khung: F1 = 2 x 2 / (3 + 2) = 0.8 — van tinh la khop.
            Assert.Equal("[true,true]", r.Tools["vat"]!["agrees"]!.ToJsonString());
            Assert.False(r.Disputed);
            Assert.Equal(3, r.Approved.Count);

            SortedDictionary<string, SortedDictionary<string, int>> phanBo = ResultAggregator.PhanBo(TapNhan, kq);
            Assert.Equal(1, phanBo["loai"]["cho"]);
            Assert.Equal(5, phanBo["vat"]["mat"]);
        }

        [Fact]
        public void Hai_nguoi_ve_khung_o_hai_cho_khac_nhau_la_tranh_chap()
        {
            LabelAnnotation a = Nhan("{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"mat\",\"x\":1,\"y\":1,\"w\":10,\"h\":10}]}");
            LabelAnnotation b = Nhan("{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"mat\",\"x\":60,\"y\":60,\"w\":10,\"h\":10}]}");

            SampleResult r = ResultAggregator.Chot(TapNhan, new[] { a, b }).Single();

            Assert.True(r.Disputed);
            Assert.Null(r.Tools["vat"]!["final"]);
            // Cong cu phan loai cua cung mau van co ket qua.
            Assert.Equal(ChiCho, Chot(r));
        }
    }
}
