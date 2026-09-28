using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Project;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Tasking.Api.Consumers
{
    // ---------------------------------------------------------------------
    // project_members_cache ← member.* (project-svc)
    // ---------------------------------------------------------------------

    /// <summary>Ap mot event thanh vien vao ban sao, dung thu tu theo occurredAt (VD-D-05).</summary>
    internal static class MemberCacheWriter
    {
        public static async Task<bool> ApDungAsync(
            TaskDbContext db, Guid projectId, Guid userId, CachedMemberRole? role, CachedMemberState state, DateTimeOffset occurredAt, CancellationToken ct)
        {
            MemberCache? m = await db.Members.FirstOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);

            if (m == null)
            {
                // Chua biet vai tro (vd member.blocked toi truoc member.added): tam coi
                // la Labeler — trang thai Blocked/Removed van chan duoc; khi added den
                // thi so moc thoi gian quyet dinh ai thang.
                db.Members.Add(MemberCache.Tao(projectId, userId, role ?? CachedMemberRole.Labeler, state, occurredAt));
                return true;
            }

            return m.ApDung(role, state, occurredAt);
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
        private readonly TaskDbContext _db;

        public MemberAddedProcessor(TaskDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public Task XuLyAsync(EventEnvelope<MemberAdded> envelope, CancellationToken ct)
        {
            MemberAdded p = envelope.Payload;
            return MemberCacheWriter.ApDungAsync(
                _db, p.ProjectId, p.UserId, MemberCacheWriter.DoiVaiTro(p.Role), CachedMemberState.Active, envelope.OccurredAt, ct);
        }
    }

    public sealed class MemberUnblockedProcessor : IEventProcessor<MemberUnblocked>
    {
        private readonly TaskDbContext _db;

        public MemberUnblockedProcessor(TaskDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public Task XuLyAsync(EventEnvelope<MemberUnblocked> envelope, CancellationToken ct)
        {
            MemberUnblocked p = envelope.Payload;
            return MemberCacheWriter.ApDungAsync(_db, p.ProjectId, p.UserId, null, CachedMemberState.Active, envelope.OccurredAt, ct);
        }
    }

    /// <summary>member.blocked: chan + THU HOI lease dang giu (FB-23) — khong cho lam not.</summary>
    public sealed class MemberBlockedProcessor : IEventProcessor<MemberBlocked>
    {
        private readonly TaskDbContext _db;
        private readonly LeaseRevoker _revoker;
        private readonly TimeProvider _clock;

        public MemberBlockedProcessor(TaskDbContext db, LeaseRevoker revoker, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (revoker == null)
            {
                throw new ArgumentNullException(nameof(revoker));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _revoker = revoker;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<MemberBlocked> envelope, CancellationToken ct)
        {
            MemberBlocked p = envelope.Payload;
            bool apDung = await MemberCacheWriter.ApDungAsync(_db, p.ProjectId, p.UserId, null, CachedMemberState.Blocked, envelope.OccurredAt, ct);

            if (apDung)
            {
                await _revoker.ThuHoiAsync(p.ProjectId, p.UserId, _clock.GetUtcNow(), ct);
            }
        }
    }

    public sealed class MemberRemovedProcessor : IEventProcessor<MemberRemoved>
    {
        private readonly TaskDbContext _db;
        private readonly LeaseRevoker _revoker;
        private readonly TimeProvider _clock;

        public MemberRemovedProcessor(TaskDbContext db, LeaseRevoker revoker, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (revoker == null)
            {
                throw new ArgumentNullException(nameof(revoker));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _revoker = revoker;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<MemberRemoved> envelope, CancellationToken ct)
        {
            MemberRemoved p = envelope.Payload;
            bool apDung = await MemberCacheWriter.ApDungAsync(_db, p.ProjectId, p.UserId, null, CachedMemberState.Removed, envelope.OccurredAt, ct);

            if (apDung)
            {
                await _revoker.ThuHoiAsync(p.ProjectId, p.UserId, _clock.GetUtcNow(), ct);
            }
        }
    }

    // ---------------------------------------------------------------------
    // labeler_cache ← user.blocked / reputation.changed / level.changed (identity)
    // ---------------------------------------------------------------------

    internal static class LabelerCacheWriter
    {
        public static async Task<LabelerProfile> LayHoacTaoAsync(TaskDbContext db, Guid userId, CancellationToken ct)
        {
            LabelerProfile? p = await db.Labelers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (p == null)
            {
                p = LabelerProfile.Tao(userId);
                db.Labelers.Add(p);
            }

            return p;
        }
    }

    /// <summary>Tai khoan bi khoa: thu hoi MOI lease o MOI du an.</summary>
    public sealed class UserBlockedProcessor : IEventProcessor<UserBlocked>
    {
        private readonly TaskDbContext _db;
        private readonly LeaseRevoker _revoker;
        private readonly TimeProvider _clock;

        public UserBlockedProcessor(TaskDbContext db, LeaseRevoker revoker, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (revoker == null)
            {
                throw new ArgumentNullException(nameof(revoker));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _revoker = revoker;
            _clock = clock;
        }

        public async Task XuLyAsync(EventEnvelope<UserBlocked> envelope, CancellationToken ct)
        {
            LabelerProfile p = await LabelerCacheWriter.LayHoacTaoAsync(_db, envelope.Payload.UserId, ct);

            if (p.Khoa(envelope.OccurredAt))
            {
                await _revoker.ThuHoiAsync(null, envelope.Payload.UserId, _clock.GetUtcNow(), ct);
            }
        }
    }

    public sealed class ReputationChangedProcessor : IEventProcessor<ReputationChanged>
    {
        private readonly TaskDbContext _db;
        private readonly ILogger<ReputationChangedProcessor> _logger;

        public ReputationChangedProcessor(TaskDbContext db, ILogger<ReputationChangedProcessor> logger)
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

        public async Task XuLyAsync(EventEnvelope<ReputationChanged> envelope, CancellationToken ct)
        {
            LabelerProfile p = await LabelerCacheWriter.LayHoacTaoAsync(_db, envelope.Payload.UserId, ct);

            if (!p.DatReputation(envelope.Payload.Reputation, envelope.OccurredAt))
            {
                _logger.LogInformation("reputation.changed {EventId} cu hon ban dang co — bo qua", envelope.EventId);
            }
        }
    }

    public sealed class LevelChangedProcessor : IEventProcessor<LevelChanged>
    {
        private readonly TaskDbContext _db;
        private readonly ILogger<LevelChangedProcessor> _logger;

        public LevelChangedProcessor(TaskDbContext db, ILogger<LevelChangedProcessor> logger)
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

        public async Task XuLyAsync(EventEnvelope<LevelChanged> envelope, CancellationToken ct)
        {
            LabelerProfile p = await LabelerCacheWriter.LayHoacTaoAsync(_db, envelope.Payload.UserId, ct);

            if (!p.DatLevel(envelope.Payload.Level, envelope.OccurredAt))
            {
                _logger.LogInformation("level.changed {EventId} cu hon ban dang co — bo qua", envelope.EventId);
            }
        }
    }
}
