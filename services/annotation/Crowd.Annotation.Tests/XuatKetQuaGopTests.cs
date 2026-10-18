using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Api.Services;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Labeling;

namespace Crowd.Annotation.Tests
{
    /// <summary>Xuat file dung ket qua DA GOP (ResultAggregator) — chay that tu nhan da duyet toi file.</summary>
    public sealed class XuatKetQuaGopTests
    {
        private static readonly LabelSchema TapNhan = LabelSchema.Doc(
            "{\"modality\":\"image\",\"tools\":["
            + "{\"name\":\"vat\",\"kind\":\"bbox\",\"classes\":[\"xe\"],\"attributes\":[{\"name\":\"bien_so\",\"type\":\"text\"}]},"
            + "{\"name\":\"vung\",\"kind\":\"mask\",\"classes\":[\"duong\"],\"required\":false},"
            + "{\"name\":\"so_xe\",\"kind\":\"number\",\"integer\":true},"
            + "{\"name\":\"mo_ta\",\"kind\":\"freeText\",\"required\":false}]}");

        private static readonly SampleMetadata Anh4x4 = new SampleMetadata { Width = 4, Height = 4 };

        private static LabelAnnotation Nhan(Guid mau, string duLieu, int phutSau)
        {
            LabelAnnotation a = LabelAnnotation.TaoTuBaiNop(
                Guid.NewGuid(), Guid.NewGuid(), mau, Guid.NewGuid(),
                LabelPayload.Tao(TapNhan, duLieu, Anh4x4), DateTimeOffset.UnixEpoch.AddMinutes(phutSau));
            a.ChotTheoKetQua(true, DateTimeOffset.UnixEpoch.AddHours(1));
            return a;
        }

        /// <summary>Hai mau: mau 1 ba nguoi thong nhat; mau 2 hai nguoi ve khung o hai cho khac nhau (tranh chap).</summary>
        private static ProjectResultsResponse KetQua()
        {
            Guid mau1 = new Guid("00000000-0000-0000-0000-000000000001");
            Guid mau2 = new Guid("00000000-0000-0000-0000-000000000002");

            List<LabelAnnotation> nhan = new List<LabelAnnotation>
            {
                Nhan(mau1, "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":2,\"h\":2,\"attributes\":{\"bien_so\":\"30A-1\"}}],"
                           + "\"vung\":[{\"labelId\":\"duong\",\"rle\":{\"size\":[4,4],\"counts\":[8,8]}}],\"so_xe\":{\"value\":1},\"mo_ta\":{\"text\":\"mot xe\"}}", 1),
                Nhan(mau1, "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":2,\"h\":2,\"attributes\":{\"bien_so\":\"30A-1\"}}],"
                           + "\"vung\":[{\"labelId\":\"duong\",\"rle\":{\"size\":[4,4],\"counts\":[8,8]}}],\"so_xe\":{\"value\":1}}", 2),
                Nhan(mau1, "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":2,\"h\":2,\"attributes\":{\"bien_so\":\"30A-7\"}}],"
                           + "\"vung\":[{\"labelId\":\"duong\",\"rle\":{\"size\":[4,4],\"counts\":[10,6]}}],\"so_xe\":{\"value\":2}}", 3),
                Nhan(mau2, "{\"vat\":[{\"labelId\":\"xe\",\"x\":0,\"y\":0,\"w\":1,\"h\":1}],\"so_xe\":{\"value\":1}}", 1),
                Nhan(mau2, "{\"vat\":[{\"labelId\":\"xe\",\"x\":3,\"y\":3,\"w\":1,\"h\":1}],\"so_xe\":{\"value\":1}}", 2),
            };

            Dictionary<Guid, SampleMetadata> thongTinMau = new Dictionary<Guid, SampleMetadata> { { mau1, Anh4x4 }, { mau2, Anh4x4 } };
            IReadOnlyList<SampleResult> chot = ResultAggregator.Chot(TapNhan, nhan, NguongKhop.CuaThuVien, thongTinMau, 0.8);

            List<SampleResultResponse> mau = new List<SampleResultResponse>();
            foreach (SampleResult r in chot)
            {
                mau.Add(new SampleResultResponse
                {
                    SampleId = r.SampleId,
                    StorageKey = "p/raw/" + r.SampleId + ".png",
                    SampleMetadata = Anh4x4.ToRawJson(),
                    ApprovedCount = r.ApprovedCount,
                    Disputed = r.Disputed,
                    Tools = RawJson.Tu(r.Tools),
                    Labels = r.Approved.Select(a => RawJson.Tu(a.Payload.DataJson)).ToList(),
                });
            }

            return new ProjectResultsResponse
            {
                ProjectId = Guid.NewGuid(),
                Modality = "image",
                LabelSchema = TapNhan.ToRawJson(),
                SampleCount = mau.Count,
                DisputedCount = mau.Count(m => m.Disputed),
                LabelDistribution = new Dictionary<string, SortedDictionary<string, int>>(),
                ImbalanceWarnings = new List<ImbalanceWarningDto>(),
                Samples = mau,
            };
        }

