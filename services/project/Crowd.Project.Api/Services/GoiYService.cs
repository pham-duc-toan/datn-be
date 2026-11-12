using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Du an cho LABELER tim viec: CHO DU AN (GET /projects/market) va GOI Y (GET /projects/recommended, FL-02).
    ///
    /// Ca hai chi tra du an nguoi goi LAM DUOC NGAY (bat dau / lam tiep / tham gia / xin vao / lam bai test):
    ///   - con nhan bai: dang chay, dot thu bai chua dong, CHUA QUA HAN CHOT (khong cho worker dong du an);
    ///   - mo kenh chuyen nghiep, khong rieng tu (tru khi da la thanh vien);
    ///   - khong bi chan (khoi du an hoac boi danh sach chan cua doanh nghiep);
    ///   - chua nop bai du an nay (da nop thi xem o "Bai lam cua toi");
    ///   - con cho lam bai (tran nguoi lam) — hoac minh dang giu mot bai;
    ///   - DU DIEU KIEN ho so: cap do / uy tin (CUNG ham voi luc mo bai — EligibilityPolicy) va xac minh / du
    ///     tuoi (CUNG ham voi luc tham gia). Dong y dieu khoan van lam duoc sau khi mo trang du an nen khong loc.
    /// Nho vay du an hien trong cho bam vao khong bi bao "chua du dieu kien".
    ///
    /// Goi y xep hang them: diem = 3 (trung loai du lieu) + 2 x so the linh vuc trung + 2 x so the ngon ngu trung
    /// (the = ma trong danh muc admin), roi don gia cao truoc. Chi goi y du an CHUA tham gia.
    /// </summary>
    public sealed class GoiYService
    {
        /// <summary>So ung vien toi da xet moi lan (du an dang chay moi nhat). Dieu kien ho so loc trong bo nho.</summary>
        private const int UngVienToiDa = 500;

        private readonly ProjectDbContext _db;
        private readonly IdentityClient _identity;
        private readonly TimeProvider _clock;

        public GoiYService(ProjectDbContext db, IdentityClient identity, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _identity = identity;
            _clock = clock;
        }

        /// <summary>GET /projects/market?modality=&page=&pageSize= (labeler) — moi du an minh lam duoc ngay, moi nhat truoc.</summary>
        public async Task<PagedResponse<ProjectListItem>> ChoAsync(string? modality, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            ProjectService.ChuanHoaTrang(ref page, ref pageSize);
            Guid uid = caller.LayUserId();
            NguoiDungTraCuu toi = await LayToiAsync(uid, ct);

            IQueryable<LabelingProject> q = UngVien(uid, _clock.GetUtcNow());
            if (!string.IsNullOrEmpty(modality))
            {
                string m = modality;
                q = q.Where(p => p.Modality == m);
            }

            List<LabelingProject> duoc = await LocHoSoAsync(q, toi, ct);

            return new PagedResponse<ProjectListItem>
            {
                Items = duoc.Skip((page - 1) * pageSize).Take(pageSize).Select(p => ProjectService.TaoListItem(p, caller)).ToList(),
                Page = page,
                PageSize = pageSize,
                Total = duoc.Count,
            };
        }

        /// <summary>GET /projects/recommended?limit= (labeler).</summary>
        public async Task<IReadOnlyList<RecommendedProjectResponse>> GoiYAsync(int limit, Caller caller, CancellationToken ct)
        {
            int n = limit < 1 || limit > 50 ? 10 : limit;
            Guid uid = caller.LayUserId();
            NguoiDungTraCuu toi = await LayToiAsync(uid, ct);

            IQueryable<LabelingProject> q = UngVien(uid, _clock.GetUtcNow())
                .Where(p => !_db.ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == uid));
            List<LabelingProject> ungVien = await LocHoSoAsync(q, toi, ct);

            HashSet<string> linhVuc = new HashSet<string>(toi.Domains, StringComparer.OrdinalIgnoreCase);
            HashSet<string> ngonNgu = new HashSet<string>(toi.Languages, StringComparer.OrdinalIgnoreCase);
            HashSet<string> loai = new HashSet<string>(toi.Modalities, StringComparer.OrdinalIgnoreCase);

            List<RecommendedProjectResponse> kq = new List<RecommendedProjectResponse>();
            foreach (LabelingProject p in ungVien)
            {
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

        private async Task<NguoiDungTraCuu> LayToiAsync(Guid uid, CancellationToken ct)
        {
            NguoiDungTraCuu? toi = await _identity.LayAsync(uid, ct);
            if (toi == null)
            {
                throw new NotFoundException("Không tìm thấy tài khoản.");
            }

            return toi;
        }

        /// <summary>
        /// Dieu kien KHONG phu thuoc ho so — chay trong SQL. Cho lam bai dem nhu luc bat dau (LamBaiService.BatDauAsync):
        /// bai da nop + bai dang lam con han cua NGUOI KHAC, so voi tran nguoi lam chup luc duyet.
        /// </summary>
        private IQueryable<LabelingProject> UngVien(Guid uid, DateTimeOffset bayGio)
        {
            return _db.Projects.AsNoTracking()
                .Where(p => p.Status == ProjectStatus.Running
                    && p.AllowProfessional
                    && (p.Deadline == null || p.Deadline > bayGio)
                    && (p.Visibility != ProjectVisibility.Private
                        || _db.ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == uid && m.State == MemberState.Active))
                    && !_db.ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == uid && m.State == MemberState.Blocked)
                    && !_db.OwnerLabelerEntries.Any(e => e.OwnerId == p.OwnerId && e.LabelerId == uid && e.Kind == OwnerListKind.Blocked)
                    && !_db.Attempts.Any(a => a.ProjectId == p.Id && a.LabelerId == uid && a.State == AttemptState.Submitted)
                    && _db.CollectionRounds.Any(d => d.ProjectId == p.Id && d.FinishReason == null
                        && (_db.Attempts.Any(a => a.ProjectId == p.Id && a.LabelerId == uid
                                && a.State == AttemptState.InProgress && a.ExpiresAt > bayGio)
                            || _db.Attempts.Count(a => a.ProjectId == p.Id && a.LabelerId != uid
                                && (a.State == AttemptState.Submitted || (a.State == AttemptState.InProgress && a.ExpiresAt > bayGio)))
                               < d.MaxParticipants)))
                .OrderByDescending(p => p.PublishedAt);
        }

        /// <summary>Dieu kien theo ho so (cap do / uy tin / khoa / xac minh / du tuoi) — dung chung ham domain.</summary>
        private static async Task<List<LabelingProject>> LocHoSoAsync(IQueryable<LabelingProject> q, NguoiDungTraCuu toi, CancellationToken ct)
        {
            List<LabelingProject> ungVien = await q.Take(UngVienToiDa).ToListAsync(ct);
            HoSoNguoiDung hoSo = toi.HoSo();
            return ungVien
                .Where(p => p.LyDoLabelerKhongDuDieuKien(hoSo) == null
                            && p.LyDoKhongDuDieuKienBaoVe(toi.IdentityVerified, toi.Adult, true) == null)
                .ToList();
        }
    }
}
