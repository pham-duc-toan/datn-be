using System;

namespace Crowd.Admin.Api.Entities
{
    /// <summary>
    /// Gia tri HIEN TAI cua mot setting he thong (bang settings) — nguon su that duy
    /// nhat. Cac service khac chi giu ban sao (settings_replica).
    /// </summary>
    public sealed class Setting
    {
        private Setting()
        {
            Key = string.Empty;
            ValueJson = "null";
        }

        public string Key { get; private set; }

        /// <summary>JSON vo huong: 30, true, 0.5, "chuoi".</summary>
        public string ValueJson { get; private set; }

        /// <summary>Tang 1 moi lan doi — ban sao dung de bo qua event cu den tre.</summary>
        public long Version { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        /// <summary>Admin doi gan nhat. null = gia tri khoi tao tu danh muc.</summary>
        public Guid? UpdatedBy { get; private set; }

        public static Setting KhoiTao(string key, string valueJson, DateTimeOffset luc)
        {
            Setting s = new Setting();
            s.Key = key;
            s.ValueJson = valueJson;
            s.Version = 1;
            s.UpdatedAt = luc;
            return s;
        }

        public void Doi(string valueJson, Guid nguoiDoi, DateTimeOffset luc)
        {
            ValueJson = valueJson;
            Version = Version + 1;
            UpdatedAt = luc;
            UpdatedBy = nguoiDoi;
        }
    }

    /// <summary>Lich su doi setting (bang setting_history): ai doi, luc nao, cu → moi, ly do.</summary>
    public sealed class SettingHistory
    {
        private SettingHistory()
        {
            Key = string.Empty;
            NewValueJson = "null";
        }

        public long Id { get; private set; }

        public string Key { get; private set; }

        /// <summary>null = lan khoi tao.</summary>
        public string? OldValueJson { get; private set; }

        public string NewValueJson { get; private set; }

        public long Version { get; private set; }

        public Guid? ChangedBy { get; private set; }

        public DateTimeOffset ChangedAt { get; private set; }

        public string? Reason { get; private set; }

        public static SettingHistory Tao(
            string key, string? cu, string moi, long version, Guid? nguoiDoi, DateTimeOffset luc, string? lyDo)
        {
            SettingHistory h = new SettingHistory();
            h.Key = key;
            h.OldValueJson = cu;
            h.NewValueJson = moi;
            h.Version = version;
            h.ChangedBy = nguoiDoi;
            h.ChangedAt = luc;
            h.Reason = lyDo;
            return h;
        }
    }
}
