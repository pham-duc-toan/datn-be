using System;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Admin
{
    // Setting he thong (admin-svc la chu). Service khac doc thang view v_settings cua admin_db vao bo nho
    // (kien truc 3.32) — setting.changed chi con de ghi nhat ky audit, khong ai dong bo theo event nua.
    //
    // Gia tri la JSON vo huong (so, true/false, chuoi) — kieu va gioi han nam o
    // Crowd.BuildingBlocks.Settings.SettingCatalog.

    /// <summary>Admin vua doi mot setting.</summary>
    public sealed record SettingChanged : IEventPayload
    {
        public static string EventType
        {
            get { return "setting.changed"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required string Key { get; init; }

        public required RawJson Value { get; init; }

        /// <summary>Tang dan theo tung khoa.</summary>
        public required long SettingVersion { get; init; }

        public Guid? ChangedBy { get; init; }

        public required DateTimeOffset ChangedAt { get; init; }
    }
}
