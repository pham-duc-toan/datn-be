using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Tests
{
    /// <summary>Doanh nghiep sua ket qua cuoi cua mau (sample_overrides) — ap vao ket qua gop va file xuat.</summary>
    public sealed class SuaKetQuaTests
    {
        private static readonly LabelSchema TapNhan = LabelSchema.Doc(
            "{\"modality\":\"image\",\"tools\":["
            + "{\"name\":\"loai\",\"kind\":\"classification\",\"classes\":[\"cho\",\"meo\"]},"
            + "{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"xe\"]},"
            + "{\"name\":\"mo_ta\",\"kind\":\"freeText\",\"required\":false}]}");

        private static readonly SampleMetadata Anh = new SampleMetadata { Width = 10, Height = 10 };

        private static readonly Guid MauId = new Guid("00000000-0000-0000-0000-0000000000aa");

        /// <summary>Hai nguoi: phan loai tranh chap (cho / meo), khung thong nhat.</summary>
        private const string GopGoc =
            "{\"loai\":{\"method\":\"majority\",\"final\":null,\"votes\":{\"cho\":1,\"meo\":1},\"agrees\":[false,false],\"disputed\":true,\"kind\":\"classification\"},"
            + "\"vat\":{\"method\":\"cluster\",\"final\":[{\"x\":1,\"y\":1,\"w\":2,\"h\":2,\"labelId\":\"xe\"}],\"supports\":[2],\"agrees\":[true,true],\"disputed\":false,\"kind\":\"bbox\"},"
            + "\"mo_ta\":{\"method\":\"none\",\"kind\":\"freeText\"}}";

        private static SampleResult KetQuaGoc(string gop)
        {
            List<LabelAnnotation> nhan = new List<LabelAnnotation>
            {
                LabelAnnotation.TaoTuBaiNop(Guid.NewGuid(), Guid.NewGuid(), MauId, Guid.NewGuid(),
                    LabelPayload.Tao(TapNhan, "{\"loai\":{\"labelIds\":[\"cho\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":1,\"y\":1,\"w\":2,\"h\":2}]}", Anh)),
                LabelAnnotation.TaoTuBaiNop(Guid.NewGuid(), Guid.NewGuid(), MauId, Guid.NewGuid(),
                    LabelPayload.Tao(TapNhan, "{\"loai\":{\"labelIds\":[\"meo\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":1,\"y\":1,\"w\":2,\"h\":2}]}", Anh)),
            };
            return new SampleResult(MauId, 2, (JsonObject)JsonNode.Parse(gop)!, nhan);
        }

        private static SampleOverride Sua(SampleResult goc, string duLieu)
        {
            string giaTri;
            using (JsonDocument d = JsonDocument.Parse(duLieu))
            {
                giaTri = LabelPayload.ChuanHoaMotPhan(TapNhan, d.RootElement, Anh, null);
            }

            List<string> congCu = ((JsonObject)JsonNode.Parse(giaTri)!).Select(k => k.Key).ToList();
            return SampleOverride.Tao(Guid.NewGuid(), MauId, giaTri, SuaKetQua.BamSoDong(goc.Tools, congCu), "da xem lai", Guid.NewGuid(), DateTimeOffset.UtcNow);
        }

        [Fact]
        public void Cong_cu_da_sua_lay_gia_tri_doanh_nghiep_va_giu_so_dong_de_doi_chieu()
        {
            SampleResult goc = KetQuaGoc(GopGoc);
            KetQuaDaSua ap = SuaKetQua.ApDung(TapNhan, goc, Sua(goc, "{\"loai\":{\"labelIds\":[\"meo\"]}}"));

            JsonObject loai = (JsonObject)ap.KetQua.Tools["loai"]!;
            Assert.Equal("{\"labelIds\":[\"meo\"]}", loai["final"]!.ToJsonString());
            Assert.Equal("owner", loai["method"]!.GetValue<string>());
            Assert.Equal("majority", loai["consensusMethod"]!.GetValue<string>());
            Assert.Null(loai["consensusFinal"]);
            Assert.True(loai["consensusDisputed"]!.GetValue<bool>());
            Assert.True(loai["ownerEdited"]!.GetValue<bool>());
            Assert.Equal("{\"cho\":1,\"meo\":1}", loai["votes"]!.ToJsonString());

            // Cong cu tranh chap duy nhat da duoc doanh nghiep chot → mau het tranh chap.
            Assert.True(goc.Disputed);
            Assert.False(ap.KetQua.Disputed);
            Assert.Equal("loai", Assert.Single(ap.CongCu));
            Assert.False(ap.SoDongDaDoi);

            // Khong dung vao ket qua goc (co the la ban trong bo nho dem).
            Assert.Null(goc.Tools["loai"]!["final"]);
        }

        [Fact]
        public void Sua_cong_cu_danh_sach_bo_so_nguoi_ung_ho_cu()
        {
            SampleResult goc = KetQuaGoc(GopGoc);
            KetQuaDaSua ap = SuaKetQua.ApDung(TapNhan, goc,
                Sua(goc, "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":5,\"h\":5},{\"labelId\":\"xe\",\"x\":6,\"y\":6,\"w\":2,\"h\":2}]}"));

            JsonObject vat = (JsonObject)ap.KetQua.Tools["vat"]!;
            Assert.Equal(2, ((JsonArray)vat["final"]!).Count);
            Assert.Null(vat["supports"]);
            Assert.Single((JsonArray)vat["consensusFinal"]!);
        }

        [Fact]
        public void Cong_cu_khong_gop_duoc_cung_sua_duoc()
        {
            SampleResult goc = KetQuaGoc(GopGoc);
            KetQuaDaSua ap = SuaKetQua.ApDung(TapNhan, goc, Sua(goc, "{\"mo_ta\":{\"text\":\"con meo tren xe\"}}"));

            JsonObject moTa = (JsonObject)ap.KetQua.Tools["mo_ta"]!;
            Assert.Equal("none", moTa["consensusMethod"]!.GetValue<string>());
            Assert.Equal("con meo tren xe", moTa["final"]!["text"]!.GetValue<string>());
        }

        [Fact]
        public void So_dong_cua_cong_cu_da_sua_doi_sau_luc_sua_thi_bao_xem_lai()
        {
            SampleResult goc = KetQuaGoc(GopGoc);
            SampleOverride s = Sua(goc, "{\"loai\":{\"labelIds\":[\"meo\"]}}");

            // Khieu nai duoc chap nhan them mot nhan "cho" → so dong chot "cho".
            SampleResult moi = KetQuaGoc(GopGoc.Replace("\"final\":null,\"votes\":{\"cho\":1,\"meo\":1}", "\"final\":{\"labelIds\":[\"cho\"]},\"votes\":{\"cho\":2,\"meo\":1}", StringComparison.Ordinal));
            Assert.True(SuaKetQua.ApDung(TapNhan, moi, s).SoDongDaDoi);

            // Chi cong cu KHONG sua doi (khung) → khong can xem lai.
            SampleResult khungDoi = KetQuaGoc(GopGoc.Replace("\"x\":1,\"y\":1", "\"x\":2,\"y\":2", StringComparison.Ordinal));
            Assert.False(SuaKetQua.ApDung(TapNhan, khungDoi, s).SoDongDaDoi);
        }

        [Fact]
        public void File_csv_va_coco_ghi_gia_tri_doanh_nghiep_da_sua()
        {
            SampleResult goc = KetQuaGoc(GopGoc);
            KetQuaDaSua ap = SuaKetQua.ApDung(TapNhan, goc,
                Sua(goc, "{\"loai\":{\"labelIds\":[\"meo\"]},\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":5,\"h\":5}]}"));
            ProjectResultsResponse kq = KetQua(ap.KetQua);

            string[] dong = Encoding.UTF8.GetString(ResultExporter.Csv(kq, TapNhan)).TrimStart('﻿').Split('\n');
            Assert.StartsWith(MauId + ",p/raw/a.png,2,false,meo,", dong[1], StringComparison.Ordinal);

            JsonObject coco = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(ResultExporter.Coco(kq, TapNhan)))!;
            JsonArray nhan = (JsonArray)coco["annotations"]!;
            Assert.Single(nhan);
            Assert.Equal("[0,0,5,5]", nhan[0]!["bbox"]!.ToJsonString());
            Assert.Equal(0, nhan[0]!["support"]!.GetValue<int>());

            // Dau van tay file doi theo phan sua → khong dung lai file cu.
            Assert.NotEqual(XuatKetQua.DauVanTay(KetQua(goc)), XuatKetQua.DauVanTay(kq));
        }

        [Fact]
        public void Ghi_chu_qua_dai_bi_tu_choi()
        {
            SampleResult goc = KetQuaGoc(GopGoc);
            SampleOverride s = Sua(goc, "{\"loai\":{\"labelIds\":[\"meo\"]}}");
            InvalidValueException ex = Assert.Throws<InvalidValueException>(
                () => s.Sua(s.ToolsJson, s.ConsensusHash, new string('a', SampleOverride.DoDaiGhiChuToiDa + 1), Guid.NewGuid(), DateTimeOffset.UtcNow));
            Assert.Equal("ghi_chu_qua_dai", ex.Code);
        }

        private static ProjectResultsResponse KetQua(SampleResult r)
        {
            SampleResultResponse mau = new SampleResultResponse
            {
                SampleId = r.SampleId,
                StorageKey = "p/raw/a.png",
                SampleMetadata = Anh.ToRawJson(),
                ApprovedCount = r.ApprovedCount,
                Disputed = r.Disputed,
                Tools = RawJson.Tu(r.Tools),
                Labels = r.Approved.Select(a => RawJson.Tu(a.DataJson)).ToList(),
            };

            return new ProjectResultsResponse
            {
                ProjectId = Guid.Empty,
                Modality = "image",
                LabelSchema = TapNhan.ToRawJson(),
                SampleCount = 1,
                DisputedCount = r.Disputed ? 1 : 0,
                LabelDistribution = new Dictionary<string, SortedDictionary<string, int>>(),
                ImbalanceWarnings = new List<ImbalanceWarningDto>(),
                Samples = new List<SampleResultResponse> { mau },
            };
        }
    }
}
