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
    /// Chay nen: lay lan luot cac phieu ZIP anh "queued" va giai nen (ZipImporter — kien truc 3.41). Con phieu thi lam
    /// tiep ngay; het thi nghi theo setting dataset.worker_idle.
    ///
    /// Khoi dong lai giua chung: phieu bi bo do o processing — luc khoi dong dua het ve queued de lam lai tu dau
    /// (mau chi ghi o buoc cuoi, nen lam lai khong bi trung).
    /// </summary>
    public sealed class ZipImportWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly ILogger<ZipImportWorker> _logger;

        public ZipImportWorker(IServiceScopeFactory scopeFactory, ISettings settings, ILogger<ZipImportWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopeFactory = scopeFactory;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await DuaPhieuDoDangVeHangDoiAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool coViec = false;

                try
                {
                    using (IServiceScope scope = _scopeFactory.CreateScope())
                    {
                        ZipImporter importer = scope.ServiceProvider.GetRequiredService<ZipImporter>();
                        coViec = await importer.XuLyMotZipAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker giai nen ZIP loi, thu lai vong sau");
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

        private async Task DuaPhieuDoDangVeHangDoiAsync(CancellationToken ct)
        {
            try
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    ProjectDbContext db = scope.ServiceProvider.GetRequiredService<ProjectDbContext>();
                    int so = await db.ZipImports
                        .Where(z => z.State == TrangThaiZip.DangXuLy)
                        .ExecuteUpdateAsync(u => u.SetProperty(z => z.State, TrangThaiZip.ChoXuLy), ct);

                    if (so > 0)
                    {
                        _logger.LogWarning("Dua {So} phieu ZIP dang giai nen do (service tat giua chung) ve hang doi", so);
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Khong dua duoc phieu ZIP do dang ve hang doi");
            }
        }
    }
}
