using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Crowd.Labeling.Schemas;
using Crowd.Labeling.Tools;

namespace Crowd.Labeling
{
    /// <summary>
    /// MOT NHAN, o moi loai du lieu va moi cong cu:
    ///
    ///     TaskType      "image"          ← loai du lieu cua du an
    ///     SchemaVersion 1                ← phien ban dinh dang "ket qua theo cong cu"
    ///     DataJson      {"loai_anh":{"labelIds":["ngay"]},
    ///                    "vat_the":[{"labelId":"xe","x":10,"y":20,"w":100,"h":50}]}
    ///
    /// DataJson la object: KHOA = ten cong cu trong tap nhan, GIA TRI = ket qua
    /// cua cong cu do (hinh dang theo loai cong cu, xem ToolKinds).
    ///
    /// Database: ba cot (task_type, schema_version, payload jsonb). Event: mot
    /// object long nhau (LabelPayloadJsonConverter).
    ///
    /// Hai cach tao:
    ///   - Tao(tapNhan, duLieu, mau): tu nguoi dung — KIEM DAY DU (JSON Schema +
    ///     ngu nghia theo tap nhan va thong tin mau), roi chuan hoa.
    ///   - TuLuuTru(...): doc lai tu database / event — du lieu da duoc kiem luc
    ///     tao, chi chuan hoa.
    ///
    /// So sanh THEO GIA TRI tren ca ba truong.
    /// </summary>
    [JsonConverter(typeof(LabelPayloadJsonConverter))]
    public sealed class LabelPayload : IEquatable<LabelPayload>
    {
        /// <summary>Phien ban dinh dang "ket qua theo cong cu". Doi hinh dang = tang so nay.</summary>
        public const int PhienBanHienTai = 1;

        private LabelPayload(string taskType, int schemaVersion, string dataJson)
        {
            TaskType = taskType;
            SchemaVersion = schemaVersion;
            DataJson = dataJson;
        }

        /// <summary>Loai du lieu: image | text | audio | video | pair.</summary>
        public string TaskType { get; }

        public int SchemaVersion { get; }

        /// <summary>JSON chuan hoa cua phan du lieu — chinh la noi dung cot payload.</summary>
        public string DataJson { get; }

        // =====================================================================
        // TAO
        // =====================================================================

        /// <summary>
        /// Tu du lieu nguoi dung gui: kiem theo tap nhan va thong tin mau. Sai thi
        /// nem LabelFormatException — ma "nhan_sai_dinh_dang" (hinh dang) hoac
        /// "nhan_khong_hop_le" (ngu nghia).
        /// </summary>
        public static LabelPayload Tao(LabelSchema tapNhan, JsonElement duLieu, SampleMetadata? mau)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            // 1. Hinh dang: JSON Schema ghep tu tap nhan.
            JsonSchemas.KiemNhan(tapNhan, duLieu);

            // 2. Ngu nghia + chuan hoa, tung cong cu.
            SampleMetadata thongTinMau = mau ?? SampleMetadata.Rong;
            JsonObject vao = (JsonObject)JsonNode.Parse(duLieu.GetRawText())!;
            JsonObject ra = new JsonObject();

            foreach (ToolDefinition t in tapNhan.Tools)
            {
                JsonNode? ketQua = vao[t.Name];
                if (ketQua == null)
                {
                    continue;
                }

                ra[t.Name] = ToolKindRegistry.Lay(t.Kind).KiemVaChuanHoa(ketQua, t, thongTinMau);
            }

            return new LabelPayload(tapNhan.Modality, PhienBanHienTai, JsonCanonical.Viet(ra));
        }

        public static LabelPayload Tao(LabelSchema tapNhan, string duLieuJson, SampleMetadata? mau)
        {
            using (JsonDocument doc = DocJson(duLieuJson))
            {
                return Tao(tapNhan, doc.RootElement, mau);
            }
        }

