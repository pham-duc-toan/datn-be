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
    /// Chay nen: lay lan luot cac lo manifest Pending va xu ly (DatasetIngestor).
    /// Con lo thi lam tiep ngay; het lo thi nghi vai giay roi xem lai.
    ///
    /// Khoi dong lai giua chung (service bi tat khi dang doc file): lo bi bo do o
    /// trang thai Ingesting — luc khoi dong dua het ve Pending de lam lai tu dau
    /// (mau chua duoc ghi vi ghi mot lan o cuoi, nen lam lai khong bi trung).
    /// </summary>
    public sealed class DatasetIngestWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DatasetIngestWorker> _logger;
        private readonly ISettings _settings;

        public DatasetIngestWorker(IServiceScopeFactory scopeFactory, ILogger<DatasetIngestWorker> logger, ISettings settings)
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
            await DuaLoDoDangVePendingAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool coViec = false;

                try
                {
                    using (IServiceScope scope = _scopeFactory.CreateScope())
                    {
                        DatasetIngestor ingestor = scope.ServiceProvider.GetRequiredService<DatasetIngestor>();
                        coViec = await ingestor.XuLyMotLoAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker nap du lieu loi, thu lai vong sau");
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

        private async Task DuaLoDoDangVePendingAsync(CancellationToken ct)
        {
            try
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    ProjectDbContext db = scope.ServiceProvider.GetRequiredService<ProjectDbContext>();
                    int so = await db.Datasets
                        .Where(d => d.Status == DatasetStatus.Ingesting)
                        .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DatasetStatus.Pending), ct);

                    if (so > 0)
                    {
                        _logger.LogWarning("Dua {So} lo dang xu ly do (service tat giua chung) ve Pending", so);
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Khong dua duoc lo do dang ve Pending");
            }
        }
    }
}
