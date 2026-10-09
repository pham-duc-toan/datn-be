using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using ClickHouse.Client.Utility;

namespace Crowd.Gate.Infrastructure.ClickHouse
{
    /// <summary>Mot dong click_events: luot XEM trang vuot link hoac luot NOP bai.</summary>
    public sealed class DongClick
    {
        public Guid ClickId { get; set; }

        public DateTimeOffset At { get; set; }

        /// <summary>"view" | "submit".</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Voi submit: ten KetQuaLuot (camelCase). Voi view: rong.</summary>
        public string Outcome { get; set; } = string.Empty;

        public Guid LinkId { get; set; }

        public Guid OwnerId { get; set; }

        public Guid? CampaignId { get; set; }

        public Guid? ProjectId { get; set; }

        public int LabelCount { get; set; }

        /// <summary>Doanh thu UOC TINH cua sharer (ledger moi la nguon su that).</summary>
        public long RevenueVnd { get; set; }

        public string IpHashDay { get; set; } = string.Empty;

        public string ReferrerHost { get; set; } = string.Empty;
    }

    public sealed class ThongKeNgay
    {
        public DateOnly Ngay { get; set; }

        public long LuotXem { get; set; }

        public long LuotNop { get; set; }

        public long LuotTinhTien { get; set; }

        public long DoanhThuVnd { get; set; }
    }

    public sealed class ThongKeNhom
    {
        public string Khoa { get; set; } = string.Empty;

        public long LuotXem { get; set; }

        public long LuotTinhTien { get; set; }

        public long DoanhThuVnd { get; set; }
    }

    /// <summary>
    /// click_events tren ClickHouse (ch-gate): bang append-only, hang trieu dong / ngay,
    /// truy van 100% la tong hop theo thoi gian (FS-07) — workload OLAP, ly do gate dung
    /// ClickHouse thay Postgres (docs 3.2).
    ///
    /// ReplacingMergeTree theo click_id: relay giao lai (at-least-once) khong lam dem doi;
    /// truy van dung FINAL de gop ban trung chua kip merge.
    /// </summary>
    public sealed class ClickStore
    {
        private const string TaoBang = @"
CREATE TABLE IF NOT EXISTS click_events
(
    click_id      UUID,
    event_time    DateTime64(3, 'UTC'),
    event_date    Date DEFAULT toDate(event_time),
    kind          LowCardinality(String),
    outcome       LowCardinality(String),
    link_id       UUID,
    owner_id      UUID,
    campaign_id   Nullable(UUID),
    project_id    Nullable(UUID),
    label_count   UInt16,
    revenue_vnd   Int64,
    ip_hash_day   String,
    referrer_host String
)
ENGINE = ReplacingMergeTree
PARTITION BY toYYYYMM(event_date)
ORDER BY (owner_id, event_date, link_id, click_id)";

        private static readonly string[] Cot = new string[]
        {
            "click_id", "event_time", "kind", "outcome", "link_id", "owner_id", "campaign_id",
            "project_id", "label_count", "revenue_vnd", "ip_hash_day", "referrer_host",
        };

        private readonly string _chuoiKetNoi;

