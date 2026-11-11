using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Project.Api.Services;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Workers
{
    /// <summary>
    /// Chay nen: lay lan luot cac mau "queued" (file da upload / link nguon ngoai) va xu ly (SampleIngestor).
    /// Con mau thi lam tiep ngay; het mau thi nghi vai giay roi xem lai.
    ///
    /// Khoi dong lai giua chung (service bi tat khi dang doc file): mau bi bo do o trang thai processing —
    /// luc khoi dong dua het ve queued de lam lai tu dau (mau chi chot ready o buoc cuoi, nen lam lai khong bi trung).
    /// </summary>
    public sealed class SampleIngestWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SampleIngestWorker> _logger;
        private readonly ISettings _settings;

        public SampleIngestWorker(IServiceScopeFactory scopeFactory, ILogger<SampleIngestWorker> logger, ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await DuaMauDoDangVeHangDoiAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool coViec = false;

                try
                {
                    using (IServiceScope scope = _scopeFactory.CreateScope())
                    {
                        SampleIngestor ingestor = scope.ServiceProvider.GetRequiredService<SampleIngestor>();
                        coViec = await ingestor.XuLyMotMauAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker nap mau loi, thu lai vong sau");
                }

                if (!coViec)
                {
                    try
                    {
                        await ChoTheoSetting.ChoAsync(_settings, SettingKeys.DatasetWorkerIdle, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task DuaMauDoDangVeHangDoiAsync(CancellationToken ct)
        {
            try
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    ProjectDbContext db = scope.ServiceProvider.GetRequiredService<ProjectDbContext>();
                    int so = await db.Samples.IgnoreQueryFilters()
                        .Where(s => s.IngestState == TrangThaiNap.DangXuLy)
                        .ExecuteUpdateAsync(u => u.SetProperty(s => s.IngestState, TrangThaiNap.ChoXuLy), ct);

                    if (so > 0)
                    {
                        _logger.LogWarning("Dua {So} mau dang xu ly do (service tat giua chung) ve hang doi", so);
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Khong dua duoc mau do dang ve hang doi");
            }
        }
    }
}
