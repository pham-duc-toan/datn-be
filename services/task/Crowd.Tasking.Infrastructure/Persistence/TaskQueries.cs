using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Tasking.Infrastructure.Persistence
{
    /// <summary>
    /// Cac cau SQL tho ma EF Core khong sinh duoc: FOR UPDATE va SKIP LOCKED. Moi ham PHAI goi ben trong
    /// mot transaction dang mo — khoa dong chi ton tai toi luc transaction ket thuc.
    ///
    /// Cau nao doc bang attempts phai ghi RO "*, xmin": xmin la cot HE THONG cua Postgres nen "SELECT *"
    /// KHONG tra ve no, ma EF can xmin de kiem concurrency.
    ///
    /// THU TU KHOA LUON LA: ban sao du an → bai lam → cau hoi. Moi duong trong task-svc khoa theo thu tu
    /// nay nen hai giao dich khong cho nhau vong tron (deadlock).
    /// </summary>
    public static class TaskQueries
    {
        /// <summary>
        /// Khoa ban sao du an (FOR UPDATE) — xep hang moi thao tac DEM SUAT / DONG THU BAI cua du an: hai
        /// labeler bam "bat dau" cung luc khong lot qua so suat, nguoi nop cuoi va worker het han khong
        /// cung dong thu bai.
        /// </summary>
        public static Task<ProjectSnapshot?> KhoaDuAnAsync(TaskDbContext db, Guid projectId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.ProjectSnapshots
                .FromSqlRaw("SELECT * FROM project_snapshots WHERE project_id = {0} FOR UPDATE", projectId)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>Khoa mot bai lam de luu nhap / nop / khieu nai — worker het han khong chen vao giua.</summary>
        public static Task<Attempt?> KhoaBaiAsync(TaskDbContext db, Guid attemptId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.Attempts
                .FromSqlRaw("SELECT *, xmin FROM attempts WHERE id = {0} FOR UPDATE", attemptId)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Khoa moi bai DANG LAM theo du an va/hoac labeler — de huy khi labeler bi chan / khoa, du an dong
        /// thu bai hoac bi huy. null = khong loc theo truong do.
        /// </summary>
        public static Task<List<Attempt>> KhoaBaiDangLamAsync(
            TaskDbContext db, Guid? projectId, Guid? labelerId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.Attempts
                .FromSqlRaw(
                    "SELECT *, xmin FROM attempts WHERE state = 'InProgress' " +
                    "   AND ({0}::uuid IS NULL OR project_id = {0}) " +
                    "   AND ({1}::uuid IS NULL OR labeler_id = {1}) " +
                    " ORDER BY id FOR UPDATE",
                    (object?)projectId ?? DBNull.Value,
                    (object?)labelerId ?? DBNull.Value)
                .ToListAsync(ct);
        }

        /// <summary>
        /// Bai qua han giu cho worker. SKIP LOCKED: dong dang bi nguoi nop giu khoa thi de do — nguoi nop se
        /// tu thay het han hoac nop kip truoc han.
        /// </summary>
        public static Task<List<Attempt>> KhoaBaiQuaHanAsync(
            TaskDbContext db, DateTimeOffset bayGio, int toiDa, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.Attempts
                .FromSqlRaw(
                    "SELECT *, xmin FROM attempts WHERE state = 'InProgress' AND expires_at <= {0} " +
                    " ORDER BY expires_at LIMIT {1} FOR UPDATE SKIP LOCKED",
                    bayGio,
                    toiDa)
                .ToListAsync(ct);
        }

        /// <summary>Khoa DUNG cac cau hoi nay (cho neu dang bi giu). Sap theo id de khong deadlock.</summary>
        public static Task<List<LabelingTask>> KhoaTaskAsync(TaskDbContext db, IEnumerable<Guid> taskIds, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            Guid[] ids = taskIds.Distinct().OrderBy(x => x).ToArray();
            return db.Tasks
                .FromSqlRaw("SELECT * FROM tasks WHERE id = ANY({0}) ORDER BY id FOR UPDATE", ids)
                .ToListAsync(ct);
        }
    }
}
