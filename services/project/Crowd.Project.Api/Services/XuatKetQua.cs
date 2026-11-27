using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Ket qua gop → file theo dinh dang (FB-25): json (mac dinh), csv, coco / yolo / voc / yolo-seg / yolo-obb /
    /// coco-keypoints (anh), conll (van ban co span), mot (video co track).
    ///
    /// Mot cho duy nhat chon exporter, dung CA luc nhan yeu cau (KiemHopDuAn — bao 400 ngay, khong de doanh nghiep cho
    /// roi moi biet dinh dang sai) LAN luc consumer dung file (Dung).
    /// </summary>
    public static class XuatKetQua
    {
        public static readonly IReadOnlyList<string> DinhDangHoTro = new string[]
        {
            "json", "csv", "coco", "yolo", "voc", "yolo-seg", "yolo-obb", "coco-keypoints", "conll", "mot",
        };

        /// <summary>Mot instance dung lai: tao moi moi lan serialize cham di hang chuc lan.</summary>
        private static readonly JsonSerializerOptions JsonXuat = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

        /// <summary>null / rong = json. Dinh dang la → 400 dinh_dang_chua_ho_tro.</summary>
        public static string ChuanHoa(string? dinhDang)
        {
            string d = string.IsNullOrWhiteSpace(dinhDang) ? "json" : dinhDang.Trim().ToLowerInvariant();
            foreach (string x in DinhDangHoTro)
            {
                if (x == d)
                {
                    return d;
                }
            }

            throw new InvalidValueException("dinh_dang_chua_ho_tro", "Hỗ trợ " + string.Join(", ", DinhDangHoTro) + ".");
        }

        /// <summary>
        /// Dinh dang co hop tap nhan cua du an khong (vd coco can du an anh co bbox / polygon / mask). Chay chinh exporter
        /// tren ket qua RONG nen dieu kien luon trung voi luc dung file that — khong chep lai luat o day.
        /// </summary>
        public static void KiemHopDuAn(LabelSchema tapNhan, string dinhDang)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            ProjectResultsResponse rong = new ProjectResultsResponse
            {
                ProjectId = Guid.Empty,
                Modality = tapNhan.Modality,
                LabelSchema = tapNhan.ToRawJson(),
                SampleCount = 0,
                DisputedCount = 0,
                LabelDistribution = new Dictionary<string, SortedDictionary<string, int>>(),
                ImbalanceWarnings = new List<ImbalanceWarningDto>(),
                Samples = new List<SampleResultResponse>(),
            };

            Dung(rong, tapNhan, dinhDang);
        }

        public static ExportFile Dung(ProjectResultsResponse kq, LabelSchema tapNhan, string dinhDang)
        {
            if (kq == null)
            {
                throw new ArgumentNullException(nameof(kq));
            }

            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            string d = ChuanHoa(dinhDang);
            string ten = "ket-qua-" + kq.ProjectId.ToString();

            switch (d)
            {
                case "csv":
                    return new ExportFile(ResultExporter.Csv(kq, tapNhan), "text/csv; charset=utf-8", ten + ".csv");
                case "coco":
                    return new ExportFile(ResultExporter.Coco(kq, tapNhan), "application/json", ten + ".coco.json");
                case "yolo":
                    return new ExportFile(KhungAnhExporter.Yolo(kq, tapNhan), "application/zip", ten + ".yolo.zip");
                case "voc":
                    return new ExportFile(KhungAnhExporter.PascalVoc(kq, tapNhan), "application/zip", ten + ".voc.zip");
                case "yolo-seg":
                    return new ExportFile(DinhDangMoRongExporter.YoloSeg(kq, tapNhan), "application/zip", ten + ".yolo-seg.zip");
                case "yolo-obb":
                    return new ExportFile(DinhDangMoRongExporter.YoloObb(kq, tapNhan), "application/zip", ten + ".yolo-obb.zip");
                case "coco-keypoints":
                    return new ExportFile(DinhDangMoRongExporter.CocoKeypoints(kq, tapNhan), "application/json", ten + ".coco-keypoints.json");
                case "conll":
                    return new ExportFile(DinhDangMoRongExporter.Conll(kq, tapNhan), "text/plain; charset=utf-8", ten + ".conll");
                case "mot":
                    return new ExportFile(DinhDangMoRongExporter.Mot(kq, tapNhan), "application/zip", ten + ".mot.zip");
                default:
                    return new ExportFile(JsonSerializer.SerializeToUtf8Bytes(kq, JsonXuat), "application/json", ten + ".json");
            }
        }

        /// <summary>
        /// Dau van tay cua KET QUA (khong phai cua file: ZIP mang moc thoi gian tung entry nen moi lan dung mot khac).
        /// Cung dau van tay + cung dinh dang = file cu van dung — khoi luu ban moi.
        /// </summary>
        public static string DauVanTay(ProjectResultsResponse kq)
        {
            if (kq == null)
            {
                throw new ArgumentNullException(nameof(kq));
            }

            return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(kq, JsonXuat)));
        }

        /// <summary>Duoi file tu ten goi y: "ket-qua-x.yolo.zip" → ".yolo.zip".</summary>
        public static string Duoi(string tenFile, Guid projectId)
        {
            string goc = "ket-qua-" + projectId.ToString();
            return tenFile.StartsWith(goc, StringComparison.Ordinal) ? tenFile.Substring(goc.Length) : ".bin";
        }
    }
}
