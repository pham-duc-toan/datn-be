using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Common;
using Crowd.Labeling;

namespace Crowd.Annotation.Tests
{
    public sealed class ConsensusTests
    {
        private static readonly DateTimeOffset T1 = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset T2 = T1.AddMinutes(5);

        [Fact]
        public void Ket_qua_dong_thuan_cu_den_tre_khong_ghi_de_ban_moi()
        {
            TaskConsensus c = TaskConsensus.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            Assert.True(c.ApDung("agreed", RawJson.Tu("{\"loai\":{\"labelIds\":[\"cho\"]}}"), T2));
            Assert.False(c.ApDung("disputed", null, T1));

            Assert.Equal("agreed", c.Status);
            Assert.NotNull(c.Final);
        }

        [Fact]
        public void Danh_dau_khop_dong_thuan_theo_thu_tu_thoi_gian()
        {
            LabelAnnotation a = LabelAnnotation.TaoTuLuotNop(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "k.png",
                null,
                SampleMetadata.Rong.ToRawJson(),
                Guid.NewGuid(),
                LabelPayload.Tao(ResultAggregatorTests.TapNhan, "{\"loai\":{\"labelIds\":[\"cho\"]}}", null),
                T1);

            Assert.Null(a.ConsensusAgrees);
            Assert.True(a.GhiDongThuan(false, T1));
            Assert.True(a.GhiDongThuan(true, T2));
            Assert.False(a.GhiDongThuan(false, T1));
            Assert.True(a.ConsensusAgrees);

            // Goi y dong thuan KHONG doi trang thai duyet.
            Assert.Equal(AnnotationStatus.PendingReview, a.Status);
        }

        [Fact]
        public void Tu_duyet_chi_cho_nhan_khop_dong_thuan_va_khong_co_reviewer()
        {
            LabelAnnotation a = LabelAnnotation.TaoTuLuotNop(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "k.png",
                null,
                SampleMetadata.Rong.ToRawJson(),
                Guid.NewGuid(),
                LabelPayload.Tao(ResultAggregatorTests.TapNhan, "{\"loai\":{\"labelIds\":[\"cho\"]}}", null),
                T1);

            RuleViolationException chuaKhop = Assert.Throws<RuleViolationException>(() => a.TuDuyetTheoDongThuan(T2));
            Assert.Equal("chua_khop_dong_thuan", chuaKhop.Code);

            a.GhiDongThuan(true, T1);
            a.TuDuyetTheoDongThuan(T2);

            Assert.Equal(AnnotationStatus.Approved, a.Status);
            Assert.Null(a.ReviewerId);
            Assert.Equal(T2, a.ReviewedAt);

            // Mot nhan chi Approved mot lan (chi tien mot lan).
            Assert.Throws<RuleViolationException>(() => a.TuDuyetTheoDongThuan(T2));
        }
    }

    public sealed class ReviewTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Labeler = Guid.NewGuid();
        private static readonly Guid Reviewer = Guid.NewGuid();
        private static readonly Guid Admin = Guid.NewGuid();
        private static readonly string[] NopRoiDuyet = new string[] { "submitted", "approved" };

        /// <summary>Gia tri mac dinh cua setting annotation.reason_max_length / appeal_window.</summary>
        private static readonly QuyDinhDuyetNhan QuyDinh = new QuyDinhDuyetNhan
        {
            DoDaiLyDoToiDa = 1000,
            HanKhieuNai = TimeSpan.FromDays(7),
        };

        private static LabelAnnotation NhanMoi()
        {
            return LabelAnnotation.TaoTuLuotNop(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "k.png",
                null,
                SampleMetadata.Rong.ToRawJson(),
                Labeler,
                LabelPayload.Tao(ResultAggregatorTests.TapNhan, "{\"loai\":{\"labelIds\":[\"cho\"]}}", null),
                Luc);
        }

        private static LabelAnnotation NhanBiTuChoi()
        {
            LabelAnnotation a = NhanMoi();
            a.TuChoi(Reviewer, "Sai lop", Luc.AddHours(1), QuyDinh);
            return a;
        }

        [Fact]
        public void Duyet_ghi_nhat_ky()
        {
            LabelAnnotation a = NhanMoi();
            a.Duyet(Reviewer, Luc.AddHours(1));

            Assert.Equal(AnnotationStatus.Approved, a.Status);
            Assert.Equal(NopRoiDuyet, a.History.Select(h => h.Action).ToArray());
        }

