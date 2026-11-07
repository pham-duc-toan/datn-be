using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Datasets;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Infrastructure.Persistence
{
    /// <summary>
    /// Cac cau SQL tho ma EF Core khong sinh duoc: FOR UPDATE va SKIP LOCKED. Moi ham PHAI goi ben trong
    /// mot transaction dang mo — khoa dong chi ton tai toi luc transaction ket thuc.
    ///
    /// Cau nao doc bang co cot xmin (attempts) phai ghi RO "*, xmin": xmin la cot HE THONG cua Postgres nen
    /// "SELECT *" KHONG tra ve no, ma EF can xmin de kiem concurrency.
    ///
    /// THU TU KHOA LUON LA: dot thu bai (project_collections) → bai lam → mau. Moi duong deu khoa theo thu tu
    /// nay nen hai giao dich khong cho nhau vong tron (deadlock).
    /// </summary>
    public static class LamBaiQueries
    {
        /// <summary>
        /// Khoa dot thu bai (FOR UPDATE) — xep hang moi thao tac DEM SUAT / DONG THU BAI cua du an: hai labeler
        /// bam "bat dau" cung luc khong lot qua so suat, nguoi nop cuoi va worker het han khong cung dong thu bai.
        /// </summary>
        public static Task<CollectionRound?> KhoaDotAsync(ProjectDbContext db, Guid projectId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.CollectionRounds
                .FromSqlRaw("SELECT * FROM project_collections WHERE project_id = {0} FOR UPDATE", projectId)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>Khoa mot bai lam de luu nhap / nop / khieu nai — worker het han khong chen vao giua.</summary>
        public static Task<Attempt?> KhoaBaiAsync(ProjectDbContext db, Guid attemptId, CancellationToken ct)
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
        /// Khoa moi bai DANG LAM theo du an va/hoac labeler — de huy khi labeler bi chan / khoa, du an dong thu
        /// bai hoac bi huy. null = khong loc theo truong do.
        /// </summary>
        public static Task<List<Attempt>> KhoaBaiDangLamAsync(
            ProjectDbContext db, Guid? projectId, Guid? labelerId, CancellationToken ct)
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
        /// Bai qua han giu cho worker. SKIP LOCKED: dong dang bi nguoi nop giu khoa thi de do — nguoi nop se tu
        /// thay het han hoac nop kip truoc han.
        /// </summary>
        public static Task<List<Attempt>> KhoaBaiQuaHanAsync(
            ProjectDbContext db, DateTimeOffset bayGio, int toiDa, CancellationToken ct)
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

        /// <summary>Khoa DUNG mot mau cua du an (loai mau do bao loi). Loc theo project_id → mot phan vung.</summary>
        public static Task<Sample?> KhoaMauAsync(ProjectDbContext db, Guid projectId, Guid sampleId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.Samples
                .FromSqlRaw("SELECT * FROM samples WHERE project_id = {0} AND id = {1} FOR UPDATE", projectId, sampleId)
                .FirstOrDefaultAsync(ct);
        }
    }
}
