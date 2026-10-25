using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
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
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Don xin tham gia du an che do Approval: labeler gui / rut, chu du an xem hang doi va
    /// duyet / tu choi. Du an co bai test dau vao thi don do EntranceTestService tao khi lam dat.
    /// </summary>
    public sealed class JoinRequestService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly MemberService _members;
        private readonly ProjectEventPublisher _events;
        private readonly IdentityClient _identity;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<JoinRequestService> _logger;

        public JoinRequestService(
            ProjectDbContext db,
            ProjectAccessService access,
            MemberService members,
            ProjectEventPublisher events,
            IdentityClient identity,
            ISettings settings,
            TimeProvider clock,
            ILogger<JoinRequestService> logger)
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

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
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
            _events = events;
            _identity = identity;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>POST /projects/{id}/join-requests — labeler gui don (du an approval, khong co bai test).</summary>
        public async Task<JoinRequestResponse> GuiAsync(Guid projectId, CreateJoinRequestRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeXemAsync(projectId, caller, ct);
            duAn.KiemTraChoXinThamGia();

            if (duAn.Visibility != ProjectVisibility.Approval)
            {
                throw new RuleViolationException(
                    "khong_can_duyet",
                    "Dự án công khai không cần duyệt — tham gia trực tiếp.");
            }

            if (duAn.RequireEntranceTest)
            {
                throw new RuleViolationException(
                    "can_lam_test",
                    "Dự án yêu cầu bài test đầu vào — làm đạt thì đơn xin tham gia tự tạo, kèm điểm.");
            }

            Guid userId = caller.LayUserId();
            DateTimeOffset luc = _clock.GetUtcNow();
            await KiemTraDuocGuiDonAsync(duAn, userId, luc, ct);

            JoinRequest don = JoinRequest.Tao(projectId, userId, body.Message, null, null, luc);
            await LuuDonMoiAsync(don, duAn, caller, ct);
            return TaoResponse(don, duAn.Name, null);
        }

        /// <summary>
        /// Kiem chung cho gui don va bat dau bai test (du an approval): chua la thanh vien, chua co
        /// don dang cho, het thoi gian cho sau lan bi tu choi gan nhat, du dieu kien labeler.
        /// </summary>
        public async Task KiemTraDuocGuiDonAsync(LabelingProject duAn, Guid userId, DateTimeOffset luc, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            await _members.KiemTraChuaLaThanhVienAsync(duAn.Id, userId, ct);

            bool dangCho = await _db.JoinRequests.AnyAsync(
                d => d.ProjectId == duAn.Id && d.UserId == userId && d.Status == JoinRequestStatus.Pending, ct);
            if (dangCho)
            {
                throw new RuleViolationException("da_co_don_dang_cho", "Bạn đã có đơn xin tham gia đang chờ chủ dự án duyệt.");
            }

            JoinRequest? biTuChoi = await _db.JoinRequests.AsNoTracking()
                .Where(d => d.ProjectId == duAn.Id && d.UserId == userId && d.Status == JoinRequestStatus.Rejected)
                .OrderByDescending(d => d.DecidedAt)
                .FirstOrDefaultAsync(ct);
            if (biTuChoi != null)
            {
                DateTimeOffset? tu = biTuChoi.DuocXinLaiTu(QuyDinhTuSetting.ThanhVien(_settings).ChoXinLaiSauTuChoi);
                if (tu.HasValue && luc < tu.Value)
                {
                    throw new RuleViolationException(
                        "chua_duoc_xin_lai",
                        "Đơn trước vừa bị từ chối — gửi lại được từ " + tu.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC.");
                }
            }

            await _members.KiemDieuKienLabelerAsync(duAn, userId, ct);
        }

        /// <summary>Them don + phat join_request.submitted trong MOT SaveChanges (cung moi thay doi dang theo doi).</summary>
        public async Task LuuDonMoiAsync(JoinRequest don, LabelingProject duAn, Caller caller, CancellationToken ct)
        {
            if (don == null)
            {
                throw new ArgumentNullException(nameof(don));
            }

            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            _db.JoinRequests.Add(don);

            // FB-23: labeler trong danh sach UU TIEN cua doanh nghiep → don duoc tu duyet, vao luon.
            bool uuTien = await _db.OwnerLabelerEntries.AnyAsync(
                e => e.OwnerId == duAn.OwnerId && e.LabelerId == don.UserId && e.Kind == OwnerListKind.Preferred, ct);
            if (uuTien)
            {
                DateTimeOffset luc = _clock.GetUtcNow();
                don.Duyet(duAn.OwnerId, luc);
                PhatDaQuyetDinh(don, duAn, caller);
                ProjectMember moi = ProjectMember.TaoThanhVien(duAn.Id, don.UserId, MemberRole.Labeler, luc);
                _db.ProjectMembers.Add(moi);
                _members.PhatDaThem(moi, MemberJoinVia.JoinRequest, duAn, caller);
                _logger.LogInformation("Don {RequestId} tu duyet: labeler {UserId} trong danh sach uu tien", don.Id, don.UserId);
            }
            else
            {
                _events.Phat(caller, new JoinRequestSubmitted
                {
                    RequestId = don.Id,
                    ProjectId = duAn.Id,
                    ProjectName = duAn.Name,
                    OwnerId = duAn.OwnerId,
                    UserId = don.UserId,
                    ScorePercent = don.ScorePercent,
                });
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_join_requests_dang_cho"))
            {
                _db.ChangeTracker.Clear();
                throw new RuleViolationException("da_co_don_dang_cho", "Bạn đã có đơn xin tham gia đang chờ chủ dự án duyệt.");
            }
        }

        /// <summary>DELETE /projects/{id}/join-requests/mine — labeler rut don dang cho.</summary>
        public async Task<JoinRequestResponse> RutAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();
            JoinRequest? don = await _db.JoinRequests
                .FirstOrDefaultAsync(d => d.ProjectId == projectId && d.UserId == userId && d.Status == JoinRequestStatus.Pending, ct);
            if (don == null)
            {
                throw new NotFoundException("Không có đơn xin tham gia đang chờ.");
            }

            don.Huy(_clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoResponse(don, null, null);
        }

        /// <summary>GET /projects/join-requests/mine — moi don cua labeler (moi nhat truoc).</summary>
        public async Task<IReadOnlyList<JoinRequestResponse>> CuaToiAsync(Caller caller, CancellationToken ct)
        {
            Guid userId = caller.LayUserId();
            List<JoinRequest> ds = await _db.JoinRequests.AsNoTracking()
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.CreatedAt)
                .Take(200)
                .ToListAsync(ct);

            List<Guid> duAnIds = ds.Select(d => d.ProjectId).Distinct().ToList();
            Dictionary<Guid, string> ten = await _db.Projects.AsNoTracking()
                .Where(p => duAnIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            TimeSpan cho = QuyDinhTuSetting.ThanhVien(_settings).ChoXinLaiSauTuChoi;
            List<JoinRequestResponse> kq = new List<JoinRequestResponse>();
            foreach (JoinRequest d in ds)
            {
                string? tenDuAn;
                ten.TryGetValue(d.ProjectId, out tenDuAn);
                kq.Add(TaoResponse(d, tenDuAn, null, d.DuocXinLaiTu(cho)));
            }

            return kq;
        }

        /// <summary>
        /// GET /projects/{id}/join-requests?status= — hang doi cua chu du an (mac dinh: dang cho, cu
        /// nhat truoc). Kem ten / cap do / uy tin hien tai tu identity-svc; identity khong tra loi
        /// thi van tra danh sach, cac truong do de null.
        /// </summary>
        public async Task<IReadOnlyList<JoinRequestResponse>> DanhSachAsync(
            Guid projectId, JoinRequestStatus? status, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            JoinRequestStatus loc = status ?? JoinRequestStatus.Pending;

            List<JoinRequest> ds = await _db.JoinRequests.AsNoTracking()
                .Where(d => d.ProjectId == projectId && d.Status == loc)
                .OrderByDescending(d => d.CreatedAt)
                .Take(500)
                .ToListAsync(ct);

            Dictionary<Guid, NguoiDungTraCuu> nguoi = new Dictionary<Guid, NguoiDungTraCuu>();
            if (ds.Count > 0)
            {
                try
                {
                    IReadOnlyList<NguoiDungTraCuu> tim = await _identity.TraCuuAsync(
                        ds.Select(d => d.UserId).Distinct().ToList(), Array.Empty<string>(), ct);
                    foreach (NguoiDungTraCuu n in tim)
                    {
                        nguoi[n.UserId] = n;
                    }
                }
                catch (DichVuKhongSanSangException ex)
                {
                    _logger.LogWarning(ex, "Khong lay duoc ho so nguoi xin tham gia du an {ProjectId}", projectId);
                }
            }

            List<JoinRequestResponse> kq = new List<JoinRequestResponse>();
            foreach (JoinRequest d in ds)
            {
                NguoiDungTraCuu? n;
                nguoi.TryGetValue(d.UserId, out n);
                kq.Add(TaoResponse(d, duAn.Name, n));
            }

            return kq;
        }

        /// <summary>
        /// POST /projects/{id}/join-requests/{requestId}/approve — dieu kien labeler kiem LAI luc
        /// duyet (uy tin co the da doi tu luc gui don): khong con du → 409 kem ma, chu du an tu choi.
        /// </summary>
        public async Task<JoinRequestResponse> DuyetAsync(Guid projectId, Guid requestId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            if (duAn.DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Dự án đã kết thúc.");
            }

            JoinRequest don = await LayDonAsync(projectId, requestId, ct);
            if (don.Status != JoinRequestStatus.Pending)
            {
                throw new RuleViolationException("don_da_xu_ly", "Đơn xin tham gia đã được xử lý (" + don.Status + ").");
            }

            NguoiDungTraCuu nguoi = await _members.KiemDieuKienLabelerAsync(duAn, don.UserId, ct);

            DateTimeOffset luc = _clock.GetUtcNow();
            don.Duyet(caller.LayUserId(), luc);
            PhatDaQuyetDinh(don, duAn, caller);

            ProjectMember moi = ProjectMember.TaoThanhVien(projectId, don.UserId, MemberRole.Labeler, luc);
            await _members.LuuThanhVienMoiAsync(moi, MemberJoinVia.JoinRequest, duAn, caller, ct);
            return TaoResponse(don, duAn.Name, nguoi);
        }

        /// <summary>POST /projects/{id}/join-requests/{requestId}/reject — kem ly do (khong bat buoc).</summary>
        public async Task<JoinRequestResponse> TuChoiAsync(
            Guid projectId, Guid requestId, RejectJoinRequestRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            JoinRequest don = await LayDonAsync(projectId, requestId, ct);

            DateTimeOffset luc = _clock.GetUtcNow();
            don.TuChoi(caller.LayUserId(), body.Reason, luc);
            PhatDaQuyetDinh(don, duAn, caller);

            await _db.SaveChangesAsync(ct);
            return TaoResponse(don, duAn.Name, null, don.DuocXinLaiTu(QuyDinhTuSetting.ThanhVien(_settings).ChoXinLaiSauTuChoi));
        }

        /// <summary>
        /// Worker: don cho qua project.join_request_ttl → Expired + join_request.decided (approved=false,
        /// reason het_han) de labeler biet va gui lai. Tra so don vua het han.
        /// </summary>
        public async Task<int> HetHanAsync(Caller heThong, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            DateTimeOffset moc = bayGio - _settings.ThoiGian(SettingKeys.ProjectJoinRequestTtl);
            List<JoinRequest> ds = await _db.JoinRequests
                .Where(d => d.Status == JoinRequestStatus.Pending && d.CreatedAt < moc)
                .OrderBy(d => d.CreatedAt)
                .Take(500)
                .ToListAsync(ct);
            if (ds.Count == 0)
            {
                return 0;
            }

            List<Guid> ids = ds.Select(d => d.ProjectId).Distinct().ToList();
            Dictionary<Guid, LabelingProject> duAn = await _db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            foreach (JoinRequest d in ds)
            {
                d.HetHan(bayGio);
                _events.Phat(heThong, new JoinRequestDecided
                {
                    RequestId = d.Id,
                    ProjectId = d.ProjectId,
                    ProjectName = duAn[d.ProjectId].Name,
                    UserId = d.UserId,
                    Approved = false,
                    Reason = "het_han",
                });
            }

            await _db.SaveChangesAsync(ct);
            return ds.Count;
        }

        private void PhatDaQuyetDinh(JoinRequest don, LabelingProject duAn, Caller caller)
        {
            _events.Phat(caller, new JoinRequestDecided
            {
                RequestId = don.Id,
                ProjectId = duAn.Id,
                ProjectName = duAn.Name,
                UserId = don.UserId,
                Approved = don.Status == JoinRequestStatus.Approved,
                Reason = don.RejectReason,
            });
        }

        private async Task<JoinRequest> LayDonAsync(Guid projectId, Guid requestId, CancellationToken ct)
        {
            JoinRequest? don = await _db.JoinRequests.FirstOrDefaultAsync(d => d.Id == requestId && d.ProjectId == projectId, ct);
            if (don == null)
            {
                throw new NotFoundException("Không tìm thấy đơn xin tham gia.");
            }

            return don;
        }

        public static JoinRequestResponse TaoResponse(JoinRequest d, string? tenDuAn, NguoiDungTraCuu? nguoi)
        {
            return TaoResponse(d, tenDuAn, nguoi, null);
        }

        private static JoinRequestResponse TaoResponse(JoinRequest d, string? tenDuAn, NguoiDungTraCuu? nguoi, DateTimeOffset? xinLaiTu)
        {
            return new JoinRequestResponse
            {
                Id = d.Id,
                ProjectId = d.ProjectId,
                ProjectName = tenDuAn,
                UserId = d.UserId,
                DisplayName = nguoi == null ? null : nguoi.DisplayName,
                Level = nguoi == null ? null : nguoi.Level,
                Reputation = nguoi == null ? null : nguoi.Reputation,
                Message = d.Message,
                Status = d.Status,
                EntranceAttemptId = d.EntranceAttemptId,
                ScorePercent = d.ScorePercent,
                CreatedAt = d.CreatedAt,
                DecidedAt = d.DecidedAt,
                RejectReason = d.RejectReason,
                CanRequestAgainAt = xinLaiTu,
            };
        }
    }
}
