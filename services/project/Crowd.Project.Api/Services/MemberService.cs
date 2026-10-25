using System;
using System.Collections.Generic;
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

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Quan ly project_members. Moi thay doi phat member.* de task/annotation/
    /// media cap nhat ban sao cua chung (docs 3.8).
    ///
    /// Moi DUONG VAO du an deu kiem dieu kien labeler (khoa, vai tro, cap do, uy tin) bang ho so
    /// lay tu identity-svc ngay luc vao — cung luat task-svc ap khi cap task, nen nguoi khong du
    /// dieu kien bi chan ngay o cua thay vi vao roi moi lan nhan task deu bi tu choi.
    /// </summary>
    public sealed class MemberService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectEventPublisher _events;
        private readonly IdentityClient _identity;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly BaoVeService _baoVe;

        public MemberService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            IdentityClient identity,
            ISettings settings,
            TimeProvider clock,
            BaoVeService baoVe)
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

            _db = db;
            _access = access;
            _events = events;
            _identity = identity;
            _settings = settings;
            _clock = clock;
        }

        public async Task<IReadOnlyList<MemberResponse>> DanhSachAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayDeQuanLyAsync(projectId, caller, ct);

            List<ProjectMember> ds = await _db.ProjectMembers
                .AsNoTracking()
                .Where(m => m.ProjectId == projectId)
                .OrderByDescending(m => m.JoinedAt)
                .ToListAsync(ct);

            List<MemberResponse> ketQua = new List<MemberResponse>();
            foreach (ProjectMember m in ds)
            {
                ketQua.Add(TaoResponse(m));
            }

            return ketQua;
        }

        /// <summary>
        /// POST /members — chu du an moi MOT nguoi theo userId HOAC email (FP-05 private pool, hoac
        /// them reviewer). Khong can bai test — chu du an tu chiu trach nhiem nguoi minh moi; dieu
        /// kien cap do / uy tin van ap cho labeler. Loi tra dung ma nhu ban nhieu nguoi.
        /// Theo email: LUON 202 + loi moi (VD-S-16) — xem ThemNhieuAsync.
        /// </summary>
        public async Task<ThemMotKetQua> ThemAsync(Guid projectId, AddMemberRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            bool coId = body.UserId.HasValue;
            bool coEmail = !string.IsNullOrWhiteSpace(body.Email);
            bool coTen = !string.IsNullOrWhiteSpace(body.Username);
            if (body.Role == null || (coId ? 1 : 0) + (coEmail ? 1 : 0) + (coTen ? 1 : 0) != 1)
            {
                throw new InvalidValueException("thieu_thong_tin", "Cần role và MỘT trong ba: username, userId hoặc email.");
            }

            AddMembersBatchRequest mot = new AddMembersBatchRequest
            {
                Role = body.Role,
                UserIds = coId ? new List<Guid> { body.UserId!.Value } : null,
                Emails = coEmail ? new List<string> { body.Email! } : null,
                Usernames = coTen ? new List<string> { body.Username! } : null,
            };

            AddMembersBatchResponse kq = await ThemNhieuAsync(projectId, mot, caller, ct);
            AddMemberResult r = kq.Results[0];
            if (r.Status == KetQuaMoi.DaThem)
            {
                ProjectMember m = await _db.ProjectMembers.AsNoTracking()
                    .FirstAsync(x => x.ProjectId == projectId && x.UserId == r.UserId!.Value, ct);
                return new ThemMotKetQua { Member = TaoResponse(m) };
            }

            if (r.InvitationId.HasValue)
            {
                ProjectInvitation moi = await _db.ProjectInvitations.AsNoTracking().FirstAsync(x => x.Id == r.InvitationId.Value, ct);
                LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
                return new ThemMotKetQua { Invitation = TaoLoiMoiResponse(moi, duAn, true) };
            }

            if (r.Status == KetQuaMoi.KhongTimThay)
            {
                throw new NotFoundException(r.Message ?? "Không tìm thấy người dùng.");
            }

            if (r.Status == KetQuaMoi.EmailKhongHopLe)
            {
                throw new InvalidValueException(r.Status, r.Message ?? "Email không hợp lệ.");
            }

            throw new RuleViolationException(r.Status, r.Message ?? r.Status);
        }

        /// <summary>
        /// POST /members/batch — moi NHIEU nguoi mot lan theo userId va / hoac email, trong MOT transaction.
        ///
        /// Theo userId: tung nguoi mot ket qua chi tiet (them duoc, khong tim thay, da la thanh vien, bi chan,
        /// chua du cap do...) — chu du an da biet nguoi do (bang xep hang, ho so cong khai, du an truoc).
        ///
        /// Theo email (VD-S-16): moi email hop le deu ra CUNG mot ket qua "invited" kem loi moi, khong kem
        /// userId / ten — o moi khong con la cach do email nao co tai khoan. Nguoi co tai khoan nhan loi moi
        /// trong ung dung va tu chap nhan (khong them thang nua); email chua co tai khoan nhan thu moi dang ky.
        /// Nguoi da la thanh vien / dang bi chan: loi moi "cam" (khong gui gi) de ket qua van giong het.
        /// So email moi trong 24 gio co han muc rieng (project.email_invite_daily_max).
        /// </summary>
        public async Task<AddMembersBatchResponse> ThemNhieuAsync(
            Guid projectId, AddMembersBatchRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (body.Role == null)
            {
                throw new InvalidValueException("thieu_thong_tin", "Cần role (labeler / reviewer).");
            }

            if (body.Role.Value == MemberRole.Owner)
            {
                throw new InvalidValueException("khong_the_them_owner", "Mỗi dự án chỉ có một chủ sở hữu.");
            }

            List<DauVaoMoi> dauVao = DocDauVao(body);
            dauVao.AddRange(await DocUsernameAsync(body.Usernames, ct));
            QuyDinhThanhVien quyDinh = QuyDinhTuSetting.ThanhVien(_settings);
            if (dauVao.Count == 0)
            {
                throw new InvalidValueException("thieu_thong_tin", "Cần ít nhất một username, userId hoặc email.");
            }

            if (dauVao.Count > quyDinh.SoNguoiMoiToiDa)
            {
                throw new InvalidValueException(
                    "qua_nhieu_nguoi", "Mỗi lần mời tối đa " + quyDinh.SoNguoiMoiToiDa + " người.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            duAn.KiemTraChoThemThanhVien();

            List<Guid> ids = dauVao.Where(d => d.UserId.HasValue).Select(d => d.UserId!.Value).ToList();
            List<string> emails = dauVao.Where(d => d.Email != null && d.Loi == null).Select(d => d.Email!).Distinct(StringComparer.Ordinal).ToList();
            if (emails.Count > 0)
            {
                await KiemHanMucMoiEmailAsync(caller.UserId ?? duAn.OwnerId, emails.Count, ct);
            }

            IReadOnlyList<NguoiDungTraCuu> tim = await _identity.TraCuuAsync(ids, emails, ct);

            // Hai request moi cung mot nguoi cung luc: khoa chinh (project_id, user_id) chon mot,
            // request kia lam lai tu dau va thay nguoi do "da la thanh vien".
            for (int lan = 0; ; lan++)
            {
                try
                {
                    return await ThemTheoKetQuaTraCuuAsync(duAn, body.Role.Value, dauVao, tim, caller, ct);
                }
                catch (DbUpdateException ex) when (lan == 0
                    && (PostgresErrors.IsUniqueViolation(ex, "PK_project_members") || PostgresErrors.IsUniqueViolation(ex, "ux_project_invitations_cho")))
                {
                    _db.ChangeTracker.Clear();
                }
            }
        }

        /// <summary>
        /// POST /join — labeler tu tham gia du an cong khai khong can duyet, khong yeu cau test.
        /// Du an can test thi di duong EntranceTestService — dau moi thanh thanh vien.
        /// </summary>
        public async Task<MemberResponse> ThamGiaAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeXemAsync(projectId, caller, ct);
            duAn.KiemTraChoTuThamGia();

            if (duAn.RequireEntranceTest)
            {
                throw new RuleViolationException("can_lam_test", "Dự án yêu cầu làm bài test đầu vào trước khi tham gia.");
            }

            Guid userId = caller.LayUserId();
            await KiemTraChuaLaThanhVienAsync(projectId, userId, ct);
            await KiemDieuKienLabelerAsync(duAn, userId, ct);

            ProjectMember moi = ProjectMember.TaoThanhVien(projectId, userId, MemberRole.Labeler, _clock.GetUtcNow());
            await LuuThanhVienMoiAsync(moi, MemberJoinVia.Join, duAn, caller, ct);

            return TaoResponse(moi);
        }

        /// <summary>
        /// POST /projects/{id}/leave — labeler / reviewer tu roi du an bat ky luc nao (muc 2.9: khong bi
        /// phat — uy tin chi tinh tu nhan da nop). Luot giu task dang do bi task-svc thu hoi qua member.removed.
        /// </summary>
        public async Task RoiAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            ProjectMember m = await LayThanhVienAsync(projectId, uid, ct);
            m.KiemTraTuRoiDuoc();
            _db.ProjectMembers.Remove(m);
            _events.Phat(caller, new MemberRemoved { ProjectId = projectId, UserId = uid });
            await _db.SaveChangesAsync(ct);
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

        // =====================================================================
        // Dung chung cho moi duong vao (moi, ma moi, join, bai test, don xin)
        // =====================================================================

        /// <summary>
        /// Them thanh vien moi + phat member.added, trong MOT SaveChanges (cung moi thay doi khac
        /// dang theo doi: luot dung ma moi, don xin tham gia, ket qua bai test).
        /// </summary>
        public async Task LuuThanhVienMoiAsync(ProjectMember moi, string via, LabelingProject duAn, Caller caller, CancellationToken ct)
        {
            if (moi == null)
            {
                throw new ArgumentNullException(nameof(moi));
            }

            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            await KiemTraChuaLaThanhVienAsync(moi.ProjectId, moi.UserId, ct);

            _db.ProjectMembers.Add(moi);
            PhatDaThem(moi, via, duAn, caller);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "PK_project_members"))
            {
                // Hai request tham gia cung luc: ca hai lot qua kiem tra o tren,
                // khoa chinh (project_id, user_id) chon ra mot.
                _db.ChangeTracker.Clear();
                throw new RuleViolationException("da_la_thanh_vien", "Người này đã là thành viên dự án.");
            }
        }

        /// <summary>Da la thanh vien (hoac bi chan) thi 409 — truoc khi ton luot ma moi / goi identity.</summary>
        public async Task KiemTraChuaLaThanhVienAsync(Guid projectId, Guid userId, CancellationToken ct)
        {
            ProjectMember? cu = await _db.ProjectMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);

            if (cu != null)
            {
                if (cu.State == MemberState.Blocked)
                {
                    throw new RuleViolationException("bi_chan", "Bạn đã bị chặn khỏi dự án này.");
                }

                throw new RuleViolationException("da_la_thanh_vien", "Người này đã là thành viên dự án.");
            }
        }

        /// <summary>Hoi identity-svc roi kiem dieu kien labeler cua du an; khong du → 409 kem ma.</summary>
        public async Task<NguoiDungTraCuu> KiemDieuKienLabelerAsync(LabelingProject duAn, Guid userId, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            NguoiDungTraCuu? nguoi = await _identity.LayAsync(userId, ct);
            if (nguoi == null)
            {
                throw new NotFoundException("Không tìm thấy tài khoản.");
            }

            duAn.KiemTraLabelerDuDieuKien(nguoi.HoSo());

            // FB-23: bi doanh nghiep dua vao danh sach chan → khong vao duoc moi du an cua doanh nghiep do.
            if (await BiDoanhNghiepChanAsync(duAn.OwnerId, userId, ct))
            {
                throw new RuleViolationException("bi_chan_boi_doanh_nghiep", "Doanh nghiệp sở hữu dự án đã chặn tài khoản này.");
            }

            // Muc 2.9 / FP-01 / FP-05: xac minh danh tinh, du tuoi (lay tu identity luc vao — moi hon claim
            // trong token), da dong y canh bao / ky NDA phien ban hien hanh.
            string? lyDo = duAn.LyDoKhongDuDieuKienBaoVe(nguoi.IdentityVerified, nguoi.Adult, await _baoVe.DaDongYAsync(duAn, userId, ct));
            if (lyDo != null)
            {
                throw new RuleViolationException(lyDo, LabelingProject.GiaiThichBaoVe(lyDo));
            }

            return nguoi;
        }

        public Task<bool> BiDoanhNghiepChanAsync(Guid ownerId, Guid labelerId, CancellationToken ct)
        {
            return _db.OwnerLabelerEntries.AnyAsync(
                e => e.OwnerId == ownerId && e.LabelerId == labelerId && e.Kind == OwnerListKind.Blocked, ct);
        }

        /// <summary>
        /// Tao loi moi cho + phat project.invitation_created (notification gui thu / thong bao). Chi theo doi,
        /// SaveChanges o noi goi. Da co loi moi dang cho cung (du an, email) → tra loi moi cu.
        /// guiThu = false: loi moi "cam" (VD-S-16) — co dong de chu du an thay ket qua giong het, nhung khong gan
        /// tai khoan, khong gui gi (nguoi da la thanh vien / dang bi chan).
        /// </summary>
        public async Task<ProjectInvitation> TaoLoiMoiAsync(
            LabelingProject duAn, string email, Guid? userId, MemberRole role, bool quaEmail, bool guiThu, Caller caller, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            DateTimeOffset luc = _clock.GetUtcNow();
            ProjectInvitation? cu = await _db.ProjectInvitations.FirstOrDefaultAsync(
                i => i.ProjectId == duAn.Id && i.Email == email && i.ByEmail == quaEmail && i.Status == InvitationStatus.Pending, ct);
            if (cu != null && cu.ExpiresAt > luc)
            {
                if (guiThu && userId.HasValue && !cu.UserId.HasValue)
                {
                    // Loi moi cu la loi moi "cam" (luc do nguoi nay dang bi chan / da la thanh vien), nay moi lai
                    // hop le → gan tai khoan va gui that.
                    cu.GanTaiKhoan(userId.Value);
                    PhatLoiMoi(cu, duAn, caller);
                }

                return cu;
            }

            if (cu != null)
            {
                cu.HetHan(luc);
            }

            ProjectInvitation moi = ProjectInvitation.Tao(
                duAn.Id, email, guiThu ? userId : null, role, caller.UserId ?? duAn.OwnerId, quaEmail, luc,
                _settings.ThoiGian(SettingKeys.ProjectInvitationTtl));
            _db.ProjectInvitations.Add(moi);
            if (guiThu)
            {
                PhatLoiMoi(moi, duAn, caller);
            }

            return moi;
        }

        /// <summary>
        /// Loi moi nhin tu phia nao. choChuDuAn = true (chu du an): moi bang email thi an userId cho toi khi chap
        /// nhan (khong lo email nao co tai khoan); moi bang userId thi an email (khong lo email cua nguoi do).
        /// </summary>
        public static InvitationResponse TaoLoiMoiResponse(ProjectInvitation i, LabelingProject duAn, bool choChuDuAn)
        {
            if (i == null)
            {
                throw new ArgumentNullException(nameof(i));
            }

            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            return new InvitationResponse
            {
                InvitationId = i.Id,
                ProjectId = i.ProjectId,
                ProjectName = duAn.Name,
                Email = choChuDuAn && !i.ByEmail ? null : i.Email,
                UserId = choChuDuAn && i.ByEmail && i.Status != InvitationStatus.Accepted ? null : i.UserId,
                ByEmail = i.ByEmail,
                Role = i.Role,
                Status = i.Status,
                RequiresConsent = duAn.CanDongYCaNhan(),
                CreatedAt = i.CreatedAt,
                ExpiresAt = i.ExpiresAt,
            };
        }

        /// <summary>
        /// Nguoi vua thanh thanh vien bang duong khac (chu du an moi, ma moi) ma con don dang cho
        /// → dong don de hang doi duyet khong con don thua. Chi theo doi, SaveChanges o noi goi.
        /// </summary>
        public async Task DongDonDangChoAsync(Guid projectId, Guid userId, Guid? chuDuAn, DateTimeOffset luc, CancellationToken ct)
        {
            JoinRequest? don = await _db.JoinRequests
                .FirstOrDefaultAsync(d => d.ProjectId == projectId && d.UserId == userId && d.Status == JoinRequestStatus.Pending, ct);

            if (don == null)
            {
                return;
            }

            if (chuDuAn.HasValue)
            {
                don.Duyet(chuDuAn.Value, luc);
            }
            else
            {
                don.Huy(luc);
            }
        }

        public void PhatDaThem(ProjectMember moi, string via, LabelingProject duAn, Caller caller)
        {
            if (moi == null)
            {
                throw new ArgumentNullException(nameof(moi));
            }

            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            _events.Phat(caller, new MemberAdded
            {
                ProjectId = moi.ProjectId,
                UserId = moi.UserId,
                Role = ContractMapper.ToContract(moi.Role),
                Via = via,
                ProjectName = duAn.Name,
            });
        }

        public static MemberResponse TaoResponse(ProjectMember m)
        {
            if (m == null)
            {
                throw new ArgumentNullException(nameof(m));
            }

            return new MemberResponse
            {
                UserId = m.UserId,
                Role = m.Role,
                State = m.State,
                JoinedAt = m.JoinedAt,
            };
        }

        // =====================================================================
        // Moi nhieu nguoi
        // =====================================================================

        private async Task<AddMembersBatchResponse> ThemTheoKetQuaTraCuuAsync(
            LabelingProject duAn,
            MemberRole role,
            List<DauVaoMoi> dauVao,
            IReadOnlyList<NguoiDungTraCuu> tim,
            Caller caller,
            CancellationToken ct)
        {
            Dictionary<Guid, NguoiDungTraCuu> theoId = new Dictionary<Guid, NguoiDungTraCuu>();
            Dictionary<string, NguoiDungTraCuu> theoEmail = new Dictionary<string, NguoiDungTraCuu>(StringComparer.Ordinal);
            foreach (NguoiDungTraCuu n in tim)
            {
                theoId[n.UserId] = n;
                theoEmail[n.Email] = n;
            }

            List<Guid> timThay = tim.Select(n => n.UserId).ToList();
            Dictionary<Guid, MemberState> daCo = await _db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == duAn.Id && timThay.Contains(m.UserId))
                .ToDictionaryAsync(m => m.UserId, m => m.State, ct);
            Dictionary<Guid, JoinRequest> donDangCho = await _db.JoinRequests
                .Where(x => x.ProjectId == duAn.Id && x.Status == JoinRequestStatus.Pending && timThay.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, ct);
            HashSet<Guid> biChanDn = new HashSet<Guid>(await _db.OwnerLabelerEntries.AsNoTracking()
                .Where(e => e.OwnerId == duAn.OwnerId && e.Kind == OwnerListKind.Blocked && timThay.Contains(e.LabelerId))
                .Select(e => e.LabelerId)
                .ToListAsync(ct));
            HashSet<Guid> daDongY = new HashSet<Guid>();
            if (duAn.CanDongYCaNhan())
            {
                foreach (ProjectConsent c in await _db.ProjectConsents.AsNoTracking()
                    .Where(c => c.ProjectId == duAn.Id && timThay.Contains(c.UserId)).ToListAsync(ct))
                {
                    if (c.ConHieuLuc(duAn))
                    {
                        daDongY.Add(c.UserId);
                    }
                }
            }

            int soMoi = 0;

            DateTimeOffset luc = _clock.GetUtcNow();
            HashSet<Guid> daXet = new HashSet<Guid>();
            HashSet<string> daXetEmail = new HashSet<string>(StringComparer.Ordinal);
            List<AddMemberResult> ketQua = new List<AddMemberResult>();
            int soThem = 0;

            foreach (DauVaoMoi d in dauVao)
            {
                NguoiDungTraCuu? n = null;
                if (d.Loi == null)
                {
                    if (d.UserId.HasValue)
                    {
                        theoId.TryGetValue(d.UserId.Value, out n);
                    }
                    else
                    {
                        theoEmail.TryGetValue(d.Email!, out n);
                    }
                }

                string trangThai;
                string? loiGiai = null;
                Guid? loiMoiId = null;
                if (d.Loi != null)
                {
                    trangThai = d.Loi;
                    loiGiai = d.Loi == KetQuaMoi.EmailKhongHopLe ? "Email không hợp lệ." : "Không tìm thấy tài khoản (username / userId).";
                }
                else if (d.Email != null)
                {
                    // VD-S-16: moi nhanh email ra CUNG mot ket qua — trung lap chi xet theo chuoi email (xet theo
                    // tai khoan se lo email nao trung voi userId da gui). Co tai khoan: loi moi trong ung dung,
                    // nguoi do tu chap nhan (cua kiem dieu kien o InvitationService). Chua co: thu moi dang ky,
                    // dang ky + xac thuc email xong thi loi moi gan voi tai khoan moi (GanTaiKhoanAsync).
                    if (!daXetEmail.Add(d.Email))
                    {
                        trangThai = KetQuaMoi.TrungLap;
                        loiGiai = "Email nay da co o dong khac trong cung lan moi.";
                    }
                    else
                    {
                        bool cam = n != null
                            && (daXet.Contains(n.UserId) || daCo.ContainsKey(n.UserId) || biChanDn.Contains(n.UserId));
                        ProjectInvitation moi = await TaoLoiMoiAsync(
                            duAn, d.Email, n == null ? null : n.UserId, role, true, !cam, caller, ct);
                        trangThai = KetQuaMoi.DaGuiLoiMoi;
                        loiGiai = "Da gui loi moi toi email nay.";
                        loiMoiId = moi.Id;
                        soMoi++;
                    }

                    n = null;
                }
                else if (n == null)
                {
                    trangThai = KetQuaMoi.KhongTimThay;
                    loiGiai = "Không có tài khoản này.";
                }
                else if (!daXet.Add(n.UserId))
                {
                    trangThai = KetQuaMoi.TrungLap;
                    loiGiai = "Nguoi nay da co o dong khac trong cung lan moi.";
                }
                else if (daCo.ContainsKey(n.UserId))
                {
                    trangThai = daCo[n.UserId] == MemberState.Blocked ? "bi_chan" : "da_la_thanh_vien";
                    loiGiai = trangThai == "bi_chan"
                        ? "Người này đang bị chặn khỏi dự án — bỏ chặn thay vì mời lại."
                        : "Người này đã là thành viên dự án.";
                }
                else
                {
                    HoSoNguoiDung hoSo = n.HoSo();
                    string? lyDo = role == MemberRole.Labeler
                        ? duAn.LyDoLabelerKhongDuDieuKien(hoSo)
                        : (hoSo.BiKhoa ? "tai_khoan_bi_khoa" : null);

                    string? lyDoBaoVe = duAn.LyDoKhongDuDieuKienBaoVe(n.IdentityVerified, n.Adult, true);
                    if (lyDo != null)
                    {
                        trangThai = lyDo;
                        loiGiai = duAn.GiaiThichKhongDuDieuKien(lyDo, hoSo);
                    }
                    else if (biChanDn.Contains(n.UserId))
                    {
                        trangThai = "bi_chan_boi_doanh_nghiep";
                        loiGiai = "Nguoi nay nam trong danh sach chan cua doanh nghiep — bo khoi danh sach truoc.";
                    }
                    else if (lyDoBaoVe != null)
                    {
                        trangThai = lyDoBaoVe;
                        loiGiai = LabelingProject.GiaiThichBaoVe(lyDoBaoVe);
                    }
                    else if (duAn.CanDongYCaNhan() && !daDongY.Contains(n.UserId))
                    {
                        // Chu du an khong dong y canh bao / ky NDA thay nguoi khac duoc: loi moi cho nguoi do tu dong y.
                        ProjectInvitation moi = await TaoLoiMoiAsync(duAn, n.Email, n.UserId, role, false, true, caller, ct);
                        trangThai = KetQuaMoi.ChoDongY;
                        loiGiai = "Dự án cần người được mời tự đồng ý điều khoản — đã gửi lời mời.";
                        loiMoiId = moi.Id;
                        soMoi++;
                    }
                    else
                    {
                        ProjectMember moi = ProjectMember.TaoThanhVien(duAn.Id, n.UserId, role, luc);
                        _db.ProjectMembers.Add(moi);
                        PhatDaThem(moi, MemberJoinVia.OwnerInvite, duAn, caller);
                        JoinRequest? don;
                        if (donDangCho.TryGetValue(n.UserId, out don))
                        {
                            don.Duyet(caller.UserId ?? duAn.OwnerId, luc);
                        }

                        trangThai = KetQuaMoi.DaThem;
                        soThem++;
                    }
                }

                ketQua.Add(new AddMemberResult
                {
                    Input = d.NguyenVan,
                    UserId = n == null ? null : n.UserId,
                    Username = n == null ? null : n.Username,
                    DisplayName = n == null ? null : n.DisplayName,
                    Status = trangThai,
                    Message = loiGiai,
                    InvitationId = loiMoiId,
                });
            }

            if (soThem > 0 || soMoi > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            return new AddMembersBatchResponse { Added = soThem, Invited = soMoi, Results = ketQua };
        }

        /// <summary>
        /// VD-S-16: so email mot nguoi moi trong 24 gio (dem loi moi by_email da tao) cong lan nay khong vuot
        /// project.email_invite_daily_max. Vuot → 429, Retry-After = luc loi moi cu nhat trong cua so het tinh.
        /// </summary>
        private async Task KiemHanMucMoiEmailAsync(Guid nguoiMoi, int soLanNay, CancellationToken ct)
        {
            int toiDa = _settings.SoNguyen(SettingKeys.ProjectEmailInviteDailyMax);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            DateTimeOffset tu = bayGio - TimeSpan.FromDays(1);
            IQueryable<ProjectInvitation> trongCua = _db.ProjectInvitations.AsNoTracking()
                .Where(i => i.ByEmail && i.InvitedBy == nguoiMoi && i.CreatedAt > tu);
            int daMoi = await trongCua.CountAsync(ct);
            if (daMoi + soLanNay <= toiDa)
            {
                return;
            }

            // Cho bao lau thi du cho lan nay: bo di (daMoi + soLanNay - toiDa) loi moi cu nhat khoi cua so.
            int canNha = daMoi + soLanNay - toiDa;
            TimeSpan cho = TimeSpan.FromDays(1);
            if (canNha <= daMoi)
            {
                DateTimeOffset moc = await trongCua.OrderBy(i => i.CreatedAt).Skip(canNha - 1).Select(i => i.CreatedAt).FirstAsync(ct);
                cho = moc + TimeSpan.FromDays(1) - bayGio;
            }

            throw new VuotHanMucException(
                "vuot_han_muc_moi_email",
                "Mời tối đa " + toiDa + " email trong 24 giờ (đã mời " + daMoi + ", lần này " + soLanNay + ").",
                cho);
        }

        private void PhatLoiMoi(ProjectInvitation moi, LabelingProject duAn, Caller caller)
        {
            _events.Phat(caller, new ProjectInvitationCreated
            {
                InvitationId = moi.Id,
                ProjectId = duAn.Id,
                ProjectName = duAn.Name,
                Email = moi.Email,
                UserId = moi.UserId,
                RequiresConsent = duAn.CanDongYCaNhan(),
                ExpiresAt = moi.ExpiresAt,
            });
        }

        /// <summary>userId truoc, email sau, giu thu tu nguoi dung gui; email chuan hoa trim + chu thuong.</summary>
        private static List<DauVaoMoi> DocDauVao(AddMembersBatchRequest body)
        {
            List<DauVaoMoi> ds = new List<DauVaoMoi>();
            if (body.UserIds != null)
            {
                foreach (Guid id in body.UserIds)
                {
                    ds.Add(new DauVaoMoi(id.ToString(), id, null, id == Guid.Empty ? KetQuaMoi.KhongTimThay : null));
                }
            }

            if (body.Emails != null)
            {
                foreach (string? e in body.Emails)
                {
                    string nguyenVan = e ?? string.Empty;
                    string chuan = nguyenVan.Trim().ToLowerInvariant();
                    bool hopLe = chuan.Length >= 3 && chuan.Length <= 320 && chuan.IndexOf('@') > 0 && chuan.IndexOf('@') < chuan.Length - 1;
                    ds.Add(new DauVaoMoi(nguyenVan, null, chuan, hopLe ? null : KetQuaMoi.EmailKhongHopLe));
                }
            }

            return ds;
        }

        /// <summary>
        /// Username → userId qua identity-svc (mot lan cho ca lo). Khong ton tai → dong "khong_tim_thay" (chu du an
        /// biet username, khong lo gi them). Dau vao nguyen van giu dang "@ten" de ket qua de doc.
        /// </summary>
        private async Task<List<DauVaoMoi>> DocUsernameAsync(IReadOnlyList<string>? usernames, CancellationToken ct)
        {
            List<DauVaoMoi> ds = new List<DauVaoMoi>();
            if (usernames == null || usernames.Count == 0)
            {
                return ds;
            }

            List<string> chuan = usernames
                .Select(x => (x ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant())
                .ToList();
            List<string> hopLe = chuan.Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, Guid> theoTen = new Dictionary<string, Guid>(StringComparer.Ordinal);
            if (hopLe.Count > 0)
            {
                foreach (NguoiDungTraCuu n in await _identity.TraCuuAsync(new List<Guid>(), new List<string>(), hopLe, ct))
                {
                    theoTen[n.Username] = n.UserId;
                }
            }

            foreach (string ten in chuan)
            {
                Guid id;
                if (ten.Length > 0 && theoTen.TryGetValue(ten, out id))
                {
                    ds.Add(new DauVaoMoi("@" + ten, id, null, null));
                }
                else
                {
                    ds.Add(new DauVaoMoi("@" + ten, null, null, KetQuaMoi.KhongTimThay));
                }
            }

            return ds;
        }

        private async Task<ProjectMember> LayThanhVienAsync(Guid projectId, Guid userId, CancellationToken ct)
        {
            ProjectMember? m = await _db.ProjectMembers
                .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);

            if (m == null)
            {
                throw new NotFoundException("Không tìm thấy thành viên.");
            }

            return m;
        }

        private sealed class DauVaoMoi
        {
            public DauVaoMoi(string nguyenVan, Guid? userId, string? email, string? loi)
            {
                NguyenVan = nguyenVan;
                UserId = userId;
                Email = email;
                Loi = loi;
            }

            public string NguyenVan { get; }

            public Guid? UserId { get; }

            public string? Email { get; }

            /// <summary>Loi doc duoc ngay tu dau vao (email sai dang...), khong can tra cuu.</summary>
            public string? Loi { get; }
        }
    }

    /// <summary>Gia tri "status" cua tung dong ket qua moi nhieu nguoi (ngoai cac ma dieu kien).</summary>
    public static class KetQuaMoi
    {
        public const string DaThem = "added";
        public const string KhongTimThay = "khong_tim_thay";
        public const string TrungLap = "trung_lap";
        public const string EmailKhongHopLe = "email_khong_hop_le";

        /// <summary>Moi theo email — da tao loi moi cho (giong het du email co tai khoan hay khong, VD-S-16).</summary>
        public const string DaGuiLoiMoi = "invited";

        /// <summary>Du an can tu dong y dieu khoan — da gui loi moi cho nguoi do chap nhan.</summary>
        public const string ChoDongY = "pending_consent";
    }

    /// <summary>Ket qua moi MOT nguoi: hoac da them (Member), hoac da tao loi moi cho (Invitation).</summary>
    public sealed class ThemMotKetQua
    {
        public MemberResponse? Member { get; init; }

        public InvitationResponse? Invitation { get; init; }
    }
}