        public ClickStore(string chuoiKetNoi)
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
                await c.ExecuteStatementAsync(TaoBang);
            }
        }

        public async Task GhiAsync(IReadOnlyList<DongClick> dong, CancellationToken ct)
        {
            if (dong == null || dong.Count == 0)
            {
                return;
            }

            List<object?[]> hang = new List<object?[]>();
            foreach (DongClick d in dong)
            {
                hang.Add(new object?[]
                {
                    d.ClickId, d.At.UtcDateTime, d.Kind, d.Outcome, d.LinkId, d.OwnerId, d.CampaignId,
                    d.ProjectId, (ushort)Math.Min(d.LabelCount, ushort.MaxValue), d.RevenueVnd, d.IpHashDay, d.ReferrerHost,
                });
            }

            using (ClickHouseConnection c = new ClickHouseConnection(_chuoiKetNoi))
            {
                await c.OpenAsync(ct);
                using (ClickHouseBulkCopy copy = new ClickHouseBulkCopy(c) { DestinationTableName = "click_events", ColumnNames = Cot })
                {
                    await copy.InitAsync();
                    await copy.WriteToServerAsync(hang, ct);
                }
            }
        }

        /// <summary>FS-07: theo ngay — luot xem, luot nop, luot tinh tien, doanh thu uoc tinh.</summary>
        public async Task<List<ThongKeNgay>> TheoNgayAsync(Guid ownerId, DateOnly tu, DateOnly den, CancellationToken ct)
        {
            const string sql = @"
SELECT event_date,
       countIf(kind = 'view')                            AS luot_xem,
       countIf(kind = 'submit')                          AS luot_nop,
       countIf(outcome = 'tinhTien')                     AS luot_tinh_tien,
       sumIf(revenue_vnd, outcome = 'tinhTien')          AS doanh_thu
FROM click_events FINAL
WHERE owner_id = {o:UUID} AND event_date BETWEEN {tu:Date} AND {den:Date}
GROUP BY event_date
ORDER BY event_date";

            List<ThongKeNgay> kq = new List<ThongKeNgay>();
            using (ClickHouseConnection c = new ClickHouseConnection(_chuoiKetNoi))
            {
                await c.OpenAsync(ct);
                using (ClickHouseCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.AddParameter("o", ownerId);
                    cmd.AddParameter("tu", tu.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    cmd.AddParameter("den", den.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    using (DbDataReader r = await cmd.ExecuteReaderAsync(ct))
                    {
                        while (await r.ReadAsync(ct))
                        {
                            kq.Add(new ThongKeNgay
                            {
                                Ngay = DateOnly.FromDateTime(Convert.ToDateTime(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)),
                                LuotXem = Convert.ToInt64(r.GetValue(1), System.Globalization.CultureInfo.InvariantCulture),
                                LuotNop = Convert.ToInt64(r.GetValue(2), System.Globalization.CultureInfo.InvariantCulture),
                                LuotTinhTien = Convert.ToInt64(r.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
                                DoanhThuVnd = Convert.ToInt64(r.GetValue(4), System.Globalization.CultureInfo.InvariantCulture),
                            });
                        }
                    }
                }
            }

            return kq;
        }

        /// <summary>
        /// Gom theo mot cot: "link" (Top Link), "referrer" (nguon traffic), "outcome" (ly do khong tinh tien).
        /// Ten cot lay tu danh sach CO DINH — khong bao gio ghep chuoi tu nguoi dung vao SQL.
        /// </summary>
        public async Task<List<ThongKeNhom>> TheoNhomAsync(Guid ownerId, string nhom, DateOnly tu, DateOnly den, int gioiHan, CancellationToken ct)
        {
            string cot;
            switch (nhom)
            {
                case "link":
                    cot = "toString(link_id)";
                    break;
                case "referrer":
                    cot = "referrer_host";
                    break;
                case "outcome":
                    cot = "outcome";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(nhom), nhom, "Nhom thong ke khong ho tro.");
            }

            string sql = @"
SELECT " + cot + @"                                    AS khoa,
       countIf(kind = 'view')                          AS luot_xem,
       countIf(outcome = 'tinhTien')                   AS luot_tinh_tien,
       sumIf(revenue_vnd, outcome = 'tinhTien')        AS doanh_thu
FROM click_events FINAL
WHERE owner_id = {o:UUID} AND event_date BETWEEN {tu:Date} AND {den:Date}" + (nhom == "outcome" ? " AND kind = 'submit'" : string.Empty) + @"
GROUP BY khoa
ORDER BY doanh_thu DESC, luot_xem DESC
LIMIT {n:UInt32}";

            List<ThongKeNhom> kq = new List<ThongKeNhom>();
            using (ClickHouseConnection c = new ClickHouseConnection(_chuoiKetNoi))
            {
                await c.OpenAsync(ct);
                using (ClickHouseCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.AddParameter("o", ownerId);
                    cmd.AddParameter("tu", tu.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    cmd.AddParameter("den", den.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    cmd.AddParameter("n", (uint)gioiHan);
                    using (DbDataReader r = await cmd.ExecuteReaderAsync(ct))
                    {
                        while (await r.ReadAsync(ct))
                        {
                            kq.Add(new ThongKeNhom
                            {
                                Khoa = Convert.ToString(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                                LuotXem = Convert.ToInt64(r.GetValue(1), System.Globalization.CultureInfo.InvariantCulture),
                                LuotTinhTien = Convert.ToInt64(r.GetValue(2), System.Globalization.CultureInfo.InvariantCulture),
                                DoanhThuVnd = Convert.ToInt64(r.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
                            });
                        }
                    }
                }
            }

            return kq;
        }
    }
}
