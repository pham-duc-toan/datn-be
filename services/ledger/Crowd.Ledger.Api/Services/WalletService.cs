using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Ledger;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Exceptions;
using Crowd.Ledger.Api.Helpers;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Escrows;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Domain.Withdrawals;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// Vi cua nguoi dung: xem so du (FB-04, FL-10), lich su (FB-05), rut tien
    /// (FL-11). Moi thu CHI cua chinh nguoi goi — danh tinh lay tu token (VD-S-03).
    /// </summary>
    public sealed class WalletService
    {
        private readonly LedgerDbContext _db;
        private readonly LedgerWriter _writer;
        private readonly LedgerEventPublisher _events;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public WalletService(
            LedgerDbContext db,
            LedgerWriter writer,
            LedgerEventPublisher events,
            ISettings settings,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _writer = writer;
            _events = events;
            _settings = settings;
            _clock = clock;
        }

        public async Task<BalanceResponse> SoDuAsync(Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();

            List<Guid> duAnDangKyQuy = await _db.Escrows
                .Where(e => e.OwnerId == uid && e.State != EscrowState.Closed)
                .Select(e => e.ProjectId)
                .ToListAsync(ct);

            List<string> maEscrow = new List<string>();
            foreach (Guid p in duAnDangKyQuy)
            {
                maEscrow.Add(AccountCodes.ProjectEscrow(p));
            }

            List<string> maCuaToi = new List<string>
            {
                AccountCodes.BusinessAvailable(uid),
                AccountCodes.LabelerPending(uid),
                AccountCodes.LabelerAvailable(uid),
            };
            maCuaToi.AddRange(maEscrow);

            Dictionary<string, long> soDu = await _db.Accounts.AsNoTracking()
                .Where(a => maCuaToi.Contains(a.Code))
                .ToDictionaryAsync(a => a.Code, a => a.Balance, ct);

            long escrow = 0;
            foreach (string ma in maEscrow)
            {
                escrow = escrow + Lay(soDu, ma);
            }

            return new BalanceResponse
            {
                BusinessAvailableVnd = Lay(soDu, AccountCodes.BusinessAvailable(uid)),
                EscrowVnd = escrow,
                PendingVnd = Lay(soDu, AccountCodes.LabelerPending(uid)),
                AvailableVnd = Lay(soDu, AccountCodes.LabelerAvailable(uid)),
            };
        }

        /// <summary>Moi dong but toan cham vao tai khoan CUA nguoi goi, moi nhat truoc.</summary>
        public async Task<PagedResponse<TransactionResponse>> LichSuAsync(Caller caller, int page, int pageSize, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            List<Guid> duAn = await _db.Escrows.Where(e => e.OwnerId == uid).Select(e => e.ProjectId).ToListAsync(ct);
            List<string> ma = new List<string>
            {
                AccountCodes.BusinessAvailable(uid),
                AccountCodes.LabelerPending(uid),
                AccountCodes.LabelerAvailable(uid),
            };
            foreach (Guid p in duAn)
            {
                ma.Add(AccountCodes.ProjectEscrow(p));
            }

            // Moi dong but toan (kem thong tin but toan cha) cham vao tai khoan cua toi.
            IQueryable<TransactionResponse> q = _db.JournalEntries.AsNoTracking()
                .SelectMany(
                    e => e.Lines,
                    (e, l) => new TransactionResponse
                    {
                        Seq = e.Seq,
                        Type = e.Type,
                        Reference = e.Reference,
                        Description = e.Description,
                        AccountCode = l.AccountCode,
                        AmountVnd = l.Amount,
                        At = e.CreatedAt,
                    })
                .Where(x => ma.Contains(x.AccountCode));

            int tong = await q.CountAsync(ct);
            List<TransactionResponse> items = await q
                .OrderByDescending(x => x.Seq)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedResponse<TransactionResponse> { Items = items, Page = page, PageSize = pageSize, Total = tong };
        }

        /// <summary>
        /// Rut tien (FL-11). Header Idempotency-Key bat buoc: bam hai lan (mang cham,
        /// client tu thu lai) chi tao MOT lenh — lan hai tra lai chinh lenh do.
        /// </summary>
        public async Task<WithdrawalResponse> RutAsync(WithdrawRequest body, string? idempotencyKey, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            Guid uid = caller.LayUserId();
            string khoa = idempotencyKey ?? string.Empty;

            Withdrawal? cu = await _db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(w => w.LabelerId == uid && w.IdempotencyKey == khoa, ct);
            if (cu != null)
            {
                return TaoResponse(cu);
            }

            if (await _db.BlockedUsers.AnyAsync(b => b.UserId == uid, ct))
            {
                throw new ForbiddenException("tai_khoan_bi_khoa", "Tai khoan dang bi khoa, khong rut duoc tien.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Withdrawal w = Withdrawal.Tao(uid, body.AmountVnd, body.BankAccount ?? string.Empty, khoa, QuyDinhTuSetting.Rut(_settings), bayGio);

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _writer.KhoaAsync(ct);

                // KIEM LAI SAU KHI GIU KHOA. Lan kiem o dau ham chay TRUOC khoa: hai
                // request cung key cung luc deu thay "chua co", request thu hai xep
                // hang sau khoa, toi luot thi so du da bi request dau tru — va bao
                // "khong du so du" oan. Test dong thoi 4 request da bat duoc loi nay.
                Withdrawal? daCo = await _db.Withdrawals.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.LabelerId == uid && x.IdempotencyKey == khoa, ct);
                if (daCo != null)
                {
                    await tx.RollbackAsync(ct);
                    return TaoResponse(daCo);
                }

                long kheDung = await _writer.SoDuAsync(AccountCodes.LabelerAvailable(uid), ct);
                if (kheDung < w.AmountVnd)
                {
                    throw new RuleViolationException(
                        "khong_du_so_du",
                        "So du kha dung " + kheDung + "d, khong du rut " + w.AmountVnd + "d.");
                }

                _db.Withdrawals.Add(w);

                // Tien GIU ngay ca khi con cho duyet — labeler khong rut trung duoc.
                await _writer.GhiAsync(Postings.YeuCauRut(w.Id, uid, w.AmountVnd, w.TaxVnd, bayGio), ct);

                // Setting ledger.withdraw_auto_approve (+ tran so tien): he thong duyet
                // luon va gui payment-svc. Khong thi cho admin duyet.
                if (QuyDinhTuSetting.DuocTuDuyetRut(_settings, w.AmountVnd))
                {
                    w.Duyet(null, bayGio);
                    _events.PhatYeuCauChi(w, caller);
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_withdrawals_idempotency"))
            {
                // Hai request cung Idempotency-Key CUNG LUC: ben thang da tao lenh.
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();

                Withdrawal benThang = await _db.Withdrawals.AsNoTracking().FirstAsync(x => x.LabelerId == uid && x.IdempotencyKey == khoa, ct);
                return TaoResponse(benThang);
            }
            finally
            {
                await tx.DisposeAsync();
            }

            return TaoResponse(w);
        }

        public async Task<IReadOnlyList<WithdrawalResponse>> LenhRutCuaToiAsync(Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            List<Withdrawal> ds = await _db.Withdrawals.AsNoTracking()
                .Where(w => w.LabelerId == uid)
                .OrderByDescending(w => w.CreatedAt)
                .Take(100)
                .ToListAsync(ct);

            return ds.Select(TaoResponse).ToList();
        }

        internal static WithdrawalResponse TaoResponse(Withdrawal w)
        {
            return new WithdrawalResponse
            {
                Id = w.Id,
                AmountVnd = w.AmountVnd,
                TaxVnd = w.TaxVnd,
                NetVnd = w.NetVnd,
                State = w.State,
                CreatedAt = w.CreatedAt,
                FailureReason = w.FailureReason,
                ReviewedAt = w.ReviewedAt,
            };
        }

        private static long Lay(Dictionary<string, long> soDu, string ma)
        {
            long v;
            if (soDu.TryGetValue(ma, out v))
            {
                return v;
            }

            return 0;
        }
    }
}
