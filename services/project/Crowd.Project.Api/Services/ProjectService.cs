using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.BuildingBlocks.Tags;
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
using Crowd.Settings;
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
        private static readonly string[] LyDoDaCoBaiNop = { "da_co_bai_nop" };

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
                throw new InvalidValueException("tap_nhan_sai_dinh_dang", "Tập nhãn phải là một object JSON.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);

            // Doi tap nhan sau khi da co cau hoi vang se lam dap an vang tro vao
            // lop khong con ton tai. Bat xoa cau hoi vang truoc.
            bool coCauVang = await _db.GoldItems.AnyAsync(g => g.ProjectId == id, ct);
            if (coCauVang)
            {
                throw new RuleViolationException(
                    "da_co_cau_hoi_vang",
                    "Dự án đã có câu hỏi vàng. Xoá câu hỏi vàng trước khi đổi tập nhãn.");
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
                throw new InvalidValueException("thieu_deadline", "Phải có hạn chót.");
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.DatCauHinhGia(
                body.RewardVnd,
                body.Slots,
                body.PassThresholdPercent,
                body.FillSlotsMinPercent,
                body.Deadline.Value,
                _clock.GetUtcNow(), QuyDinhTuSetting.DuAn(_settings));

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
            duAn.DatKenhPhanPhoi(
                body.AllowProfessional, body.AllowLinkGateway, body.AllowCollaborative, body.GateUnitPriceVnd, body.GateBudgetVnd, _clock.GetUtcNow());

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

        /// <summary>PUT /projects/{id}/retention { deleteFilesAfterClose } — dong y xoa file goc sau khi dong (muc 3.14).</summary>
        public async Task<ProjectResponse> DatLuuTruAsync(Guid id, RetentionRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.DatXoaFileSauKhiDong(body.DeleteFilesAfterClose, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>
        /// Worker: xoa file trong kho cua cac du an da dong qua project.closed_data_retention ma doanh nghiep
        /// DA DONG Y. Mau trong database giu nguyen (lich su, nhan, ket qua); chi file goc bi xoa — link xem
        /// mau sau do tra 404. Tra so du an da xoa.
        /// </summary>
        public async Task<int> XoaFileDuAnDaDongAsync(IObjectStorage kho, CancellationToken ct)
        {
            if (kho == null)
            {
                throw new ArgumentNullException(nameof(kho));
            }

            TimeSpan hanGiu = _settings.ThoiGian(SettingKeys.ProjectClosedDataRetention);
            if (hanGiu <= TimeSpan.Zero)
            {
                return 0;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            DateTimeOffset moc = bayGio - hanGiu;
            List<LabelingProject> ds = await _db.Projects
                .Where(p => p.DeleteFilesAfterClose && p.FilesPurgedAt == null && p.ClosedAt != null && p.ClosedAt < moc
                    && (p.Status == ProjectStatus.Completed || p.Status == ProjectStatus.Cancelled))
                .Take(20)
                .ToListAsync(ct);

            int n = 0;
            foreach (LabelingProject p in ds)
            {
                if (!p.DenHanXoaFile(hanGiu, bayGio))
                {
                    continue;
                }

                int soFile = await kho.XoaTheoTienToAsync(p.Id + "/", ct);
                p.DanhDauDaXoaFile(bayGio);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Xoa {SoFile} file cua du an da dong {ProjectId} (doanh nghiep dong y)", soFile, p.Id);
                n++;
            }

            return n;
        }

        /// <summary>PUT /projects/{id}/tags { domains, languages } — nhan goi y du an (FL-02).</summary>
        public async Task<ProjectResponse> DatNhanAsync(Guid id, TagsRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.DatNhan(VeMaThe(body.Domains, "Lĩnh vực"), VeMaThe(body.Languages, "Ngôn ngữ"), _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>
        /// Nhan du an la MA THE cua danh muc admin (MaThe) — goi y khop theo ma voi ho so ky nang. Chu tu do cung doi
        /// ve ma ("Y tế" → "y-te") de client cu van gui duoc.
        /// </summary>
        private static List<string>? VeMaThe(IReadOnlyList<string>? ds, string ten)
        {
            if (ds == null)
            {
                return null;
            }

            List<string> kq = new List<string>();
            foreach (string? muc in ds)
            {
                string ma = MaThe.TuChu(muc);
                if (ma.Length == 0)
                {
                    throw new InvalidValueException("nhan_khong_hop_le", ten + ": “" + muc + "” không phải thẻ hợp lệ.");
                }

                kq.Add(ma);
            }

            return kq;
        }

        /// <summary>
        /// tag.merged: doi ma the cu sang ma moi o moi du an dang gan. KHONG SaveChanges — consumer luu mot lan.
        /// </summary>
        public async Task<int> GopTheAsync(string loai, string maCu, string maMoi, CancellationToken ct)
        {
            List<LabelingProject> ds = loai == MaThe.NgonNgu
                ? await _db.Projects.Where(p => EF.Property<List<string>>(p, "_languages").Contains(maCu)).ToListAsync(ct)
                : await _db.Projects.Where(p => EF.Property<List<string>>(p, "_domains").Contains(maCu)).ToListAsync(ct);
            int doi = 0;
            foreach (LabelingProject p in ds)
            {
                if (p.DoiMaThe(loai, maCu, maMoi))
                {
                    doi++;
                }
            }

            return doi;
        }

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
                    RewardVnd = p.RewardVnd,
                    Slots = p.Slots,
                    PassThresholdPercent = p.PassThresholdPercent,
                    QuestionCount = p.QuestionCount,
                    ToolKinds = LoaiCongCu(p),
                    Deadline = p.Deadline,
                    RequireEntranceTest = p.RequireEntranceTest,
                    IsOwner = caller.UserId != null && p.OwnerId == caller.UserId.Value,
                    SensitiveContent = p.SensitiveContent,
                    ConsentRequired = p.CanDongYCaNhan(),
                    RequireVerifiedIdentity = p.RequireVerifiedIdentity,
                    Domains = p.Domains,
                    Languages = p.Languages,
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
            int soCauThat = await DemCauThatAsync(id, ct);
            int soVangTest = await DemVangAsync(id, Crowd.Project.Domain.Gold.GoldPurpose.EntranceTest, ct);
            int soVangKiemTra = await DemVangAsync(id, Crowd.Project.Domain.Gold.GoldPurpose.QualityCheck, ct);
            bool dangXuLy = await CoDuLieuDangXuLyAsync(id, ct);
            int videoDangChuanBi = await DemVideoAsync(id, TrangThaiMedia.DangChuanBi, ct);
            int videoLoi = await DemVideoAsync(id, TrangThaiMedia.Loi, ct);

            int phi = _settings.SoNguyen(SettingKeys.FeePlatformPercent);
            QuyDinhDuAn quyDinh = QuyDinhTuSetting.DuAn(_settings);
            IReadOnlyList<string> thieu = duAn.NhungGiConThieu(
                soCauThat, soVangTest, dangXuLy, quyDinh, CongCuChoCongLink(), _clock.GetUtcNow(), videoDangChuanBi);

            return new ReadinessResponse
            {
                Ready = thieu.Count == 0,
                Missing = thieu,
                Warnings = LabelingProject.CanhBao(duAn.LabelSchema, videoLoi),
                VideoPreparingCount = videoDangChuanBi,
                VideoFailedCount = videoLoi,
                SampleCount = soMau,
                QuestionCount = soCauThat,
                QualityGoldCount = soVangKiemTra,
                EntranceGoldCount = soVangTest,
                PlatformFeePercent = phi,
                PlatformFeePerPassVnd = LabelingProject.TinhPhiVnd(duAn.RewardVnd, phi),
                EstimatedCostVnd = duAn.ChiPhiUocTinhVnd(phi),
                Limits = new ProjectLimits
                {
                    PassThresholdMin = quyDinh.NguongDatToiThieu,
                    PassThresholdMax = quyDinh.NguongDatToiDa,
                    MinSlots = quyDinh.SoSuatToiThieu,
                    MaxSlots = quyDinh.SoSuatToiDa,
                    ParticipantMultiplier = _settings.SoNguyen(SettingKeys.ProjectParticipantMultiplier),
                    MaxQuestions = quyDinh.SoCauToiDa,
                    MinRewardVnd = quyDinh.ThuongToiThieu,
                    MatchThresholds = NguongKhopMacDinh(),
                },
            };
        }

        /// <summary>Nguong khop mac dinh cua moi loai cong cu co nguong (labeling.threshold.*).</summary>
        private Dictionary<string, double> NguongKhopMacDinh()
        {
            NguongKhop nguong = NguongKhopTuSetting.Doc(_settings);
            Dictionary<string, double> kq = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (string kind in ToolKinds.TatCa)
            {
                double? v = nguong.Lay(kind);
                if (v.HasValue)
                {
                    kq[kind] = v.Value;
                }
            }

            return kq;
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

            // KYB (FB-02) + xac thuc email (FC-01): doc claim cua access token (identity cap) — khong giu ban
            // sao. Chi kiem khi CHU DU AN tu publish; admin publish ho thi bo qua (admin la nguoi duyet KYB).
            // Vua duoc duyet / vua xac thuc ma token cu chua co claim: client lam moi token roi goi lai.
            bool chuTuPublish = caller.UserId != null && caller.UserId.Value == duAn.OwnerId;
            if (chuTuPublish && !caller.DaXacMinhDoanhNghiep)
            {
                throw new RuleViolationException(
                    "chua_xac_minh_doanh_nghiep",
                    "Doanh nghiệp chưa được xác minh (KYB). Nộp hồ sơ ở mục Xác minh doanh nghiệp và chờ admin duyệt trước khi publish. Vừa được duyệt thì làm mới token (đăng nhập lại).");
            }

            if (chuTuPublish && !caller.DaXacThucEmail)
            {
                throw new RuleViolationException(
                    "chua_xac_thuc_email",
                    "Chưa xác thực email — bấm link trong email trước khi publish dự án. Vừa xác thực xong thì làm mới token (đăng nhập lại).");
            }

            int soCauThat = await DemCauThatAsync(id, ct);
            int soVangTest = await DemVangAsync(id, Crowd.Project.Domain.Gold.GoldPurpose.EntranceTest, ct);

            bool dangXuLy = await CoDuLieuDangXuLyAsync(id, ct);
            int videoDangChuanBi = await DemVideoAsync(id, TrangThaiMedia.DangChuanBi, ct);

            IReadOnlyList<string> congCuSai = duAn.CongCuKhongHopCongLink(CongCuChoCongLink());
            if (congCuSai.Count > 0)
            {
                throw new RuleViolationException(
                    "cong_cu_khong_hop_cong_link",
                    "Cổng link chỉ cho công cụ làm nhanh (" + string.Join(", ", CongCuChoCongLink()) + "). Tắt cổng link hoặc bỏ công cụ: "
                    + string.Join(", ", congCuSai) + ".");
            }

            duAn.YeuCauPublish(
                soCauThat, soVangTest, _settings.SoNguyen(SettingKeys.FeePlatformPercent), dangXuLy, QuyDinhTuSetting.DuAn(_settings),
                CongCuChoCongLink(), bayGio, videoDangChuanBi);

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
                    EscrowAmountVnd = duAn.ChiPhiUocTinhVnd(duAn.PlatformFeePercent),
                    ProjectName = duAn.Name,
                });
            }

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>Danh sach cho admin duyet, moi nhat truoc (mac dinh moi danh sach).</summary>
        public async Task<IReadOnlyList<ProjectResponse>> DanhSachChoDuyetAsync(Caller caller, CancellationToken ct)
        {
            List<LabelingProject> ds = await _db.Projects
                .AsNoTracking()
                .Where(p => p.Status == ProjectStatus.PendingApproval)
                .OrderByDescending(p => p.SubmittedForApprovalAt)
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

            // Publish da chan khi video chua chuan bi xong; chan them o day phong du lieu nap sau / ban cu.
            int videoDangChuanBi = await DemVideoAsync(id, TrangThaiMedia.DangChuanBi, ct);
            if (videoDangChuanBi > 0)
            {
                throw new RuleViolationException(
                    "video_dang_chuan_bi",
                    "Còn " + videoDangChuanBi + " video đang được chuẩn bị (mã hoá lại để gắn watermark). Duyệt sau khi xong.");
            }

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

            PhatDaHuy(duAn, caller);

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

        /// <summary>
        /// "Ket thuc som": danh dau va bao task-svc dong thu bai. Trang thai chuyen sang Dang cham khi
        /// task-svc phat project.collection_finished (consumer).
        /// </summary>
        public async Task<ProjectResponse> KetThucSomAsync(Guid id, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            duAn.YeuCauKetThucSom(_clock.GetUtcNow());

            _events.Phat(caller, new ProjectFinishRequested { ProjectId = duAn.Id });

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        public async Task<ProjectResponse> HuyAsync(Guid id, string? lyDo, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(id, caller, ct);
            await DamBaoChuaCoBaiNopAsync(duAn, ct);
            duAn.Huy(lyDo, _clock.GetUtcNow());

            PhatDaHuy(duAn, caller);

            await _db.SaveChangesAsync(ct);
            return TaoResponse(duAn, caller);
        }

        /// <summary>
        /// Huy mot du an DA CHAY — ngay sau do ledger tra TOAN BO ky quy ve doanh nghiep — chi khi
        /// task-svc xac nhan: da nhan project.paused (khong ai nop them duoc) va CHUA CO BAI NAO NOP.
        /// Con bai nop thi phai "Ket thuc som" de cham va tra thuong — khong ai lam khong cong. Bai dang
        /// lam do bi huy theo du an. Du an chua chay (Nhap, Cho duyet) bo qua; dang Running thi domain
        /// nem can_tam_dung_truoc.
        /// </summary>
        private async Task DamBaoChuaCoBaiNopAsync(LabelingProject duAn, CancellationToken ct)
        {
            if (duAn.Status != ProjectStatus.Paused)
            {
                return;
            }

            TaskCloseCheck task = await _dongSo.KiemTaskAsync(duAn.Id, ct);
            if (!task.Paused || task.SubmittedCount > 0)
            {
                throw new ChuaTheDongDuAnException(
                    !task.Paused
                        ? "Hệ thống đang xử lý lệnh tạm dừng, thử lại sau vài giây."
                        : "Đã có " + task.SubmittedCount + " bài nộp — không huỷ được. Bấm \"Kết thúc sớm\" để chấm và trả thưởng cho người đạt.",
                    new
                    {
                        reasons = !task.Paused ? LyDoChuaDongBoTamDung : LyDoDaCoBaiNop,
                        inProgressCount = task.InProgressCount,
                        submittedCount = task.SubmittedCount,
                    });
            }
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
                PhatDaHuy(duAn, Caller.HeThong(Guid.CreateVersion7(), null));

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

        /// <summary>
        /// Dieu khoan luc publish (dung payload project.published) — cho service ban sao tu bu khi lo mat event
        /// (vd quality-svc ra doi sau khi du an da publish). Du an chua tung publish → 404.
        /// </summary>
        public async Task<PublishedTermsResponse> DieuKhoanDaPublishAsync(Guid id, CancellationToken ct)
        {
            LabelingProject? duAn = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (duAn == null || duAn.PublishedAt == null || duAn.LabelSchema == null || duAn.Deadline == null)
            {
                throw new NotFoundException("Dự án chưa publish.");
            }

            int soMau = await DemMauAsync(id, ct);
            return new PublishedTermsResponse
            {
                PublishedAt = duAn.PublishedAt.Value,
                Terms = TaoProjectPublished(duAn, soMau),
            };
        }

        /// <summary>Loai cong cu khach vang lai lam duoc o cong link (setting gate.allowed_tools).</summary>
        private HashSet<string> CongCuChoCongLink()
        {
            return _settings.Chuoi(SettingKeys.GateAllowedTools)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
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
                RewardVnd = duAn.RewardVnd,
                PlatformFeeVnd = LabelingProject.TinhPhiVnd(duAn.RewardVnd, duAn.PlatformFeePercent),
                Slots = duAn.Slots,
                PassThresholdPercent = duAn.PassThresholdPercent,
                FillSlotsMinPercent = duAn.FillSlotsMinPercent,
                QuestionCount = duAn.QuestionCount,
                Deadline = duAn.Deadline.Value,
                AllowProfessional = duAn.AllowProfessional,
                AllowLinkGateway = duAn.AllowLinkGateway,
                AllowCollaborative = duAn.AllowCollaborative,
                GateUnitPriceVnd = duAn.AllowLinkGateway ? duAn.GateUnitPriceVnd : null,
                GatePlatformFeeVnd = duAn.AllowLinkGateway ? LabelingProject.TinhPhiVnd(duAn.GateUnitPriceVnd, duAn.PlatformFeePercent) : null,
                GateBudgetVnd = duAn.AllowLinkGateway ? duAn.GateBudgetVnd : null,
                IsPrivate = duAn.Visibility == ProjectVisibility.Private,
                MinLevel = duAn.MinLevel,
                MinReputation = duAn.MinReputation,
                RequireEntranceTest = duAn.RequireEntranceTest,
                SampleCount = soMau,
                SensitiveContent = duAn.SensitiveContent,
            };
        }

        private void PhatDaHuy(LabelingProject duAn, Caller caller)
        {
            _events.Phat(caller, new ProjectCancelled
            {
                ProjectId = duAn.Id,
                OwnerId = duAn.OwnerId,
                WasEscrowed = duAn.WasEscrowed,
                Reason = duAn.StatusReason,
            });
        }

        /// <summary>Cac loai cong cu cua tap nhan (khong lap) — "dang gan nhan" hien o cho du an.</summary>
        public static IReadOnlyList<string> LoaiCongCu(LabelingProject p)
        {
            List<string> ds = new List<string>();
            if (p == null || p.LabelSchema == null)
            {
                return ds;
            }

            foreach (ToolDefinition t in p.LabelSchema.Tools)
            {
                if (!ds.Contains(t.Kind))
                {
                    ds.Add(t.Kind);
                }
            }

            return ds;
        }

        private async Task<LabelingProject> LayTheoIdAsync(Guid id, CancellationToken ct)
        {
            LabelingProject? duAn = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (duAn == null)
            {
                throw new NotFoundException("Không tìm thấy dự án.");
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

        /// <summary>So FILE video (khong phai so mau — doan cat dung chung file) o trang thai media cho truoc.</summary>
        private Task<int> DemVideoAsync(Guid projectId, string trangThai, CancellationToken ct)
        {
            return _db.Samples
                .Where(s => s.ProjectId == projectId && s.MediaState == trangThai)
                .Select(s => s.StorageKey)
                .Distinct()
                .CountAsync(ct);
        }

        private Task<int> DemMauAsync(Guid projectId, CancellationToken ct)
        {
            return _db.Samples.CountAsync(s => s.ProjectId == projectId, ct);
        }

        private Task<int> DemVangAsync(Guid projectId, Crowd.Project.Domain.Gold.GoldPurpose mucDich, CancellationToken ct)
        {
            return _db.GoldItems.CountAsync(g => g.ProjectId == projectId && g.Purpose == mucDich, ct);
        }

        /// <summary>So cau THAT: mau khong phai cau vang (ca vang bai test lan vang kiem tra) — moi labeler lam het.</summary>
        public Task<int> DemCauThatAsync(Guid projectId, CancellationToken ct)
        {
            return _db.Samples.CountAsync(
                s => s.ProjectId == projectId && !_db.GoldItems.Any(g => g.ProjectId == projectId && g.SampleId == s.Id),
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
                throw new InvalidValueException("mau_khong_thuoc_du_an", "Có mẫu không thuộc dự án này.");
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
                RewardVnd = p.RewardVnd,
                Slots = p.Slots,
                PassThresholdPercent = p.PassThresholdPercent,
                FillSlotsMinPercent = p.FillSlotsMinPercent,
                QuestionCount = p.QuestionCount,
                ToolKinds = LoaiCongCu(p),
                GateUnitPriceVnd = p.GateUnitPriceVnd,
                GateBudgetVnd = laChu ? p.GateBudgetVnd : 0,
                FinishRequestedAt = p.FinishRequestedAt,
                GradingStartedAt = p.GradingStartedAt,
                ClosedAt = p.ClosedAt,
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
                SensitiveContent = p.SensitiveContent,
                ContentWarning = p.ContentWarning,
                NdaRequired = p.NdaRequired,
                NdaVersion = p.NdaVersion,
                RequireVerifiedIdentity = p.RequireVerifiedIdentity,
                ConsentRequired = p.CanDongYCaNhan(),
                Domains = p.Domains,
                Languages = p.Languages,
                DeleteFilesAfterClose = laChu && p.DeleteFilesAfterClose,
                FilesPurgedAt = laChu ? p.FilesPurgedAt : null,

                OwnerId = laChu ? p.OwnerId : null,
                EscrowVnd = laChu && p.PlatformFeePercent > 0 ? p.ChiPhiUocTinhVnd(p.PlatformFeePercent) : null,
                PlatformFeePercent = laChu && p.PlatformFeePercent > 0 ? p.PlatformFeePercent : null,
                StatusReason = laChu ? p.StatusReason : null,
                CreatedAt = laChu ? p.CreatedAt : null,
                UpdatedAt = laChu ? p.UpdatedAt : null,
            };
        }
    }
}
