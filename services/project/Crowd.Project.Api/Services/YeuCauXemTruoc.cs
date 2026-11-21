using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Project;
using Crowd.Labeling;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Kien truc 3.40: nho media-svc tao SAN ban xem truoc DUNG CHUNG cho the cho du an (anh thu nho / clip 15 giay, chu
    /// "nguồn: &lt;doanh nghiep&gt; - &lt;domain&gt;"). Goi khi du an bat dau chay (duyet / tu duyet) va khi doanh nghiep bat
    /// lai xem truoc luc dang chay. Mau = CUNG luat voi view v_reco_preview_samples (mau that nap som nhat, khong phai cau
    /// vang). KHONG SaveChanges — event vao outbox cung transaction cua noi goi. media-svc chua kip tao thi tu tao luc phat.
    /// </summary>
    public sealed class YeuCauXemTruoc
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectEventPublisher _events;
        private readonly IMediaLinks _media;
        private readonly TenDoanhNghiep _tenDoanhNghiep;
        private readonly ISettings _settings;

        public YeuCauXemTruoc(ProjectDbContext db, ProjectEventPublisher events, IMediaLinks media, TenDoanhNghiep tenDoanhNghiep, ISettings settings)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            if (tenDoanhNghiep == null)
            {
                throw new ArgumentNullException(nameof(tenDoanhNghiep));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _db = db;
            _events = events;
            _media = media;
            _tenDoanhNghiep = tenDoanhNghiep;
            _settings = settings;
        }

        /// <summary>Phat project.preview_requested neu du an hien mau va mau co file. Tra true neu da phat.</summary>
        public async Task<bool> PhatAsync(LabelingProject duAn, Caller caller, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (!duAn.HienMauXemTruoc())
            {
                return false;
            }

            Guid projectId = duAn.Id;
            Sample? mau = await _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId
                            && s.IngestState == TrangThaiNap.SanSang
                            && s.ExcludedAt == null
                            && !_db.GoldItems.Any(g => g.ProjectId == projectId && g.SampleId == s.Id))
                .OrderBy(s => s.CreatedAt)
                .ThenBy(s => s.Id)
                .FirstOrDefaultAsync(ct);
            if (mau == null || mau.StorageKey == null
                || (mau.Modality != Modalities.Image && mau.Modality != Modalities.Video && mau.Modality != Modalities.Audio))
            {
                return false;
            }

            SampleMetadata md = SampleMetadata.Tu(mau.Metadata);
            double? ketThuc = md.SegmentEnd ?? md.DurationSec;
            string? chu = null;
            if (_settings.DungSai(SettingKeys.MediaWatermarkEnabled) && mau.Modality != Modalities.Audio)
            {
                chu = _media.ChuDungChung(await _tenDoanhNghiep.LayAsync(duAn.OwnerId, ct));
            }

            _events.Phat(caller, new ProjectPreviewRequested
            {
                ProjectId = projectId,
                SampleId = mau.Id,
                Modality = mau.Modality,
                StorageKey = mau.StorageKey,
                SegmentStartMs = md.SegmentStart.HasValue ? (long)Math.Round(md.SegmentStart.Value * 1000) : null,
                SegmentEndMs = ketThuc.HasValue ? (long)Math.Round(ketThuc.Value * 1000) : null,
                WatermarkText = chu,
            });
            return true;
        }
    }
}
