using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.Labeling;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>
    /// Thong tin mau (annotation_samples) — luu MOT lan cho moi mau, cac nhan tro toi bang sample_id.
    /// </summary>
    public static class MauDaGanStore
    {
        /// <summary>
        /// Ghi mau neu chua co. ON CONFLICT DO NOTHING: hai nhan cua cung mot mau den cung luc (hai consumer)
        /// khong loi trung khoa; mau khong doi sau khi nap nen ban ghi dau la du. Chay trong transaction dang mo.
        /// </summary>
        public static Task<int> DamBaoAsync(
            AnnotationDbContext db, Guid sampleId, Guid projectId, string? storageKey, RawJson? content, RawJson metadata, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            string? noiDung = content == null ? null : content.Json;
            string md = metadata.Json;
            return db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO annotation_samples (sample_id, project_id, storage_key, content, metadata)
                VALUES ({sampleId}, {projectId}, {storageKey}, CAST({noiDung} AS jsonb), CAST({md} AS jsonb))
                ON CONFLICT (sample_id) DO NOTHING", ct);
        }

        public static async Task<Dictionary<Guid, AnnotationSample>> NapAsync(
            AnnotationDbContext db, IEnumerable<Guid> sampleIds, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            List<Guid> ids = sampleIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<Guid, AnnotationSample>();
            }

            return await db.Samples.AsNoTracking().Where(m => ids.Contains(m.SampleId)).ToDictionaryAsync(m => m.SampleId, ct);
        }
    }
}
