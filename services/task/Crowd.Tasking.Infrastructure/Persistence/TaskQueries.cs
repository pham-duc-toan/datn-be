using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Tasking.Infrastructure.Persistence
{
    /// <summary>
    /// Cac cau SQL tho ma EF Core khong sinh duoc: FOR UPDATE va SKIP LOCKED.
    /// Moi ham PHAI goi ben trong mot transaction dang mo — khoa dong chi ton tai
    /// toi luc transaction ket thuc.
    ///
    /// Cau nao doc bang assignments phai ghi RO "*, xmin": xmin la cot HE THONG
    /// cua Postgres nen "SELECT *" KHONG tra ve no, ma EF can xmin de kiem
    /// concurrency (thieu thi nem "required column 'xmin' was not present").
    /// </summary>
    public static class TaskQueries
    {
        /// <summary>Dieu kien "task nay con cap duoc cho labeler {1}" — dung chung cho hai cau duoi.</summary>
        private const string DieuKienUngVien =
            " WHERE t.project_id = {0} " +
            "   AND t.state = 'Open' " +
            "   AND t.redundancy_target > 0 " +
            "   AND t.active_lease_count + t.submitted_count < t.redundancy_target " +
            "   AND NOT EXISTS (SELECT 1 FROM assignments a " +
            "                    WHERE a.task_id = t.id AND a.labeler_id = {1} " +
            "                      AND a.state IN ('Leased','Submitted')) ";

        /// <summary>
        /// Chon NGAU NHIEN MOT task con cho va KHOA no (VD-T-06).
        ///
        /// SKIP LOCKED: dong dang bi request khac giu khoa thi BO QUA, lay dong ke
        /// — 50 labeler bam "nhan task" cung luc khong xep hang sau nhau.
        ///
        /// ORDER BY random(): moi request thu cac dong theo mot thu tu khac nhau,
        /// nen ho toa ra nhieu task thay vi cung tranh "dong dau tien".
        ///
        /// CHI KHOA MOT DONG (LIMIT 1). Ban dau khoa 20 ung vien roi chon mot —
        /// test dong thoi cho thay loi: request dau khoa CA 20 dong, request sau
        /// SKIP het va tuong la "het task" trong khi task van con.
        ///
        /// NOT EXISTS: khong cap lai task ma chinh labeler nay dang giu hoac da nop.
        /// </summary>
        public static Task<LabelingTask?> KhoaMotUngVienAsync(
            TaskDbContext db, Guid projectId, Guid labelerId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            string sql = "SELECT t.* FROM tasks t " + DieuKienUngVien +
                         " ORDER BY random() LIMIT 1 FOR UPDATE OF t SKIP LOCKED";

            return db.Tasks.FromSqlRaw(sql, projectId, labelerId).FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Con task nao cap duoc KHONG (bo qua khoa). Khoa mot ung vien that bai ma
        /// cau nay van true nghia la cac task con lai chi DANG bi request khac giu
        /// trong tich tac — nen thu lai, khong phai "het task".
        /// </summary>
        public static Task<bool> ConUngVienAsync(TaskDbContext db, Guid projectId, Guid labelerId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            string sql = "SELECT t.* FROM tasks t " + DieuKienUngVien + " LIMIT 1";

            return db.Tasks.FromSqlRaw(sql, projectId, labelerId).AsNoTracking().AnyAsync(ct);
        }

        /// <summary>
        /// Khoa DUNG cac task nay (CHO neu dang bi giu, khong bo qua). Dung khi nop,
        /// bo qua, het han, thu hoi — nhung thao tac PHAI xay ra, chi can xep hang.
        /// Sap theo id de hai giao dich khoa nhieu dong luon theo cung thu tu,
        /// khong the deadlock.
        /// </summary>
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

        /// <summary>Khoa mot luot lease de nop / bo qua — reaper khong chen vao giua duoc.</summary>
        public static Task<Assignment?> KhoaAssignmentAsync(TaskDbContext db, Guid assignmentId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.Assignments
                .FromSqlRaw("SELECT *, xmin FROM assignments WHERE id = {0} FOR UPDATE", assignmentId)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Khoa moi lease DANG GIU theo du an va/hoac labeler — de thu hoi khi
        /// labeler bi chan/khoa hoac du an dong. null = khong loc theo truong do.
        /// </summary>
        public static Task<List<Assignment>> KhoaLeaseDangGiuAsync(
            TaskDbContext db, Guid? projectId, Guid? labelerId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.Assignments
                .FromSqlRaw(
                    "SELECT *, xmin FROM assignments WHERE state = 'Leased' " +
                    "   AND ({0}::uuid IS NULL OR project_id = {0}) " +
                    "   AND ({1}::uuid IS NULL OR labeler_id = {1}) " +
                    " ORDER BY id FOR UPDATE",
                    (object?)projectId ?? DBNull.Value,
                    (object?)labelerId ?? DBNull.Value)
                .ToListAsync(ct);
        }

        /// <summary>
        /// Lease qua han cho reaper. SKIP LOCKED: dong dang bi nguoi nop giu khoa
        /// thi de do — nguoi nop se tu thay het han hoac nop kip truoc han.
        /// </summary>
        public static Task<List<Assignment>> KhoaLeaseQuaHanAsync(
            TaskDbContext db, DateTimeOffset bayGio, int toiDa, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.Assignments
                .FromSqlRaw(
                    "SELECT *, xmin FROM assignments WHERE state = 'Leased' AND expires_at <= {0} " +
                    " ORDER BY expires_at LIMIT {1} FOR UPDATE SKIP LOCKED",
                    bayGio,
                    toiDa)
                .ToListAsync(ct);
        }
    }
}