        /// <summary>
        /// Doc lai nhan DA KIEM tu database / event: chi kiem la object JSON va
        /// loai du lieu / phien ban duoc ho tro, roi chuan hoa.
        /// </summary>
        public static LabelPayload TuLuuTru(string taskType, int schemaVersion, string dataJson)
        {
            if (!Modalities.HopLe(taskType))
            {
                throw new LabelFormatException("loai_nhan_chua_ho_tro", "Loai du lieu '" + taskType + "' khong hop le.");
            }

            if (schemaVersion != PhienBanHienTai)
            {
                throw new LabelFormatException(
                    "loai_nhan_chua_ho_tro",
                    "Chua ho tro dinh dang nhan phien ban " + schemaVersion + ".");
            }

            JsonObject? o;
            try
            {
                o = JsonNode.Parse(dataJson ?? string.Empty) as JsonObject;
            }
            catch (JsonException ex)
            {
                throw new LabelFormatException("Du lieu nhan khong phai JSON hop le.", ex);
            }

            if (o == null)
            {
                throw new LabelFormatException("Du lieu nhan phai la mot object.");
            }

            return new LabelPayload(taskType, schemaVersion, JsonCanonical.Viet(o));
        }

        // =====================================================================
        // DOC
        // =====================================================================

        /// <summary>Ket qua cua mot cong cu; null neu nhan khong co cong cu nay.</summary>
        public JsonNode? KetQua(string tenCongCu)
        {
            JsonObject o = (JsonObject)JsonNode.Parse(DataJson)!;
            JsonNode? n = o[tenCongCu];
            return n == null ? null : n.DeepClone();
        }

        /// <summary>
        /// Nhan nay co khop dap an khong (cau vang, bai test). Moi cong cu co trong
        /// dap an phai khop theo cach cua loai cong cu do (tap lop, IoU, F1, CER).
        /// </summary>
        public bool KhopDapAn(LabelSchema tapNhan, LabelPayload dapAn)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            if (TaskType != dapAn.TaskType || SchemaVersion != dapAn.SchemaVersion)
            {
                return false;
            }

            foreach (ToolDefinition t in tapNhan.Tools)
            {
                JsonNode? nop = KetQua(t.Name);
                JsonNode? dung = dapAn.KetQua(t.Name);

                if (dung == null && nop == null)
                {
                    continue;
                }

                if (dung == null || nop == null)
                {
                    return false;
                }

                if (!ToolKindRegistry.Lay(t.Kind).Khop(nop, dung, t))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Du lieu duoi dang JsonElement doc lap (da Clone) — de tra ra API.</summary>
        public JsonElement DataElement()
        {
            using (JsonDocument doc = JsonDocument.Parse(DataJson))
            {
                return doc.RootElement.Clone();
            }
        }

        // =====================================================================
        // SO SANH THEO GIA TRI
        // =====================================================================

        public bool Equals(LabelPayload? other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(TaskType, other.TaskType, StringComparison.Ordinal)
                   && SchemaVersion == other.SchemaVersion
                   && string.Equals(DataJson, other.DataJson, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as LabelPayload);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TaskType, SchemaVersion, DataJson);
        }

        public override string ToString()
        {
            return TaskType + "/v" + SchemaVersion + " " + DataJson;
        }

        private static JsonDocument DocJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LabelFormatException("nhan_sai_dinh_dang", "Du lieu nhan rong.");
            }

            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new LabelFormatException("Du lieu nhan khong phai JSON hop le.", ex);
            }
        }
    }

    /// <summary>
    /// Gop nhan cua nhieu nguoi (da duyet) cho MOT mau, theo tung cong cu:
    ///     {"loai_anh": {"method":"majority","final":{...},"votes":{...},"disputed":false},
    ///      "vat_the":  {"method":"none"}}
    /// "none" = cong cu chua co cach gop tu dong — ket qua cuoi la cac nhan da duyet.
    /// </summary>
    public static class LabelAggregator
    {
        public static JsonObject Gop(LabelSchema tapNhan, IReadOnlyList<LabelPayload> nhan)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (nhan == null)
            {
                throw new ArgumentNullException(nameof(nhan));
            }

            JsonObject ra = new JsonObject();
            foreach (ToolDefinition t in tapNhan.Tools)
            {
                List<JsonNode> ketQua = new List<JsonNode>();
                foreach (LabelPayload p in nhan)
                {
                    JsonNode? k = p.KetQua(t.Name);
                    if (k != null)
                    {
                        ketQua.Add(k);
                    }
                }

                JsonObject gop = ketQua.Count == 0
                    ? new JsonObject { ["method"] = "none" }
                    : ToolKindRegistry.Lay(t.Kind).Gop(ketQua, t);

                gop["kind"] = t.Kind;
                ra[t.Name] = gop;
            }

            return ra;
        }
    }
}
