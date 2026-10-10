using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Api.Dtos;
using Crowd.Annotation.Api.Helpers;
using Crowd.Annotation.Domain.Annotations;
using Crowd.Annotation.Domain.Projects;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Api.Services
{
    /// <summary>
    /// DONG SO du an — project-svc goi (API noi bo) truoc khi hoan thanh / huy, vi ngay sau
    /// do ledger tra ky quy con lai ve doanh nghiep. Chi dong khi KHONG con viec nao co the
    /// sinh tien cho labeler:
    ///   - so nhan o day KHOP so luot nop that ben task-svc (khong nhan nao dang tren duong);
    ///   - khong nhan nao cho duyet, khong khieu nai nao dang mo;
    ///   - khong nhan bi tu choi nao con trong han khieu nai (chong "tu choi hang loat roi
    ///     dong ngay de labeler mat quyen khieu nai").
    /// Kiem va dong trong MOT transaction, khoa dong dieu khoan FOR UPDATE: cac thao tac
    /// duyet / khieu nai giu khoa FOR SHARE nen khong chen vao giua duoc.
    /// Nhan cong link khong gan tien (sharer da duoc tra theo luot) — khong chan viec dong.
    /// </summary>
    public sealed class DongSoService
    {
        private readonly AnnotationDbContext _db;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public DongSoService(AnnotationDbContext db, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _settings = settings;
            _clock = clock;
        }

        public async Task<CloseProjectResponse> DongSoAsync(Guid projectId, int soLuotNop, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            TimeSpan hanKhieuNai = QuyDinhTuSetting.DuyetNhan(_settings).HanKhieuNai;
            DateTimeOffset moc = bayGio - hanKhieuNai;

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                ProjectTerms? dieuKhoan = await DieuKhoanDuAn.KhoaAsync(_db, projectId, ct);

                IQueryable<LabelAnnotation> nhan = _db.Annotations.Where(
                    a => a.ProjectId == projectId && a.Source == LabelSource.Professional);

                int soNhan = await nhan.CountAsync(ct);
                int choDuyet = await nhan.CountAsync(a => a.Status == AnnotationStatus.PendingReview, ct);
                int khieuNai = await nhan.CountAsync(a => a.Status == AnnotationStatus.Appealed, ct);
                IQueryable<LabelAnnotation> conHan = nhan.Where(
                    a => a.Status == AnnotationStatus.Rejected && a.AppealMessage == null && a.ReviewedAt >= moc);
                int soConHan = await conHan.CountAsync(ct);
                DateTimeOffset? tuChoiMuonNhat = await conHan.MaxAsync(a => a.ReviewedAt, ct);

                List<string> lyDo = new List<string>();
                if (soNhan != soLuotNop || (dieuKhoan == null && soLuotNop > 0))
                {
                    lyDo.Add("nhan_chua_dong_bo");
                }

                if (choDuyet > 0)
                {
                    lyDo.Add("con_nhan_cho_duyet");
                }

                if (khieuNai > 0)
                {
                    lyDo.Add("con_khieu_nai");
                }

                if (soConHan > 0)
                {
                    lyDo.Add("con_han_khieu_nai");
                }

                bool dong = lyDo.Count == 0;
                if (dong && dieuKhoan != null)
                {
                    dieuKhoan.DongSo(bayGio);
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                DateTimeOffset? hetHan = null;
                if (tuChoiMuonNhat.HasValue)
                {
                    hetHan = tuChoiMuonNhat.Value + hanKhieuNai;
                }

                return new CloseProjectResponse
                {
                    Closed = dong,
                    Reasons = lyDo,
                    AnnotationCount = soNhan,
                    SubmittedCount = soLuotNop,
                    PendingReview = choDuyet,
                    OpenAppeals = khieuNai,
                    RejectedInAppealWindow = soConHan,
                    AppealWindowEndsAt = hetHan,
                };
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }
    }
}
