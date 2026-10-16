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
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Loi moi CHO (bang project_invitations) — xem ProjectInvitation:
    ///   - nguoi duoc moi xem / chap nhan / tu choi loi moi cua minh;
    ///   - chu du an xem / thu hoi loi moi cua du an;
    ///   - email vua duoc xac thuc (user.email_verified) → gan loi moi cho email do vao tai khoan; du an
    ///     khong can dong y rieng va nguoi do du dieu kien thi vao luon (khong phai bam gi them);
    ///   - worker danh dau het han.
    /// Chap nhan di qua CUNG cua kiem dieu kien voi moi duong vao khac (MemberService.KiemDieuKienLabelerAsync).
    /// </summary>
    public sealed class InvitationService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly MemberService _members;
        private readonly IdentityClient _identity;
        private readonly BaoVeService _baoVe;
        private readonly TimeProvider _clock;
        private readonly ILogger<InvitationService> _logger;

        public InvitationService(
            ProjectDbContext db,
            ProjectAccessService access,
            MemberService members,
            IdentityClient identity,
            BaoVeService baoVe,
            TimeProvider clock,
            ILogger<InvitationService> logger)
        {
            if (baoVe == null)
            {
                throw new ArgumentNullException(nameof(baoVe));
            }

            _baoVe = baoVe;
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
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

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _access = access;
            _members = members;
            _identity = identity;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>GET /projects/invitations/mine — loi moi dang cho cua toi (con han).</summary>
        public async Task<IReadOnlyList<InvitationResponse>> CuaToiAsync(Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();
            List<ProjectInvitation> ds = await _db.ProjectInvitations.AsNoTracking()
                .Where(i => i.UserId == uid && i.Status == InvitationStatus.Pending && i.ExpiresAt > bayGio)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync(ct);
            return await TaoResponseAsync(ds, false, ct);
        }

        /// <summary>POST /projects/invitations/{id}/accept — phai da dong y dieu khoan (neu du an yeu cau) va du dieu kien.</summary>
        public async Task<MemberResponse> ChapNhanAsync(Guid invitationId, Caller caller, CancellationToken ct)
        {
            ProjectInvitation moi = await LayCuaToiAsync(invitationId, caller, ct);
            LabelingProject duAn = await _db.Projects.FirstAsync(p => p.Id == moi.ProjectId, ct);
            if (duAn.DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Du an da ket thuc.");
            }

            Guid uid = caller.LayUserId();
            await _members.KiemTraChuaLaThanhVienAsync(duAn.Id, uid, ct);
            await KiemDieuKienTheoVaiTroAsync(duAn, uid, moi.Role, ct);

            DateTimeOffset luc = _clock.GetUtcNow();
            moi.ChapNhan(luc);
            ProjectMember m = ProjectMember.TaoThanhVien(duAn.Id, uid, moi.Role, luc);
            await _members.DongDonDangChoAsync(duAn.Id, uid, duAn.OwnerId, luc, ct);
            await _members.LuuThanhVienMoiAsync(m, MemberJoinVia.Invitation, duAn, caller, ct);
            return MemberService.TaoResponse(m);
        }

        /// <summary>POST /projects/invitations/{id}/decline</summary>
        public async Task<InvitationResponse> TuChoiAsync(Guid invitationId, Caller caller, CancellationToken ct)
        {
            ProjectInvitation moi = await LayCuaToiAsync(invitationId, caller, ct);
            moi.TuChoi(_clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return (await TaoResponseAsync(new List<ProjectInvitation> { moi }, false, ct))[0];
        }

        /// <summary>
        /// GET /projects/{id}/invitations — chu du an. Loi moi theo email khong kem userId cho toi khi chap nhan,
        /// loi moi theo userId khong kem email (VD-S-16).
        /// </summary>
        public async Task<IReadOnlyList<InvitationResponse>> CuaDuAnAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);
            List<ProjectInvitation> ds = await _db.ProjectInvitations.AsNoTracking()
                .Where(i => i.ProjectId == projectId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync(ct);
            return await TaoResponseAsync(ds, true, ct);
        }

        /// <summary>DELETE /projects/{id}/invitations/{invitationId} — chu du an thu hoi.</summary>
        public async Task<InvitationResponse> ThuHoiAsync(Guid projectId, Guid invitationId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);
            ProjectInvitation? moi = await _db.ProjectInvitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.ProjectId == projectId, ct);
            if (moi == null)
            {
                throw new NotFoundException("Khong tim thay loi moi.");
            }

            moi.ThuHoi(_clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return (await TaoResponseAsync(new List<ProjectInvitation> { moi }, true, ct))[0];
        }

        /// <summary>
        /// user.email_verified: tai khoan nay vua CHUNG MINH so huu email → nhan cac loi moi dang cho gui toi
        /// email do. Cho xac thuc (khong lam o user.registered) vi ai cung dang ky duoc bang email nguoi khac.
        /// Tra so loi moi da vao thang du an.
        /// </summary>
        public async Task<int> GanTaiKhoanAsync(Guid userId, Caller heThong, CancellationToken ct)
        {
            NguoiDungTraCuu? nguoi = await _identity.LayAsync(userId, ct);
            if (nguoi == null)
            {
                return 0;
            }

            DateTimeOffset luc = _clock.GetUtcNow();
            List<ProjectInvitation> ds = await _db.ProjectInvitations
                .Where(i => i.Email == nguoi.Email && i.UserId == null && i.Status == InvitationStatus.Pending && i.ExpiresAt > luc)
                .ToListAsync(ct);

            foreach (ProjectInvitation i in ds)
            {
                i.GanTaiKhoan(userId);
            }

            await _db.SaveChangesAsync(ct);

            int vao = 0;
            foreach (ProjectInvitation i in ds)
            {
                LabelingProject duAn = await _db.Projects.FirstAsync(p => p.Id == i.ProjectId, ct);
                if (duAn.CanDongYCaNhan() || duAn.DaKetThuc())
                {
                    continue;
                }

                try
                {
                    await _members.KiemTraChuaLaThanhVienAsync(duAn.Id, userId, ct);
                    await KiemDieuKienTheoVaiTroAsync(duAn, userId, i.Role, ct);
                    i.ChapNhan(luc);
                    ProjectMember m = ProjectMember.TaoThanhVien(duAn.Id, userId, i.Role, luc);
                    await _members.LuuThanhVienMoiAsync(m, MemberJoinVia.Invitation, duAn, heThong, ct);
                    vao++;
                }
                catch (RuleViolationException ex)
                {
                    // Chua du dieu kien (cap do, xac minh...): loi moi van cho, nguoi do tu chap nhan khi du.
                    // KHONG ChangeTracker.Clear(): goi tu consumer, tracker con dau vet processed_events cua guard.
                    _logger.LogInformation("Loi moi {InvitationId} chua tu vao duoc: {Ma}", i.Id, ex.Code);
                }
            }

            return vao;
        }

        /// <summary>Worker: loi moi qua han → Expired. Tra so loi moi vua het han.</summary>
        public async Task<int> HetHanAsync(CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            List<ProjectInvitation> ds = await _db.ProjectInvitations
                .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt <= bayGio)
                .Take(500)
                .ToListAsync(ct);
            foreach (ProjectInvitation i in ds)
            {
                i.HetHan(bayGio);
            }

            await _db.SaveChangesAsync(ct);
            return ds.Count;
        }

        // =====================================================================

        /// <summary>Labeler: du dieu kien labeler + bao ve. Reviewer: khong bi khoa / chan + bao ve (cung xem du lieu).</summary>
        private async Task KiemDieuKienTheoVaiTroAsync(LabelingProject duAn, Guid userId, MemberRole role, CancellationToken ct)
        {
            if (role == MemberRole.Labeler)
            {
                await _members.KiemDieuKienLabelerAsync(duAn, userId, ct);
                return;
            }

            NguoiDungTraCuu? nguoi = await _identity.LayAsync(userId, ct);
            if (nguoi == null || nguoi.Locked)
            {
                throw new RuleViolationException("tai_khoan_bi_khoa", "Tai khoan dang bi khoa.");
            }

            if (await _members.BiDoanhNghiepChanAsync(duAn.OwnerId, userId, ct))
            {
                throw new RuleViolationException("bi_chan_boi_doanh_nghiep", "Doanh nghiep so huu du an da chan tai khoan nay.");
            }

            string? lyDo = duAn.LyDoKhongDuDieuKienBaoVe(nguoi.IdentityVerified, nguoi.Adult, await _baoVe.DaDongYAsync(duAn, userId, ct));
            if (lyDo != null)
            {
                throw new RuleViolationException(lyDo, LabelingProject.GiaiThichBaoVe(lyDo));
            }
        }

        private async Task<ProjectInvitation> LayCuaToiAsync(Guid invitationId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            ProjectInvitation? moi = await _db.ProjectInvitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.UserId == uid, ct);
            if (moi == null)
            {
                throw new NotFoundException("Khong tim thay loi moi.");
            }

            return moi;
        }

        private async Task<IReadOnlyList<InvitationResponse>> TaoResponseAsync(List<ProjectInvitation> ds, bool choChuDuAn, CancellationToken ct)
        {
            List<Guid> ids = ds.Select(i => i.ProjectId).Distinct().ToList();
            Dictionary<Guid, LabelingProject> duAn = await _db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            List<InvitationResponse> kq = new List<InvitationResponse>();
            foreach (ProjectInvitation i in ds)
            {
                kq.Add(MemberService.TaoLoiMoiResponse(i, duAn[i.ProjectId], choChuDuAn));
            }

            return kq;
        }
    }
}
