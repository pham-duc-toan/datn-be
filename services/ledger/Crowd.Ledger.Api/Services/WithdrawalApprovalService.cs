using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Exceptions;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Domain.Withdrawals;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// Hang doi duyet lenh rut (admin). Lenh vao hang doi khi setting
    /// ledger.withdraw_auto_approve tat, hoac so tien vuot ledger.withdraw_auto_approve_max_vnd.
    ///
    /// Tien da GIU tu luc xin rut (but toan YeuCauRut), nen:
    ///   duyet   → Requested + payout.requested (payment-svc chuyen khoan)
    ///   tu choi → Rejected + but toan DAO (tien ve lai kha dung cua labeler)
    /// Ca hai chay duoi khoa so cai — hai admin bam cung luc thi nguoi sau thay
    /// lenh da chot va nhan 409.
    /// </summary>
    public sealed class WithdrawalApprovalService
    {
        private readonly LedgerDbContext _db;
        private readonly LedgerWriter _writer;
        private readonly LedgerEventPublisher _events;
        private readonly TimeProvider _clock;

        public WithdrawalApprovalService(LedgerDbContext db, LedgerWriter writer, LedgerEventPublisher events, TimeProvider clock)
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

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _writer = writer;
            _events = events;
            _clock = clock;
        }

        /// <summary>Mac dinh liet ke lenh CHO DUYET, cu nhat truoc (xu ly theo thu tu xin).</summary>
        public async Task<PagedResponse<AdminWithdrawalResponse>> DanhSachAsync(
            WithdrawalState? state, int page, int pageSize, CancellationToken ct)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            WithdrawalState loc = state.HasValue ? state.Value : WithdrawalState.PendingApproval;
            IQueryable<Withdrawal> q = _db.Withdrawals.AsNoTracking().Where(w => w.State == loc);

            int tong = await q.CountAsync(ct);
            List<Withdrawal> ds = await q
                .OrderBy(w => w.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedResponse<AdminWithdrawalResponse>
            {
                Items = ds.Select(TaoResponse).ToList(),
                Page = page,
                PageSize = pageSize,
                Total = tong,
            };
        }

        public async Task<AdminWithdrawalResponse> DuyetAsync(Guid id, Caller caller, CancellationToken ct)
        {
            Guid admin = caller.LayUserId();
            return await TrongKhoaAsync(id, (w, bayGio) =>
            {
                w.Duyet(admin, bayGio);
                _events.PhatYeuCauChi(w, caller);
                return Task.CompletedTask;
            }, ct);
        }

        public async Task<AdminWithdrawalResponse> TuChoiAsync(Guid id, string? lyDo, Caller caller, CancellationToken ct)
        {
            Guid admin = caller.LayUserId();
            return await TrongKhoaAsync(id, async (w, bayGio) =>
            {
                w.TuChoi(admin, lyDo ?? string.Empty, bayGio);
                await _writer.GhiAsync(
                    Postings.DaoRut(w.Id, w.LabelerId, w.NetVnd, w.TaxVnd, bayGio, "Dao but toan rut bi admin tu choi"),
                    ct);
            }, ct);
        }

        private async Task<AdminWithdrawalResponse> TrongKhoaAsync(
            Guid id, Func<Withdrawal, DateTimeOffset, Task> hanhDong, CancellationToken ct)
        {
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _writer.KhoaAsync(ct);

                Withdrawal? w = await _db.Withdrawals.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (w == null)
                {
                    throw new NotFoundException("Khong tim thay lenh rut.");
                }

                await hanhDong(w, _clock.GetUtcNow());

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return TaoResponse(w);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        private static AdminWithdrawalResponse TaoResponse(Withdrawal w)
        {
            return new AdminWithdrawalResponse
            {
                Id = w.Id,
                LabelerId = w.LabelerId,
                AmountVnd = w.AmountVnd,
                TaxVnd = w.TaxVnd,
                NetVnd = w.NetVnd,
                BankAccount = w.BankAccount,
                State = w.State,
                CreatedAt = w.CreatedAt,
                ReviewedBy = w.ReviewedBy,
                ReviewedAt = w.ReviewedAt,
                FailureReason = w.FailureReason,
            };
        }
    }
}
