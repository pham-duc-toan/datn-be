using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Crowd.BuildingBlocks.Persistence.Idempotency
{
    /// <summary>
    /// Hien thuc IIdempotencyGuard tren mot DbContext bat ky.
    ///
    /// Dang ky SCOPED, cung vong doi voi DbContext — giong IOutboxWriter.
    /// </summary>
    public sealed class IdempotencyGuard : IIdempotencyGuard
    {
        /// <summary>Ma loi cua Postgres cho vi pham rang buoc duy nhat.</summary>
        private const string ViPhamDuyNhat = "23505";

        private readonly DbContext _db;
        private readonly TimeProvider _clock;
        private readonly ILogger<IdempotencyGuard> _logger;

        public IdempotencyGuard(
            DbContext db,
            TimeProvider clock,
            ILogger<IdempotencyGuard> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _clock = clock;
            _logger = logger;
        }

        public async Task<bool> XuLyMotLanAsync(
            Guid eventId,
            string handler,
            Func<CancellationToken, Task> nghiepVu,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(handler))
            {
                throw new ArgumentException("handler khong duoc rong", nameof(handler));
            }

            if (nghiepVu == null)
            {
                throw new ArgumentNullException(nameof(nghiepVu));
            }

            // DUONG NHANH: da co dau vet thi khoi mo transaction va khoi chay
            // nghiep vu. Day chi la toi uu, KHONG phai co che dam bao — hai
            // message toi cung luc deu se thay "chua xu ly" o buoc nay. Thu
            // that su chan trung la rang buoc duy nhat o buoc commit ben duoi.
            bool daCo = await _db.Set<ProcessedEvent>()
                .AnyAsync(x => x.EventId == eventId && x.Handler == handler, ct)
                .ConfigureAwait(false);

            if (daCo)
            {
                _logger.LogDebug(
                    "Bo qua event {EventId} cho handler {Handler}: da xu ly tu truoc",
                    eventId,
                    handler);

                return false;
            }

            var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            try
            {
                // Ghi dau vet TRUOC khi chay nghiep vu. Thu tu khong quan trong
                // voi tinh dung dan (ca hai cung mot transaction), nhung dat
                // truoc thi doc code ro y do hon.
                _db.Set<ProcessedEvent>().Add(
                    ProcessedEvent.Tao(eventId, handler, _clock.GetUtcNow()));

                await nghiepVu(ct).ConfigureAwait(false);

                // MOT SaveChanges cho ca dau vet lan nghiep vu. Neu mot message
                // giong het vua commit truoc do mot phan nghin giay, dong nay
                // nem loi 23505 va TOAN BO transaction bi quay lui — ke ca but
                // toan. Do chinh la dieu ta muon.
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);

                return true;
            }
            catch (DbUpdateException ex) when (LaViPhamDuyNhat(ex))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);

                // Khong phai loi. Day la hai ban sao cua cung mot message chay
                // song song, va database vua lam trong tai chon ra mot ban.
                _logger.LogInformation(
                    "Event {EventId} cho handler {Handler} da duoc mot tien trinh " +
                    "khac xu ly xong truoc; bo qua ban nay",
                    eventId,
                    handler);

                // Change tracker dang giu cac thay doi da bi quay lui. Khong don
                // thi lan SaveChanges sau cua cung DbContext se ghi lai chung.
                _db.ChangeTracker.Clear();

                return false;
            }
            catch
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                _db.ChangeTracker.Clear();
                throw;
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static bool LaViPhamDuyNhat(DbUpdateException ex)
        {
            PostgresException? loiPg = ex.InnerException as PostgresException;

            if (loiPg == null)
            {
                return false;
            }

            return string.Equals(loiPg.SqlState, ViPhamDuyNhat, StringComparison.Ordinal);
        }
    }
}