        [Fact]
        public void Coco_moi_doi_tuong_da_gop_la_mot_annotation_ke_ca_mat_na()
        {
            ProjectResultsResponse kq = KetQua();
            JsonObject coco = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(ResultExporter.Coco(kq, TapNhan)))!;

            JsonArray anh = (JsonArray)coco["images"]!;
            Assert.False(anh[0]!["disputed"]!.GetValue<bool>());
            Assert.True(anh[1]!["disputed"]!.GetValue<bool>());

            // Mau 1: MOT khung (ba nguoi ve trung nhau) + MOT mat na; mau 2 tranh chap khong co gi.
            JsonArray nhan = (JsonArray)coco["annotations"]!;
            Assert.Equal(2, nhan.Count);
            Assert.All(nhan, n => Assert.Equal(1, n!["image_id"]!.GetValue<int>()));

            JsonNode khung = nhan[0]!;
            Assert.Equal("[0,0,2,2]", khung["bbox"]!.ToJsonString());
            Assert.Equal(3, khung["support"]!.GetValue<int>());
            // Bien so: hai ban "30A-1", mot ban "30A-7" → lay ban o giua.
            Assert.Equal("30A-1", khung["attributes"]!["bien_so"]!.GetValue<string>());

            // Mat na: pixel 8..15 (hai nguoi) va 10..15 (mot nguoi) → da so 8..15 = hai cot ben phai.
            JsonNode matNa = nhan[1]!;
            Assert.Equal("{\"counts\":[8,8],\"size\":[4,4]}", matNa["segmentation"]!.ToJsonString());
            Assert.Equal(8, matNa["area"]!.GetValue<long>());
            Assert.Equal("[2,0,2,4]", matNa["bbox"]!.ToJsonString());
        }

        [Fact]
        public void Csv_ghi_ket_qua_gop_va_de_trong_cong_cu_tranh_chap()
        {
            ProjectResultsResponse kq = KetQua();
            string[] dong = Encoding.UTF8.GetString(ResultExporter.Csv(kq, TapNhan)).TrimStart('﻿').Split('\n');

            Assert.Equal("sample_id,storage_key,approved_count,disputed,vat,vung,so_xe,mo_ta", dong[0]);

            // Mau 1: khung da gop (JSON), mat na, trung vi so xe = 1, mo ta la cau tra loi mo → JSON tung nguoi.
            Assert.Contains(",3,false,", dong[1], StringComparison.Ordinal);
            Assert.Contains("\"\"bien_so\"\":\"\"30A-1\"\"", dong[1], StringComparison.Ordinal);
            Assert.Contains(",1,\"[{\"\"text\"\":\"\"mot xe\"\"},null,null]\"", dong[1], StringComparison.Ordinal);

            // Mau 2: khung tranh chap → o trong; khong ai to mat na / mo ta (JSON tung nguoi = null); so xe thong nhat = 1.
            Assert.EndsWith(",2,true,,\"[null,null]\",1,\"[null,null]\"", dong[2], StringComparison.Ordinal);
        }

        [Fact]
        public void Yolo_chi_xuat_khung_da_gop()
        {
            ProjectResultsResponse kq = KetQua();
            byte[] zip = KhungAnhExporter.Yolo(kq, TapNhan);

            using (System.IO.Compression.ZipArchive a = new System.IO.Compression.ZipArchive(new System.IO.MemoryStream(zip)))
            {
                string Doc(string ten)
                {
                    using (System.IO.StreamReader r = new System.IO.StreamReader(a.GetEntry(ten)!.Open(), Encoding.UTF8))
                    {
                        return r.ReadToEnd();
                    }
                }

                // Mau 1: ba nguoi → MOT dong; mau 2 tranh chap → file rong.
                Assert.Equal("0 0.25 0.25 0.5 0.5\n", Doc("labels/" + kq.Samples[0].SampleId + ".txt"));
                Assert.Equal(string.Empty, Doc("labels/" + kq.Samples[1].SampleId + ".txt"));
            }
        }
    }
}
