using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Admin
{
    // Setting he thong (admin-svc la chu). Moi service giu BAN SAO trong DB cua
    // minh va doc tai cho — khong goi HTTP sang admin-svc luc chay.
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

        /// <summary>Tang dan theo tung khoa. Ban sao chi ghi de khi version MOI hon (event den sai thu tu).</summary>
        public required long SettingVersion { get; init; }

        public Guid? ChangedBy { get; init; }

        public required DateTimeOffset ChangedAt { get; init; }
    }

    public sealed record SettingSnapshotItem
    {
        public required string Key { get; init; }

        public required RawJson Value { get; init; }

        public required long SettingVersion { get; init; }
    }

    /// <summary>
    /// Toan bo setting hien tai. admin-svc phat luc khoi dong, dinh ky, va khi co
    /// service xin (settings.snapshot_requested) — service lo mat event van dong bo lai duoc.
    /// </summary>
    public sealed record SettingsSnapshot : IEventPayload
    {
        public static string EventType
        {
            get { return "settings.snapshot"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required IReadOnlyList<SettingSnapshotItem> Items { get; init; }
    }

    /// <summary>Service vua khoi dong xin admin-svc phat lai toan bo setting.</summary>
    public sealed record SettingsSnapshotRequested : IEventPayload
    {
        public static string EventType
        {
            get { return "settings.snapshot_requested"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required string Service { get; init; }
    }
}
