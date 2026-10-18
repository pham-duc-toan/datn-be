using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Infrastructure.Persistence;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// Huy cac bai DANG LAM ma khong nop — consumer goi khi labeler bi chan khoi du an, bi go khoi du an, hoac
    /// tai khoan bi khoa. Bai DA NOP giu nguyen (van duoc cham, van duoc tra thuong neu dat).
    ///
    /// KHONG SaveChanges va KHONG mo transaction: IdempotencyGuard cua consumer lo.
    /// </summary>
    public sealed class BaiLamRevoker
    {
        private readonly TaskDbContext _db;

        public BaiLamRevoker(TaskDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        /// <summary>Huy moi bai dang lam theo du an va/hoac labeler. Tra ve so bai da huy.</summary>
        public async Task<int> ThuHoiAsync(Guid? projectId, Guid? labelerId, DateTimeOffset luc, CancellationToken ct)
        {
            List<Attempt> dangLam = await TaskQueries.KhoaBaiDangLamAsync(_db, projectId, labelerId, ct);
            int n = 0;
            foreach (Attempt a in dangLam)
            {
                if (a.Huy(luc))
                {
                    n++;
                }
            }

            return n;
        }
    }
}
