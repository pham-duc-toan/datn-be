using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Media;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Consumers
{
    /// <summary>
    /// media-svc chuan bi xong mot file video (video.prepared) → ghi trang thai vao MOI mau dung file do
    /// (video dai cat thanh nhieu doan dung chung file). Het video dang chuan bi thi du an moi publish /
    /// duyet duoc.
    ///
    /// KHONG SaveChanges: IdempotencyGuard luu thay doi + processed_events trong mot transaction.
    /// </summary>
    public sealed class VideoPreparedProcessor : IEventProcessor<VideoPrepared>
    {
        private readonly ProjectDbContext _db;
        private readonly ILogger<VideoPreparedProcessor> _logger;

        public VideoPreparedProcessor(ProjectDbContext db, ILogger<VideoPreparedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<VideoPrepared> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            VideoPrepared p = envelope.Payload;
            List<Sample> mau = await _db.Samples
                .Where(s => s.ProjectId == p.ProjectId && s.StorageKey == p.StorageKey)
                .ToListAsync(ct);

            // Du an da xoa / mau khong con: khong nem loi (nem thi message quay vong roi vao DLQ vo ich).
            if (mau.Count == 0)
            {
                _logger.LogWarning(
                    "video.prepared {EventId}: khong co mau nao cua du an {ProjectId} dung file {StorageKey} — bo qua",
                    envelope.EventId, p.ProjectId, p.StorageKey);
                return;
            }

            foreach (Sample s in mau)
            {
                s.GhiKetQuaMedia(p.Succeeded, p.Error);
            }

            if (!p.Succeeded)
            {
                _logger.LogWarning("Video {StorageKey} cua du an {ProjectId} khong chuan bi duoc: {Error}", p.StorageKey, p.ProjectId, p.Error);
            }
        }
    }
}
