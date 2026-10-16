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

        /// <summary>
        /// Bi chan VI doanh nghiep dua labeler vao danh sach chan chung (FB-23), khong phai chan rieng du an
        /// nay — bo khoi danh sach thi tu mo lai; chan rieng thi giu.
        /// </summary>
        public bool BlockedByOwnerList { get; private set; }

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

        /// <summary>Chan vi danh sach chan cua doanh nghiep; da bi chan rieng thi giu nguyen (tra false).</summary>
        public bool ChanTheoDanhSach(DateTimeOffset luc)
        {
            if (Role == MemberRole.Owner || State == MemberState.Blocked)
            {
                return false;
            }

            State = MemberState.Blocked;
            BlockedByOwnerList = true;
            UpdatedAt = luc;
            return true;
        }

        /// <summary>Bo khoi danh sach chan: chi mo nhung dong bi chan VI danh sach.</summary>
        public bool BoChanTheoDanhSach(DateTimeOffset luc)
        {
            if (State != MemberState.Blocked || !BlockedByOwnerList)
            {
                return false;
            }

            State = MemberState.Active;
            BlockedByOwnerList = false;
            UpdatedAt = luc;
            return true;
        }

        /// <summary>Labeler tu roi du an (muc 2.9: roi bat ky luc nao, khong bi phat). Dang bi chan thi khong roi duoc — roi la xoa dau vet chan.</summary>
        public void KiemTraTuRoiDuoc()
        {
            if (Role == MemberRole.Owner)
            {
                throw new RuleViolationException("chu_du_an_khong_roi", "Chu du an khong roi du an cua minh.");
            }

            if (State == MemberState.Blocked)
            {
                throw new RuleViolationException("bi_chan", "Ban dang bi chan khoi du an nay.");
            }
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
            BlockedByOwnerList = false;
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
