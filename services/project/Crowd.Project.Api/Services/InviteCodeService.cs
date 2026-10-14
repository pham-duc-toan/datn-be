using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
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
    /// Ma moi tham gia du an: chu du an tao / xem / thu hoi; labeler nhap ma de vao du an
    /// (ke ca du an rieng tu) ma chu du an khong can biet userId cua ho.
    /// </summary>
    public sealed class InviteCodeService
    {
        /// <summary>Chu du an khong dat han: 7 ngay (khong vuot project.invite_code_max_ttl).</summary>
        private static readonly TimeSpan HanMacDinh = TimeSpan.FromDays(7);

        private const int SoLanThuToiDa = 3;

        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly MemberService _members;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public InviteCodeService(
            ProjectDbContext db,
            ProjectAccessService access,
            MemberService members,
            ISettings settings,
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

            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _access = access;
            _members = members;
            _settings = settings;
            _clock = clock;
        }

        public async Task<InviteCodeResponse> TaoAsync(Guid projectId, CreateInviteCodeRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraChoVaoBangMaMoi();

            QuyDinhThanhVien quyDinh = QuyDinhTuSetting.ThanhVien(_settings);
            TimeSpan han;
            if (body.ExpiresInHours.HasValue)
            {
                if (body.ExpiresInHours.Value < 1 || body.ExpiresInHours.Value > quyDinh.HanMaMoiToiDa.TotalHours)
                {
                    throw new InvalidValueException(
                        "han_ma_moi_khong_hop_le",
                        "Han dung cua ma moi phai tu 1 gio den " + quyDinh.HanMaMoiToiDa.TotalHours + " gio.");
                }

                han = TimeSpan.FromHours(body.ExpiresInHours.Value);
            }
            else
            {
                han = HanMacDinh <= quyDinh.HanMaMoiToiDa ? HanMacDinh : quyDinh.HanMaMoiToiDa;
            }

            DateTimeOffset luc = _clock.GetUtcNow();
            InviteCode ma = InviteCode.Tao(projectId, caller.LayUserId(), body.MaxUses, han, body.Note, luc, quyDinh);
            _db.InviteCodes.Add(ma);
            await _db.SaveChangesAsync(ct);

            return TaoResponse(ma, luc);
        }

        public async Task<IReadOnlyList<InviteCodeResponse>> DanhSachAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            List<InviteCode> ds = await _db.InviteCodes.AsNoTracking()
                .Where(c => c.ProjectId == projectId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync(ct);

            DateTimeOffset luc = _clock.GetUtcNow();
            return ds.Select(c => TaoResponse(c, luc)).ToList();
        }

        public async Task<InviteCodeResponse> ThuHoiAsync(Guid projectId, Guid codeId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            InviteCode? ma = await _db.InviteCodes.FirstOrDefaultAsync(c => c.Id == codeId && c.ProjectId == projectId, ct);
            if (ma == null)
            {
                throw new NotFoundException("Khong tim thay ma moi.");
            }

            DateTimeOffset luc = _clock.GetUtcNow();
            ma.ThuHoi(luc);
            await _db.SaveChangesAsync(ct);
            return TaoResponse(ma, luc);
        }

        /// <summary>
        /// POST /projects/join-by-code — labeler nhap ma. Kiem theo thu tu: ma ton tai → du an chua
        /// ket thuc → chua la thanh vien (khong ton luot) → dieu kien cap do / uy tin → ma con dung
        /// duoc → ghi luot + them thanh vien trong MOT transaction.
        /// </summary>
        public async Task<JoinByCodeResponse> VaoBangMaAsync(JoinByCodeRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string code = InviteCode.ChuanHoa(body.Code);
            if (code.Length != InviteCode.DoDaiMa)
            {
                throw new NotFoundException("Ma moi khong ton tai.");
            }

            Guid userId = caller.LayUserId();
            bool daKiemDieuKien = false;

            for (int lan = 1; ; lan++)
            {
                InviteCode? ma = await _db.InviteCodes.FirstOrDefaultAsync(c => c.Code == code, ct);
                if (ma == null)
                {
                    throw new NotFoundException("Ma moi khong ton tai.");
                }

                // Labeler chua la thanh vien du an rieng tu thi khong "xem" duoc du an — doc thang.
                LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == ma.ProjectId, ct);
                duAn.KiemTraChoVaoBangMaMoi();
                await _members.KiemTraChuaLaThanhVienAsync(duAn.Id, userId, ct);

                if (!daKiemDieuKien)
                {
                    await _members.KiemDieuKienLabelerAsync(duAn, userId, ct);
                    daKiemDieuKien = true;
                }

                DateTimeOffset luc = _clock.GetUtcNow();
                ma.Dung(luc);
                await _members.DongDonDangChoAsync(duAn.Id, userId, null, luc, ct);

                ProjectMember moi = ProjectMember.TaoThanhVien(duAn.Id, userId, MemberRole.Labeler, luc);
                try
                {
                    await _members.LuuThanhVienMoiAsync(moi, MemberJoinVia.InviteCode, duAn, caller, ct);
                }
                catch (DbUpdateConcurrencyException) when (lan < SoLanThuToiDa)
                {
                    // Nguoi khac vua dung ma cung luc (xmin doi): doc lai — co the ma vua het luot.
                    _db.ChangeTracker.Clear();
                    continue;
                }

                return new JoinByCodeResponse
                {
                    ProjectId = duAn.Id,
                    ProjectName = duAn.Name,
                    Member = MemberService.TaoResponse(moi),
                };
            }
        }

        private static InviteCodeResponse TaoResponse(InviteCode c, DateTimeOffset luc)
        {
            return new InviteCodeResponse
            {
                Id = c.Id,
                ProjectId = c.ProjectId,
                Code = c.Code,
                Note = c.Note,
                MaxUses = c.MaxUses,
                UsedCount = c.UsedCount,
                ExpiresAt = c.ExpiresAt,
                CreatedAt = c.CreatedAt,
                RevokedAt = c.RevokedAt,
                Status = c.TrangThai(luc),
            };
        }
    }
}
