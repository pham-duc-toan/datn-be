using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Services;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Members;
using Crowd.Annotation.Domain.Projects;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Project;
using Crowd.Contracts.Tasking;
using Crowd.Labeling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Annotation.Api.Consumers
{
    // GIAO KEO: KHONG SaveChanges — IdempotencyGuard luu + ghi processed_events
    // + commit MOT lan cho ca processor.

    /// <summary>
    /// attempt.submitted (task-svc): LUU moi cau tra loi that cua bai thanh mot nhan (trang thai Submitted,
    /// cho cham). Cau bo trong (LabelPayload null) khong thanh nhan. Bai giao lai → khong tao nhan thu hai
    /// (UNIQUE (attempt, mau) + kiem truoc).
    /// </summary>
    public sealed class AttemptSubmittedProcessor : IEventProcessor<AttemptSubmitted>
    {
        private readonly AnnotationDbContext _db;
        private readonly ILogger<AttemptSubmittedProcessor> _logger;

        public AttemptSubmittedProcessor(AnnotationDbContext db, ILogger<AttemptSubmittedProcessor> logger)
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

        public async Task XuLyAsync(EventEnvelope<AttemptSubmitted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptSubmitted p = envelope.Payload;

            bool daCo = await _db.Annotations.AnyAsync(a => a.AttemptId == p.AttemptId, ct);
            if (daCo)
            {
                _logger.LogInformation("Bai {AttemptId} da co nhan — bo qua", p.AttemptId);
                return;
            }

            // Du an da KET THUC (project.completed / cancelled) ma bai nop moi toi: task-svc dong thu bai TRUOC
            // khi cham, cham truoc khi chot — bai nop toi sau la sai bat bien. NEM LOI de vao DLQ (VD-D-06).
            ProjectTerms? dieuKhoan = await _db.ProjectTerms.AsNoTracking().FirstOrDefaultAsync(t => t.ProjectId == p.ProjectId, ct);
            if (dieuKhoan != null && dieuKhoan.IsFinal)
            {
                throw new InvalidOperationException("Bai nop " + p.AttemptId + " toi sau khi du an " + p.ProjectId + " da ket thuc.");
            }

            int n = 0;
            foreach (AttemptAnswer tl in p.Answers)
            {
                // Thong tin mau luu MOT lan (annotation_samples); nhan chi tro sample_id.
                await MauDaGanStore.DamBaoAsync(_db, tl.SampleId, p.ProjectId, tl.StorageKey, tl.SampleContent, tl.SampleMetadata, ct);
                if (tl.LabelPayload == null)
                {
                    continue;
                }

                _db.Annotations.Add(LabelAnnotation.TaoTuBaiNop(p.AttemptId, p.ProjectId, tl.SampleId, p.LabelerId, tl.LabelPayload, p.SubmittedAt));
                n++;
            }

            _logger.LogInformation("Luu {SoNhan} nhan cua bai {AttemptId} (du an {ProjectId})", n, p.AttemptId, p.ProjectId);
        }
    }

    /// <summary>
    /// project.graded (quality-svc): nhan cua bai DAT vao ket qua (Accepted), bai TRUOT bi loai (Excluded).
    /// Bai nop co the toi SAU event nay (hai queue khac nhau) → bai chua co nhan thi nem loi de giao lai.
    /// </summary>
    public sealed class ProjectGradedProcessor : IEventProcessor<Crowd.Contracts.Quality.ProjectGraded>
    {
        private readonly AnnotationDbContext _db;
        private readonly ILogger<ProjectGradedProcessor> _logger;

        public ProjectGradedProcessor(AnnotationDbContext db, ILogger<ProjectGradedProcessor> logger)
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

        public async Task XuLyAsync(EventEnvelope<Crowd.Contracts.Quality.ProjectGraded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            Crowd.Contracts.Quality.ProjectGraded p = envelope.Payload;
            List<Guid> ids = p.Attempts.Select(a => a.AttemptId).ToList();
            List<LabelAnnotation> nhan = await _db.Annotations
                .Where(a => a.ProjectId == p.ProjectId && ids.Contains(a.AttemptId))
                .ToListAsync(ct);
            HashSet<Guid> coNhan = nhan.Select(a => a.AttemptId).ToHashSet();

            foreach (Crowd.Contracts.Quality.GradedAttempt g in p.Attempts)
            {
                if (g.AnsweredCount > 0 && !coNhan.Contains(g.AttemptId))
                {
                    throw new InvalidOperationException("Chua co nhan cua bai " + g.AttemptId + " — cho attempt.submitted.");
                }
            }

            Dictionary<Guid, bool> dat = p.Attempts.ToDictionary(a => a.AttemptId, a => a.Passed);
            int doi = 0;
            foreach (LabelAnnotation a in nhan)
            {
                if (a.ChotTheoKetQua(dat[a.AttemptId], p.GradedAt))
                {
                    doi++;
                }
            }

            _logger.LogInformation("Du an {ProjectId} da cham: chot {Doi} nhan cua {SoBai} bai", p.ProjectId, doi, p.Attempts.Count);
        }
    }

    /// <summary>attempt.appeal_resolved: khieu nai ca bai duoc chap nhan → nhan cua bai vao ket qua.</summary>
    public sealed class AttemptAppealResolvedProcessor : IEventProcessor<AttemptAppealResolved>
    {
        private readonly AnnotationDbContext _db;

        public AttemptAppealResolvedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<AttemptAppealResolved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptAppealResolved p = envelope.Payload;
            if (!p.Accepted)
            {
                return;
            }

            List<LabelAnnotation> nhan = await _db.Annotations.Where(a => a.AttemptId == p.AttemptId && a.ProjectId == p.ProjectId).ToListAsync(ct);
            foreach (LabelAnnotation a in nhan)
            {
                a.ChapNhanSauKhieuNai(envelope.Actor == null ? null : envelope.Actor.UserId, p.Note, envelope.OccurredAt);
            }
        }
    }

    public sealed class ProjectPublishedProcessor : IEventProcessor<ProjectPublished>
    {
        private readonly AnnotationDbContext _db;

        public ProjectPublishedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectPublished> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectPublished p = envelope.Payload;

            // Dieu khoan bat bien sau publish: da co thi thoi.
            bool daCo = await _db.ProjectTerms.AnyAsync(t => t.ProjectId == p.ProjectId, ct);
            if (daCo)
            {
                return;
            }

            // Doc qua dung cua kiem cua Crowd.Labeling: tap nhan hong → NEM LOI → DLQ.
            _db.ProjectTerms.Add(ProjectTerms.Tao(p.ProjectId, p.OwnerId, LabelSchema.Doc(p.LabelSchema)));
        }
    }

    // ---------------------------------------------------------------------
    // Ket thuc du an ← project.completed | project.cancelled
    // ---------------------------------------------------------------------

    /// <summary>project.completed: danh dau du an ket thuc — bai nop toi sau la loi.</summary>
    public sealed class ProjectCompletedProcessor : IEventProcessor<ProjectCompleted>
    {
        private readonly AnnotationDbContext _db;

        public ProjectCompletedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectCompleted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectTerms? t = await DieuKhoanDuAn.KhoaAsync(_db, envelope.Payload.ProjectId, ct);
            if (t != null)
            {
                t.KetThuc(envelope.OccurredAt);
            }
        }
    }

    /// <summary>project.cancelled: nhu completed (du an huy truoc publish thi chua co dieu khoan — bo qua).</summary>
    public sealed class ProjectCancelledProcessor : IEventProcessor<ProjectCancelled>
    {
        private readonly AnnotationDbContext _db;

        public ProjectCancelledProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectCancelled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectTerms? t = await DieuKhoanDuAn.KhoaAsync(_db, envelope.Payload.ProjectId, ct);
            if (t != null)
            {
                t.KetThuc(envelope.OccurredAt);
            }
        }
    }

    // ---------------------------------------------------------------------
    // project_members_cache ← member.* (giong task-svc)
    // ---------------------------------------------------------------------

    internal static class MemberCacheWriter
    {
        public static async Task ApDungAsync(
            AnnotationDbContext db, Guid projectId, Guid userId, CachedMemberRole? role, CachedMemberState state, DateTimeOffset occurredAt, CancellationToken ct)
        {
            MemberCache? m = await db.Members.FirstOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);

            if (m == null)
            {
                db.Members.Add(MemberCache.Tao(projectId, userId, role ?? CachedMemberRole.Labeler, state, occurredAt));
                return;
            }

            m.ApDung(role, state, occurredAt);
        }

        public static CachedMemberRole DoiVaiTro(ProjectMemberRole r)
        {
            switch (r)
            {
                case ProjectMemberRole.Owner: return CachedMemberRole.Owner;
                case ProjectMemberRole.Labeler: return CachedMemberRole.Labeler;
                case ProjectMemberRole.Reviewer: return CachedMemberRole.Reviewer;
                default: throw new ArgumentOutOfRangeException(nameof(r), r, "Vai tro chua map.");
            }
        }
    }

    public sealed class MemberAddedProcessor : IEventProcessor<MemberAdded>
    {
        private readonly AnnotationDbContext _db;

        public MemberAddedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public Task XuLyAsync(EventEnvelope<MemberAdded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            MemberAdded p = envelope.Payload;
            return MemberCacheWriter.ApDungAsync(
                _db, p.ProjectId, p.UserId, MemberCacheWriter.DoiVaiTro(p.Role), CachedMemberState.Active, envelope.OccurredAt, ct);
        }
    }

    public sealed class MemberBlockedProcessor : IEventProcessor<MemberBlocked>
    {
        private readonly AnnotationDbContext _db;

        public MemberBlockedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public Task XuLyAsync(EventEnvelope<MemberBlocked> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return MemberCacheWriter.ApDungAsync(
                _db, envelope.Payload.ProjectId, envelope.Payload.UserId, null, CachedMemberState.Blocked, envelope.OccurredAt, ct);
        }
    }

    public sealed class MemberUnblockedProcessor : IEventProcessor<MemberUnblocked>
    {
        private readonly AnnotationDbContext _db;

        public MemberUnblockedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public Task XuLyAsync(EventEnvelope<MemberUnblocked> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return MemberCacheWriter.ApDungAsync(
                _db, envelope.Payload.ProjectId, envelope.Payload.UserId, null, CachedMemberState.Active, envelope.OccurredAt, ct);
        }
    }

    public sealed class MemberRemovedProcessor : IEventProcessor<MemberRemoved>
    {
        private readonly AnnotationDbContext _db;

        public MemberRemovedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public Task XuLyAsync(EventEnvelope<MemberRemoved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return MemberCacheWriter.ApDungAsync(
                _db, envelope.Payload.ProjectId, envelope.Payload.UserId, null, CachedMemberState.Removed, envelope.OccurredAt, ct);
        }
    }
}
