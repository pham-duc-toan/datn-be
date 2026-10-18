using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Entities;
using Crowd.Admin.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Admin.Api.Services
{
    /// <summary>
    /// Doc nhat ky audit (FP-06) va thong ke tong quan (FM-09) — deu tu bang audit_events, nen admin-svc
    /// khong phai hoi service nao (khong vi pham luat "khong goi dong bo"). Thong ke chi co tu luc audit
    /// bat dau ghi (AuditSince).
    /// </summary>
    public sealed class AuditService
    {
        public const int SoNgayToiDa = 366;
        private static readonly TimeSpan LechVietNam = TimeSpan.FromHours(7);

        private readonly AdminDbContext _db;
        private readonly TimeProvider _clock;

        public AuditService(AdminDbContext db, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _clock = clock;
        }

        /// <summary>Loc theo nguoi lam, loai event (khop dung hoac tien to "project."), chuoi thao tac, khoang thoi gian.</summary>
        public async Task<AuditPageResponse> TraCuuAsync(
            Guid? actorId, string? eventType, Guid? correlationId, DateTimeOffset? from, DateTimeOffset? to,
            int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 100 ? 50 : pageSize;

            IQueryable<AuditEvent> q = _db.AuditEvents.AsNoTracking();
            if (actorId.HasValue)
            {
                Guid a = actorId.Value;
                q = q.Where(x => x.ActorId == a);
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                string loai = eventType.Trim();
                q = loai.EndsWith('.')
                    ? q.Where(x => x.EventType.StartsWith(loai))
                    : q.Where(x => x.EventType == loai);
            }

            if (correlationId.HasValue)
            {
                Guid c = correlationId.Value;
                q = q.Where(x => x.CorrelationId == c);
            }

            if (from.HasValue)
            {
                DateTimeOffset tu = from.Value.ToUniversalTime();
                q = q.Where(x => x.OccurredAt >= tu);
            }

            if (to.HasValue)
            {
                DateTimeOffset den = to.Value.ToUniversalTime();
                q = q.Where(x => x.OccurredAt < den);
            }

            int tong = await q.CountAsync(ct);
            List<AuditEvent> ds = await q.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.EventId)
                .Skip((trang - 1) * co).Take(co).ToListAsync(ct);

            return new AuditPageResponse
            {
                Items = ds.Select(e => new AuditEventResponse
                {
                    EventId = e.EventId,
                    EventType = e.EventType,
                    Producer = e.Producer,
                    OccurredAt = e.OccurredAt,
                    CorrelationId = e.CorrelationId,
                    CausationId = e.CausationId,
                    ActorId = e.ActorId,
                    ActorRole = e.ActorRole,
                    Payload = JsonNode.Parse(e.PayloadJson),
                }).ToList(),
                Page = trang,
                PageSize = co,
                Total = tong,
            };
        }

        /// <summary>Thong ke [from, to] theo ngay gio Viet Nam (mac dinh 30 ngay gan nhat).</summary>
        public async Task<StatsOverviewResponse> TongQuanAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            DateOnly homNay = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(LechVietNam).DateTime);
            DateOnly den = to ?? homNay;
            DateOnly tu = from ?? den.AddDays(-29);
            if (tu > den || den.DayNumber - tu.DayNumber + 1 > SoNgayToiDa)
            {
                throw new AdminException(400, "khoang_ngay_khong_hop_le", "from <= to, toi da " + SoNgayToiDa + " ngay.");
            }

            // Doc tu bang DEM theo ngay (stats_daily, AuditConsumer cong don) — khong quet nhat ky chi tiet:
            // event tan suat cao (nhan nop / duyet...) chi duoc dem, khong luu tung dong.
            List<DongTheoLoai> theoLoai = await _db.Database.SqlQuery<DongTheoLoai>($@"
                SELECT event_type AS ""Loai"", SUM(count)::int AS ""So"",
                       SUM(amount_vnd)::bigint AS ""Tien"", SUM(fee_vnd)::bigint AS ""Phi""
                FROM stats_daily
                WHERE day >= {tu} AND day <= {den}
                GROUP BY event_type").ToListAsync(ct);
            Dictionary<string, DongTheoLoai> loai = theoLoai.ToDictionary(x => x.Loai, StringComparer.Ordinal);

            List<DongTheoNgay> theoNgay = await _db.Database.SqlQuery<DongTheoNgay>($@"
                SELECT day AS ""Ngay"", event_type AS ""Loai"", count::int AS ""So"", amount_vnd AS ""Tien""
                FROM stats_daily
                WHERE day >= {tu} AND day <= {den}
                  AND event_type IN ('user.registered', 'attempt.submitted', 'project.graded', 'deposit.confirmed')").ToListAsync(ct);

            List<StatsDay> ngay = new List<StatsDay>();
            for (DateOnly d = tu; d <= den; d = d.AddDays(1))
            {
                DateOnly hom = d;
                ngay.Add(new StatsDay
                {
                    Date = hom,
                    NewUsers = DemNgay(theoNgay, hom, "user.registered"),
                    AttemptsSubmitted = DemNgay(theoNgay, hom, "attempt.submitted"),
                    ProjectsGraded = DemNgay(theoNgay, hom, "project.graded"),
                    DepositsVnd = theoNgay.Where(x => x.Ngay == hom && x.Loai == "deposit.confirmed").Sum(x => x.Tien),
                });
            }

            // Co so lieu tu ngay nao (ngay dau tien co event duoc dem).
            DateOnly? ngayDau = await _db.ThongKeNgay.AsNoTracking()
                .OrderBy(x => x.Day).Select(x => (DateOnly?)x.Day).FirstOrDefaultAsync(ct);
            DateTimeOffset? batDauGhi = null;
            if (ngayDau.HasValue)
            {
                batDauGhi = new DateTimeOffset(ngayDau.Value.ToDateTime(TimeOnly.MinValue), LechVietNam).ToUniversalTime();
            }

            return new StatsOverviewResponse
            {
                From = tu,
                To = den,
                AuditSince = batDauGhi,
                NewUsers = So(loai, "user.registered"),
                BusinessesVerified = So(loai, "business.verified"),
                ProjectsPublished = So(loai, "project.published"),
                AttemptsSubmitted = So(loai, "attempt.submitted"),
                ProjectsGraded = So(loai, "project.graded"),
                ProjectsCompleted = So(loai, "project.completed"),
                DepositsVnd = Tien(loai, "deposit.confirmed"),
                // funds.held: thuong bai dat + doanh thu chia se link + hoa hong gioi thieu.
                LabelerPayoutsVnd = Tien(loai, "funds.held"),
                WithdrawalsVnd = Tien(loai, "payout.requested"),
                AppealsOpened = So(loai, "attempt.appeal_opened"),
                AccountsBlocked = So(loai, "user.blocked"),
                FraudCasesConfirmed = So(loai, "fraud.case_confirmed"),
                Daily = ngay,
            };
        }

        private static int So(Dictionary<string, DongTheoLoai> loai, string ten)
        {
            DongTheoLoai? d;
            return loai.TryGetValue(ten, out d) ? d.So : 0;
        }

        private static long Tien(Dictionary<string, DongTheoLoai> loai, string ten)
        {
            DongTheoLoai? d;
            return loai.TryGetValue(ten, out d) ? d.Tien : 0;
        }

        private static int DemNgay(List<DongTheoNgay> ds, DateOnly ngay, string loai)
        {
            return ds.Where(x => x.Ngay == ngay && x.Loai == loai).Sum(x => x.So);
        }

        private sealed class DongTheoLoai
        {
            public string Loai { get; set; } = string.Empty;

            public int So { get; set; }

            public long Tien { get; set; }

            public long Phi { get; set; }
        }

        private sealed class DongTheoNgay
        {
            public DateOnly Ngay { get; set; }

            public string Loai { get; set; } = string.Empty;

            public int So { get; set; }

            public long Tien { get; set; }
        }
    }
}
