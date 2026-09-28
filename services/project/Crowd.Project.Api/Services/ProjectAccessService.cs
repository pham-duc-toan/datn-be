using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// PHAN QUYEN CHIEU NGANG (VD-S-14, docs 3.8): "nguoi nay co duoc dung vao
    /// DU AN NAY khong".
    ///
    /// LUAT QUAN TRONG NHAT: MOT predicate duy nhat — XemDuoc() — dung cho CA
    /// lay mot du an LAN loc danh sach. Viet hai ban luat rieng thi som muon
    /// chung lech nhau: danh sach hien du an ma bam vao bao 404, hoac te hon,
    /// bam vao xem duoc du an khong nam trong danh sach.
    ///
    /// Xem duoc khi:
    ///   - la admin, HOAC
    ///   - la thanh vien dang hoat dong (owner / labeler / reviewer), HOAC
    ///   - du an dang chay VA cong khai.
    /// </summary>
    public sealed class ProjectAccessService
    {
        private readonly ProjectDbContext _db;

        public ProjectAccessService(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        /// <summary>Tap du an nguoi goi duoc thay. EF dich thanh MOT cau SQL co EXISTS.</summary>
        public IQueryable<LabelingProject> XemDuoc(Caller caller)
        {
            if (caller == null)
            {
                throw new ArgumentNullException(nameof(caller));
            }

            if (caller.IsAdmin)
            {
                return _db.Projects;
            }

            Guid userId = caller.LayUserId();

            return _db.Projects.Where(p =>
                _db.ProjectMembers.Any(m =>
                    m.ProjectId == p.Id && m.UserId == userId && m.State == MemberState.Active)
                || (p.Status == ProjectStatus.Running && p.Visibility == ProjectVisibility.Public));
        }

        /// <summary>Lay mot du an de XEM. Khong xem duoc thi 404 — khong tiet lo no ton tai.</summary>
        public async Task<LabelingProject> LayDeXemAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject? duAn = await XemDuoc(caller).FirstOrDefaultAsync(p => p.Id == projectId, ct);

            if (duAn == null)
            {
                throw new NotFoundException("Khong tim thay du an.");
            }

            return duAn;
        }

        /// <summary>
        /// Lay mot du an de SUA: phai la chu du an (hoac admin). Xem duoc ma
        /// khong phai chu thi 403; khong xem duoc thi 404 (qua LayDeXemAsync).
        /// </summary>
        public async Task<LabelingProject> LayDeQuanLyAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await LayDeXemAsync(projectId, caller, ct);

            if (!LaChu(duAn, caller))
            {
                throw new ForbiddenException("Chi chu du an moi duoc thuc hien thao tac nay.");
            }

            return duAn;
        }

        public static bool LaChu(LabelingProject duAn, Caller caller)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (caller == null)
            {
                throw new ArgumentNullException(nameof(caller));
            }

            return caller.IsAdmin || (caller.UserId != null && duAn.OwnerId == caller.UserId.Value);
        }
    }
}
