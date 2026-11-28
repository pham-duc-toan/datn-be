using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Annotations
{
    /// <summary>
    /// Doanh nghiep SUA ket qua cuoi cua mot mau (bang sample_overrides): xem lai ket qua gop, thay gia tri cua vai
    /// cong cu bang gia tri ho thay dung hon so dong, roi moi xuat file.
    ///
    /// Tach rieng khoi sample_results vi sample_results la BO NHO DEM (ghi de / xoa luc nao cung duoc, tinh lai tu
    /// nhan) — con day la du lieu nguoi nhap, khong tinh lai duoc.
    ///
    /// Chi anh huong KET QUA XUAT: khong doi cham bai, tien thuong, uy tin (docs 3.18 — "Khong duyet tung nhan"; neu
    /// sua lam doi dat / truot thi doanh nghiep co the sua de khong phai tra thuong).
    /// </summary>
    public sealed class SampleOverride
    {
        public const int DoDaiGhiChuToiDa = 500;

        private SampleOverride()
        {
            ToolsJson = string.Empty;
            ConsensusHash = string.Empty;
        }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>Gia tri doanh nghiep chon, JSON chuan hoa {ten cong cu: ket qua} — chi cac cong cu da sua.</summary>
        public string ToolsJson { get; private set; }

        /// <summary>
        /// Dau van tay KET QUA SO DONG cua dung cac cong cu da sua, luc sua (SuaKetQua.BamSoDong). Lech voi hien tai =
        /// so dong da doi sau khi sua (vd khieu nai duoc chap nhan them nhan) → bao doanh nghiep xem lai.
        /// </summary>
        public string ConsensusHash { get; private set; }

        public string? Note { get; private set; }

        public Guid EditedBy { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset EditedAt { get; private set; }

        public static SampleOverride Tao(
            Guid projectId, Guid sampleId, string toolsJson, string consensusHash, string? note, Guid editedBy, DateTimeOffset luc)
        {
            if (projectId == Guid.Empty || sampleId == Guid.Empty || editedBy == Guid.Empty)
            {
                throw new InvalidValueException("id_rong", "Thiếu dự án, mẫu hoặc người sửa.");
            }

            SampleOverride s = new SampleOverride();
            s.ProjectId = projectId;
            s.SampleId = sampleId;
            s.CreatedAt = luc;
            s.Sua(toolsJson, consensusHash, note, editedBy, luc);
            return s;
        }

        /// <summary>Thay TOAN BO phan sua cua mau (cong cu khong gui len = tro ve ket qua so dong).</summary>
        public void Sua(string toolsJson, string consensusHash, string? note, Guid editedBy, DateTimeOffset luc)
        {
            JsonObject? o = string.IsNullOrWhiteSpace(toolsJson) ? null : JsonNode.Parse(toolsJson) as JsonObject;
            if (o == null || o.Count == 0)
            {
                throw new InvalidValueException("chua_sua_gi", "Chưa có công cụ nào được sửa.");
            }

            if (string.IsNullOrWhiteSpace(consensusHash))
            {
                throw new ArgumentException("Thieu dau van tay ket qua so dong.", nameof(consensusHash));
            }

            string? g = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (g != null && g.Length > DoDaiGhiChuToiDa)
            {
                throw new InvalidValueException("ghi_chu_qua_dai", "Ghi chú tối đa " + DoDaiGhiChuToiDa + " ký tự.");
            }

            ToolsJson = toolsJson;
            ConsensusHash = consensusHash;
            Note = g;
            EditedBy = editedBy;
            EditedAt = luc;
        }

        /// <summary>Ten cac cong cu da sua.</summary>
        public IReadOnlyList<string> CongCu()
        {
            JsonObject o = (JsonObject)JsonNode.Parse(ToolsJson)!;
            return o.Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Mot mau sau khi ap phan sua cua doanh nghiep.</summary>
    public sealed class KetQuaDaSua
    {
        public KetQuaDaSua(SampleResult ketQua, IReadOnlyList<string> congCu, bool soDongDaDoi)
        {
            KetQua = ketQua;
            CongCu = congCu;
            SoDongDaDoi = soDongDaDoi;
        }

        /// <summary>Ket qua cuoi: cong cu da sua lay gia tri cua doanh nghiep, con lai giu ket qua so dong.</summary>
        public SampleResult KetQua { get; }

        public IReadOnlyList<string> CongCu { get; }

        /// <summary>Ket qua so dong cua cac cong cu da sua khong con nhu luc sua.</summary>
        public bool SoDongDaDoi { get; }
    }

    /// <summary>Ham thuan: dau van tay so dong + ap phan sua vao ket qua gop.</summary>
    public static class SuaKetQua
    {
        /// <summary>
        /// SHA-256 cua ket qua so dong ("final", null khi tranh chap / khong gop) cua dung cac cong cu cho truoc, JSON
        /// chuan hoa. Cong cu khac doi khong lam lech — doanh nghiep chi can xem lai khi cong cu HO SUA doi.
        /// </summary>
        public static string BamSoDong(JsonObject tools, IEnumerable<string> congCu)
        {
            if (tools == null)
            {
                throw new ArgumentNullException(nameof(tools));
            }

            if (congCu == null)
            {
                throw new ArgumentNullException(nameof(congCu));
            }

            JsonObject chon = new JsonObject();
            foreach (string ten in congCu.Distinct().OrderBy(k => k, StringComparer.Ordinal))
            {
                JsonObject? t = tools[ten] as JsonObject;
                JsonNode? final = t == null ? null : t["final"];
                chon[ten] = final == null ? null : final.DeepClone();
            }

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonCanonical.Viet(chon))));
        }

        /// <summary>
        /// Ket qua cuoi cua mau. Moi cong cu da sua:
        ///   final = gia tri doanh nghiep; method = "owner"; disputed = false; ownerEdited = true;
        ///   consensusFinal / consensusMethod / consensusDisputed = ket qua so dong (de doi chieu);
        ///   bo "supports" (so nguoi ung ho tung doi tuong khong con dung cho danh sach moi).
        /// Phieu / agrees giu nguyen — van la thong tin ve nguoi gan. Khong sua doi tuong dau vao (ket qua co the tu bo nho dem).
        /// </summary>
        public static KetQuaDaSua ApDung(LabelSchema tapNhan, SampleResult goc, SampleOverride sua)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (goc == null)
            {
                throw new ArgumentNullException(nameof(goc));
            }

            if (sua == null)
            {
                throw new ArgumentNullException(nameof(sua));
            }

            JsonObject giaTri = (JsonObject)JsonNode.Parse(sua.ToolsJson)!;
            JsonObject tools = (JsonObject)goc.Tools.DeepClone();
            List<string> congCu = new List<string>();

            foreach (ToolDefinition t in tapNhan.Tools)
            {
                JsonNode? moi = giaTri[t.Name];
                if (moi == null)
                {
                    continue;
                }

                JsonObject? cu = goc.Tools[t.Name] as JsonObject;
                JsonObject ra = cu == null ? new JsonObject { ["kind"] = t.Kind } : (JsonObject)cu.DeepClone();

                JsonNode? finalCu = cu == null ? null : cu["final"];
                JsonNode? methodCu = cu == null ? null : cu["method"];
                bool tranhChapCu = false;
                JsonValue? d = cu == null ? null : cu["disputed"] as JsonValue;
                if (d != null)
                {
                    d.TryGetValue(out tranhChapCu);
                }

                ra["consensusFinal"] = finalCu == null ? null : finalCu.DeepClone();
                ra["consensusMethod"] = methodCu == null ? "none" : methodCu.DeepClone();
                ra["consensusDisputed"] = tranhChapCu;
                ra["method"] = "owner";
                ra["final"] = moi.DeepClone();
                ra["disputed"] = false;
                ra["ownerEdited"] = true;
                ra.Remove("supports");

                tools[t.Name] = ra;
                congCu.Add(t.Name);
            }

            bool daDoi = BamSoDong(goc.Tools, congCu) != sua.ConsensusHash;
            SampleResult ketQua = new SampleResult(goc.SampleId, goc.ApprovedCount, tools, goc.Approved);
            return new KetQuaDaSua(ketQua, congCu.OrderBy(k => k, StringComparer.Ordinal).ToList(), daDoi);
        }
    }
}
