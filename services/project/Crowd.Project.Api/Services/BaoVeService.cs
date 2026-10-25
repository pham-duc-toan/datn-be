using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Bao ve nguoi gan nhan va du lieu cua mot du an:
    ///   - muc 2.9 noi dung nhay cam: canh bao + dong y, chi nguoi DA XAC MINH du tuoi, nghi giua phien
    ///     (task-svc ep), frontend lam mo mac dinh (co sensitiveContent trong task);
    ///   - FP-01 NDA dien tu: ky (ho ten tu go + bam van ban + IP + trinh duyet) truoc khi vao;
    ///   - FP-05: chi labeler da xac minh danh tinh.
    /// Kiem tra dieu kien nam o MemberService.KiemDieuKienLabelerAsync — moi duong vao deu qua do.
    /// </summary>
    public sealed class BaoVeService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly TimeProvider _clock;

        public BaoVeService(ProjectDbContext db, ProjectAccessService access, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _access = access;
            _clock = clock;
        }

        /// <summary>PUT /projects/{id}/protection — chu du an, chi khi Nhap.</summary>
        public async Task<ProjectTermsResponse> CauHinhAsync(Guid projectId, ProtectionRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.DatBaoVe(body.SensitiveContent, body.ContentWarning, body.NdaRequired, body.NdaText, body.RequireVerifiedIdentity, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoTerms(duAn, null);
        }

        /// <summary>GET /projects/{id}/terms — ai xem duoc du an deu doc duoc dieu khoan + trang thai dong y cua minh.</summary>
        public async Task<ProjectTermsResponse> DieuKhoanAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await LayDeXemHoacDuocMoiAsync(projectId, caller, ct);
            ProjectConsent? c = await _db.ProjectConsents.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == caller.LayUserId(), ct);
            return TaoTerms(duAn, c);
        }

        /// <summary>
        /// POST /projects/{id}/consent — labeler dong y canh bao / ky NDA. Duoc goi ca khi chua la thanh
        /// vien (dong y TRUOC khi vao). Du an rieng tu: chi nguoi co loi moi cho moi thay de ky.
        /// </summary>
        public async Task<ProjectTermsResponse> DongYAsync(Guid projectId, ConsentRequest body, string? ip, string? trinhDuyet, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await LayDeXemHoacDuocMoiAsync(projectId, caller, ct);
            if (duAn.DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Dự án đã kết thúc.");
            }

            ProjectConsent c = await GhiDongYAsync(duAn, caller.LayUserId(), body, ip, trinhDuyet, ct);
            return TaoTerms(duAn, c);
        }

        /// <summary>
        /// Ghi dong y canh bao / ky NDA cua mot nguoi (da kiem quyen o noi goi: xem duoc du an, duoc moi, hoac cam
        /// ma moi con dung duoc) va LUU ngay — kiem dieu kien sau do doc lai tu database.
        /// </summary>
        public async Task<ProjectConsent> GhiDongYAsync(
            LabelingProject duAn, Guid userId, ConsentRequest body, string? ip, string? trinhDuyet, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            ProjectConsent? c = await _db.ProjectConsents.FirstOrDefaultAsync(x => x.ProjectId == duAn.Id && x.UserId == userId, ct);
            if (c == null)
            {
                c = ProjectConsent.Tao(duAn.Id, userId, bayGio);
                _db.ProjectConsents.Add(c);
            }

            c.DongY(duAn, body.AcceptContentWarning, body.AcceptNda, body.FullName, ip, trinhDuyet, bayGio);
            await _db.SaveChangesAsync(ct);
            return c;
        }

        /// <summary>Dieu khoan + trang thai dong y cua mot nguoi (cho trang nhap ma moi — chua la thanh vien).</summary>
        public async Task<ProjectTermsResponse> DieuKhoanChoNguoiAsync(LabelingProject duAn, Guid userId, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            ProjectConsent? c = await _db.ProjectConsents.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProjectId == duAn.Id && x.UserId == userId, ct);
            return TaoTerms(duAn, c);
        }

        /// <summary>GET /projects/{id}/consents — chu du an xem ai da dong y / ky NDA (bang chung khi co tranh chap).</summary>
        public async Task<IReadOnlyList<ConsentRecordResponse>> DanhSachAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);
            List<ProjectConsent> ds = await _db.ProjectConsents.AsNoTracking()
                .Where(x => x.ProjectId == projectId)
                .OrderByDescending(x => x.UpdatedAt)
                .ToListAsync(ct);

            return ds.Select(c => new ConsentRecordResponse
            {
                UserId = c.UserId,
                ContentWarningAcceptedAt = c.ContentWarningAcceptedAt,
                NdaVersion = c.NdaVersion,
                NdaTextHash = c.NdaTextHash,
                NdaSignedName = c.NdaSignedName,
                NdaSignedAt = c.NdaSignedAt,
                Ip = c.Ip,
                UserAgent = c.UserAgent,
            }).ToList();
        }

        /// <summary>Nguoi nay da dong y dieu khoan HIEN HANH cua du an chua (khong can dong y thi true).</summary>
        public async Task<bool> DaDongYAsync(LabelingProject duAn, Guid userId, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (!duAn.CanDongYCaNhan())
            {
                return true;
            }

            ProjectConsent? c = await _db.ProjectConsents.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProjectId == duAn.Id && x.UserId == userId, ct);
            return c != null && c.ConHieuLuc(duAn);
        }

        /// <summary>
        /// Xem duoc du an, HOAC co loi moi dang cho vao du an (du an rieng tu khong hien voi nguoi ngoai,
        /// nhung nguoi duoc moi phai doc duoc dieu khoan de dong y).
        /// </summary>
        private async Task<LabelingProject> LayDeXemHoacDuocMoiAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject? duAn = await _access.XemDuoc(caller).FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (duAn != null)
            {
                return duAn;
            }

            Guid uid = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();
            bool duocMoi = await _db.ProjectInvitations.AnyAsync(
                i => i.ProjectId == projectId && i.UserId == uid && i.Status == InvitationStatus.Pending && i.ExpiresAt > bayGio, ct);
            if (!duocMoi)
            {
                throw new NotFoundException("Không tìm thấy dự án.");
            }

            return await _db.Projects.FirstAsync(p => p.Id == projectId, ct);
        }

        public static ProjectTermsResponse TaoTerms(LabelingProject duAn, ProjectConsent? c)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            return new ProjectTermsResponse
            {
                ProjectId = duAn.Id,
                SensitiveContent = duAn.SensitiveContent,
                ContentWarning = duAn.ContentWarning,
                NdaRequired = duAn.NdaRequired,
                NdaText = duAn.NdaText,
                NdaVersion = duAn.NdaVersion,
                RequireVerifiedIdentity = duAn.RequireVerifiedIdentity,
                ConsentRequired = duAn.CanDongYCaNhan(),
                MyConsentValid = c != null && duAn.CanDongYCaNhan() && c.ConHieuLuc(duAn),
                MyNdaSignedAt = c == null ? null : c.NdaSignedAt,
                MyContentWarningAcceptedAt = c == null ? null : c.ContentWarningAcceptedAt,
            };
        }
    }
}
