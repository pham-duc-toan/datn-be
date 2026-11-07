using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Project.Tests
{
    /// <summary>Vong doi nhan trong du an lam trung tam (docs 3.18): nop ca bai → cham → chot.</summary>
    public sealed class VongDoiNhanTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Labeler = Guid.NewGuid();

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

            Assert.True(a.ChapNhanSauKhieuNai(Luc.AddDays(2)));
            Assert.Equal(AnnotationStatus.Accepted, a.Status);
            Assert.Equal(Luc.AddDays(2), a.DecidedAt);

            // Chap nhan giao lai: khong doi moc chot.
            Assert.False(a.ChapNhanSauKhieuNai(Luc.AddDays(3)));
            Assert.Equal(Luc.AddDays(2), a.DecidedAt);
        }

        [Fact]
        public void Khieu_nai_chi_ap_dung_cho_nhan_bi_loai()
        {
            LabelAnnotation a = NhanMoi();

            Assert.False(a.ChapNhanSauKhieuNai(Luc.AddDays(1)));
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

        [Fact]
        public void Phan_bo_dem_tren_ket_qua_da_gop()
        {
            // Ket qua gop (quality-svc tinh qua gRPC, 3.24): mau co hai khung da gop + lop "cho".
            JsonObject tools = (JsonObject)JsonNode.Parse(
                "{\"loai\":{\"method\":\"majority\",\"final\":{\"labelIds\":[\"cho\"]},\"votes\":{\"cho\":3},"
                + "\"agrees\":[true,true,true],\"disputed\":false,\"kind\":\"classification\"},"
                + "\"vat\":{\"method\":\"cluster\",\"final\":[{\"x\":1.5,\"y\":1,\"w\":10,\"h\":10,\"labelId\":\"mat\"},"
                + "{\"x\":50,\"y\":50.5,\"w\":10,\"h\":10,\"labelId\":\"mat\"}],\"supports\":[2,2],\"agrees\":[true,true],"
                + "\"disputed\":false,\"kind\":\"bbox\"}}")!;
            LabelAnnotation a = Nhan(
                "{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"mat\",\"x\":1,\"y\":1,\"w\":10,\"h\":10},"
                + "{\"labelId\":\"mat\",\"x\":50,\"y\":50,\"w\":10,\"h\":10}]}");
            LabelAnnotation b = Lop("cho");
            LabelAnnotation c = Nhan(
                "{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"mat\",\"x\":2,\"y\":1,\"w\":10,\"h\":10},"
                + "{\"labelId\":\"mat\",\"x\":50,\"y\":51,\"w\":10,\"h\":10},{\"labelId\":\"mat\",\"x\":80,\"y\":80,\"w\":5,\"h\":5}]}");
            SampleResult r = new SampleResult(Mau, 3, tools, new[] { a, b, c });

            Assert.False(r.Disputed);
            SortedDictionary<string, SortedDictionary<string, int>> phanBo = ResultAggregator.PhanBo(TapNhan, new[] { r });
            Assert.Equal(1, phanBo["loai"]["cho"]);
            Assert.Equal(5, phanBo["vat"]["mat"]);
        }

        [Fact]
        public void Mau_co_cong_cu_tranh_chap_la_tranh_chap()
        {
            JsonObject tools = (JsonObject)JsonNode.Parse(
                "{\"loai\":{\"method\":\"majority\",\"final\":null,\"votes\":{\"cho\":1,\"meo\":1},\"agrees\":[false,false],"
                + "\"disputed\":true,\"kind\":\"classification\"},\"vat\":{\"method\":\"none\",\"kind\":\"bbox\"}}")!;

            SampleResult r = new SampleResult(Mau, 2, tools, new[] { Lop("cho"), Lop("meo") });

            Assert.True(r.Disputed);
        }
    }
}
