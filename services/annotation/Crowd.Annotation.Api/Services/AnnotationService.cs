using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Annotation;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>
    /// Duyet nhan (FB-21), khieu nai (FL-09), lich su (FL-08), ket qua (FB-22)
    /// va xuat file (FB-25).
    ///
    /// Duyet nhan la cho SINH RA TIEN: annotation.approved → ledger chi tra. Nen
    /// moi lan duyet la MOT SaveChanges gom ca doi trang thai lan event (VD-D-01),
    /// va khoa lac quan xmin chan hai reviewer cung xu mot nhan (VD-D-11).
    /// </summary>
    public sealed class AnnotationService
    {
        /// <summary>Mot instance dung lai: tao moi moi lan serialize cham di hang chuc lan.</summary>
        private static readonly JsonSerializerOptions JsonXuat = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

        private readonly AnnotationDbContext _db;
        private readonly AnnotationEventPublisher _events;
        private readonly IObjectStorage _storage;
        private readonly TimeProvider _clock;

        public AnnotationService(
            AnnotationDbContext db,
            AnnotationEventPublisher events,
            IObjectStorage storage,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _events = events;
            _storage = storage;
            _clock = clock;
        }

        // =====================================================================
        // PHAN QUYEN CHIEU NGANG — MOT predicate cho ca danh sach lan tung nhan
        // =====================================================================

        /// <summary>
        /// Nguoi goi co duoc DUYET / XEM nhan cua du an nay khong: admin, hoac chu
        /// du an / reviewer DANG HOAT DONG (theo ban sao project_members). Khong co
        /// dong trong ban sao thi TU CHOI (VD-D-04).
        /// </summary>
        private async Task KiemQuyenDuyetAsync(Guid projectId, Caller caller, CancellationToken ct)
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
                     && (m.Role == CachedMemberRole.Owner || m.Role == CachedMemberRole.Reviewer),
                ct);

            if (!duocPhep)
            {
                // 404 chu khong phai 403: khong tiet lo du an co ton tai hay khong.
                throw new NotFoundException("Khong tim thay du an.");
            }
        }

        // =====================================================================
        // DOANH NGHIEP / REVIEWER
        // =====================================================================

        public async Task<PagedResponse<AnnotationResponse>> DanhSachDuAnAsync(
            Guid projectId, AnnotationStatus? status, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            await KiemQuyenDuyetAsync(projectId, caller, ct);

            IQueryable<LabelAnnotation> q = _db.Annotations.AsNoTracking().Where(a => a.ProjectId == projectId);
            if (status.HasValue)
            {
                AnnotationStatus s = status.Value;
                q = q.Where(a => a.Status == s);
            }

            return await TaoTrangAsync(q.OrderBy(a => a.SubmittedAt), page, pageSize, DateTimeOffset.MinValue, ct);
        }

        public async Task<AnnotationResponse> DuyetAsync(Guid annotationId, Caller caller, CancellationToken ct)
        {
            LabelAnnotation a = await LayDeDuyetAsync(annotationId, caller, ct);
            ProjectTerms dieuKhoan = await LayDieuKhoanAsync(a.ProjectId, ct);

            a.Duyet(caller.LayUserId(), _clock.GetUtcNow());
            PhatDaDuyet(a, dieuKhoan, caller);

            await _db.SaveChangesAsync(ct);
            return await TaoResponseAsync(a, _clock.GetUtcNow());
        }

        public async Task<AnnotationResponse> TuChoiAsync(Guid annotationId, string? lyDo, Caller caller, CancellationToken ct)
        {
            LabelAnnotation a = await LayDeDuyetAsync(annotationId, caller, ct);

            a.TuChoi(caller.LayUserId(), lyDo ?? string.Empty, _clock.GetUtcNow());

            _events.Phat(caller, new AnnotationRejected
            {
                AnnotationId = a.Id,
                TaskId = a.TaskId,
                ProjectId = a.ProjectId,
                LabelerId = a.LabelerId,
                Reason = a.RejectReason!,
                IsFinal = false,
            });

            await _db.SaveChangesAsync(ct);
            return await TaoResponseAsync(a, _clock.GetUtcNow());
        }

        public async Task<IReadOnlyList<HistoryEntryResponse>> LichSuAsync(Guid annotationId, Caller caller, CancellationToken ct)
        {
            LabelAnnotation? a = await _db.Annotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == annotationId, ct);
            if (a == null)
            {
                throw new NotFoundException("Khong tim thay nhan.");
            }

            bool laCuaMinh = caller.UserId != null && a.LabelerId == caller.UserId;
            if (!laCuaMinh)
            {
                await KiemQuyenDuyetAsync(a.ProjectId, caller, ct);
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

        /// <summary>Lich su cong viec + ti le duyet (FL-08). Chi nhan CUA CHINH minh.</summary>
        public async Task<MyAnnotationsResponse> CuaToiAsync(
            AnnotationStatus? status, int page, int pageSize, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            IQueryable<LabelAnnotation> cuaToi = _db.Annotations.AsNoTracking().Where(a => a.LabelerId == uid);

            Dictionary<AnnotationStatus, int> dem = await cuaToi
                .GroupBy(a => a.Status)
                .Select(g => new { g.Key, SoLuong = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.SoLuong, ct);

            int daDuyet = LayDem(dem, AnnotationStatus.Approved);
            int tuChoi = LayDem(dem, AnnotationStatus.Rejected);

            IQueryable<LabelAnnotation> q = cuaToi;
            if (status.HasValue)
            {
                AnnotationStatus s = status.Value;
                q = q.Where(a => a.Status == s);
            }

            PagedResponse<AnnotationResponse> trang = await TaoTrangAsync(
                q.OrderByDescending(a => a.SubmittedAt), page, pageSize, _clock.GetUtcNow(), ct);

            int? tiLe = null;
            if (daDuyet + tuChoi > 0)
            {
                tiLe = daDuyet * 100 / (daDuyet + tuChoi);
            }

            return new MyAnnotationsResponse
            {
                Annotations = trang,
                PendingCount = LayDem(dem, AnnotationStatus.PendingReview),
                ApprovedCount = daDuyet,
                RejectedCount = tuChoi,
                AppealedCount = LayDem(dem, AnnotationStatus.Appealed),
                ApprovalRatePercent = tiLe,
            };
        }

        public async Task<AnnotationResponse> KhieuNaiAsync(Guid annotationId, string? noiDung, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            LabelAnnotation? a = await _db.Annotations.FirstOrDefaultAsync(x => x.Id == annotationId, ct);

            // Nhan cua nguoi khac → 404, khong tiet lo no ton tai (BOLA).
            if (a == null || a.LabelerId != uid)
            {
                throw new NotFoundException("Khong tim thay nhan.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            a.KhieuNai(uid, noiDung ?? string.Empty, bayGio);

            _events.Phat(caller, new AppealOpened
            {
                AnnotationId = a.Id,
                ProjectId = a.ProjectId,
                LabelerId = uid,
                Message = a.AppealMessage!,
            });

            await _db.SaveChangesAsync(ct);
            return await TaoResponseAsync(a, bayGio);
        }

        // =====================================================================
        // ADMIN — phan xu khieu nai (FM-06)
        // =====================================================================

        public async Task<PagedResponse<AnnotationResponse>> DanhSachKhieuNaiAsync(int page, int pageSize, CancellationToken ct)
        {
            IQueryable<LabelAnnotation> q = _db.Annotations.AsNoTracking()
                .Where(a => a.Status == AnnotationStatus.Appealed)
                .OrderBy(a => a.AppealedAt);

            return await TaoTrangAsync(q, page, pageSize, DateTimeOffset.MinValue, ct);
        }

        /// <summary>
        /// Tam dat o annotation-svc cho toi khi co admin-svc (P6); luc do admin-svc
        /// phat dispute.resolved va annotation-svc nghe — goi cung phuong thuc domain.
        /// </summary>
        public async Task<AnnotationResponse> XuLyKhieuNaiAsync(
            Guid annotationId, ResolveAppealRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelAnnotation? a = await _db.Annotations.FirstOrDefaultAsync(x => x.Id == annotationId, ct);
            if (a == null)
            {
                throw new NotFoundException("Khong tim thay nhan.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            bool chapNhan = a.XuLyKhieuNai(caller.LayUserId(), body.Accept, body.Note, bayGio);

            if (chapNhan)
            {
                // Khieu nai thang: labeler duoc tra tien nhu duoc duyet tu dau.
                PhatDaDuyet(a, await LayDieuKhoanAsync(a.ProjectId, ct), caller);
            }
            else
            {
                _events.Phat(caller, new AnnotationRejected
                {
                    AnnotationId = a.Id,
                    TaskId = a.TaskId,
                    ProjectId = a.ProjectId,
                    LabelerId = a.LabelerId,
                    Reason = body.Note ?? a.RejectReason ?? "Khieu nai bi bac",
                    IsFinal = true,
                });
            }

            await _db.SaveChangesAsync(ct);
            return await TaoResponseAsync(a, bayGio);
        }

        // =====================================================================
        // KET QUA & XUAT FILE
        // =====================================================================

        public async Task<ProjectResultsResponse> KetQuaAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await KiemQuyenDuyetAsync(projectId, caller, ct);

            List<LabelAnnotation> daDuyet = await _db.Annotations.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.Status == AnnotationStatus.Approved)
                .ToListAsync(ct);

            IReadOnlyList<SampleResult> ketQua = ResultAggregator.Chot(daDuyet);

            SortedDictionary<string, int> phanBo = new SortedDictionary<string, int>(StringComparer.Ordinal);
            List<SampleResultResponse> mau = new List<SampleResultResponse>();

            foreach (SampleResult r in ketQua)
            {
                foreach (string lop in r.FinalLabels)
                {
                    int cu;
                    phanBo.TryGetValue(lop, out cu);
                    phanBo[lop] = cu + 1;
                }

                mau.Add(new SampleResultResponse
                {
                    SampleId = r.SampleId,
                    FinalLabels = r.FinalLabels,
                    Votes = r.Votes,
                    ApprovedCount = r.ApprovedCount,
                    Disputed = r.Disputed,
                });
            }

            return new ProjectResultsResponse
            {
                ProjectId = projectId,
                SampleCount = mau.Count,
                DisputedCount = mau.Count(x => x.Disputed),
                LabelDistribution = phanBo,
                Samples = mau,
            };
        }

        /// <summary>Xuat ket qua (FB-25): "json" hoac "csv".</summary>
        public async Task<ExportFile> XuatAsync(
            Guid projectId, string? dinhDang, Caller caller, CancellationToken ct)
        {
            ProjectResultsResponse kq = await KetQuaAsync(projectId, caller, ct);
            string ten = "ket-qua-" + projectId.ToString();

            if (string.Equals(dinhDang, "csv", StringComparison.OrdinalIgnoreCase))
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("sample_id,final_labels,approved_count,disputed\n");

                foreach (SampleResultResponse s in kq.Samples)
                {
                    sb.Append(s.SampleId).Append(',')
                      .Append(OCsv(string.Join(";", s.FinalLabels))).Append(',')
                      .Append(s.ApprovedCount).Append(',')
                      .Append(s.Disputed ? "true" : "false").Append('\n');
                }

                // BOM UTF-8: Excel mo file tieng Viet khong bi loi font.
                byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
                byte[] than = Encoding.UTF8.GetBytes(sb.ToString());
                byte[] tatCa = new byte[bom.Length + than.Length];
                Buffer.BlockCopy(bom, 0, tatCa, 0, bom.Length);
                Buffer.BlockCopy(than, 0, tatCa, bom.Length, than.Length);

                return new ExportFile(tatCa, "text/csv; charset=utf-8", ten + ".csv");
            }

            if (dinhDang != null && !string.Equals(dinhDang, "json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidValueException(
                    "dinh_dang_chua_ho_tro",
                    "Hien ho tro json, csv. COCO/YOLO/Pascal VOC danh cho bounding box (P6).");
            }

            return new ExportFile(JsonSerializer.SerializeToUtf8Bytes(kq, JsonXuat), "application/json", ten + ".json");
        }

        /// <summary>
        /// Mot o CSV an toan:
        ///   - boc ngoac kep neu co dau phay/ngoac kep/xuong dong;
        ///   - CHONG CSV INJECTION: o bat dau bang = + - @ se bi Excel chay nhu
        ///     CONG THUC (vd ten lop "=HYPERLINK(...)" do doanh nghiep khac dat).
        ///     Chen dau ' phia truoc de Excel coi la chu.
        /// </summary>
        private static string OCsv(string giaTri)
        {
            string v = giaTri;

            if (v.Length > 0 && (v[0] == '=' || v[0] == '+' || v[0] == '-' || v[0] == '@'))
            {
                v = "'" + v;
            }

            if (v.Contains(',', StringComparison.Ordinal) || v.Contains('"', StringComparison.Ordinal) || v.Contains('\n', StringComparison.Ordinal))
            {
                v = "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            }

            return v;
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        private async Task<LabelAnnotation> LayDeDuyetAsync(Guid annotationId, Caller caller, CancellationToken ct)
        {
            LabelAnnotation? a = await _db.Annotations.FirstOrDefaultAsync(x => x.Id == annotationId, ct);
            if (a == null)
            {
                throw new NotFoundException("Khong tim thay nhan.");
            }

            await KiemQuyenDuyetAsync(a.ProjectId, caller, ct);
            return a;
        }

        /// <summary>
        /// Don gia + phi TU BAN SAO. Chua co (project.published chua toi) thi TU
        /// CHOI duyet — khong bao gio phat annotation.approved voi so tien doan mo.
        /// </summary>
        private async Task<ProjectTerms> LayDieuKhoanAsync(Guid projectId, CancellationToken ct)
        {
            ProjectTerms? t = await _db.ProjectTerms.FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);
            if (t == null)
            {
                throw new RuleViolationException(
                    "chua_co_dieu_khoan",
                    "Chua dong bo xong don gia cua du an. Thu lai sau it giay.");
            }

            return t;
        }

        private void PhatDaDuyet(LabelAnnotation a, ProjectTerms t, Caller caller)
        {
            _events.Phat(caller, new AnnotationApproved
            {
                AnnotationId = a.Id,
                TaskId = a.TaskId,
                ProjectId = a.ProjectId,
                LabelerId = a.LabelerId,
                AmountVnd = t.UnitPriceVnd,
                PlatformFeeVnd = t.PlatformFeeVnd,
                Source = AnnotationSource.Professional,
            });
        }

        private async Task<PagedResponse<AnnotationResponse>> TaoTrangAsync(
            IQueryable<LabelAnnotation> q, int page, int pageSize, DateTimeOffset bayGio, CancellationToken ct)
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
            foreach (LabelAnnotation a in ds)
            {
                items.Add(await TaoResponseAsync(a, bayGio));
            }

            return new PagedResponse<AnnotationResponse> { Items = items, Page = page, PageSize = pageSize, Total = tong };
        }

        private async Task<AnnotationResponse> TaoResponseAsync(LabelAnnotation a, DateTimeOffset bayGio)
        {
            return new AnnotationResponse
            {
                Id = a.Id,
                ProjectId = a.ProjectId,
                TaskId = a.TaskId,
                SampleId = a.SampleId,
                ImageUrl = await _storage.TaoLinkXemAsync(a.StorageKey),
                LabelerId = a.LabelerId,
                Payload = a.Payload,
                Status = a.Status,
                SubmittedAt = a.SubmittedAt,
                ReviewedAt = a.ReviewedAt,
                RejectReason = a.RejectReason,
                AppealMessage = a.AppealMessage,
                CanAppeal = a.ConKhieuNaiDuoc(bayGio),
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
}
