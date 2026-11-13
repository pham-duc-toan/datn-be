using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Nhan cua du an (xem), lich su (FL-08), ket qua (FB-22) va xuat file (FB-25).
    ///
    /// Du an lam trung tam (docs 3.18): KHONG con duyet tung nhan, khong con tien o day. Nhan vao ket qua
    /// khi bai cua labeler DAT (project.graded / khieu nai ca bai duoc chap nhan) — xem ThuBaiService.
    ///
    /// Truoc day la annotation-svc, giu ban sao du an (project_terms), mau (annotation_samples) va thanh vien.
    /// Nay doc thang projects / samples / project_members cung database.
    /// </summary>
    public sealed class AnnotationService
    {
        /// <summary>Mot instance dung lai: tao moi moi lan serialize cham di hang chuc lan.</summary>
        private static readonly JsonSerializerOptions JsonXuat = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly IMediaLinks _media;
        private readonly TimeProvider _clock;
        private readonly ISettings _settings;
        private readonly GopNhanClient _gop;

        public AnnotationService(
            ProjectDbContext db,
            ProjectAccessService access,
            IMediaLinks media,
            TimeProvider clock,
            ISettings settings,
            GopNhanClient gop)
        {
            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            _access = access;

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

            if (gop == null)
            {
                throw new ArgumentNullException(nameof(gop));
            }

            _db = db;
            _media = media;
            _clock = clock;
            _gop = gop;
        }

        // =====================================================================
        // PHAN QUYEN CHIEU NGANG — MOT predicate cho ca danh sach lan tung nhan
        // =====================================================================

        /// <summary>
        /// Nguoi goi co duoc XEM nhan / ket qua cua du an nay khong: admin, hoac chu du an. Cung luat voi moi
        /// API quan ly du an (ProjectAccessService) — khong con ban sao thanh vien rieng.
        /// </summary>
        private async Task<LabelingProject> KiemQuyenXemAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            return await _access.LayChoChuAsync(projectId, caller, ct);
        }

        // =====================================================================
        // DOANH NGHIEP
        // =====================================================================

        /// <summary>Nhan cua du an — loc theo trang thai va / hoac bai lam (xem bai cua mot labeler).</summary>
        public async Task<PagedResponse<AnnotationResponse>> DanhSachDuAnAsync(
            Guid projectId, AnnotationStatus? status, Guid? attemptId, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            await KiemQuyenXemAsync(projectId, caller, ct);

            IQueryable<LabelAnnotation> nhan = _db.Annotations.AsNoTracking().Where(a => a.ProjectId == projectId);
            if (attemptId.HasValue)
            {
                Guid bai = attemptId.Value;
                nhan = nhan.Where(a => a.AttemptId == bai);
            }

            IQueryable<NhanKemBai> q = KemBai(nhan);
            if (status.HasValue)
            {
                AnnotationStatus s = status.Value;
                q = q.Where(x => x.TrangThai == s);
            }

            return await TaoTrangAsync(SapXepMoiNhat(q), page, pageSize, GocXemDuAn(caller), ct);
        }

        /// <summary>
        /// GET /annotations/attempts/{attemptId}/history — vong doi cua BAI chua nhan (nop, cham dat / truot, khieu nai,
        /// phan xu), dung lai tu chinh cac cot cua attempts (LichSuBai — khong co bang lich su rieng, kien truc 3.31).
        /// Moi nhan cua bai co chung lich su nay. Chu bai, chu du an hoac admin.
        /// </summary>
        public async Task<IReadOnlyList<HistoryEntryResponse>> LichSuAsync(Guid attemptId, Caller caller, CancellationToken ct)
        {
            Attempt? a = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attemptId, ct);
            if (a == null)
            {
                throw new NotFoundException("Không tìm thấy bài làm.");
            }

            bool laCuaMinh = caller.UserId != null && a.LabelerId == caller.UserId;
            if (!laCuaMinh)
            {
                await KiemQuyenXemAsync(a.ProjectId, caller, ct);
            }

            List<HistoryEntryResponse> ds = new List<HistoryEntryResponse>();
            foreach (SuKienBai h in LichSuBai.Tu(a))
            {
                ds.Add(new HistoryEntryResponse { Action = h.Action, ActorId = h.ActorId, Note = h.Note, At = h.At });
            }

            return ds;
        }

        // =====================================================================
        // LABELER
        // =====================================================================

        /// <summary>
        /// Nhan cua toi (FL-08) — chi nhan CUA CHINH minh. Ket qua / tien xem theo bai lam.
        ///
        /// Doc QUA attempts (nho, chi muc theo labeler) roi loc annotations theo (du an, bai): Postgres chi mo
        /// cac phan vung cua nhung du an labeler da lam, thay vi quet chi muc labeler cua ca 64 phan vung.
        /// </summary>
        public async Task<MyAnnotationsResponse> CuaToiAsync(
            AnnotationStatus? status, Guid? projectId, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            IQueryable<Attempt> baiCuaToi = _db.Attempts.AsNoTracking()
                .Where(a => a.LabelerId == uid && a.State == AttemptState.Submitted);
            if (projectId.HasValue)
            {
                Guid p = projectId.Value;
                baiCuaToi = baiCuaToi.Where(a => a.ProjectId == p);
            }

            List<Guid> duAnIds = await baiCuaToi.Select(a => a.ProjectId).Distinct().ToListAsync(ct);
            List<Guid> baiIds = await baiCuaToi.Select(a => a.Id).ToListAsync(ct);
            IQueryable<NhanKemBai> cuaToi = KemBai(_db.Annotations.AsNoTracking()
                .Where(a => duAnIds.Contains(a.ProjectId) && baiIds.Contains(a.AttemptId) && a.LabelerId == uid));

            int daNop = await cuaToi.CountAsync(x => x.TrangThai == AnnotationStatus.Submitted, ct);
            int chapNhan = await cuaToi.CountAsync(x => x.TrangThai == AnnotationStatus.Accepted, ct);
            int biLoai = await cuaToi.CountAsync(x => x.TrangThai == AnnotationStatus.Excluded, ct);

            IQueryable<NhanKemBai> q = cuaToi;
            if (status.HasValue)
            {
                AnnotationStatus s = status.Value;
                q = q.Where(x => x.TrangThai == s);
            }

            PagedResponse<AnnotationResponse> trang = await TaoTrangAsync(SapXepMoiNhat(q), page, pageSize, GocXem.CuaLabeler(uid), ct);

            return new MyAnnotationsResponse
            {
                Annotations = trang,
                SubmittedCount = daNop,
                AcceptedCount = chapNhan,
                ExcludedCount = biLoai,
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
            LabelingProject duAn = await KiemQuyenXemAsync(projectId, caller, ct);
            if (duAn.LabelSchema == null)
            {
                throw new RuleViolationException("chua_co_tap_nhan", "Dự án chưa có tập nhãn.");
            }

            LabelSchema tapNhan = duAn.LabelSchema;

            // Nhan DA CHOT = bai dat / khieu nai duoc chap nhan / cong link (suy ra tu bai). Thu tu: bai nop som
            // truoc, nhan cong link (khong co luc nop) sau cung — on dinh cho dau van tay va thu tu nhan tra ra.
            List<LabelAnnotation> daDuyet = await KemBai(_db.Annotations.AsNoTracking().Where(a => a.ProjectId == projectId))
                .Where(x => x.TrangThai == AnnotationStatus.Accepted)
                .OrderBy(x => x.NopLuc == null)
                .ThenBy(x => x.NopLuc)
                .ThenBy(x => x.Nhan.AttemptId)
                .Select(x => x.Nhan)
                .ToListAsync(ct);

            List<Guid> coNhan = daDuyet.Select(a => a.SampleId).Distinct().ToList();
            Dictionary<Guid, Sample> mauGoc = await _db.Samples.AsNoTracking()
                .Where(m => m.ProjectId == projectId && coNhan.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);

            Dictionary<Guid, SampleMetadata> thongTinMau = new Dictionary<Guid, SampleMetadata>();
            foreach (KeyValuePair<Guid, Sample> m in mauGoc)
            {
                thongTinMau[m.Key] = SampleMetadata.Tu(m.Value.Metadata);
            }

            KeyValuePair<IReadOnlyList<SampleResult>, int[]> coDem = await ChotCoBoNhoDemAsync(projectId, tapNhan, daDuyet, thongTinMau, ct);
            IReadOnlyList<SampleResult> ketQua = coDem.Key;

            List<SampleResultResponse> mau = new List<SampleResultResponse>();
            foreach (SampleResult r in ketQua)
            {
                Sample? goc;
                mauGoc.TryGetValue(r.SampleId, out goc);

                mau.Add(new SampleResultResponse
                {
                    SampleId = r.SampleId,
                    StorageKey = goc == null ? null : goc.StorageKey,
                    SampleContent = goc == null ? null : goc.Content,
                    SampleQuestion = goc == null ? tapNhan.Question : goc.CauHoiHienThi(tapNhan),
                    SampleMetadata = goc == null ? SampleMetadata.Rong.ToRawJson() : goc.Metadata,
                    ApprovedCount = r.ApprovedCount,
                    Disputed = r.Disputed,
                    Tools = RawJson.Tu(r.Tools),
                    Labels = r.Approved.Select(a => RawJson.Tu(a.DataJson)).ToList(),
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
        /// Mau doi (hoac chua co) thi nho quality-svc gop qua gRPC (3.24 — thuat toan gop chi con o quality).
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

            Dictionary<Guid, KetQuaMauDem> dem = await _db.SampleResults.AsNoTracking()
                .Where(k => k.ProjectId == projectId)
                .ToDictionaryAsync(k => k.SampleId, ct);

            List<SampleResult> dungLai = new List<SampleResult>();
            List<MauCanGop> canTinh = new List<MauCanGop>();
            Dictionary<Guid, string> dauMoi = new Dictionary<Guid, string>();
            foreach (IGrouping<Guid, LabelAnnotation> nhom in daDuyet.GroupBy(a => a.SampleId))
            {
                // Nhan da theo thu tu nop (truy van o KetQuaAsync). Trong MOT mau, moi bai mot nhan → attempt_id dinh
                // danh nhan cho dau van tay.
                List<LabelAnnotation> ds = nhom.ToList();
                string dau = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                    dauChung + "|" + string.Join(",", ds.Select(a => a.AttemptId.ToString("N")).OrderBy(s => s, StringComparer.Ordinal)))));
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
                    SampleMetadata? thongTin;
                    thongTinMau.TryGetValue(nhom.Key, out thongTin);
                    canTinh.Add(new MauCanGop(nhom.Key, thongTin, ds));
                    dauMoi[nhom.Key] = dau;
                }
            }

            List<SampleResult> moi = new List<SampleResult>();
            if (canTinh.Count > 0)
            {
                Dictionary<Guid, JsonObject> gop = await _gop.GopAsync(tapNhan, canTinh, nguong, f1, ct);
                foreach (MauCanGop m in canTinh)
                {
                    JsonObject? tools;
                    if (!gop.TryGetValue(m.SampleId, out tools))
                    {
                        throw new InvalidOperationException("quality-svc khong tra ket qua gop cho mau " + m.SampleId + ".");
                    }

                    moi.Add(new SampleResult(m.SampleId, m.Nhan.Count, tools, m.Nhan));
                }
            }

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
                await _db.SampleResults.Where(k => k.ProjectId == projectId && thua.Contains(k.SampleId)).ExecuteDeleteAsync(ct);
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

        /// <summary>
        /// Gan cho moi nhan trang thai / moc thoi gian SUY RA tu bai lam — cung luat voi LabelAnnotation.TrangThai,
        /// viet bang bieu thuc de Postgres tinh (CASE) va loc / dem / sap xep duoc. LEFT JOIN vi nhan cong link
        /// khong thuoc bai lam nao.
        /// </summary>
        private IQueryable<NhanKemBai> KemBai(IQueryable<LabelAnnotation> nhan)
        {
            return from a in nhan
                   join t in _db.Attempts.AsNoTracking() on a.AttemptId equals t.Id into bai
                   from t in bai.DefaultIfEmpty()
                   select new NhanKemBai
                   {
                       Nhan = a,
                       TrangThai = a.Source == LabelSource.LinkGateway
                           ? AnnotationStatus.Accepted
                           : (t == null || t.GradedAt == null
                               ? AnnotationStatus.Submitted
                               : (t.Passed == true || t.AppealState == AppealState.Accepted
                                   ? AnnotationStatus.Accepted
                                   : AnnotationStatus.Excluded)),
                       NopLuc = t == null ? null : t.SubmittedAt,
                       ChotLuc = t == null
                           ? null
                           : (t.AppealState == AppealState.Accepted || t.AppealState == AppealState.Rejected ? t.AppealResolvedAt : t.GradedAt),
                   };
        }

        /// <summary>Moi nhat truoc: bai nop sau truoc, nhan cong link (khong co luc nop) cuoi.</summary>
        private static IQueryable<NhanKemBai> SapXepMoiNhat(IQueryable<NhanKemBai> q)
        {
            return q.OrderBy(x => x.NopLuc == null)
                .ThenByDescending(x => x.NopLuc)
                .ThenByDescending(x => x.Nhan.AttemptId)
                .ThenBy(x => x.Nhan.SampleId);
        }

        private async Task<PagedResponse<AnnotationResponse>> TaoTrangAsync(
            IQueryable<NhanKemBai> q, int page, int pageSize, GocXem goc, CancellationToken ct)
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
            List<NhanKemBai> ds = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            List<AnnotationResponse> items = new List<AnnotationResponse>();
            List<Guid> duAnIds = ds.Select(x => x.Nhan.ProjectId).Distinct().ToList();
            List<Guid> mauIds = ds.Select(x => x.Nhan.SampleId).Distinct().ToList();
            Dictionary<Guid, Sample> mau = await _db.Samples.AsNoTracking()
                .Where(m => duAnIds.Contains(m.ProjectId) && mauIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, ct);

            // Loai du lieu cua nhan = loai du lieu du an (bang annotations khong luu task_type).
            // Kem tap nhan: cau hoi chung cho mau khong co cau rieng.
            var duAn = await _db.Projects.AsNoTracking()
                .Where(p => duAnIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Modality, p.LabelSchema })
                .ToDictionaryAsync(p => p.Id, ct);
            foreach (NhanKemBai x in ds)
            {
                Sample? m;
                mau.TryGetValue(x.Nhan.SampleId, out m);
                var da = duAn[x.Nhan.ProjectId];
                items.Add(TaoResponse(x, da.Modality, goc, m, da.LabelSchema));
            }

            return new PagedResponse<AnnotationResponse> { Items = items, Page = page, PageSize = pageSize, Total = tong };
        }

        /// <summary>Ai dang xem nhan cua du an: admin / chu du an xem ban goc (FP-02/03).</summary>
        private static GocXem GocXemDuAn(Caller caller)
        {
            Guid uid = caller.LayUserId();
            return caller.IsAdmin ? GocXem.CuaAdmin(uid) : new GocXem(NguoiXem.ChuDuAn(uid), CheDoXem.Goc);
        }

        private AnnotationResponse TaoResponse(NhanKemBai x, string modality, GocXem goc, Sample? thongTinMau, LabelSchema? tapNhan)
        {
            LabelAnnotation a = x.Nhan;
            string? fileUrl = null;
            if (thongTinMau != null && thongTinMau.StorageKey != null)
            {
                MauCanXem mau = new MauCanXem
                {
                    SampleId = a.SampleId,
                    ProjectId = a.ProjectId,
                    StorageKey = thongTinMau.StorageKey,
                    Modality = modality,
                };
                fileUrl = _media.TaoLink(mau, goc.Nguoi, goc.CheDo);
            }

            return new AnnotationResponse
            {
                ProjectId = a.ProjectId,
                SampleId = a.SampleId,
                Source = a.Source,
                FileUrl = fileUrl,
                SampleContent = thongTinMau == null ? null : thongTinMau.Content,
                SampleQuestion = thongTinMau == null ? null : thongTinMau.CauHoiHienThi(tapNhan),
                SampleMetadata = thongTinMau == null ? SampleMetadata.Rong.ToRawJson() : thongTinMau.Metadata,
                LabelerId = a.LabelerId,
                Payload = a.LayPayload(modality),
                Status = x.TrangThai,
                SubmittedAt = x.NopLuc,
                AttemptId = a.AttemptId,
                DecidedAt = x.TrangThai == AnnotationStatus.Submitted ? null : x.ChotLuc,
            };
        }
    }

    /// <summary>
    /// Mot nhan kem trang thai va moc thoi gian SUY RA tu bai lam (bang annotations chi luu cau tra loi). Dung
    /// trong truy van EF — cac thuoc tinh co setter de EF dung duoc bieu thuc khoi tao.
    /// </summary>
    internal sealed class NhanKemBai
    {
        public LabelAnnotation Nhan { get; set; } = null!;

        public AnnotationStatus TrangThai { get; set; }

        /// <summary>attempts.submitted_at; null voi nhan cong link.</summary>
        public DateTimeOffset? NopLuc { get; set; }

        /// <summary>Luc phan xu khieu nai neu co, khong thi luc cham.</summary>
        public DateTimeOffset? ChotLuc { get; set; }
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
