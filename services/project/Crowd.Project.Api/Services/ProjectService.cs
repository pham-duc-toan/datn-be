using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Project;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Api.Settings;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Tang DIEU PHOI cho du an: nap aggregate → goi MOT phuong thuc domain →
    /// phat event → luu. Khong chua luat nghiep vu; luat nam trong LabelingProject.
    ///
    /// Moi thao tac la MOT SaveChanges: thay doi du an va event trong outbox
    /// commit cung nhau (VD-D-01).
    /// </summary>
    public sealed class ProjectService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly ProjectEventPublisher _events;
        private readonly ProjectSagaOptions _saga;
        private readonly TimeProvider _clock;
        private readonly ILogger<ProjectService> _logger;
        private readonly ISettings _settings;
        private readonly DongSoClient _dongSo;

        private static readonly string[] LyDoChuaDongBoTamDung = { "chua_dong_bo_tam_dung" };
        private static readonly string[] LyDoConLuotDangLam = { "con_luot_dang_lam" };

        public ProjectService(
            ProjectDbContext db,
            ProjectAccessService access,
            ProjectEventPublisher events,
            IOptions<ProjectSagaOptions> saga,
            TimeProvider clock,
            ILogger<ProjectService> logger,
            ISettings settings,
            DongSoClient dongSo)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (dongSo == null)
            {
                throw new ArgumentNullException(nameof(dongSo));
            }

            _settings = settings;
            _dongSo = dongSo;

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

            if (saga == null)
            {
                throw new ArgumentNullException(nameof(saga));
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
            _events = events;
            _saga = saga.Value;
            _clock = clock;
            _logger = logger;
        }

        // =====================================================================
        // TAO VA CAU HINH
        // =====================================================================

        public async Task<ProjectResponse> TaoAsync(CreateProjectRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Guid ownerId = caller.LayUserId();

            LabelingProject duAn = LabelingProject.Tao(
                ownerId,
                body.Name ?? string.Empty,
                body.Description,
                body.Modality ?? string.Empty,
                body.Visibility ?? ProjectVisibility.Public,
                bayGio, QuyDinhTuSetting.DuAn(_settings));

            // Chu du an cung la mot dong trong project_members — nho vay MOT
            // predicate (ProjectAccessService) tra loi duoc moi cau hoi quyen.
            ProjectMember chu = ProjectMember.TaoChuSoHuu(duAn.Id, ownerId, bayGio);

            _db.Projects.Add(duAn);
            _db.ProjectMembers.Add(chu);

            _events.Phat(caller, new MemberAdded
            {
                ProjectId = duAn.Id,
                UserId = ownerId,
                Role = ProjectMemberRole.Owner,
            });

            await _db.SaveChangesAsync(ct);

            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> CapNhatThongTinAsync(
            Guid id, UpdateProjectInfoRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.CapNhatThongTin(
                body.Name ?? string.Empty,
                body.Description,
                body.Visibility ?? duAn.Visibility,
                _clock.GetUtcNow(), QuyDinhTuSetting.DuAn(_settings));

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>
        /// Dat tap nhan: body la chinh tap nhan {"tools":[...], "segmentSeconds"?: ...}.
        /// "modality" co the bo — lay theo du an; neu gui thi phai trung.
        /// Kiem hinh dang bang JSON Schema roi ngu nghia (Crowd.Labeling.LabelSchema).
        /// </summary>
        public async Task<ProjectResponse> DatLabelSchemaAsync(
            Guid id, JsonElement body, Caller caller, CancellationToken ct)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidValueException("tap_nhan_sai_dinh_dang", "Tap nhan phai la mot object JSON.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);

            // Doi tap nhan sau khi da co cau hoi vang se lam dap an vang tro vao
            // lop khong con ton tai. Bat xoa cau hoi vang truoc.
            bool coCauVang = await _db.GoldItems.AnyAsync(g => g.ProjectId == id, ct);
            if (coCauVang)
            {
                throw new RuleViolationException(
                    "da_co_cau_hoi_vang",
                    "Du an da co cau hoi vang. Xoa cau hoi vang truoc khi doi tap nhan.");
            }

            System.Text.Json.Nodes.JsonObject o = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(body.GetRawText())!;
            if (o["modality"] == null)
            {
                o["modality"] = duAn.Modality;
            }

            LabelSchema schema = LabelSchema.Doc(o.ToJsonString());
            duAn.DatLabelSchema(schema, _clock.GetUtcNow());

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> DatHuongDanAsync(
            Guid id, GuidelineRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);

            List<GuidelineExample> viDu = new List<GuidelineExample>();
            List<Guid> mauCanKiem = new List<Guid>();

            if (body.Examples != null)
            {
                foreach (GuidelineExampleDto e in body.Examples)
                {
                    viDu.Add(new GuidelineExample(e.SampleId, e.Label, e.IsCorrect, e.Explanation ?? string.Empty));

                    if (e.SampleId.HasValue)
                    {
                        mauCanKiem.Add(e.SampleId.Value);
                    }
                }
            }

            // Vi du chi duoc tro toi mau CUA CHINH du an nay — khong thi chu du an
            // A nhet id anh cua du an B vao huong dan de xem trom (BOLA).
            await KiemMauThuocDuAnAsync(id, mauCanKiem, ct);

            duAn.DatHuongDan(Guideline.Tao(body.Markdown ?? string.Empty, viDu, QuyDinhTuSetting.DuAn(_settings)), _clock.GetUtcNow());

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> DatCauHinhGiaAsync(
            Guid id, PricingRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (body.Deadline == null)
            {
                throw new InvalidValueException("thieu_deadline", "Phai co deadline.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.DatCauHinhGia(
                body.UnitPriceVnd,
                body.Redundancy,
                body.MaxRedundancy ?? body.Redundancy,
                body.BudgetVnd,
                body.Deadline.Value,
                _clock.GetUtcNow(), QuyDinhTuSetting.DuAn(_settings));

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> DatKiemSoatChatLuongAsync(
            Guid id, QualityControlRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.DatKiemSoatChatLuong(body.GoldCheckPercent, _clock.GetUtcNow(), QuyDinhTuSetting.DuAn(_settings));

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> DatKenhAsync(
            Guid id, ChannelsRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.DatKenhPhanPhoi(body.AllowProfessional, body.AllowLinkGateway, body.AllowCollaborative, _clock.GetUtcNow());

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> DatDieuKienAsync(
            Guid id, EligibilityRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.DatDieuKienThamGia(
                body.MinLevel,
                body.MinReputation,
                body.RequireEntranceTest,
                body.EntranceQuestionCount,
                body.EntrancePassPercent,
                _clock.GetUtcNow(), QuyDinhTuSetting.DuAn(_settings));

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        // =====================================================================
        // DOC
        // =====================================================================

        public async Task<ProjectResponse> ChiTietAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeXemAsync(id, caller, ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>
        /// Danh sach du an nguoi goi XEM DUOC — cung predicate voi ChiTietAsync.
        /// chiCuaToi = chi du an minh la thanh vien (trang "Du an cua toi").
        /// </summary>
        public async Task<PagedResponse<ProjectListItem>> DanhSachAsync(
            Caller caller,
            ProjectStatus? status,
            string? modality,
            bool chiCuaToi,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            ChuanHoaTrang(ref page, ref pageSize);

            IQueryable<LabelingProject> q = _access.XemDuoc(caller).AsNoTracking();

            if (chiCuaToi && caller.UserId != null)
            {
                Guid uid = caller.UserId.Value;
                q = q.Where(p => _db.ProjectMembers.Any(m => m.ProjectId == p.Id && m.UserId == uid));
            }

            if (status.HasValue)
            {
                ProjectStatus s = status.Value;
                q = q.Where(p => p.Status == s);
            }

            if (!string.IsNullOrEmpty(modality))
            {
                string m = modality;
                q = q.Where(p => p.Modality == m);
            }

            int tong = await q.CountAsync(ct);

            List<LabelingProject> trang = await q
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            List<ProjectListItem> items = new List<ProjectListItem>();
            foreach (LabelingProject p in trang)
            {
                items.Add(new ProjectListItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    Modality = p.Modality,
                    Status = p.Status,
                    Visibility = p.Visibility,
                    UnitPriceVnd = p.UnitPriceVnd,
                    Deadline = p.Deadline,
                    RequireEntranceTest = p.RequireEntranceTest,
                    IsOwner = caller.UserId != null && p.OwnerId == caller.UserId.Value,
                });
            }

            return new PagedResponse<ProjectListItem>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                Total = tong,
            };
        }

        public async Task<ReadinessResponse> KiemTraSanSangAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);

            int soMau = await DemMauAsync(id, ct);
            int soVangTest = await DemVangTestAsync(id, ct);

            bool dangXuLy = await CoDuLieuDangXuLyAsync(id, ct);

            int phi = _settings.SoNguyen(SettingKeys.FeePlatformPercent);
            IReadOnlyList<string> thieu = duAn.NhungGiConThieu(soMau, soVangTest, phi, dangXuLy, _clock.GetUtcNow());

            return new ReadinessResponse
            {
                Ready = thieu.Count == 0,
                Missing = thieu,
                SampleCount = soMau,
                EntranceGoldCount = soVangTest,
                PlatformFeePercent = phi,
                PlatformFeePerLabelVnd = LabelingProject.PhiMoiNhanVnd(duAn.UnitPriceVnd, phi),
                EstimatedCostVnd = duAn.ChiPhiUocTinhVnd(soMau, phi),
            };
        }

        // =====================================================================
        // VONG DOI — SAGA PUBLISH (docs 3.4)
        // =====================================================================

        /// <summary>
        /// Nhap → Cho ky quy, phat project.publish_requested cho ledger.
        /// Dev chua co ledger (Saga:BoQuaKyQuy) thi di tiep luon sang Cho duyet.
        /// </summary>
        public async Task<ProjectResponse> YeuCauPublishAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            DateTimeOffset bayGio = _clock.GetUtcNow();

            int soMau = await DemMauAsync(id, ct);
            int soVangTest = await DemVangTestAsync(id, ct);

            bool dangXuLy = await CoDuLieuDangXuLyAsync(id, ct);

            duAn.YeuCauPublish(soMau, soVangTest, _settings.SoNguyen(SettingKeys.FeePlatformPercent), dangXuLy, bayGio);

            if (_saga.BoQuaKyQuy)
            {
                _logger.LogWarning(
                    "Saga:BoQuaKyQuy = true — du an {ProjectId} coi nhu da ky quy, KHONG hoi ledger. Chi dung o dev.",
                    duAn.Id);

                duAn.XacNhanDaKyQuy(bayGio);
                await TuDuyetNeuBatAsync(duAn, caller, ct);
            }
            else
            {
                _events.Phat(caller, new ProjectPublishRequested
                {
                    ProjectId = duAn.Id,
                    OwnerId = duAn.OwnerId,
                    EscrowAmountVnd = duAn.BudgetVnd,
                });
            }

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>Danh sach cho admin duyet, cu nhat truoc (ai cho lau duoc xem truoc).</summary>
        public async Task<IReadOnlyList<ProjectResponse>> DanhSachChoDuyetAsync(Caller caller, CancellationToken ct)
        {
            List<LabelingProject> ds = await _db.Projects
                .AsNoTracking()
                .Where(p => p.Status == ProjectStatus.PendingApproval)
                .OrderBy(p => p.SubmittedForApprovalAt)
                .Take(200)
                .ToListAsync(ct);

            List<ProjectResponse> ketQua = new List<ProjectResponse>();
            foreach (LabelingProject p in ds)
            {
                ketQua.Add(TaoResponse(p, caller));
            }

            return ketQua;
        }

        /// <summary>Admin duyet: Cho duyet → Dang chay, phat project.published.</summary>
        /// <summary>
        /// Setting project.auto_approve bat: du an vua ky quy xong (PendingApproval) duoc
        /// duyet ngay, nguoi duyet la HE THONG. Goi tu consumer escrow.reserved (va duong
        /// dev BoQuaKyQuy). KHONG SaveChanges — noi goi lo.
        /// </summary>
        public async Task<bool> TuDuyetNeuBatAsync(LabelingProject duAn, Caller caller, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (!_settings.DungSai(SettingKeys.ProjectAutoApprove) || duAn.Status != ProjectStatus.PendingApproval)
            {
                return false;
            }

            duAn.Duyet(_clock.GetUtcNow());
            int soMau = await DemMauAsync(duAn.Id, ct);
            _events.Phat(caller, TaoProjectPublished(duAn, soMau));
            _logger.LogInformation("Du an {ProjectId} tu duyet (project.auto_approve)", duAn.Id);
            return true;
        }

        public async Task<ProjectResponse> DuyetAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await LayTheoIdAsync(id, ct);
            duAn.Duyet(_clock.GetUtcNow());

            int soMau = await DemMauAsync(id, ct);
            _events.Phat(caller, TaoProjectPublished(duAn, soMau));

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>Admin tu choi: huy + ledger hoan ky quy (compensation).</summary>
        public async Task<ProjectResponse> TuChoiDuyetAsync(Guid id, string? lyDo, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await LayTheoIdAsync(id, ct);
            duAn.TuChoiDuyet(lyDo ?? string.Empty, _clock.GetUtcNow());

            PhatDaHuy(duAn, 0, caller);

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> TamDungAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.TamDung(_clock.GetUtcNow());

            _events.Phat(caller, new ProjectPaused { ProjectId = duAn.Id });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> TiepTucAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.TiepTuc(_clock.GetUtcNow());

            _events.Phat(caller, new ProjectResumed { ProjectId = duAn.Id });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> HoanThanhAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            int soNhanDaDuyet = await DamBaoDongSoAsync(duAn, ct);
            duAn.HoanThanh(_clock.GetUtcNow());

            _events.Phat(caller, new ProjectCompleted
            {
                ProjectId = duAn.Id,
                OwnerId = duAn.OwnerId,
                ApprovedAnnotationCount = soNhanDaDuyet,
            });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> HuyAsync(Guid id, string? lyDo, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            int soNhanDaDuyet = await DamBaoDongSoAsync(duAn, ct);
            duAn.Huy(lyDo, _clock.GetUtcNow());

            PhatDaHuy(duAn, soNhanDaDuyet, caller);

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>
        /// Truoc khi dong (hoan thanh / huy) mot du an DA CHAY — ngay sau do ledger tra ky quy
        /// con lai ve doanh nghiep — bao dam khong con viec nao sinh tien cho labeler:
        ///   1. task-svc: da nhan project.paused, khong con luot dang giu, lay so luot nop that;
        ///   2. annotation-svc: so nhan khop so luot nop, khong nhan cho duyet / khieu nai mo /
        ///      nhan bi tu choi con han khieu nai — dat thi DONG SO ngay trong cung transaction.
        /// Du an chua chay (Nhap, Cho duyet) khong co nhan → bo qua. Dang Running thi domain nem
        /// can_tam_dung_truoc o buoc sau — KHONG goi dong so de khoi dong bang nham.
        /// Annotation da dong so ma luu du an loi → doanh nghiep bam lai (dong so idempotent),
        /// hoac chay tiep: project.resumed mo lai so ben annotation-svc.
        /// Tra ve so nhan da duyet luc dong — project.completed / cancelled mang theo de ledger chi
        /// hoan ky quy SAU KHI da chi du (annotation.approved co the toi ledger sau event dong).
        /// </summary>
        private async Task<int> DamBaoDongSoAsync(LabelingProject duAn, CancellationToken ct)
        {
            if (duAn.Status != ProjectStatus.Paused)
            {
                return 0;
            }

            TaskCloseCheck task = await _dongSo.KiemTaskAsync(duAn.Id, ct);
            if (!task.Paused || task.ActiveLeases > 0)
            {
                throw new ChuaTheDongDuAnException(
                    !task.Paused
                        ? "He thong dang xu ly lenh tam dung, thu lai sau vai giay."
                        : "Con " + task.ActiveLeases + " luot labeler dang lam do. Cho ho nop xong (hoac het han lease) roi dong.",
                    new
                    {
                        reasons = !task.Paused ? LyDoChuaDongBoTamDung : LyDoConLuotDangLam,
                        activeLeases = task.ActiveLeases,
                        submittedCount = task.SubmittedCount,
                    });
            }

            AnnotationCloseResult so = await _dongSo.DongSoAnnotationAsync(duAn.Id, task.SubmittedCount, ct);
            if (!so.Closed)
            {
                throw new ChuaTheDongDuAnException(MoTaChuaDong(so), new
                {
                    reasons = so.Reasons,
                    activeLeases = 0,
                    submittedCount = so.SubmittedCount,
                    annotationCount = so.AnnotationCount,
                    pendingReview = so.PendingReview,
                    openAppeals = so.OpenAppeals,
                    rejectedInAppealWindow = so.RejectedInAppealWindow,
                    appealWindowEndsAt = so.AppealWindowEndsAt,
                });
            }

            return so.ApprovedCount;
        }

        private static string MoTaChuaDong(AnnotationCloseResult so)
        {
            List<string> phan = new List<string>();
            if (so.PendingReview > 0)
            {
                phan.Add(so.PendingReview + " nhan cho duyet");
            }

            if (so.OpenAppeals > 0)
            {
                phan.Add(so.OpenAppeals + " khieu nai cho admin phan xu");
            }

            if (so.RejectedInAppealWindow > 0)
            {
                phan.Add(so.RejectedInAppealWindow + " nhan bi tu choi con trong han khieu nai"
                         + (so.AppealWindowEndsAt.HasValue ? " (het han " + so.AppealWindowEndsAt.Value.ToString("u") + ")" : string.Empty));
            }

            if (so.AnnotationCount != so.SubmittedCount)
            {
                phan.Add("nhan dang dong bo (" + so.AnnotationCount + "/" + so.SubmittedCount + "), thu lai sau vai giay");
            }

            return "Chua dong duoc du an: con " + string.Join("; ", phan) + ".";
        }

        /// <summary>
        /// Worker goi dinh ky: huy moi du an cho duyet qua han (compensation cua
        /// saga). Moi du an mot SaveChanges rieng — mot du an loi khong keo cac
        /// du an khac.
        /// </summary>
        public async Task<int> HuyCacDuAnQuaHanAsync(CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            DateTimeOffset moc = bayGio - _settings.ThoiGian(SettingKeys.ProjectApprovalTimeout);

            List<Guid> ids = await _db.Projects
                .Where(p => p.Status == ProjectStatus.PendingApproval && p.SubmittedForApprovalAt <= moc)
                .Select(p => p.Id)
                .Take(100)
                .ToListAsync(ct);

            int soDaHuy = 0;
            TimeSpan hanChoDuyet = _settings.ThoiGian(SettingKeys.ProjectApprovalTimeout);

            foreach (Guid id in ids)
            {
                _db.ChangeTracker.Clear();

                LabelingProject? duAn = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
                if (duAn == null || !duAn.DaQuaHanChoDuyet(hanChoDuyet, bayGio))
                {
                    continue;
                }

                duAn.HuyDoQuaHanChoDuyet(hanChoDuyet, bayGio);
                PhatDaHuy(duAn, 0, Caller.HeThong(Guid.CreateVersion7(), null));

                try
                {
                    await _db.SaveChangesAsync(ct);
                    soDaHuy = soDaHuy + 1;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Admin vua duyet dung luc nay — admin thang, bo qua.
                    _logger.LogInformation("Du an {ProjectId} vua doi trang thai, bo qua huy qua han", id);
                }
            }

            return soDaHuy;
        }

        // =====================================================================
        // Ham dung chung (cung duoc consumer dung)
        // =====================================================================

        public static ProjectPublished TaoProjectPublished(LabelingProject duAn, int soMau)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (duAn.LabelSchema == null || duAn.Deadline == null)
            {
                throw new InvalidOperationException("Du an dang chay phai co tap nhan va deadline.");
            }

            return new ProjectPublished
            {
                ProjectId = duAn.Id,
                OwnerId = duAn.OwnerId,
                Modality = duAn.Modality,
                LabelSchema = duAn.LabelSchema.ToRawJson(),
                UnitPriceVnd = duAn.UnitPriceVnd,
                PlatformFeeVnd = LabelingProject.PhiMoiNhanVnd(duAn.UnitPriceVnd, duAn.PlatformFeePercent),
                Redundancy = duAn.Redundancy,
                MaxRedundancy = duAn.TranRedundancy(),
                GoldCheckPercent = duAn.GoldCheckPercent,
                Deadline = duAn.Deadline.Value,
                AllowProfessional = duAn.AllowProfessional,
                AllowLinkGateway = duAn.AllowLinkGateway,
                AllowCollaborative = duAn.AllowCollaborative,
                IsPrivate = duAn.Visibility == ProjectVisibility.Private,
                MinLevel = duAn.MinLevel,
                MinReputation = duAn.MinReputation,
                RequireEntranceTest = duAn.RequireEntranceTest,
                SampleCount = soMau,
            };
        }

        /// <summary>soNhanDaDuyet: tu lan dong so (du an da chay); 0 khi huy luc chua chay (chua co nhan).</summary>
        private void PhatDaHuy(LabelingProject duAn, int soNhanDaDuyet, Caller caller)
        {
            _events.Phat(caller, new ProjectCancelled
            {
                ProjectId = duAn.Id,
                OwnerId = duAn.OwnerId,
                WasEscrowed = duAn.WasEscrowed,
                Reason = duAn.StatusReason,
                ApprovedAnnotationCount = soNhanDaDuyet,
            });
        }

        private async Task<LabelingProject> LayTheoIdAsync(Guid id, CancellationToken ct)
        {
            LabelingProject? duAn = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (duAn == null)
            {
                throw new NotFoundException("Khong tim thay du an.");
            }

            return duAn;
        }

        /// <summary>Con lo manifest dang cho / dang xu ly — chua biet het so mau.</summary>
        private Task<bool> CoDuLieuDangXuLyAsync(Guid projectId, CancellationToken ct)
        {
            return _db.Datasets.AnyAsync(
                d => d.ProjectId == projectId && (d.Status == DatasetStatus.Pending || d.Status == DatasetStatus.Ingesting),
                ct);
        }

        private Task<int> DemMauAsync(Guid projectId, CancellationToken ct)
        {
            return _db.Samples.CountAsync(s => s.ProjectId == projectId, ct);
        }

        private Task<int> DemVangTestAsync(Guid projectId, CancellationToken ct)
        {
            return _db.GoldItems.CountAsync(
                g => g.ProjectId == projectId && g.Purpose == Crowd.Project.Domain.Gold.GoldPurpose.EntranceTest,
                ct);
        }

        private async Task KiemMauThuocDuAnAsync(Guid projectId, List<Guid> sampleIds, CancellationToken ct)
        {
            if (sampleIds.Count == 0)
            {
                return;
            }

            List<Guid> khacNhau = sampleIds.Distinct().ToList();
            int soThuoc = await _db.Samples.CountAsync(s => s.ProjectId == projectId && khacNhau.Contains(s.Id), ct);

            if (soThuoc != khacNhau.Count)
            {
                throw new InvalidValueException("mau_khong_thuoc_du_an", "Co mau khong thuoc du an nay.");
            }
        }

        private static void ChuanHoaTrang(ref int page, ref int pageSize)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }
        }

        public static ProjectResponse TaoResponse(LabelingProject p, Caller caller)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            bool laChu = ProjectAccessService.LaChu(p, caller);

            GuidelineResponse? huongDan = null;
            if (p.Guideline != null)
            {
                List<GuidelineExampleDto> viDu = new List<GuidelineExampleDto>();
                foreach (GuidelineExample e in p.Guideline.Examples)
                {
                    viDu.Add(new GuidelineExampleDto
                    {
                        SampleId = e.SampleId,
                        Label = e.Label,
                        IsCorrect = e.IsCorrect,
                        Explanation = e.Explanation,
                    });
                }

                huongDan = new GuidelineResponse { Markdown = p.Guideline.Markdown, Examples = viDu };
            }

            return new ProjectResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Modality = p.Modality,
                Status = p.Status,
                Visibility = p.Visibility,
                LabelSchema = p.LabelSchema == null ? null : p.LabelSchema.ToRawJson(),
                Guideline = huongDan,
                UnitPriceVnd = p.UnitPriceVnd,
                Redundancy = p.Redundancy,
                MaxRedundancy = p.TranRedundancy(),
                GoldCheckPercent = p.GoldCheckPercent,
                Deadline = p.Deadline,
                AllowProfessional = p.AllowProfessional,
                AllowLinkGateway = p.AllowLinkGateway,
                AllowCollaborative = p.AllowCollaborative,
                MinLevel = p.MinLevel,
                MinReputation = p.MinReputation,
                RequireEntranceTest = p.RequireEntranceTest,
                EntranceQuestionCount = p.EntranceQuestionCount,
                EntrancePassPercent = p.EntrancePassPercent,
                PublishedAt = p.PublishedAt,
                IsOwner = caller.UserId != null && p.OwnerId == caller.UserId.Value,

                OwnerId = laChu ? p.OwnerId : null,
                BudgetVnd = laChu ? p.BudgetVnd : null,
                StatusReason = laChu ? p.StatusReason : null,
                CreatedAt = laChu ? p.CreatedAt : null,
                UpdatedAt = laChu ? p.UpdatedAt : null,
            };
        }
    }
}
