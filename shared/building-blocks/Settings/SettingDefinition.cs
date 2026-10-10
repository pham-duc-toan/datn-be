using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Linq;

namespace Crowd.BuildingBlocks.Settings
{
    /// <summary>Kieu gia tri cua mot setting. Luu trong DB duoi dang JSON vo huong (so, bool, chuoi).</summary>
    public enum SettingType
    {
        /// <summary>true / false.</summary>
        DungSai,

        /// <summary>So nguyen 32 bit.</summary>
        SoNguyen,

        /// <summary>So nguyen 64 bit (tien, byte).</summary>
        SoLon,

        /// <summary>So thuc.</summary>
        SoThuc,

        /// <summary>Khoang thoi gian, luu bang SO GIAY (co the le, vd 0.5).</summary>
        ThoiGian,

        /// <summary>Chuoi.</summary>
        Chuoi,
    }

    /// <summary>Doi gia tri thi co tac dung luc nao.</summary>
    public enum SettingEffect
    {
        /// <summary>Ap dung ngay cho moi thao tac tiep theo (vai giay de lan toi moi service).</summary>
        NewOperations,

        /// <summary>Chi co tac dung khi service khoi dong lai (vd prefetch cua kenh RabbitMQ).</summary>
        Restart,
    }

    /// <summary>
    /// DINH NGHIA mot setting: khoa, kieu, gioi han, mo ta, gia tri KHOI TAO.
    ///
    /// Day khong phai noi luu gia tri: gia tri that nam trong bang settings cua
    /// admin-svc (admin sua luc chay). Gia tri khoi tao chi dung de nap lan dau va
    /// lam du phong khi ban sao chua kip dong bo.
    /// </summary>
    public sealed class SettingDefinition
    {
        public SettingDefinition(
            string key,
            string group,
            SettingType type,
            JsonNode defaultValue,
            double? min,
            double? max,
            string unit,
            SettingEffect effect,
            string description)
            : this(key, group, type, defaultValue, min, max, unit, effect, description, null)
        {
        }

        /// <param name="choices">Chi voi kieu Chuoi: tap gia tri hop le (setting dang "chon mot"). null = chuoi tu do.</param>
        public SettingDefinition(
            string key,
            string group,
            SettingType type,
            JsonNode defaultValue,
            double? min,
            double? max,
            string unit,
            SettingEffect effect,
            string description,
            IReadOnlyList<string>? choices)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Thieu khoa setting.", nameof(key));
            }

            Key = key;
            Group = group;
            Type = type;
            if (defaultValue == null)
            {
                throw new ArgumentNullException(nameof(defaultValue));
            }

            // Chuan hoa ve dang "doc tu JSON" — giong gia tri doc tu DB/event.
            DefaultValue = JsonNode.Parse(defaultValue.ToJsonString())!;
            Min = min;
            Max = max;
            Unit = unit;
            Effect = effect;
            Description = description;

            if (choices != null && type != SettingType.Chuoi)
            {
                throw new ArgumentException("Chi setting kieu chuoi moi co danh sach lua chon: " + key, nameof(choices));
            }

            Choices = choices;

            string? loi = KiemGiaTri(DefaultValue);
            if (loi != null)
            {
                throw new ArgumentException("Gia tri khoi tao cua '" + key + "' sai: " + loi);
            }
        }

        /// <summary>Vd "fee.platform_percent". Doi ten la mat gia tri admin da dat.</summary>
        public string Key { get; }

        /// <summary>Nhom hien thi tren man hinh admin, vd "Phi va tien".</summary>
        public string Group { get; }

        public SettingType Type { get; }

        public JsonNode DefaultValue { get; }

        public double? Min { get; }

        public double? Max { get; }

        /// <summary>Don vi hien thi: "%", "dong", "giay", "byte", "ky tu"...</summary>
        public string Unit { get; }

        public SettingEffect Effect { get; }

        public string Description { get; }

        /// <summary>Gia tri hop le cua setting "chon mot" (vd quality.redundancy_policy). null = khong gioi han.</summary>
        public IReadOnlyList<string>? Choices { get; }

        /// <summary>Ten kieu tren API / catalog.json: bool, int, long, double, durationSeconds, text.</summary>
        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case SettingType.DungSai:
                        return "bool";
                    case SettingType.SoNguyen:
                        return "int";
                    case SettingType.SoLon:
                        return "long";
                    case SettingType.SoThuc:
                        return "double";
                    case SettingType.ThoiGian:
                        return "durationSeconds";
                    default:
                        return "text";
                }
            }
        }

        /// <summary>"newOperations" hoac "restart".</summary>
        public string EffectName
        {
            get { return Effect == SettingEffect.Restart ? "restart" : "newOperations"; }
        }

        /// <summary>Kiem mot gia tri admin gui len. null = hop le, nguoc lai la ly do sai.</summary>
        public string? KiemGiaTri(JsonNode? giaTri)
        {
            JsonValue? v = giaTri as JsonValue;
            if (v == null)
            {
                return "phai la mot gia tri don (so, true/false hoac chuoi).";
            }

            if (Type == SettingType.DungSai)
            {
                bool b;
                return v.TryGetValue(out b) ? null : "phai la true hoac false.";
            }

            if (Type == SettingType.Chuoi)
            {
                string? s;
                if (!v.TryGetValue(out s) || s == null)
                {
                    return "phai la chuoi.";
                }

                if (Max.HasValue && s.Length > Max.Value)
                {
                    return "toi da " + Max.Value.ToString(CultureInfo.InvariantCulture) + " ky tu.";
                }

                if (Choices != null && !Choices.Contains(s))
                {
                    return "phai la mot trong: " + string.Join(", ", Choices) + ".";
                }

                return null;
            }

            // JsonValue tao bang JsonValue.Create(int) khong TryGetValue<double> duoc —
            // doc qua chuoi JSON cho moi kieu so.
            if (v.GetValueKind() != JsonValueKind.Number)
            {
                return "phai la so.";
            }

            double so = double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

            if ((Type == SettingType.SoNguyen || Type == SettingType.SoLon) && Math.Floor(so) != so)
            {
                return "phai la so nguyen.";
            }

            if (Type == SettingType.SoNguyen && (so < int.MinValue || so > int.MaxValue))
            {
                return "vuot gioi han so nguyen.";
            }

            if (Min.HasValue && so < Min.Value)
            {
                return "nho nhat la " + Min.Value.ToString(CultureInfo.InvariantCulture) + " " + Unit + ".";
            }

            if (Max.HasValue && so > Max.Value)
            {
                return "lon nhat la " + Max.Value.ToString(CultureInfo.InvariantCulture) + " " + Unit + ".";
            }

            return null;
        }
    }
}
