using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// Ket thuc cac lease dang giu MA KHONG NOP — dung chung cho reaper (het han)
    /// va consumer (labeler bi chan/khoa, du an dong).
    ///
    /// THU TU KHOA LUON LA: assignment truoc, task sau (va trong moi loai sap theo
    /// id). Moi duong di trong task-svc deu khoa theo thu tu nay nen hai giao dich
    /// khong the cho nhau vong tron (deadlock).
    ///
    /// KHONG SaveChanges va KHONG mo transaction: noi goi lo (reaper tu mo,
    /// consumer thi IdempotencyGuard mo).
    /// </summary>
    public sealed class LeaseRevoker
    {
        private readonly TaskDbContext _db;

        public LeaseRevoker(TaskDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        /// <summary>Thu hoi moi lease dang giu theo du an va/hoac labeler. Tra ve so luot da thu hoi.</summary>
        public async Task<int> ThuHoiAsync(Guid? projectId, Guid? labelerId, DateTimeOffset luc, CancellationToken ct)
        {
            List<Assignment> dangGiu = await TaskQueries.KhoaLeaseDangGiuAsync(_db, projectId, labelerId, ct);
            if (dangGiu.Count == 0)
            {
                return 0;
            }

            Dictionary<Guid, LabelingTask> tasks = await KhoaTaskCuaAsync(dangGiu, ct);

            foreach (Assignment a in dangGiu)
            {
                a.ThuHoi(tasks[a.TaskId], luc);
            }

            return dangGiu.Count;
        }

        /// <summary>Reaper: ket thuc cac lease qua han trong danh sach da khoa.</summary>
        public async Task HetHanAsync(List<Assignment> quaHan, DateTimeOffset luc, CancellationToken ct)
        {
            if (quaHan == null)
            {
                throw new ArgumentNullException(nameof(quaHan));
            }

            if (quaHan.Count == 0)
            {
                return;
            }

            Dictionary<Guid, LabelingTask> tasks = await KhoaTaskCuaAsync(quaHan, ct);

            foreach (Assignment a in quaHan)
            {
                a.HetHan(tasks[a.TaskId], luc);
            }
        }

        private async Task<Dictionary<Guid, LabelingTask>> KhoaTaskCuaAsync(List<Assignment> ds, CancellationToken ct)
        {
            List<LabelingTask> tasks = await TaskQueries.KhoaTaskAsync(_db, ds.Select(a => a.TaskId), ct);
            return tasks.ToDictionary(t => t.Id);
        }
    }
}
