using System;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Members
{
    public enum MemberRole
    {
        Owner,
        Labeler,
        Reviewer,
    }

    public enum MemberState
    {
        Active,

        /// <summary>Bi chu du an chan (FB-23): khong nhan task, khong tu tham gia lai duoc.</summary>
        Blocked,
    }

    /// <summary>
    /// Mot dong trong project_members — nguon su that cua phan quyen CHIEU NGANG
    /// (VD-S-14): "user nay co duoc dung vao du an nay khong".
    ///
    /// Khoa chinh la cap (ProjectId, UserId): moi nguoi mot vai tro trong mot du
    /// an. Bang nay duoc nhan ban sang task/annotation/media qua member.* event.
    /// </summary>
    public sealed class ProjectMember
    {
        private ProjectMember()
        {
        }

        public Guid ProjectId { get; private set; }

        public Guid UserId { get; private set; }

        public MemberRole Role { get; private set; }

        public MemberState State { get; private set; }

        public DateTimeOffset JoinedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static ProjectMember TaoChuSoHuu(Guid projectId, Guid ownerId, DateTimeOffset luc)
        {
            return Tao(projectId, ownerId, MemberRole.Owner, luc);
        }

        /// <summary>Them labeler hoac reviewer. Chu so huu chi duoc tao cung du an.</summary>
        public static ProjectMember TaoThanhVien(Guid projectId, Guid userId, MemberRole role, DateTimeOffset luc)
        {
            if (role == MemberRole.Owner)
            {
                throw new InvalidValueException("khong_the_them_owner", "Moi du an chi co mot chu so huu.");
            }

            return Tao(projectId, userId, role, luc);
        }

        public bool LaHoatDong()
        {
            return State == MemberState.Active;
        }

        public void Chan(DateTimeOffset luc)
        {
            if (Role == MemberRole.Owner)
            {
                throw new RuleViolationException("khong_the_chan_owner", "Khong the chan chu so huu du an.");
            }

            if (State == MemberState.Blocked)
            {
                throw new RuleViolationException("da_bi_chan", "Thanh vien da bi chan tu truoc.");
            }

            State = MemberState.Blocked;
            UpdatedAt = luc;
        }

        public void BoChan(DateTimeOffset luc)
        {
            if (State != MemberState.Blocked)
            {
                throw new RuleViolationException("khong_bi_chan", "Thanh vien khong bi chan.");
            }

            State = MemberState.Active;
            UpdatedAt = luc;
        }

        /// <summary>Kiem truoc khi xoa khoi du an.</summary>
        public void KiemTraCoTheXoa()
        {
            if (Role == MemberRole.Owner)
            {
                throw new RuleViolationException("khong_the_xoa_owner", "Khong the xoa chu so huu khoi du an.");
            }
        }

        private static ProjectMember Tao(Guid projectId, Guid userId, MemberRole role, DateTimeOffset luc)
        {
            if (projectId == Guid.Empty || userId == Guid.Empty)
            {
                throw new InvalidValueException("id_rong", "Thieu du an hoac nguoi dung.");
            }

            ProjectMember m = new ProjectMember();
            m.ProjectId = projectId;
            m.UserId = userId;
            m.Role = role;
            m.State = MemberState.Active;
            m.JoinedAt = luc;
            m.UpdatedAt = luc;
            return m;
        }
    }
}
