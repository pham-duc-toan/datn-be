using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Contracts.Ledger;
using Crowd.Ledger.Api.Dtos;
using Crowd.Ledger.Api.Exceptions;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Disputes;
using Crowd.Ledger.Domain.Escrows;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// Tranh chap (FM-06): doanh nghiep phan doi mot nhan DA DUYET trong luc thu lao con treo.
    ///
    /// Vi sao o ledger: dong bang khoan treo va tra tien phai NGUYEN TU voi worker giai phong treo.
    ///   - Mo: khoa dong holds FOR UPDATE (worker cung khoa dong nay voi state='Held'): ai giu khoa
    ///     truoc thi thang — khong co chuyen vua giai phong vua dong bang.
    ///   - Phan xu tra doanh nghiep: DAO but toan chi tra (labeler treo + phi nen tang → ky quy / vi
    ///     doanh nghiep) trong transaction cua so cai.
    /// Phat dispute.opened / dispute.resolved: notification bao hai ben; annotation danh dau nhan bi
    /// loai, identity tru so nhan duoc duyet (khi doanh nghiep thang).
    /// </summary>
    public sealed class DisputeService
    {
        private readonly LedgerDbContext _db;
        private readonly LedgerWriter _writer;
        private readonly LedgerEventPublisher _events;
        private readonly TimeProvider _clock;

        public DisputeService(LedgerDbContext db, LedgerWriter writer, LedgerEventPublisher events, TimeProvider clock)
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

        /// <summary>
        /// Doanh nghiep (chu du an) mo tranh chap cho mot nhan da duyet. Khong phai chu / nhan khong
        /// co khoan treo → 404 (khong tiet lo). Tien da het treo → 409 khong_con_treo.
        /// </summary>
        public async Task<DisputeResponse> MoAsync(OpenDisputeRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            Guid uid = caller.LayUserId();
            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                FundsHold? hold = await _db.Holds
                    .FromSqlInterpolated($"SELECT * FROM holds WHERE annotation_id = {body.AnnotationId} AND kind = 'Annotation' FOR UPDATE")
                    .FirstOrDefaultAsync(ct);
                ProjectEscrow? escrow = hold == null
                    ? null
                    : await _db.Escrows.AsNoTracking().FirstOrDefaultAsync(e => e.ProjectId == hold.ProjectId, ct);
                if (hold == null || escrow == null || (escrow.OwnerId != uid && !caller.IsAdmin))
                {
                    throw new NotFoundException("Khong tim thay nhan da duyet nay trong du an cua ban.");
                }

                if (await _db.Disputes.AnyAsync(d => d.AnnotationId == body.AnnotationId, ct))
                {
                    throw new RuleViolationException("da_tranh_chap", "Nhan nay da co tranh chap.");
                }

                hold.DongBang();
                Dispute d = Dispute.Mo(hold.Id, hold.AnnotationId, hold.ProjectId, hold.LabelerId, escrow.OwnerId,
                    hold.AmountVnd, body.Reason ?? string.Empty, _clock.GetUtcNow());
                _db.Disputes.Add(d);

                _events.Phat(caller, new DisputeOpened
                {
                    DisputeId = d.Id,
                    AnnotationId = d.AnnotationId,
                    ProjectId = d.ProjectId,
                    LabelerId = d.LabelerId,
                    BusinessId = d.BusinessId,
                    AmountVnd = d.AmountVnd,
                    Reason = d.Reason,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return TaoResponse(d);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>
        /// Admin phan xu. refundBusiness: dao but toan chi tra, tien ve ky quy (ky quy con mo) hoac vi
        /// doanh nghiep (ky quy da dong). payLabeler: mo bang, khoan treo chay tiep.
        /// </summary>
        public async Task<DisputeResponse> PhanXuAsync(Guid disputeId, ResolveDisputeRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            bool traDoanhNghiep;
            if (string.Equals(body.Outcome, "refundBusiness", StringComparison.OrdinalIgnoreCase))
            {
                traDoanhNghiep = true;
            }
            else if (string.Equals(body.Outcome, "payLabeler", StringComparison.OrdinalIgnoreCase))
            {
                traDoanhNghiep = false;
            }
            else
            {
                throw new InvalidValueException("ket_cuc_khong_hop_le", "outcome: refundBusiness | payLabeler.");
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _writer.KhoaAsync(ct);

                Dispute? d = await _db.Disputes
                    .FromSqlInterpolated($"SELECT * FROM disputes WHERE id = {disputeId} FOR UPDATE")
                    .FirstOrDefaultAsync(ct);
                if (d == null)
                {
                    throw new NotFoundException("Khong tim thay tranh chap.");
                }

                DateTimeOffset bayGio = _clock.GetUtcNow();
                d.PhanXu(traDoanhNghiep, body.Note ?? string.Empty, caller.LayUserId(), bayGio);

                FundsHold hold = await _db.Holds
                    .FromSqlInterpolated($"SELECT * FROM holds WHERE id = {d.HoldId} FOR UPDATE")
                    .FirstAsync(ct);

                if (traDoanhNghiep)
                {
                    hold.HoanChoDoanhNghiep(bayGio);
                    ProjectEscrow escrow = await _db.Escrows.AsNoTracking().FirstAsync(e => e.ProjectId == d.ProjectId, ct);
                    long phi = await PhiNenTangCuaNhanAsync(d.AnnotationId, ct);
                    await _writer.GhiAsync(
                        Postings.HoanTranhChap(d.Id, d.ProjectId, d.BusinessId, d.LabelerId, d.AmountVnd, phi, escrow.State != EscrowState.Closed, bayGio),
                        ct);
                }
                else
                {
                    hold.MoBang();
                }

                _events.Phat(caller, new DisputeResolved
                {
                    DisputeId = d.Id,
                    AnnotationId = d.AnnotationId,
                    ProjectId = d.ProjectId,
                    LabelerId = d.LabelerId,
                    BusinessId = d.BusinessId,
                    AmountVnd = d.AmountVnd,
                    Outcome = traDoanhNghiep ? DisputeOutcome.RefundBusiness : DisputeOutcome.PayLabeler,
                    Note = d.ResolutionNote!,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return TaoResponse(d);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>Tranh chap cua doanh nghiep dang goi (status rong = moi trang thai).</summary>
        public Task<PagedResponse<DisputeResponse>> CuaToiAsync(Caller caller, string? status, int page, int pageSize, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            return TrangAsync(_db.Disputes.AsNoTracking().Where(d => d.BusinessId == uid), status, page, pageSize, ct);
        }

        /// <summary>Hang doi admin: mac dinh tranh chap dang mo, cu nhat truoc.</summary>
        public Task<PagedResponse<DisputeResponse>> HangDoiAsync(string? status, int page, int pageSize, CancellationToken ct)
        {
            return TrangAsync(_db.Disputes.AsNoTracking(), string.IsNullOrWhiteSpace(status) ? "open" : status, page, pageSize, ct);
        }

        private static async Task<PagedResponse<DisputeResponse>> TrangAsync(
            IQueryable<Dispute> q, string? status, int page, int pageSize, CancellationToken ct)
        {
            int trang = page < 1 ? 1 : page;
            int co = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

            if (!string.IsNullOrWhiteSpace(status))
            {
                DisputeState loc;
                if (!Enum.TryParse(status, true, out loc) || !Enum.IsDefined(loc))
                {
                    throw new InvalidValueException("trang_thai_khong_hop_le", "status: open | refundedBusiness | paidLabeler.");
                }

                q = q.Where(d => d.State == loc);
            }

            int tong = await q.CountAsync(ct);
            List<Dispute> ds = await q.OrderBy(d => d.OpenedAt).Skip((trang - 1) * co).Take(co).ToListAsync(ct);
            return new PagedResponse<DisputeResponse>
            {
                Items = ds.Select(TaoResponse).ToList(),
                Page = trang,
                PageSize = co,
                Total = tong,
            };
        }

        /// <summary>Phi nen tang da thu khi chi tra nhan nay — doc tu chinh but toan goc.</summary>
        private Task<long> PhiNenTangCuaNhanAsync(Guid annotationId, CancellationToken ct)
        {
            string thamChieu = "annotation:" + annotationId;
            return _db.JournalEntries
                .Where(e => e.Type == JournalEntryType.AnnotationPayout && e.Reference == thamChieu)
                .SelectMany(e => e.Lines)
                .Where(l => l.AccountCode == AccountCodes.PlatformFee)
                .SumAsync(l => l.Amount, ct);
        }

        private static DisputeResponse TaoResponse(Dispute d)
        {
            return new DisputeResponse
            {
                Id = d.Id,
                AnnotationId = d.AnnotationId,
                ProjectId = d.ProjectId,
                LabelerId = d.LabelerId,
                BusinessId = d.BusinessId,
                AmountVnd = d.AmountVnd,
                Reason = d.Reason,
                State = char.ToLowerInvariant(d.State.ToString()[0]) + d.State.ToString().Substring(1),
                OpenedAt = d.OpenedAt,
                ResolvedAt = d.ResolvedAt,
                ResolutionNote = d.ResolutionNote,
            };
        }
    }
}
