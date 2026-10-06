using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crowd.Labeling
{
    /// <summary>
    /// MOT NHAN, o moi dang: loai nhan + phien ban dinh dang + du lieu JSON.
    ///
    ///     TaskType      "imageClassification"
    ///     SchemaVersion 1
    ///     DataJson      {"labelIds":["do"]}
    ///
    /// Trong database la ba cot (task_type, schema_version, payload jsonb). Tren
    /// bus la mot object JSON long nhau (xem LabelPayloadJsonConverter).
    ///
    /// BAT BIEN va LUON HOP LE: chi tao duoc qua Tao(...), ma Tao luon kiem cau
    /// truc theo dinh dang va luu ban CHUAN HOA. Code nao cam duoc mot
    /// LabelPayload thi khong phai kiem lai.
    ///
    /// So sanh THEO GIA TRI (Equals tren ca ba truong): hai nhan cung y nghia la
    /// bang nhau — can cho record event so sanh duoc khi test round-trip.
    /// </summary>
    [JsonConverter(typeof(LabelPayloadJsonConverter))]
    public sealed class LabelPayload : IEquatable<LabelPayload>
    {
        private LabelPayload(string taskType, int schemaVersion, string dataJson)
        {
            TaskType = taskType;
            SchemaVersion = schemaVersion;
            DataJson = dataJson;
        }

        public string TaskType { get; }

        public int SchemaVersion { get; }

        /// <summary>JSON chuan hoa cua phan du lieu — chinh la noi dung cot payload.</summary>
        public string DataJson { get; }

        // =====================================================================
        // TAO
        // =====================================================================

        /// <summary>Tao tu du lieu JSON client gui / event mang. Sai dinh dang thi nem LabelFormatException.</summary>
        public static LabelPayload Tao(string taskType, int schemaVersion, JsonElement data)
        {
            if (string.IsNullOrWhiteSpace(taskType))
            {
                throw new LabelFormatException("Thieu loai nhan.");
            }

            ILabelFormat dinhDang = LabelFormats.Lay(taskType, schemaVersion);
            return new LabelPayload(taskType, schemaVersion, dinhDang.ChuanHoa(data));
        }

        /// <summary>Tao tu chuoi JSON — dung khi doc tu database.</summary>
        public static LabelPayload Tao(string taskType, int schemaVersion, string dataJson)
        {
            using (JsonDocument doc = DocJson(dataJson))
            {
                return Tao(taskType, schemaVersion, doc.RootElement);
            }
        }

        /// <summary>Loi tat cho phan loai anh, phien ban moi nhat: PhanLoai("do").</summary>
        public static LabelPayload PhanLoai(params string[] labelIds)
        {
            if (labelIds == null)
            {
                throw new ArgumentNullException(nameof(labelIds));
            }

            Dictionary<string, string[]> data = new Dictionary<string, string[]>();
            data["labelIds"] = labelIds;

            string taskType = LabelTaskTypes.ImageClassification;
            return Tao(taskType, LabelFormats.PhienBanMoiNhat(taskType), JsonSerializer.Serialize(data));
        }

        // =====================================================================
        // DOC
        // =====================================================================

        /// <summary>Cac lop nhan xuat hien trong nhan (kiem voi tap nhan cua du an).</summary>
        public IReadOnlyList<string> CacLop()
        {
            using (JsonDocument doc = DocJson(DataJson))
            {
                return DinhDang().CacLop(doc.RootElement);
            }
        }

        /// <summary>
        /// Nhan nay co khop dap an khong (cau hoi vang, bai test). Khac loai nhan
        /// la khong khop — khong so duoc bounding box voi phan loai.
        /// </summary>
        public bool KhopDapAn(LabelPayload dapAn)
        {
            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            if (TaskType != dapAn.TaskType || SchemaVersion != dapAn.SchemaVersion)
            {
                return false;
            }

            using (JsonDocument nop = DocJson(DataJson))
            using (JsonDocument dung = DocJson(dapAn.DataJson))
            {
                return DinhDang().KhopDapAn(nop.RootElement, dung.RootElement);
            }
        }

        /// <summary>Du lieu duoi dang JsonElement doc lap (da Clone) — de tra ra API.</summary>
        public JsonElement DataElement()
        {
            using (JsonDocument doc = DocJson(DataJson))
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

        // =====================================================================

        private ILabelFormat DinhDang()
        {
            return LabelFormats.Lay(TaskType, SchemaVersion);
        }

        private static JsonDocument DocJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new LabelFormatException("Du lieu nhan rong.");
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
}
