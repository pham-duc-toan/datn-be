using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.Contracts.Project;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Quan ly project_members. Moi thay doi phat member.* de task/annotation/
    /// media cap nhat ban sao cua chung (docs 3.8).
    /// </summary>
    public sealed class MemberService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectEventPublisher _events;
        private readonly TimeProvider _clock;

        public MemberService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _access = access;
            _events = events;
            _clock = clock;
        }

        public async Task<IReadOnlyList<MemberResponse>> DanhSachAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            List<ProjectMember> ds = await _db.ProjectMembers
                .AsNoTracking()
                .Where(m => m.ProjectId == projectId)
                .OrderBy(m => m.JoinedAt)
                .ToListAsync(ct);

            List<MemberResponse> ketQua = new List<MemberResponse>();
            foreach (ProjectMember m in ds)
            {
                ketQua.Add(TaoResponse(m));
            }

            return ketQua;
        }

        /// <summary>
        /// Chu du an moi/them nguoi (FP-05 private pool, hoac them reviewer).
        /// Khong can bai test — chu du an tu chiu trach nhiem nguoi minh moi.
        /// </summary>
        public async Task<MemberResponse> ThemAsync(Guid projectId, AddMemberRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (body.UserId == null || body.Role == null)
            {
                throw new InvalidValueException("thieu_thong_tin", "Can userId va role.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            if (duAn.DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Du an da ket thuc.");
            }

            ProjectMember moi = ProjectMember.TaoThanhVien(projectId, body.UserId.Value, body.Role.Value, _clock.GetUtcNow());
            await LuuThanhVienMoiAsync(moi, caller, ct);

            return TaoResponse(moi);
        }

        /// <summary>
        /// Labeler tu tham gia du an cong khai khong yeu cau test. Du an can test
        /// thi di duong EntranceTestService — dau moi thanh thanh vien.
        /// </summary>
        public async Task<MemberResponse> ThamGiaAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeXemAsync(projectId, caller, ct);
            duAn.KiemTraChoTuThamGia();

            if (duAn.RequireEntranceTest)
            {
                throw new RuleViolationException("can_lam_test", "Du an yeu cau lam bai test dau vao truoc khi tham gia.");
            }

            ProjectMember moi = ProjectMember.TaoThanhVien(projectId, caller.LayUserId(), MemberRole.Labeler, _clock.GetUtcNow());
            await LuuThanhVienMoiAsync(moi, caller, ct);

            return TaoResponse(moi);
        }

        public async Task<MemberResponse> ChanAsync(Guid projectId, Guid userId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);
            ProjectMember m = await LayThanhVienAsync(projectId, userId, ct);

            m.Chan(_clock.GetUtcNow());
            _events.Phat(caller, new MemberBlocked { ProjectId = projectId, UserId = userId });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(m);
        }

        public async Task<MemberResponse> BoChanAsync(Guid projectId, Guid userId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);
            ProjectMember m = await LayThanhVienAsync(projectId, userId, ct);

            m.BoChan(_clock.GetUtcNow());
            _events.Phat(caller, new MemberUnblocked { ProjectId = projectId, UserId = userId });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(m);
        }

        public async Task XoaAsync(Guid projectId, Guid userId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);
            ProjectMember m = await LayThanhVienAsync(projectId, userId, ct);

            m.KiemTraCoTheXoa();
            _db.ProjectMembers.Remove(m);
            _events.Phat(caller, new MemberRemoved { ProjectId = projectId, UserId = userId });

            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Them thanh vien moi + phat member.added, trong MOT SaveChanges.
        /// Dung chung cho moi, tu tham gia, va dau bai test.
        /// </summary>
        public async Task LuuThanhVienMoiAsync(ProjectMember moi, Caller caller, CancellationToken ct)
        {
            if (moi == null)
            {
                throw new ArgumentNullException(nameof(moi));
            }

            ProjectMember? cu = await _db.ProjectMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == moi.ProjectId && m.UserId == moi.UserId, ct);

            if (cu != null)
            {
                if (cu.State == MemberState.Blocked)
                {
                    throw new RuleViolationException("bi_chan", "Ban da bi chan khoi du an nay.");
                }

                throw new RuleViolationException("da_la_thanh_vien", "Nguoi nay da la thanh vien du an.");
            }

            _db.ProjectMembers.Add(moi);
            _events.Phat(caller, new MemberAdded
            {
                ProjectId = moi.ProjectId,
                UserId = moi.UserId,
                Role = ContractMapper.ToContract(moi.Role),
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "PK_project_members"))
            {
                // Hai request tham gia cung luc: ca hai lot qua kiem tra o tren,
                // khoa chinh (project_id, user_id) chon ra mot.
                _db.ChangeTracker.Clear();
                throw new RuleViolationException("da_la_thanh_vien", "Nguoi nay da la thanh vien du an.");
            }
        }

        private async Task<ProjectMember> LayThanhVienAsync(Guid projectId, Guid userId, CancellationToken ct)
        {
            ProjectMember? m = await _db.ProjectMembers
                .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);

            if (m == null)
            {
                throw new NotFoundException("Khong tim thay thanh vien.");
            }

            return m;
        }

        private static MemberResponse TaoResponse(ProjectMember m)
        {
            return new MemberResponse
            {
                UserId = m.UserId,
                Role = m.Role,
                State = m.State,
                JoinedAt = m.JoinedAt,
            };
        }
    }
}
