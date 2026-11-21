using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using ClickHouse.Client.Utility;

namespace Crowd.Reco.Infrastructure.ClickHouse
{
    /// <summary>Mot hanh vi: hien the / mo / khong quan tam (frontend) hoac bat dau / nop / dat / truot / bo do (event).</summary>
    public sealed class DongHanhVi
    {
        public Guid EventId { get; set; }

        public DateTimeOffset At { get; set; }

        public Guid UserId { get; set; }

        public Guid ProjectId { get; set; }

        public string Kind { get; set; } = string.Empty;

        public int Position { get; set; } = -1;

        public string SessionId { get; set; } = string.Empty;

        public string RequestId { get; set; } = string.Empty;

        public string ModelVersion { get; set; } = string.Empty;

        public string AbGroup { get; set; } = string.Empty;
    }

    /// <summary>Mot the da tra trong feed, KEM vector dac trung luc tra — du lieu huan luyen cua ml-svc.</summary>
    public sealed class DongDaTra
    {
        public Guid EventId { get; set; }

        public DateTimeOffset At { get; set; }

        public string RequestId { get; set; } = string.Empty;

        public Guid UserId { get; set; }

        public Guid ProjectId { get; set; }

        public int Position { get; set; }

        public string SessionId { get; set; } = string.Empty;

        public string ModelVersion { get; set; } = string.Empty;

        public string AbGroup { get; set; } = string.Empty;

        public double Score { get; set; }

        public double PStart { get; set; }

        public double PPass { get; set; }

        public double[] Features { get; set; } = Array.Empty<double>();

        public bool Explore { get; set; }
    }

    /// <summary>
    /// Database ClickHouse "reco" DO reco-svc SO HUU (user rieng; ml-svc doc bang user chi doc — kien truc 3.39):
    ///   reco_events  — hanh vi (append-only, giu 90 ngay);
    ///   feed_served  — the da tra kem dac trung (giu 90 ngay) — ml-svc ghep voi reco_events thanh du lieu hoc;
    ///   reco_daily   — bang gop theo ngay (materialized view) cho so sanh A/B nhanh.
    /// ReplacingMergeTree theo event_id: relay giao lai (at-least-once) khong lam dem doi.
    /// </summary>
    public sealed class RecoClickStore
    {
        private static readonly string[] BangTao = new string[]
        {
            @"CREATE TABLE IF NOT EXISTS reco_events
(
    event_id      UUID,
    event_time    DateTime64(3, 'UTC'),
    event_date    Date DEFAULT toDate(event_time),
    user_id       UUID,
    project_id    UUID,
    kind          LowCardinality(String),
    position      Int32,
    session_id    String,
    request_id    String,
    model_version LowCardinality(String),
    ab_group      LowCardinality(String)
)
ENGINE = ReplacingMergeTree
PARTITION BY toYYYYMM(event_date)
ORDER BY (event_date, user_id, event_id)
TTL event_date + INTERVAL 90 DAY",
            @"CREATE TABLE IF NOT EXISTS feed_served
(
    event_id      UUID,
    event_time    DateTime64(3, 'UTC'),
    event_date    Date DEFAULT toDate(event_time),
    request_id    String,
    user_id       UUID,
    project_id    UUID,
    position      Int32,
    session_id    String,
    model_version LowCardinality(String),
    ab_group      LowCardinality(String),
    score         Float64,
    p_start       Float64,
    p_pass        Float64,
    features      Array(Float64),
    explore       UInt8
)
ENGINE = ReplacingMergeTree
PARTITION BY toYYYYMM(event_date)
ORDER BY (event_date, user_id, event_id)
TTL event_date + INTERVAL 90 DAY",
            @"CREATE TABLE IF NOT EXISTS reco_daily
(
    event_date    Date,
    kind          LowCardinality(String),
    ab_group      LowCardinality(String),
    model_version LowCardinality(String),
    n             UInt64
)
ENGINE = SummingMergeTree
ORDER BY (event_date, kind, ab_group, model_version)",
            @"CREATE MATERIALIZED VIEW IF NOT EXISTS reco_daily_mv TO reco_daily AS
SELECT event_date, kind, ab_group, model_version, count() AS n
FROM reco_events
GROUP BY event_date, kind, ab_group, model_version",
        };

        private static readonly string[] CotHanhVi = new string[]
        {
            "event_id", "event_time", "user_id", "project_id", "kind", "position", "session_id", "request_id", "model_version", "ab_group",
        };

        private static readonly string[] CotDaTra = new string[]
        {
            "event_id", "event_time", "request_id", "user_id", "project_id", "position", "session_id", "model_version", "ab_group",
            "score", "p_start", "p_pass", "features", "explore",
        };

        private readonly string _chuoiKetNoi;

        public RecoClickStore(string chuoiKetNoi)
        {
            if (string.IsNullOrWhiteSpace(chuoiKetNoi))
            {
                throw new ArgumentException("Thieu chuoi ket noi ClickHouse.", nameof(chuoiKetNoi));
            }

            _chuoiKetNoi = chuoiKetNoi;
        }

        public async Task TaoBangAsync(CancellationToken ct)
        {
            using (ClickHouseConnection c = new ClickHouseConnection(_chuoiKetNoi))
            {
                await c.OpenAsync(ct);
                foreach (string sql in BangTao)
                {
                    await c.ExecuteStatementAsync(sql);
                }
            }
        }

        public async Task GhiHanhViAsync(IReadOnlyList<DongHanhVi> dong, CancellationToken ct)
        {
            if (dong == null || dong.Count == 0)
            {
                return;
            }

            List<object?[]> hang = new List<object?[]>();
            foreach (DongHanhVi d in dong)
            {
                hang.Add(new object?[]
                {
                    d.EventId, d.At.UtcDateTime, d.UserId, d.ProjectId, d.Kind, d.Position, d.SessionId, d.RequestId, d.ModelVersion, d.AbGroup,
                });
            }

            await GhiAsync("reco_events", CotHanhVi, hang, ct);
        }

        public async Task GhiDaTraAsync(IReadOnlyList<DongDaTra> dong, CancellationToken ct)
        {
            if (dong == null || dong.Count == 0)
            {
                return;
            }

            List<object?[]> hang = new List<object?[]>();
            foreach (DongDaTra d in dong)
            {
                hang.Add(new object?[]
                {
                    d.EventId, d.At.UtcDateTime, d.RequestId, d.UserId, d.ProjectId, d.Position, d.SessionId, d.ModelVersion, d.AbGroup,
                    d.Score, d.PStart, d.PPass, d.Features, d.Explore ? (byte)1 : (byte)0,
                });
            }

            await GhiAsync("feed_served", CotDaTra, hang, ct);
        }

        private async Task GhiAsync(string bang, string[] cot, List<object?[]> hang, CancellationToken ct)
        {
            using (ClickHouseConnection c = new ClickHouseConnection(_chuoiKetNoi))
            {
                await c.OpenAsync(ct);
                using (ClickHouseBulkCopy copy = new ClickHouseBulkCopy(c) { DestinationTableName = bang, ColumnNames = cot })
                {
                    await copy.InitAsync();
                    await copy.WriteToServerAsync(hang, ct);
                }
            }
        }
    }
}
