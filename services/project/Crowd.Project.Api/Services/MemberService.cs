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

        public MemberService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            IdentityClient identity,
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
        /// POST /members — chu du an moi MOT nguoi theo userId HOAC email (FP-05 private pool, hoac
        /// them reviewer). Khong can bai test — chu du an tu chiu trach nhiem nguoi minh moi; dieu
        /// kien cap do / uy tin van ap cho labeler. Loi tra dung ma nhu ban nhieu nguoi.
        /// </summary>
        public async Task<MemberResponse> ThemAsync(Guid projectId, AddMemberRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            bool coId = body.UserId.HasValue;
            bool coEmail = !string.IsNullOrWhiteSpace(body.Email);
            if (body.Role == null || coId == coEmail)
            {
                throw new InvalidValueException("thieu_thong_tin", "Can role va MOT trong hai: userId hoac email.");
            }

            AddMembersBatchRequest mot = new AddMembersBatchRequest
            {
                Role = body.Role,
                UserIds = coId ? new List<Guid> { body.UserId!.Value } : null,
                Emails = coEmail ? new List<string> { body.Email! } : null,
            };

            AddMembersBatchResponse kq = await ThemNhieuAsync(projectId, mot, caller, ct);
            AddMemberResult r = kq.Results[0];
            if (r.Status == KetQuaMoi.DaThem)
            {
                ProjectMember m = await _db.ProjectMembers.AsNoTracking()
                    .FirstAsync(x => x.ProjectId == projectId && x.UserId == r.UserId!.Value, ct);
                return TaoResponse(m);
            }

            if (r.Status == KetQuaMoi.KhongTimThay)
            {
                throw new NotFoundException(r.Message ?? "Khong tim thay nguoi dung.");
            }

            if (r.Status == KetQuaMoi.EmailKhongHopLe)
            {
                throw new InvalidValueException(r.Status, r.Message ?? "Email khong hop le.");
            }

            throw new RuleViolationException(r.Status, r.Message ?? r.Status);
        }

        /// <summary>
        /// POST /members/batch — moi NHIEU nguoi mot lan theo userId va / hoac email. Tung nguoi mot
        /// ket qua (them duoc, khong tim thay, da la thanh vien, bi chan, chua du cap do...); nguoi
        /// hop le duoc them het trong MOT transaction cung cac event member.added.
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
                throw new InvalidValueException("thieu_thong_tin", "Can role (labeler / reviewer).");
            }

            if (body.Role.Value == MemberRole.Owner)
            {
                throw new InvalidValueException("khong_the_them_owner", "Moi du an chi co mot chu so huu.");
            }

            List<DauVaoMoi> dauVao = DocDauVao(body);
            QuyDinhThanhVien quyDinh = QuyDinhTuSetting.ThanhVien(_settings);
            if (dauVao.Count == 0)
            {
                throw new InvalidValueException("thieu_thong_tin", "Can it nhat mot userId hoac email.");
            }

            if (dauVao.Count > quyDinh.SoNguoiMoiToiDa)
            {
                throw new InvalidValueException(
                    "qua_nhieu_nguoi", "Moi lan moi toi da " + quyDinh.SoNguoiMoiToiDa + " nguoi.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            if (duAn.DaKetThuc())
            {
                throw new RuleViolationException("du_an_da_ket_thuc", "Du an da ket thuc.");
            }

            List<Guid> ids = dauVao.Where(d => d.UserId.HasValue).Select(d => d.UserId!.Value).ToList();
            List<string> emails = dauVao.Where(d => d.Email != null && d.Loi == null).Select(d => d.Email!).ToList();
            IReadOnlyList<NguoiDungTraCuu> tim = await _identity.TraCuuAsync(ids, emails, ct);

            // Hai request moi cung mot nguoi cung luc: khoa chinh (project_id, user_id) chon mot,
            // request kia lam lai tu dau va thay nguoi do "da la thanh vien".
            for (int lan = 0; ; lan++)
            {
                try
                {
                    return await ThemTheoKetQuaTraCuuAsync(duAn, body.Role.Value, dauVao, tim, caller, ct);
                }
                catch (DbUpdateException ex) when (lan == 0 && PostgresErrors.IsUniqueViolation(ex, "PK_project_members"))
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
                throw new RuleViolationException("can_lam_test", "Du an yeu cau lam bai test dau vao truoc khi tham gia.");
            }

            Guid userId = caller.LayUserId();
            await KiemTraChuaLaThanhVienAsync(projectId, userId, ct);
            await KiemDieuKienLabelerAsync(duAn, userId, ct);

            ProjectMember moi = ProjectMember.TaoThanhVien(projectId, userId, MemberRole.Labeler, _clock.GetUtcNow());
            await LuuThanhVienMoiAsync(moi, MemberJoinVia.Join, duAn, caller, ct);

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
                throw new RuleViolationException("da_la_thanh_vien", "Nguoi nay da la thanh vien du an.");
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
                    throw new RuleViolationException("bi_chan", "Ban da bi chan khoi du an nay.");
                }

                throw new RuleViolationException("da_la_thanh_vien", "Nguoi nay da la thanh vien du an.");
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
                throw new NotFoundException("Khong tim thay tai khoan.");
            }

            duAn.KiemTraLabelerDuDieuKien(nguoi.HoSo());
            return nguoi;
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

            DateTimeOffset luc = _clock.GetUtcNow();
            HashSet<Guid> daXet = new HashSet<Guid>();
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
                if (d.Loi != null)
                {
                    trangThai = d.Loi;
                    loiGiai = d.Loi == KetQuaMoi.EmailKhongHopLe ? "Email khong hop le." : "userId khong hop le.";
                }
                else if (n == null)
                {
                    trangThai = KetQuaMoi.KhongTimThay;
                    loiGiai = "Khong co tai khoan voi " + (d.UserId.HasValue ? "userId" : "email") + " nay.";
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
                        ? "Nguoi nay dang bi chan khoi du an — bo chan thay vi moi lai."
                        : "Nguoi nay da la thanh vien du an.";
                }
                else
                {
                    HoSoNguoiDung hoSo = n.HoSo();
                    string? lyDo = role == MemberRole.Labeler
                        ? duAn.LyDoLabelerKhongDuDieuKien(hoSo)
                        : (hoSo.BiKhoa ? "tai_khoan_bi_khoa" : null);

                    if (lyDo != null)
                    {
                        trangThai = lyDo;
                        loiGiai = duAn.GiaiThichKhongDuDieuKien(lyDo, hoSo);
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
                    DisplayName = n == null ? null : n.DisplayName,
                    Status = trangThai,
                    Message = loiGiai,
                });
            }

            if (soThem > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            return new AddMembersBatchResponse { Added = soThem, Results = ketQua };
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
    }
}
