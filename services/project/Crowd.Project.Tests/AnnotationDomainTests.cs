using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Vong doi nhan (docs 3.18): nhan chi luu cau tra loi; trang thai SUY RA tu bai lam — nop → cham dat / truot →
    /// khieu nai. Cham va phan xu chi sua bai, khong sua nhan.
    /// </summary>
    public sealed class VongDoiNhanTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Labeler = Guid.NewGuid();

        private static LabelPayload Cho()
        {
            return LabelPayload.Tao(ResultAggregatorTests.TapNhan, "{\"loai\":{\"labelIds\":[\"cho\"]}}", null);
        }

        private static Attempt BaiDaNop()
        {
            Attempt a = Attempt.BatDau(Guid.NewGuid(), Labeler, new List<Guid> { Guid.NewGuid() }, Luc, Luc.AddHours(48));
            a.Nop(0, 0, 1, null, null, null, Luc.AddHours(1));
            return a;
        }

        [Fact]
        public void Nhan_chi_giu_cau_tra_loi_va_doc_lai_theo_loai_du_lieu_du_an()
        {
            Guid bai = Guid.NewGuid();
            Guid duAn = Guid.NewGuid();
            Guid mau = Guid.NewGuid();
            LabelAnnotation a = LabelAnnotation.TaoTuBaiNop(bai, duAn, mau, Labeler, Cho());

            Assert.Equal(bai, a.AttemptId);
            Assert.Equal(duAn, a.ProjectId);
            Assert.Equal(mau, a.SampleId);
            Assert.Equal(LabelSource.Professional, a.Source);

            LabelPayload doc = a.LayPayload("image");
            Assert.Equal(Cho(), doc);
            Assert.NotNull(a.KetQua("loai"));
            Assert.Null(a.KetQua("khong_co"));
        }

        [Fact]
        public void Bai_chua_cham_thi_nhan_cho_cham()
        {
            Assert.Equal(AnnotationStatus.Submitted, LabelAnnotation.TrangThai(LabelSource.Professional, BaiDaNop()));
            // Nhan mo coi (khong tim thay bai) khong bao gio tinh la vao ket qua.
            Assert.Equal(AnnotationStatus.Submitted, LabelAnnotation.TrangThai(LabelSource.Professional, null));
        }

        [Fact]
        public void Bai_dat_thi_nhan_vao_ket_qua_bai_truot_thi_bi_loai()
        {
            Attempt dat = BaiDaNop();
            dat.GhiKetQua(10, 9, 90, true, true, Luc.AddDays(1));
            Assert.Equal(AnnotationStatus.Accepted, LabelAnnotation.TrangThai(LabelSource.Professional, dat));

            Attempt truot = BaiDaNop();
            truot.GhiKetQua(10, 5, 50, false, false, Luc.AddDays(1));
            Assert.Equal(AnnotationStatus.Excluded, LabelAnnotation.TrangThai(LabelSource.Professional, truot));
        }

        [Fact]
        public void Khieu_nai_duoc_chap_nhan_thi_nhan_vao_lai_ket_qua_bac_bo_thi_van_bi_loai()
        {
            Attempt a = BaiDaNop();
            a.GhiKetQua(10, 5, 50, false, false, Luc.AddDays(1));
            a.KhieuNai("Xem lai", Luc.AddDays(4), Luc.AddDays(2));
            Assert.Equal(AnnotationStatus.Excluded, LabelAnnotation.TrangThai(LabelSource.Professional, a));
            a.PhanXu(true, null, Guid.NewGuid(), true, Luc.AddDays(3));
            Assert.Equal(AnnotationStatus.Accepted, LabelAnnotation.TrangThai(LabelSource.Professional, a));

            Attempt b = BaiDaNop();
            b.GhiKetQua(10, 5, 50, false, false, Luc.AddDays(1));
            b.KhieuNai("Xem lai", Luc.AddDays(4), Luc.AddDays(2));
            b.PhanXu(false, "Khong du", Guid.NewGuid(), true, Luc.AddDays(3));
            Assert.Equal(AnnotationStatus.Excluded, LabelAnnotation.TrangThai(LabelSource.Professional, b));
        }

        [Fact]
        public void Nhan_cong_link_luon_vao_ket_qua_khong_co_labeler()
        {
            LabelAnnotation a = LabelAnnotation.TaoTuCongLink(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Cho());

            Assert.Equal(LabelSource.LinkGateway, a.Source);
            Assert.Null(a.LabelerId);
            Assert.Equal(AnnotationStatus.Accepted, LabelAnnotation.TrangThai(a.Source, null));
        }

        [Fact]
        public void Nhan_rong_bi_tu_choi()
        {
            InvalidValueException ex = Assert.Throws<InvalidValueException>(() =>
                LabelAnnotation.TaoTuBaiNop(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Labeler, null!));
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
                LabelPayload.Tao(TapNhan, duLieu, Anh));
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
