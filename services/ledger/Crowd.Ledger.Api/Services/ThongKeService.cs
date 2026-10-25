using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Exceptions;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Escrows;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// Thong ke tien (chi doc):
    ///   - thu nhap cua labeler / sharer theo ngay hoac theo du an (FL-11) — tu bang holds: moi khoan
    ///     thu (nhan duoc duyet, lo vuot link, hoa hong) sinh dung mot khoan treo;
    ///   - chi phi mot du an (FB-20) — tu cac dong so cai cua tai khoan ky quy du an, nen khop dung
    ///     voi doi soat.
    /// Ngay tinh theo gio Viet Nam (UTC+7).
    /// </summary>
    public sealed class ThongKeService
    {
        public const int SoNgayToiDa = 366;
        private const string MuiGio = "Asia/Ho_Chi_Minh";
        private static readonly TimeSpan LechVietNam = TimeSpan.FromHours(7);

        private readonly LedgerDbContext _db;
        private readonly TimeProvider _clock;

        public ThongKeService(LedgerDbContext db, TimeProvider clock)
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

        /// <summary>
        /// Thu nhap trong [from, to] (ngay VN, mac dinh 30 ngay gan nhat). Khoan bi giu lai (link vi
        /// pham) va bi hoan cho doanh nghiep (thua tranh chap) khong tinh.
        /// </summary>
        public async Task<EarningsResponse> ThuNhapAsync(Caller caller, DateOnly? from, DateOnly? to, string? groupBy, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            string nhom = string.IsNullOrWhiteSpace(groupBy) ? "day" : groupBy.Trim().ToLowerInvariant();
            if (nhom != "day" && nhom != "project")
            {
                throw new InvalidValueException("nhom_khong_hop_le", "groupBy: day | project.");
            }

            DateOnly homNay = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(LechVietNam).DateTime);
            DateOnly den = to ?? homNay;
            DateOnly tu = from ?? den.AddDays(-29);
            if (tu > den || den.DayNumber - tu.DayNumber + 1 > SoNgayToiDa)
            {
                throw new InvalidValueException("khoang_ngay_khong_hop_le", "from <= to, tối đa " + SoNgayToiDa + " ngày.");
            }

            // Npgsql chi nhan DateTimeOffset UTC (offset 0): moc ngay gio Viet Nam doi ve UTC.
            DateTimeOffset batDau = new DateTimeOffset(tu.ToDateTime(TimeOnly.MinValue), LechVietNam).ToUniversalTime();
            DateTimeOffset ketThuc = new DateTimeOffset(den.AddDays(1).ToDateTime(TimeOnly.MinValue), LechVietNam).ToUniversalTime();

            List<DongThuNhap> dong = nhom == "day"
                ? await _db.Database.SqlQuery<DongThuNhap>($@"
                    SELECT to_char(held_at AT TIME ZONE {MuiGio}, 'YYYY-MM-DD') AS ""Khoa"", kind AS ""Nguon"",
                           SUM(amount_vnd)::bigint AS ""Tong"", COUNT(*)::int AS ""SoKhoan""
                    FROM holds
                    WHERE labeler_id = {uid} AND state NOT IN ('Withheld', 'Refunded')
                      AND held_at >= {batDau} AND held_at < {ketThuc}
                    GROUP BY 1, 2").ToListAsync(ct)
                : await _db.Database.SqlQuery<DongThuNhap>($@"
                    SELECT project_id::text AS ""Khoa"", kind AS ""Nguon"",
                           SUM(amount_vnd)::bigint AS ""Tong"", COUNT(*)::int AS ""SoKhoan""
                    FROM holds
                    WHERE labeler_id = {uid} AND state NOT IN ('Withheld', 'Refunded')
                      AND held_at >= {batDau} AND held_at < {ketThuc}
                    GROUP BY 1, 2").ToListAsync(ct);

            Dictionary<string, long> theoNguon = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["projectReward"] = 0,
                ["gateClick"] = 0,
                ["referral"] = 0,
            };
            foreach (DongThuNhap d in dong)
            {
                string nguon = char.ToLowerInvariant(d.Nguon[0]) + d.Nguon.Substring(1);
                long cu;
                theoNguon[nguon] = (theoNguon.TryGetValue(nguon, out cu) ? cu : 0) + d.Tong;
            }

            List<EarningsItem> items = dong
                .GroupBy(d => d.Khoa)
                .Select(g => new EarningsItem { Key = g.Key, AmountVnd = g.Sum(x => x.Tong), Count = g.Sum(x => x.SoKhoan) })
                .OrderByDescending(i => nhom == "day" ? i.Key : string.Empty)
                .ThenByDescending(i => i.AmountVnd)
                .ToList();

            return new EarningsResponse
            {
                From = tu,
                To = den,
                GroupBy = nhom,
                TotalVnd = items.Sum(i => i.AmountVnd),
                ByKindVnd = theoNguon,
                Items = items,
            };
        }

        /// <summary>Chi phi du an — chu du an hoac admin; nguoi khac / du an chua ky quy → 404.</summary>
        public async Task<ProjectCostResponse> ChiPhiDuAnAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            ProjectEscrow? escrow = await _db.Escrows.AsNoTracking().FirstOrDefaultAsync(e => e.ProjectId == projectId, ct);
            if (escrow == null || (!caller.IsAdmin && escrow.OwnerId != caller.LayUserId()))
            {
                throw new NotFoundException("Không tìm thấy ký quỹ của dự án này.");
            }

            string taiKhoan = AccountCodes.ProjectEscrow(projectId);
            List<DongTheoLoai> theoLoai = await _db.Database.SqlQuery<DongTheoLoai>($@"
                SELECT e.type AS ""Loai"", SUM(l.amount)::bigint AS ""Tong""
                FROM journal_lines l JOIN journal_entries e ON e.id = l.entry_id
                WHERE l.account_code = {taiKhoan}
                GROUP BY e.type").ToListAsync(ct);

            Dictionary<string, long> tong = theoLoai.ToDictionary(x => x.Loai, x => x.Tong, StringComparer.Ordinal);
            List<FundsHold> treo = await _db.Holds.AsNoTracking()
                .Where(h => h.ProjectId == projectId && (h.Kind == HoldKind.ProjectReward || h.Kind == HoldKind.Annotation))
                .ToListAsync(ct);

            return new ProjectCostResponse
            {
                ProjectId = projectId,
                EscrowState = char.ToLowerInvariant(escrow.State.ToString()[0]) + escrow.State.ToString().Substring(1),
                ReservedVnd = Lay(tong, JournalEntryType.EscrowReserve),
                BalanceVnd = tong.Values.Sum(),
                // Du lieu cu (chi theo nhan) cong chung vao tien thuong.
                SpentRewardsVnd = -(Lay(tong, JournalEntryType.RewardPayout) + Lay(tong, JournalEntryType.AnnotationPayout)
                                    + Lay(tong, JournalEntryType.DisputeRefund)),
                SpentGateVnd = -Lay(tong, JournalEntryType.GateClickPayout),
                ReturnedVnd = -(Lay(tong, JournalEntryType.Refund) + Lay(tong, JournalEntryType.EscrowRelease)),
                Slots = escrow.Slots,
                RewardPerAttemptVnd = escrow.RewardVnd,
                PlatformFeePerAttemptVnd = escrow.PlatformFeeVnd,
                PaidAttempts = treo.Count(h => h.Kind == HoldKind.ProjectReward),
                HeldVnd = treo.Where(h => h.State == HoldState.Held).Sum(h => h.AmountVnd),
            };
        }

        private static long Lay(Dictionary<string, long> tong, JournalEntryType loai)
        {
            long v;
            return tong.TryGetValue(loai.ToString(), out v) ? v : 0;
        }

        /// <summary>Dong ket qua SQL thu nhap (ten cot khop alias trong cau SQL).</summary>
        private sealed class DongThuNhap
        {
            public string Khoa { get; set; } = string.Empty;

            public string Nguon { get; set; } = string.Empty;

            public long Tong { get; set; }

            public int SoKhoan { get; set; }
        }

        private sealed class DongTheoLoai
        {
            public string Loai { get; set; } = string.Empty;

            public long Tong { get; set; }
        }
    }
}