        [Fact]
        public void Khong_duyet_hai_lan_nen_khong_chi_tien_hai_lan()
        {
            LabelAnnotation a = NhanMoi();
            a.Duyet(Reviewer, Luc);

            Assert.Throws<RuleViolationException>(() => a.Duyet(Reviewer, Luc));
            Assert.Throws<RuleViolationException>(() => a.TuChoi(Reviewer, "x", Luc, QuyDinh));
        }

        [Fact]
        public void Tu_choi_bat_buoc_co_ly_do_FB_21()
        {
            InvalidValueException ex = Assert.Throws<InvalidValueException>(() => NhanMoi().TuChoi(Reviewer, "  ", Luc, QuyDinh));
            Assert.Equal("thieu_ly_do", ex.Code);
        }

        [Fact]
        public void Khong_tu_duyet_nhan_cua_chinh_minh()
        {
            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => NhanMoi().Duyet(Labeler, Luc));
            Assert.Equal("tu_duyet", ex.Code);
        }

        [Fact]
        public void Khieu_nai_mot_lan_trong_7_ngay_boi_chinh_labeler()
        {
            Assert.Throws<RuleViolationException>(() => NhanBiTuChoi().KhieuNai(Guid.NewGuid(), "Toi dung", Luc.AddDays(1), QuyDinh));

            RuleViolationException tre = Assert.Throws<RuleViolationException>(() =>
                NhanBiTuChoi().KhieuNai(Labeler, "Toi dung", Luc.AddHours(1) + QuyDinh.HanKhieuNai + TimeSpan.FromSeconds(1), QuyDinh));
            Assert.Equal("qua_han_khieu_nai", tre.Code);

            Assert.Throws<RuleViolationException>(() => NhanMoi().KhieuNai(Labeler, "x", Luc, QuyDinh));

            LabelAnnotation a = NhanBiTuChoi();
            a.KhieuNai(Labeler, "Anh ro rang la cho", Luc.AddDays(1), QuyDinh);
            Assert.Equal(AnnotationStatus.Appealed, a.Status);
        }

        [Fact]
        public void Admin_chap_nhan_khieu_nai_thi_thanh_Approved()
        {
            LabelAnnotation a = NhanBiTuChoi();
            a.KhieuNai(Labeler, "Anh ro rang la cho", Luc.AddDays(1), QuyDinh);

            Assert.True(a.XuLyKhieuNai(Admin, true, "Dong y", Luc.AddDays(2)));
            Assert.Equal(AnnotationStatus.Approved, a.Status);
        }

        [Fact]
        public void Admin_bac_thi_tu_choi_cuoi_cung_khong_khieu_nai_lai_duoc()
        {
            LabelAnnotation a = NhanBiTuChoi();
            a.KhieuNai(Labeler, "Anh ro rang la cho", Luc.AddDays(1), QuyDinh);

            Assert.False(a.XuLyKhieuNai(Admin, false, "Giu nguyen", Luc.AddDays(2)));
            Assert.True(a.LaTuChoiCuoiCung());
            Assert.False(a.ConKhieuNaiDuoc(Luc.AddDays(2), QuyDinh.HanKhieuNai));

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => a.KhieuNai(Labeler, "lan nua", Luc.AddDays(2), QuyDinh));
            Assert.Equal("da_khieu_nai", ex.Code);
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
            LabelAnnotation a = LabelAnnotation.TaoTuLuotNop(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Mau,
                "k",
                null,
                Anh.ToRawJson(),
                Guid.NewGuid(),
                LabelPayload.Tao(TapNhan, duLieu, Anh),
                DateTimeOffset.UtcNow);
            a.Duyet(Guid.NewGuid(), DateTimeOffset.UtcNow);
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
        public void Khung_chua_gop_tu_dong_nhung_van_dem_phan_bo()
        {
            LabelAnnotation a = Nhan(
                "{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"mat\",\"x\":1,\"y\":1,\"w\":10,\"h\":10},"
                + "{\"labelId\":\"mat\",\"x\":50,\"y\":50,\"w\":10,\"h\":10}]}");
            LabelAnnotation b = Lop("cho");

            IReadOnlyList<SampleResult> kq = ResultAggregator.Chot(TapNhan, new[] { a, b });
            SampleResult r = kq.Single();

            Assert.Equal("none", r.Tools["vat"]!["method"]!.GetValue<string>());
            Assert.Equal(2, r.Approved.Count);

            SortedDictionary<string, SortedDictionary<string, int>> phanBo = ResultAggregator.PhanBo(TapNhan, kq);
            Assert.Equal(1, phanBo["loai"]["cho"]);
            Assert.Equal(2, phanBo["vat"]["mat"]);
        }
    }
}
