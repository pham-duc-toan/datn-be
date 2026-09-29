using System;

namespace Crowd.Annotation.Domain.Members
{
    public enum CachedMemberRole
    {
        Owner,
        Labeler,
        Reviewer,
    }

    public enum CachedMemberState
    {
        Active,
        Blocked,
        Removed,
    }

    /// <summary>
    /// Ban sao cua project_members (docs 3.8), dung tu member.* event. Nen cua
    /// phan quyen CHIEU NGANG trong annotation-svc: chi owner/reviewer dang hoat dong moi
    /// duyet duoc nhan cua du an (FB-21).
    ///
    /// Giu dong Removed thay vi xoa: neu member.removed toi TRUOC member.added
    /// (sai thu tu), dong Removed voi moc thoi gian moi hon se chan ban added cu
    /// ghi de len (VD-D-05). Xoa di thi mat dau vet de so sanh.
    /// </summary>
    public sealed class MemberCache
    {
        private MemberCache()
        {
        }

        public Guid ProjectId { get; private set; }

        public Guid UserId { get; private set; }

        public CachedMemberRole Role { get; private set; }

        public CachedMemberState State { get; private set; }

        /// <summary>occurredAt cua event gan nhat da ap dung.</summary>
        public DateTimeOffset UpdatedAt { get; private set; }

        public static MemberCache Tao(Guid projectId, Guid userId, CachedMemberRole role, CachedMemberState state, DateTimeOffset occurredAt)
        {
            MemberCache m = new MemberCache();
            m.ProjectId = projectId;
            m.UserId = userId;
            m.Role = role;
            m.State = state;
            m.UpdatedAt = occurredAt;
            return m;
        }

        /// <summary>Ap dung mot event. Tra ve false neu event cu hon (bo qua).</summary>
        public bool ApDung(CachedMemberRole? role, CachedMemberState state, DateTimeOffset occurredAt)
        {
            if (occurredAt < UpdatedAt)
            {
                return false;
            }

            if (role.HasValue)
            {
                Role = role.Value;
            }

            State = state;
            UpdatedAt = occurredAt;
            return true;
        }
    }
}
