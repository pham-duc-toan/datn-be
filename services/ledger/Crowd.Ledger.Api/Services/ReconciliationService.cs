using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// DOI SOAT (FM-05) — kiem so cai con lanh khong, bang chinh cac bat bien:
    ///
    ///   1. Moi but toan co tong = 0                   (ghi kep)
    ///   2. Tong MOI dong cua ca he thong = 0          (tien khong tu sinh/mat)
    ///   3. accounts.balance == SUM(dong) tung tai khoan (bo nho dem khong lech su that)
    ///   4. Khong tai khoan nao am, tru tai khoan cong
    ///   5. Chuoi bam nguyen ven                        (khong ai sua tay, VD-M-04)
    ///
    /// Chay tren MOT snapshot (transaction REPEATABLE READ) de khong bao lech gia
    /// khi dang co giao dich bay (VD-M-13).
    /// </summary>
    public sealed class ReconciliationService
    {
        private readonly LedgerDbContext _db;

        public ReconciliationService(LedgerDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task<ReconciliationResponse> DoiSoatAsync(CancellationToken ct)
        {
            var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);

            try
            {
                List<string> lech = await _db.Database.SqlQueryRaw<string>(
                        "SELECT entry_id::text AS \"Value\" FROM journal_lines GROUP BY entry_id HAVING SUM(amount) <> 0")
                    .ToListAsync(ct);

                long tongToanHeThong = await _db.Database.SqlQueryRaw<long>(
                        "SELECT COALESCE(SUM(amount), 0)::bigint AS \"Value\" FROM journal_lines")
                    .FirstAsync(ct);

                List<string> soDuLech = await _db.Database.SqlQueryRaw<string>(
                        "SELECT a.code || ': balance=' || a.balance || ', sum=' || COALESCE(SUM(l.amount), 0) AS \"Value\" " +
                        "  FROM accounts a LEFT JOIN journal_lines l ON l.account_code = a.code " +
                        " GROUP BY a.code, a.balance " +
                        "HAVING a.balance <> COALESCE(SUM(l.amount), 0)")
                    .ToListAsync(ct);

                List<string> am = await _db.Accounts.AsNoTracking()
                    .Where(a => a.Balance < 0 && !a.Code.StartsWith("gateway:"))
                    .Select(a => a.Code + "=" + a.Balance)
                    .ToListAsync(ct);

                List<JournalEntry> tatCa = await _db.JournalEntries.AsNoTracking().OrderBy(e => e.Seq).ToListAsync(ct);
                List<long> dutChuoi = new List<long>();
                string hashTruoc = JournalEntry.HashGoc;

                foreach (JournalEntry e in tatCa)
                {
                    if (!string.Equals(e.PrevHash, hashTruoc, StringComparison.Ordinal) || !e.HashKhop())
                    {
                        dutChuoi.Add(e.Seq);
                    }

                    hashTruoc = e.Hash;
                }

                await tx.CommitAsync(ct);

                return new ReconciliationResponse
                {
                    Healthy = lech.Count == 0 && tongToanHeThong == 0 && soDuLech.Count == 0 && am.Count == 0 && dutChuoi.Count == 0,
                    EntryCount = tatCa.Count,
                    GrandTotal = tongToanHeThong,
                    UnbalancedEntries = lech,
                    BalanceMismatches = soDuLech,
                    NegativeAccounts = am,
                    BrokenChain = dutChuoi,
                };
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }
    }
}
