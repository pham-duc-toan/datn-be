using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Api.Exceptions;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Common;
using Crowd.Annotation.Domain.Members;
using Crowd.Annotation.Domain.Projects;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>
    /// Nhan cua du an (xem), lich su (FL-08), ket qua (FB-22) va xuat file (FB-25).
    ///
    /// Du an lam trung tam (docs 3.18): KHONG con duyet tung nhan, khong con tien o day. Nhan vao ket qua
    /// khi bai cua labeler DAT (project.graded / khieu nai ca bai duoc chap nhan) — xem consumer.
    /// </summary>
    public sealed class AnnotationService
    {
        /// <summary>Mot instance dung lai: tao moi moi lan serialize cham di hang chuc lan.</summary>
        private static readonly JsonSerializerOptions JsonXuat = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

        private readonly AnnotationDbContext _db;
        private readonly IMediaLinks _media;
        private readonly TimeProvider _clock;
        private readonly ISettings _settings;

        public AnnotationService(
            AnnotationDbContext db,
            IMediaLinks media,
            TimeProvider clock,
            ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _media = media;
            _clock = clock;
        }

        // =====================================================================
        // PHAN QUYEN CHIEU NGANG — MOT predicate cho ca danh sach lan tung nhan
        // =====================================================================

        /// <summary>
        /// Nguoi goi co duoc XEM nhan / ket qua cua du an nay khong: admin, hoac chu du an DANG HOAT DONG (theo
        /// ban sao project_members). Khong co dong trong ban sao thi TU CHOI (VD-D-04).
        /// </summary>
        private async Task KiemQuyenXemAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            if (caller.IsAdmin)
            {
                return;
            }

            Guid uid = caller.LayUserId();
            bool duocPhep = await _db.Members.AnyAsync(
                m => m.ProjectId == projectId
                     && m.UserId == uid
                     && m.State == CachedMemberState.Active
                     && m.Role == CachedMemberRole.Owner,
                ct);

            if (!duocPhep)
            {
                // 404 chu khong phai 403: khong tiet lo du an co ton tai hay khong.
                throw new NotFoundException("Không tìm thấy dự án.");
            }
        }

        // =====================================================================
        // DOANH NGHIEP
        // =====================================================================

        /// <summary>Nhan cua du an — loc theo trang thai va / hoac bai lam (xem bai cua mot labeler).</summary>
        public async Task<PagedResponse<AnnotationResponse>> DanhSachDuAnAsync(
            Guid projectId, AnnotationStatus? status, Guid? attemptId, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            await KiemQuyenXemAsync(projectId, caller, ct);

            IQueryable<LabelAnnotation> q = _db.Annotations.AsNoTracking().Where(a => a.ProjectId == projectId);
            if (status.HasValue)
            {
                AnnotationStatus s = status.Value;
                q = q.Where(a => a.Status == s);
            }

            if (attemptId.HasValue)
            {
                Guid bai = attemptId.Value;
                q = q.Where(a => a.AttemptId == bai);
            }

            return await TaoTrangAsync(q.OrderByDescending(a => a.SubmittedAt).ThenByDescending(a => a.Id), page, pageSize, GocXemDuAn(caller), ct);
        }

        public async Task<IReadOnlyList<HistoryEntryResponse>> LichSuAsync(Guid annotationId, Caller caller, CancellationToken ct)
        {
            LabelAnnotation? a = await _db.Annotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == annotationId, ct);
            if (a == null)
            {
                throw new NotFoundException("Không tìm thấy nhãn.");
            }

            bool laCuaMinh = caller.UserId != null && a.LabelerId == caller.UserId;
            if (!laCuaMinh)
            {
                await KiemQuyenXemAsync(a.ProjectId, caller, ct);
            }

            List<HistoryEntryResponse> ds = new List<HistoryEntryResponse>();
            foreach (AnnotationHistoryEntry h in a.History.OrderBy(x => x.At))
            {
                ds.Add(new HistoryEntryResponse { Action = h.Action, ActorId = h.ActorId, Note = h.Note, At = h.At });
            }

            return ds;
        }

        // =====================================================================
        // LABELER
        // =====================================================================

        /// <summary>Nhan cua toi (FL-08) — chi nhan CUA CHINH minh. Ket qua / tien xem theo bai lam (task-svc).</summary>
        public async Task<MyAnnotationsResponse> CuaToiAsync(
            AnnotationStatus? status, Guid? projectId, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            IQueryable<LabelAnnotation> cuaToi = _db.Annotations.AsNoTracking().Where(a => a.LabelerId == uid);
            if (projectId.HasValue)
            {
                Guid p = projectId.Value;
                cuaToi = cuaToi.Where(a => a.ProjectId == p);
            }

            Dictionary<AnnotationStatus, int> dem = await cuaToi
                .GroupBy(a => a.Status)
                .Select(g => new { g.Key, SoLuong = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.SoLuong, ct);

            IQueryable<LabelAnnotation> q = cuaToi;
            if (status.HasValue)
            {
                AnnotationStatus s = status.Value;
                q = q.Where(a => a.Status == s);
            }

            PagedResponse<AnnotationResponse> trang = await TaoTrangAsync(
                q.OrderByDescending(a => a.SubmittedAt), page, pageSize, GocXem.CuaLabeler(uid), ct);

            return new MyAnnotationsResponse
            {
                Annotations = trang,
                SubmittedCount = LayDem(dem, AnnotationStatus.Submitted),
                AcceptedCount = LayDem(dem, AnnotationStatus.Accepted),
                ExcludedCount = LayDem(dem, AnnotationStatus.Excluded),
            };
        }

        // =====================================================================
        // KET QUA & XUAT FILE
        // =====================================================================

        /// <summary>
        /// Ket qua gop theo tung mau tu cac nhan DA CHOT (Accepted): bai labeler dat + nhan cong link. Bai truot
        /// khong vao ket qua.
        /// </summary>
        public async Task<ProjectResultsResponse> KetQuaAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await KiemQuyenXemAsync(projectId, caller, ct);
            ProjectTerms dieuKhoan = await LayDieuKhoanAsync(projectId, ct);
            LabelSchema tapNhan = dieuKhoan.LabelSchema;

            List<LabelAnnotation> daDuyet = await _db.Annotations.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.Status == AnnotationStatus.Accepted)
                .ToListAsync(ct);

            Dictionary<Guid, AnnotationSample> mauGoc = await _db.Samples.AsNoTracking()
                .Where(m => m.ProjectId == projectId).ToDictionaryAsync(m => m.SampleId, ct);

            Dictionary<Guid, SampleMetadata> thongTinMau = new Dictionary<Guid, SampleMetadata>();
            foreach (KeyValuePair<Guid, AnnotationSample> m in mauGoc)
            {
                thongTinMau[m.Key] = SampleMetadata.Tu(m.Value.Metadata);
            }

            KeyValuePair<IReadOnlyList<SampleResult>, int[]> coDem = await ChotCoBoNhoDemAsync(projectId, tapNhan, daDuyet, thongTinMau, ct);
            IReadOnlyList<SampleResult> ketQua = coDem.Key;

            List<SampleResultResponse> mau = new List<SampleResultResponse>();
            foreach (SampleResult r in ketQua)
            {
                AnnotationSample? goc;
                mauGoc.TryGetValue(r.SampleId, out goc);

                mau.Add(new SampleResultResponse
                {
                    SampleId = r.SampleId,
                    StorageKey = goc == null ? null : goc.StorageKey,
                    SampleContent = goc == null ? null : goc.Content,
                    SampleMetadata = goc == null ? SampleMetadata.Rong.ToRawJson() : goc.Metadata,
                    ApprovedCount = r.ApprovedCount,
                    Disputed = r.Disputed,
                    Tools = RawJson.Tu(r.Tools),
                    Labels = r.Approved.Select(a => RawJson.Tu(a.Payload.DataJson)).ToList(),
                    ConsensusStatus = r.Disputed ? "disputed" : "agreed",
                    ConsensusFinal = KetQuaGop(r.Tools),
                });
            }

            SortedDictionary<string, SortedDictionary<string, int>> phanBo = ResultAggregator.PhanBo(tapNhan, ketQua);
            return new ProjectResultsResponse
            {
                ProjectId = projectId,
                Modality = tapNhan.Modality,
                LabelSchema = tapNhan.ToRawJson(),
                SampleCount = mau.Count,
                DisputedCount = mau.Count(x => x.Disputed),
                LabelDistribution = phanBo,
                ImbalanceWarnings = CanhBaoLech(tapNhan, phanBo),
                Samples = mau,
                CacheHits = coDem.Value[0],
                CacheMisses = coDem.Value[1],
            };
        }

        /// <summary>
        /// Chot ket qua tung mau, DUNG LAI ket qua da tinh khi dau van tay dau vao khong doi (KetQuaMauDem).
        /// Tra (ket qua theo thu tu sampleId, [so mau dung lai, so mau tinh moi]).
        /// </summary>
        private async Task<KeyValuePair<IReadOnlyList<SampleResult>, int[]>> ChotCoBoNhoDemAsync(
            Guid projectId,
            LabelSchema tapNhan,
            List<LabelAnnotation> daDuyet,
            Dictionary<Guid, SampleMetadata> thongTinMau,
            CancellationToken ct)
        {
            NguongKhop nguong = NguongKhopTuSetting.Doc(_settings);
            double f1 = _settings.SoThuc(SettingKeys.QualityConsensusMinF1);

            // Phan chung cua dau van tay: tap nhan + moi nguong + F1 toi thieu.
            StringBuilder chung = new StringBuilder(tapNhan.ToRawJson().Json);
            foreach (string khoa in KhoaNguong)
            {
                chung.Append('|').Append(_settings.SoThuc(khoa).ToString("R", CultureInfo.InvariantCulture));
            }

            chung.Append('|').Append(f1.ToString("R", CultureInfo.InvariantCulture));
            string dauChung = chung.ToString();

            Dictionary<Guid, KetQuaMauDem> dem = await _db.KetQuaMauDems.AsNoTracking()
                .Where(k => k.ProjectId == projectId)
                .ToDictionaryAsync(k => k.SampleId, ct);

            List<SampleResult> dungLai = new List<SampleResult>();
            List<LabelAnnotation> canTinh = new List<LabelAnnotation>();
            Dictionary<Guid, string> dauMoi = new Dictionary<Guid, string>();
            foreach (IGrouping<Guid, LabelAnnotation> nhom in daDuyet.GroupBy(a => a.SampleId))
            {
                List<LabelAnnotation> ds = nhom.OrderBy(a => a.SubmittedAt).ToList();
                string dau = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                    dauChung + "|" + string.Join(",", ds.Select(a => a.Id.ToString("N")).OrderBy(s => s, StringComparer.Ordinal)))));
                KetQuaMauDem? cu;
                JsonObject? tools = null;
                if (dem.TryGetValue(nhom.Key, out cu) && cu.Fingerprint == dau)
                {
                    tools = JsonNode.Parse(cu.ToolsJson) as JsonObject;
                }

                if (tools != null)
                {
                    dungLai.Add(new SampleResult(nhom.Key, ds.Count, tools, ds));
                }
                else
                {
                    canTinh.AddRange(ds);
                    dauMoi[nhom.Key] = dau;
                }
            }

            IReadOnlyList<SampleResult> moi = ResultAggregator.Chot(tapNhan, canTinh, nguong, thongTinMau, f1);
            DateTimeOffset luc = _clock.GetUtcNow();
            foreach (SampleResult r in moi)
            {
                string json = r.Tools.ToJsonString();
                string dau = dauMoi[r.SampleId];
                // Hai nguoi xem ket qua cung luc cung ghi: ON CONFLICT → ban ghi sau thang, ca hai deu dung.
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO sample_results (project_id, sample_id, fingerprint, tools, computed_at)
                    VALUES ({projectId}, {r.SampleId}, {dau}, {json}::jsonb, {luc})
                    ON CONFLICT (project_id, sample_id) DO UPDATE
                    SET fingerprint = EXCLUDED.fingerprint, tools = EXCLUDED.tools, computed_at = EXCLUDED.computed_at", ct);
            }

            // Mau khong con nhan da duyet nao (tranh chap doanh nghiep thang...) → bo dong cu.
            List<Guid> conNhan = daDuyet.Select(a => a.SampleId).Distinct().ToList();
            List<Guid> thua = dem.Keys.Where(k => !conNhan.Contains(k)).ToList();
            if (thua.Count > 0)
            {
                await _db.KetQuaMauDems.Where(k => k.ProjectId == projectId && thua.Contains(k.SampleId)).ExecuteDeleteAsync(ct);
            }

            List<SampleResult> tatCa = dungLai.Concat(moi).OrderBy(r => r.SampleId).ToList();
            return new KeyValuePair<IReadOnlyList<SampleResult>, int[]>(tatCa, new int[] { dungLai.Count, moi.Count });
        }

        private static readonly string[] KhoaNguong = new string[]
        {
            SettingKeys.LabelingThresholdBbox, SettingKeys.LabelingThresholdPolygon, SettingKeys.LabelingThresholdSpan,
            SettingKeys.LabelingThresholdTranscription, SettingKeys.LabelingThresholdTemporalSegment, SettingKeys.LabelingThresholdKeypoint,
            SettingKeys.LabelingThresholdPolyline, SettingKeys.LabelingThresholdMask, SettingKeys.LabelingThresholdTrack,
            SettingKeys.LabelingThresholdRelation, SettingKeys.LabelingThresholdRanking,
        };

        /// <summary>Phan bo nhan + canh bao lech (FB-24) — nhu ket qua nhung khong kem tung mau.</summary>
        public async Task<DistributionResponse> PhanBoAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            ProjectResultsResponse kq = await KetQuaAsync(projectId, caller, ct);
            return new DistributionResponse
            {
                ProjectId = projectId,
                SampleCount = kq.SampleCount,
                LabelDistribution = kq.LabelDistribution,
                ImbalanceWarnings = kq.ImbalanceWarnings,
                ThresholdPercent = _settings.SoNguyen(SettingKeys.AnnotationImbalanceThresholdPercent),
            };
        }

        private List<ImbalanceWarningDto> CanhBaoLech(LabelSchema tapNhan, SortedDictionary<string, SortedDictionary<string, int>> phanBo)
        {
            return ResultAggregator.CanhBaoLech(
                    tapNhan,
                    phanBo,
                    _settings.SoNguyen(SettingKeys.AnnotationImbalanceThresholdPercent),
                    _settings.SoNguyen(SettingKeys.AnnotationImbalanceMinLabels))
                .Select(c => new ImbalanceWarningDto
                {
                    Code = c.Code,
                    Tool = c.Tool,
                    Label = c.Label,
                    Count = c.Count,
                    Total = c.Total,
                    Percent = c.Percent,
                })
                .ToList();
        }

        /// <summary>
        /// Xuat ket qua (FB-25): json (mac dinh), csv, coco / yolo / voc / yolo-seg / yolo-obb / coco-keypoints (anh),
        /// conll (van ban co span), mot (video co track).
        /// </summary>
        public async Task<ExportFile> XuatAsync(
            Guid projectId, string? dinhDang, Caller caller, CancellationToken ct)
        {
            ProjectResultsResponse kq = await KetQuaAsync(projectId, caller, ct);
            LabelSchema tapNhan = LabelSchema.Doc(kq.LabelSchema);
            string ten = "ket-qua-" + projectId.ToString();

            if (string.Equals(dinhDang, "csv", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(ResultExporter.Csv(kq, tapNhan), "text/csv; charset=utf-8", ten + ".csv");
            }

            if (string.Equals(dinhDang, "coco", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(ResultExporter.Coco(kq, tapNhan), "application/json", ten + ".coco.json");
            }

            if (string.Equals(dinhDang, "yolo", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(KhungAnhExporter.Yolo(kq, tapNhan), "application/zip", ten + ".yolo.zip");
            }

            if (string.Equals(dinhDang, "voc", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(KhungAnhExporter.PascalVoc(kq, tapNhan), "application/zip", ten + ".voc.zip");
            }

            if (string.Equals(dinhDang, "yolo-seg", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(DinhDangMoRongExporter.YoloSeg(kq, tapNhan), "application/zip", ten + ".yolo-seg.zip");
            }

            if (string.Equals(dinhDang, "yolo-obb", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(DinhDangMoRongExporter.YoloObb(kq, tapNhan), "application/zip", ten + ".yolo-obb.zip");
            }

            if (string.Equals(dinhDang, "coco-keypoints", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(DinhDangMoRongExporter.CocoKeypoints(kq, tapNhan), "application/json", ten + ".coco-keypoints.json");
            }

            if (string.Equals(dinhDang, "conll", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(DinhDangMoRongExporter.Conll(kq, tapNhan), "text/plain; charset=utf-8", ten + ".conll");
            }

            if (string.Equals(dinhDang, "mot", StringComparison.OrdinalIgnoreCase))
            {
                return new ExportFile(DinhDangMoRongExporter.Mot(kq, tapNhan), "application/zip", ten + ".mot.zip");
            }

            if (dinhDang != null && !string.Equals(dinhDang, "json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidValueException("dinh_dang_chua_ho_tro", "Hỗ trợ json, csv, coco, yolo, voc, yolo-seg, yolo-obb, coco-keypoints, conll, mot.");
            }

            return new ExportFile(JsonSerializer.SerializeToUtf8Bytes(kq, JsonXuat), "application/json", ten + ".json");
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        /// <summary>{cong cu: ket qua gop} tu ket qua tung cong cu (bo cong cu tranh chap / khong gop).</summary>
        private static RawJson KetQuaGop(JsonObject tools)
        {
            JsonObject kq = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> t in tools)
            {
                JsonObject? o = t.Value as JsonObject;
                if (o == null)
                {
                    continue;
                }

                JsonNode? cuoi = o["final"];
                if (cuoi != null)
                {
                    kq[t.Key] = cuoi.DeepClone();
                }
            }

            return RawJson.Tu(kq.ToJsonString());
        }

        private async Task<ProjectTerms> LayDieuKhoanAsync(Guid projectId, CancellationToken ct)
        {
            ProjectTerms? t = await _db.ProjectTerms.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (t == null)
            {
                throw new RuleViolationException(
                    "chua_co_dieu_khoan",
                    "Chưa đồng bộ xong cấu hình của dự án. Thử lại sau ít giây.");
            }

            return t;
        }

        private async Task<PagedResponse<AnnotationResponse>> TaoTrangAsync(
            IQueryable<LabelAnnotation> q, int page, int pageSize, GocXem goc, CancellationToken ct)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            int tong = await q.CountAsync(ct);
            List<LabelAnnotation> ds = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            List<AnnotationResponse> items = new List<AnnotationResponse>();
            Dictionary<Guid, AnnotationSample> mau = await MauDaGanStore.NapAsync(_db, ds.Select(a => a.SampleId), ct);
            foreach (LabelAnnotation a in ds)
            {
                AnnotationSample? m;
                mau.TryGetValue(a.SampleId, out m);
                items.Add(TaoResponse(a, goc, m));
            }

            return new PagedResponse<AnnotationResponse> { Items = items, Page = page, PageSize = pageSize, Total = tong };
        }

        /// <summary>Ai dang xem nhan cua du an: admin / chu du an xem ban goc (FP-02/03).</summary>
        private static GocXem GocXemDuAn(Caller caller)
        {
            Guid uid = caller.LayUserId();
            return caller.IsAdmin ? GocXem.CuaAdmin(uid) : new GocXem(NguoiXem.ChuDuAn(uid), CheDoXem.Goc);
        }

        private AnnotationResponse TaoResponse(LabelAnnotation a, GocXem goc, AnnotationSample? thongTinMau)
        {
            string? fileUrl = null;
            if (thongTinMau != null && thongTinMau.StorageKey != null)
            {
                MauCanXem mau = new MauCanXem
                {
                    SampleId = a.SampleId,
                    ProjectId = a.ProjectId,
                    StorageKey = thongTinMau.StorageKey,
                    Modality = a.Payload.TaskType,
                };
                fileUrl = _media.TaoLink(mau, goc.Nguoi, goc.CheDo);
            }

            return new AnnotationResponse
            {
                Id = a.Id,
                ProjectId = a.ProjectId,
                TaskId = a.TaskId == Guid.Empty ? null : a.TaskId,
                SampleId = a.SampleId,
                Source = a.Source,
                FileUrl = fileUrl,
                SampleContent = thongTinMau == null ? null : thongTinMau.Content,
                SampleMetadata = thongTinMau == null ? SampleMetadata.Rong.ToRawJson() : thongTinMau.Metadata,
                LabelerId = a.LabelerId,
                Payload = a.Payload,
                Status = a.Status,
                SubmittedAt = a.SubmittedAt,
                AttemptId = a.AttemptId,
                DecidedAt = a.DecidedAt,
            };
        }

        private static int LayDem(Dictionary<AnnotationStatus, int> dem, AnnotationStatus s)
        {
            int n;
            if (dem.TryGetValue(s, out n))
            {
                return n;
            }

            return 0;
        }
    }

    /// <summary>Mot file xuat ra: noi dung + kieu + ten goi y khi tai ve.</summary>
    public sealed class ExportFile
    {
        public ExportFile(byte[] noiDung, string contentType, string tenFile)
        {
            NoiDung = noiDung;
            ContentType = contentType;
            TenFile = tenFile;
        }

        public byte[] NoiDung { get; }

        public string ContentType { get; }

        public string TenFile { get; }
    }

    /// <summary>Nguoi xem + ban duoc xem (goc / co watermark) cua file mau trong response.</summary>
    internal sealed class GocXem
    {
        public GocXem(NguoiXem nguoi, CheDoXem cheDo)
        {
            Nguoi = nguoi;
            CheDo = cheDo;
        }

        public NguoiXem Nguoi { get; }

        public CheDoXem CheDo { get; }

        public static GocXem CuaLabeler(Guid userId)
        {
            return new GocXem(NguoiXem.Labeler(userId), CheDoXem.Watermark);
        }

        public static GocXem CuaAdmin(Guid userId)
        {
            return new GocXem(NguoiXem.Admin(userId), CheDoXem.Goc);
        }
    }
}
