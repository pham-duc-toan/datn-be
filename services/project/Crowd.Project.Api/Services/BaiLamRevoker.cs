using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Huy cac bai DANG LAM ma khong nop — goi khi labeler bi chan / bi go khoi du an (cung transaction voi
    /// thao tac cua chu du an), tai khoan bi khoa (user.blocked), hoac du an bi huy. Bai DA NOP giu nguyen (van
    /// duoc cham, van duoc tra thuong neu dat).
    ///
    /// KHONG SaveChanges va KHONG mo transaction: noi goi lo.
    /// </summary>
    public sealed class BaiLamRevoker
    {
        private readonly ProjectDbContext _db;

        public BaiLamRevoker(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        /// <summary>
        /// Huy moi bai dang lam theo du an va/hoac labeler. Tra ve so bai da huy. Trong transaction thi khoa dong
        /// (FOR UPDATE); ngoai transaction (SaveChanges la mot transaction) thi xmin chan ghi de neu labeler nop
        /// dung luc do — ben den sau nhan 409, khong mat bai nop.
        /// </summary>
        public async Task<int> ThuHoiAsync(Guid? projectId, Guid? labelerId, DateTimeOffset luc, CancellationToken ct)
        {
            List<Attempt> dangLam = _db.Database.CurrentTransaction != null
                ? await LamBaiQueries.KhoaBaiDangLamAsync(_db, projectId, labelerId, ct)
                : await _db.Attempts
                    .Where(a => a.State == AttemptState.InProgress
                                && (projectId == null || a.ProjectId == projectId)
                                && (labelerId == null || a.LabelerId == labelerId))
                    .ToListAsync(ct);
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
