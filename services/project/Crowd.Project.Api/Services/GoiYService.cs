using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Goi y du an phu hop nang luc (FL-02). Ung vien: du an DANG CHAY, khong rieng tu, mo kenh chuyen
    /// nghiep, chua la thanh vien, khong bi doanh nghiep chan, DU DIEU KIEN (cap do / uy tin / xac minh /
    /// du tuoi — cung luat voi luc vao, de khong goi y du an bam vao bi tu choi). Xep hang:
    ///   diem = 3 (trung loai du lieu) + 2 x so the linh vuc trung + 2 x so the ngon ngu trung
    /// (the = ma trong danh muc admin — ho so va du an cung chon tu mot danh muc nen khop chinh xac theo ma).
    /// roi don gia cao truoc, moi truoc. Ho so ky nang (FL-01) va dieu kien lay tu identity luc goi.
    /// Ho so ky nang rong thi van goi y theo don gia (diem 0).
    /// </summary>
    public sealed class GoiYService
    {
        /// <summary>So ung vien toi da xet moi lan (du an dang chay moi nhat).</summary>
        private const int UngVienToiDa = 500;

        private readonly ProjectDbContext _db;
        private readonly IdentityClient _identity;

        public GoiYService(ProjectDbContext db, IdentityClient identity)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            _db = db;
            _identity = identity;
        }

        /// <summary>GET /projects/recommended?limit= (labeler).</summary>
        public async Task<IReadOnlyList<RecommendedProjectResponse>> GoiYAsync(int limit, Caller caller, CancellationToken ct)
        {
            int n = limit < 1 || limit > 50 ? 10 : limit;
            Guid uid = caller.LayUserId();
            NguoiDungTraCuu? toi = await _identity.LayAsync(uid, ct);
            if (toi == null)
            {
                throw new NotFoundException("Không tìm thấy tài khoản.");
            }

            List<LabelingProject> ungVien = await _db.Projects.AsNoTracking()
                .Where(p => p.Status == ProjectStatus.Running
                    && p.Visibility != ProjectVisibility.Private
                    && p.AllowProfessional
                    && !_db.ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == uid)
                    && !_db.OwnerLabelerEntries.Any(e => e.OwnerId == p.OwnerId && e.LabelerId == uid && e.Kind == OwnerListKind.Blocked))
                .OrderByDescending(p => p.PublishedAt)
                .Take(UngVienToiDa)
                .ToListAsync(ct);

            HoSoNguoiDung hoSo = toi.HoSo();
            HashSet<string> linhVuc = new HashSet<string>(toi.Domains, StringComparer.OrdinalIgnoreCase);
            HashSet<string> ngonNgu = new HashSet<string>(toi.Languages, StringComparer.OrdinalIgnoreCase);
            HashSet<string> loai = new HashSet<string>(toi.Modalities, StringComparer.OrdinalIgnoreCase);

            List<RecommendedProjectResponse> kq = new List<RecommendedProjectResponse>();
            foreach (LabelingProject p in ungVien)
            {
                if (p.LyDoLabelerKhongDuDieuKien(hoSo) != null)
                {
                    continue;
                }

                // Dong y dieu khoan van lam duoc SAU khi xem goi y → chi loai neu thieu xac minh / tuoi.
                string? baoVe = p.LyDoKhongDuDieuKienBaoVe(toi.IdentityVerified, toi.Adult, true);
                if (baoVe != null)
                {
                    continue;
                }

                List<string> lyDo = new List<string>();
                int diem = 0;
                if (loai.Contains(p.Modality))
                {
                    diem += 3;
                    lyDo.Add("modality");
                }

                foreach (string d in p.Domains)
                {
                    if (linhVuc.Contains(d))
                    {
                        diem += 2;
                        lyDo.Add("domain:" + d);
                    }
                }

                foreach (string l in p.Languages)
                {
                    if (ngonNgu.Contains(l))
                    {
                        diem += 2;
                        lyDo.Add("language:" + l);
                    }
                }

                kq.Add(new RecommendedProjectResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Modality = p.Modality,
                    Visibility = p.Visibility,
                    RewardVnd = p.RewardVnd,
                    QuestionCount = p.QuestionCount,
                    ToolKinds = ProjectService.LoaiCongCu(p),
                    Deadline = p.Deadline,
                    RequireEntranceTest = p.RequireEntranceTest,
                    SensitiveContent = p.SensitiveContent,
                    ConsentRequired = p.CanDongYCaNhan(),
                    Domains = p.Domains,
                    Languages = p.Languages,
                    Score = diem,
                    Reasons = lyDo,
                });
            }

            return kq
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.RewardVnd)
                .Take(n)
                .ToList();
        }
    }
}
