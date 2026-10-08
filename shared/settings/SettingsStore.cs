using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Crowd.BuildingBlocks.Settings;

namespace Crowd.Settings
{
    /// <summary>
    /// Gia tri setting trong BO NHO cua mot tien trinh — ISettings doc tu day (nhanh,
    /// khong cham DB moi lan). Duoc nap tu bang settings_replica luc khoi dong, nap lai
    /// dinh ky (moi instance cua service tu nap — event chi toi MOT instance), va cap
    /// nhat ngay khi chinh instance nay xu ly setting.changed.
    /// </summary>
    public sealed class SettingsStore : ISettings
    {
        private readonly ConcurrentDictionary<string, GiaTri> _giaTri =
            new ConcurrentDictionary<string, GiaTri>(StringComparer.Ordinal);

        private sealed class GiaTri
        {
            public GiaTri(JsonNode node, long version)
            {
                Node = node;
                Version = version;
            }

            public JsonNode Node { get; }

            public long Version { get; }
        }

        /// <summary>Ghi mot gia tri neu version moi hon. Khoa la (khong co trong danh muc) bi bo qua.</summary>
        public bool ApDung(string key, string valueJson, long version)
        {
            SettingDefinition? def = SettingCatalog.Tim(key);
            if (def == null)
            {
                return false;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(valueJson);
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }

            if (node == null || def.KiemGiaTri(node) != null)
            {
                // Gia tri hong (khong the xay ra neu admin-svc kiem dung) — giu gia tri cu.
                return false;
            }

            bool doi = false;
            _giaTri.AddOrUpdate(
                key,
                k =>
                {
                    doi = true;
                    return new GiaTri(node, version);
                },
                (k, cu) =>
                {
                    if (version <= cu.Version)
                    {
                        return cu;
                    }

                    doi = true;
                    return new GiaTri(node, version);
                });
            return doi;
        }

        /// <summary>So setting dang co trong bo nho (de log / kiem tra).</summary>
        public int SoLuong
        {
            get { return _giaTri.Count; }
        }

        public IReadOnlyDictionary<string, long> PhienBan()
        {
            Dictionary<string, long> d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, GiaTri> kv in _giaTri)
            {
                d[kv.Key] = kv.Value.Version;
            }

            return d;
        }

        private JsonNode Lay(string key, SettingType kieu)
        {
            SettingDefinition def = SettingCatalog.Lay(key);
            if (def.Type != kieu)
            {
                throw new InvalidOperationException("Setting '" + key + "' kieu " + def.Type + ", khong phai " + kieu + ".");
            }

            GiaTri? gt;
            return _giaTri.TryGetValue(key, out gt) ? gt.Node : def.DefaultValue;
        }

        public bool DungSai(string key)
        {
            return Lay(key, SettingType.DungSai).GetValue<bool>();
        }

        public int SoNguyen(string key)
        {
            return (int)SoDouble(Lay(key, SettingType.SoNguyen));
        }

        public long SoLon(string key)
        {
            return (long)SoDouble(Lay(key, SettingType.SoLon));
        }

        public double SoThuc(string key)
        {
            return SoDouble(Lay(key, SettingType.SoThuc));
        }

        public TimeSpan ThoiGian(string key)
        {
            return TimeSpan.FromSeconds(SoDouble(Lay(key, SettingType.ThoiGian)));
        }

        public string Chuoi(string key)
        {
            return Lay(key, SettingType.Chuoi).GetValue<string>();
        }

        private static double SoDouble(JsonNode n)
        {
            JsonValue v = (JsonValue)n;
            double d;
            if (v.TryGetValue(out d))
            {
                return d;
            }

            return double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
