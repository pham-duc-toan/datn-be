using System;
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
using Crowd.Contracts.Annotation;
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
    /// assignment.submitted (task-svc): LUU nhan, roi phat annotation.submitted
    /// cho quality/fraud. Event phat sinh mang causationId = eventId goc, nen dung
    /// lai duoc chuoi nhan qua: "nhan nay tu luot lease nao".
    /// </summary>
    public sealed class AssignmentSubmittedProcessor : IEventProcessor<AssignmentSubmitted>
    {
        private readonly AnnotationDbContext _db;
        private readonly AnnotationEventPublisher _events;
        private readonly ILogger<AssignmentSubmittedProcessor> _logger;

        public AssignmentSubmittedProcessor(
            AnnotationDbContext db,
            AnnotationEventPublisher events,
            ILogger<AssignmentSubmittedProcessor> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _events = events;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<AssignmentSubmitted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AssignmentSubmitted p = envelope.Payload;

            // Lop chan thu hai sau processed_events: cung assignment da thanh nhan
            // (vd event phat lai voi eventId khac) thi khong tao nhan thu hai.
            bool daCo = await _db.Annotations.AnyAsync(a => a.AssignmentId == p.AssignmentId, ct);
            if (daCo)
            {
                _logger.LogInformation("Assignment {AssignmentId} da co nhan — bo qua", p.AssignmentId);
                return;
            }

            // Du an da KET THUC (project.completed / cancelled): project-svc chi dong khi so luot nop
            // ben task-svc KHOP so nhan o day va khong con luot dang giu — nhan toi sau la sai bat
            // bien. NEM LOI de message vao DLQ cho nguoi xem (VD-D-06).
            // Dong so nhung CHUA ket thuc thi VAN GHI NHAN: doanh nghiep co the chay tiep sau lan dong
            // so ma project-svc chua chot, va project.resumed (mo so) di queue khac voi assignment.submitted
            // — nop co the toi truoc (TLA+ NC-B-01 tim ra). Nhan moi o "cho duyet" tu chan lan dong sau.
            ProjectTerms? dieuKhoan = await DieuKhoanDuAn.KhoaChiaSeAsync(_db, p.ProjectId, ct);
            if (dieuKhoan != null && dieuKhoan.IsFinal)
            {
                throw new InvalidOperationException(
                    "Luot nop " + p.AssignmentId + " toi sau khi du an " + p.ProjectId + " da ket thuc.");
            }

            LabelAnnotation a = LabelAnnotation.TaoTuLuotNop(
                p.AssignmentId,
                p.TaskId,
                p.ProjectId,
                p.SampleId,
                p.StorageKey,
                p.SampleContent,
                p.SampleMetadata,
                p.LabelerId,
                p.LabelPayload,
                p.SubmittedAt);

            _db.Annotations.Add(a);

            _events.Phat(Caller.HeThong(envelope.CorrelationId, envelope.EventId), new AnnotationSubmitted
            {
                AnnotationId = a.Id,
                TaskId = a.TaskId,
                ProjectId = a.ProjectId,
                SampleId = a.SampleId,
                LabelerId = a.LabelerId,
                LabelPayload = p.LabelPayload,
                Source = AnnotationSource.Professional,
            });
        }
    }

    /// <summary>project.published: chot don gia + phi + tap nhan cho viec duyet.</summary>
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
            _db.ProjectTerms.Add(ProjectTerms.Tao(
                p.ProjectId, p.OwnerId, p.UnitPriceVnd, p.PlatformFeeVnd, LabelSchema.Doc(p.LabelSchema)));
        }
    }

    // ---------------------------------------------------------------------
    // Dong so du an ← project.resumed | project.completed | project.cancelled
    // ---------------------------------------------------------------------

    /// <summary>
    /// project.resumed: mo lai so neu lan dong so truoc do khong duoc project-svc chot
    /// (doanh nghiep chay tiep). Event cu hon luc dong so thi bo qua.
    /// </summary>
    public sealed class ProjectResumedProcessor : IEventProcessor<ProjectResumed>
    {
        private readonly AnnotationDbContext _db;

        public ProjectResumedProcessor(AnnotationDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public async Task XuLyAsync(EventEnvelope<ProjectResumed> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectTerms? t = await DieuKhoanDuAn.KhoaAsync(_db, envelope.Payload.ProjectId, ct);
            if (t != null)
            {
                t.MoLai(envelope.OccurredAt);
            }
        }
    }

    /// <summary>project.completed: dong so VINH VIEN (thuong da dong qua API noi bo truoc do).</summary>
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
