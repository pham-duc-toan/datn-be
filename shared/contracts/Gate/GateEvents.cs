using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Labeling;

namespace Crowd.Contracts.Gate
{
    // gate-svc (P2): trang vuot link. Khach vang lai giai k cau vang + m cau that;
    // DAT cau vang thi nhan cau that duoc gui sang annotation-svc va (neu luot hop le)
    // nguoi chia se link duoc tra tien.

    /// <summary>Khach da qua cau vang: nhan cua cac cau THAT (khong co cau vang).</summary>
    public sealed record GateSolved : IEventPayload
    {
        public static string EventType
        {
            get { return "gate.solved"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        public required Guid SessionId { get; init; }

        public required Guid LinkId { get; init; }

        public required Guid ProjectId { get; init; }

        public required IReadOnlyList<GateLabel> Labels { get; init; }

        public required DateTimeOffset SolvedAt { get; init; }
    }

    public sealed record GateLabel
    {
        public required Guid SampleId { get; init; }

        public string? StorageKey { get; init; }

        public RawJson? SampleContent { get; init; }

        public required RawJson SampleMetadata { get; init; }

        public required LabelPayload LabelPayload { get; init; }
    }

    /// <summary>
    /// Luot vuot link HOP LE (dat cau vang, khong trung IP trong cua so, khong tu vuot):
    /// ledger chuyen tu ky quy du an sang vi nguoi chia se (treo) + phi nen tang.
    /// So tien tinh theo don gia da chot cua du an (dac ta 2.4):
    ///   sharer   = so nhan that x (don gia − phi moi nhan)
    ///   ky quy tru = so nhan that x (don gia + phi moi nhan)
    /// </summary>
    public sealed record ClickValidated : IEventPayload
    {
        public static string EventType
        {
            get { return "click.validated"; }
        }

        public static int Version
        {
            get { return 1; }
        }

        /// <summary>Khoa idempotency o ledger: moi luot chi tra mot lan.</summary>
        public required Guid ClickId { get; init; }

        public required Guid LinkId { get; init; }

        public required Guid SharerId { get; init; }

        public required Guid ProjectId { get; init; }

        public required int LabelCount { get; init; }

        public required long SharerAmountVnd { get; init; }

        /// <summary>Phan nen tang giu (ky quy tru = SharerAmountVnd + PlatformAmountVnd).</summary>
        public required long PlatformAmountVnd { get; init; }

        public required DateTimeOffset ValidatedAt { get; init; }
    }
}
