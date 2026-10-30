using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
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
    /// Quan he doanh nghiep — labeler (FB-23), rong hon "chan trong mot du an" da co:
    ///   - danh sach UU TIEN: moi nhanh vao du an moi (POST /projects/{id}/members/invite-preferred), don
    ///     xin tham gia du an che do approval duoc tu duyet;
    ///   - danh sach CHAN: khong vao duoc moi du an cua doanh nghiep (MemberService kiem o moi cua); dang
    ///     la thanh vien du an nao chua ket thuc thi bi chan o do (member.blocked); bo khoi danh sach thi
    ///     chi mo nhung du an bi chan VI danh sach (BlockedByOwnerList), chan rieng du an thi giu;
    ///   - DANH GIA 1–5 sao theo du an (labeler phai da la thanh vien labeler cua du an do).
    /// </summary>
    public sealed class QuanHeLabelerService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectEventPublisher _events;
        private readonly MemberService _members;
        private readonly IdentityClient _identity;
        private readonly TimeProvider _clock;

        public QuanHeLabelerService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            MemberService members,
            IdentityClient identity,
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

            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _access = access;
            _events = events;
            _members = members;
            _identity = identity;
            _clock = clock;
        }

        // =====================================================================
        // DANH SACH UU TIEN / CHAN
        // =====================================================================

        /// <summary>GET /projects/labeler-lists?kind=preferred|blocked</summary>
        public async Task<IReadOnlyList<OwnerListEntryResponse>> DanhSachAsync(OwnerListKind? kind, Caller caller, CancellationToken ct)
        {
            Guid ownerId = caller.LayUserId();
            IQueryable<OwnerLabelerEntry> q = _db.OwnerLabelerEntries.AsNoTracking().Where(e => e.OwnerId == ownerId);
            if (kind.HasValue)
            {
                OwnerListKind k = kind.Value;
                q = q.Where(e => e.Kind == k);
            }

            List<OwnerLabelerEntry> ds = await q.OrderByDescending(e => e.UpdatedAt).ToListAsync(ct);
            return ds.Select(e => TaoResponse(e, null)).ToList();
        }

        /// <summary>PUT /projects/labeler-lists/{labelerId} { kind, note } — them / doi loai.</summary>
        public async Task<OwnerListEntryResponse> DatAsync(Guid labelerId, OwnerListRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null || body.Kind == null)
            {
                throw new InvalidValueException("thieu_thong_tin", "Cần kind: preferred | blocked.");
            }

            Guid ownerId = caller.LayUserId();
            DateTimeOffset luc = _clock.GetUtcNow();
            OwnerLabelerEntry? e = await _db.OwnerLabelerEntries.FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.LabelerId == labelerId, ct);
            bool dangChan = e != null && e.Kind == OwnerListKind.Blocked;
            if (e == null)
            {
                e = OwnerLabelerEntry.Tao(ownerId, labelerId, body.Kind.Value, body.Note, luc);
                _db.OwnerLabelerEntries.Add(e);
            }
            else
            {
                e.DoiLoai(body.Kind.Value, body.Note, luc);
            }

            int anhHuong = 0;
            if (body.Kind.Value == OwnerListKind.Blocked && !dangChan)
            {
                anhHuong = await ChanOMoiDuAnAsync(ownerId, labelerId, caller, luc, ct);
            }
            else if (body.Kind.Value != OwnerListKind.Blocked && dangChan)
            {
                anhHuong = await MoOMoiDuAnAsync(ownerId, labelerId, caller, luc, ct);
            }

            await _db.SaveChangesAsync(ct);
            return TaoResponse(e, anhHuong);
        }

        /// <summary>
        /// PUT /projects/labeler-lists/by-username/{username} { kind, note } — doanh nghiep chi biet username
        /// cua labeler (userId la ma noi bo), tra identity ra userId roi them nhu DatAsync.
        /// </summary>
        public async Task<OwnerListEntryResponse> DatTheoUsernameAsync(string username, OwnerListRequest body, Caller caller, CancellationToken ct)
        {
            string ten = (username ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
            if (ten.Length == 0)
            {
                throw new InvalidValueException("thieu_thong_tin", "Cần username của labeler.");
            }

            IReadOnlyList<NguoiDungTraCuu> tim = await _identity.TraCuuAsync(new List<Guid>(), new List<string>(), new List<string> { ten }, ct);
            NguoiDungTraCuu? n = tim.FirstOrDefault(x => string.Equals(x.Username, ten, StringComparison.Ordinal));
            if (n == null)
            {
                throw new NotFoundException("Không có tài khoản @" + ten + ".");
            }

            if (n.UserId == caller.LayUserId())
            {
                throw new InvalidValueException("khong_hop_le", "Không thêm chính mình vào danh sách được.");
            }

            return await DatAsync(n.UserId, body, caller, ct);
        }

        /// <summary>DELETE /projects/labeler-lists/{labelerId} — bo khoi danh sach (dang chan thi mo lai).</summary>
        public async Task XoaAsync(Guid labelerId, Caller caller, CancellationToken ct)
        {
            Guid ownerId = caller.LayUserId();
            OwnerLabelerEntry? e = await _db.OwnerLabelerEntries.FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.LabelerId == labelerId, ct);
            if (e == null)
            {
                throw new NotFoundException("Labeler không có trong danh sách.");
            }

            if (e.Kind == OwnerListKind.Blocked)
            {
                await MoOMoiDuAnAsync(ownerId, labelerId, caller, _clock.GetUtcNow(), ct);
            }

            _db.OwnerLabelerEntries.Remove(e);
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>POST /projects/{id}/members/invite-preferred — moi moi labeler trong danh sach uu tien.</summary>
        public async Task<AddMembersBatchResponse> MoiUuTienAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            List<Guid> ds = await _db.OwnerLabelerEntries.AsNoTracking()
                .Where(e => e.OwnerId == duAn.OwnerId && e.Kind == OwnerListKind.Preferred)
                .Select(e => e.LabelerId)
                .ToListAsync(ct);
            if (ds.Count == 0)
            {
                throw new RuleViolationException("danh_sach_uu_tien_rong", "Danh sách ưu tiên đang rỗng.");
            }

            return await _members.ThemNhieuAsync(projectId, new AddMembersBatchRequest { Role = MemberRole.Labeler, UserIds = ds }, caller, ct);
        }

        public Task<bool> LaUuTienAsync(Guid ownerId, Guid labelerId, CancellationToken ct)
        {
            return _db.OwnerLabelerEntries.AnyAsync(e => e.OwnerId == ownerId && e.LabelerId == labelerId && e.Kind == OwnerListKind.Preferred, ct);
        }

        private async Task<int> ChanOMoiDuAnAsync(Guid ownerId, Guid labelerId, Caller caller, DateTimeOffset luc, CancellationToken ct)
        {
            List<ProjectMember> ds = await (from m in _db.ProjectMembers
                                            join p in _db.Projects on m.ProjectId equals p.Id
                                            where p.OwnerId == ownerId && m.UserId == labelerId
                                                  && p.Status != ProjectStatus.Completed && p.Status != ProjectStatus.Cancelled
                                            select m).ToListAsync(ct);
            int n = 0;
            foreach (ProjectMember m in ds)
            {
                if (m.ChanTheoDanhSach(luc))
                {
                    _events.Phat(caller, new MemberBlocked { ProjectId = m.ProjectId, UserId = labelerId });
                    n++;
                }
            }

            return n;
        }

        private async Task<int> MoOMoiDuAnAsync(Guid ownerId, Guid labelerId, Caller caller, DateTimeOffset luc, CancellationToken ct)
        {
            List<ProjectMember> ds = await (from m in _db.ProjectMembers
                                            join p in _db.Projects on m.ProjectId equals p.Id
                                            where p.OwnerId == ownerId && m.UserId == labelerId && m.BlockedByOwnerList
                                            select m).ToListAsync(ct);
            int n = 0;
            foreach (ProjectMember m in ds)
            {
                if (m.BoChanTheoDanhSach(luc))
                {
                    _events.Phat(caller, new MemberUnblocked { ProjectId = m.ProjectId, UserId = labelerId });
                    n++;
                }
            }

            return n;
        }

        // =====================================================================
        // DANH GIA
        // =====================================================================

        /// <summary>PUT /projects/{id}/ratings/{labelerId} { stars, comment } — chu du an danh gia labeler cua du an.</summary>
        public async Task<RatingResponse> DanhGiaAsync(Guid projectId, Guid labelerId, RatingRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            bool laLabeler = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == labelerId && m.Role == MemberRole.Labeler, ct);
            if (!laLabeler)
            {
                throw new RuleViolationException("khong_phai_labeler_cua_du_an", "Chỉ đánh giá labeler đã tham gia dự án này.");
            }

            DateTimeOffset luc = _clock.GetUtcNow();
            LabelerRating? r = await _db.LabelerRatings.FirstOrDefaultAsync(x => x.ProjectId == projectId && x.LabelerId == labelerId, ct);
            if (r == null)
            {
                r = LabelerRating.Tao(projectId, labelerId, duAn.OwnerId, body.Stars, body.Comment, luc);
                _db.LabelerRatings.Add(r);
            }
            else
            {
                r.Sua(body.Stars, body.Comment, luc);
            }

            await _db.SaveChangesAsync(ct);
            return TaoResponse(r);
        }

        /// <summary>GET /projects/{id}/ratings — chu du an xem danh gia da cho.</summary>
        public async Task<IReadOnlyList<RatingResponse>> DanhGiaCuaDuAnAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);
            List<LabelerRating> ds = await _db.LabelerRatings.AsNoTracking().Where(r => r.ProjectId == projectId).ToListAsync(ct);
            return ds.Select(TaoResponse).ToList();
        }

        /// <summary>
        /// GET /projects/labelers/{labelerId}/rating-summary — trung binh + phan bo (doanh nghiep, admin,
        /// hoac chinh labeler). Khong tra nhan xet tung doanh nghiep (rieng tu giua hai ben).
        /// </summary>
        public async Task<RatingSummaryResponse> TongHopAsync(Guid labelerId, bool laDoanhNghiep, Caller caller, CancellationToken ct)
        {
            bool duocXem = caller.IsAdmin || laDoanhNghiep || caller.LayUserId() == labelerId;
            if (!duocXem)
            {
                throw new ForbiddenException("Chỉ doanh nghiệp, admin hoặc chính labeler xem được đánh giá.");
            }

            List<int> sao = await _db.LabelerRatings.AsNoTracking().Where(r => r.LabelerId == labelerId).Select(r => r.Stars).ToListAsync(ct);
            int[] phanBo = new int[5];
            foreach (int s in sao)
            {
                phanBo[s - 1]++;
            }

            return new RatingSummaryResponse
            {
                LabelerId = labelerId,
                Average = sao.Count == 0 ? null : Math.Round(sao.Average(), 2),
                Count = sao.Count,
                Distribution = phanBo,
            };
        }

        private static OwnerListEntryResponse TaoResponse(OwnerLabelerEntry e, int? anhHuong)
        {
            return new OwnerListEntryResponse
            {
                LabelerId = e.LabelerId,
                Kind = e.Kind,
                Note = e.Note,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                AffectedProjects = anhHuong,
            };
        }

        private static RatingResponse TaoResponse(LabelerRating r)
        {
            return new RatingResponse
            {
                ProjectId = r.ProjectId,
                LabelerId = r.LabelerId,
                Stars = r.Stars,
                Comment = r.Comment,
                UpdatedAt = r.UpdatedAt,
            };
        }
    }
}
