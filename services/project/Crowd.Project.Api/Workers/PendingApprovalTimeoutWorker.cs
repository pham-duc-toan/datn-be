using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Api.Services;
using Crowd.Project.Api.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Workers
{
    /// <summary>
    /// COMPENSATION cua saga publish (docs 3.4): du an nam Cho duyet qua 72 gio
    /// ma khong ai duyet thi tu huy, phat project.cancelled de ledger hoan tien.
    ///
    /// Khong co worker nay, tien doanh nghiep bi giu vo thoi han chi vi admin
    /// quen — dung loai loi "khong ai phat hien" ma saga can chan.
    /// </summary>
    public sealed class PendingApprovalTimeoutWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ProjectSagaOptions _options;
        private readonly ILogger<PendingApprovalTimeoutWorker> _logger;

        public PendingApprovalTimeoutWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<ProjectSagaOptions> options,
            ILogger<PendingApprovalTimeoutWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Worker la singleton, ProjectService la scoped — mo scope moi vong.
                    using (IServiceScope scope = _scopeFactory.CreateScope())
                    {
                        ProjectService service = scope.ServiceProvider.GetRequiredService<ProjectService>();
                        int soDaHuy = await service.HuyCacDuAnQuaHanAsync(stoppingToken);

                        if (soDaHuy > 0)
                        {
                            _logger.LogWarning("Da tu huy {SoDuAn} du an qua han cho duyet", soDaHuy);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Khong de loi giet worker — no chet thi chet IM LANG.
                    _logger.LogError(ex, "Quet du an qua han that bai, thu lai vong sau");
                }

                try
                {
                    await Task.Delay(_options.ChuKyQuetQuaHan, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
