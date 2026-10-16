using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Ledger.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Ledger.Api.Workers
{
    /// <summary>
    /// Tu xuat hoa don GTGT thang truoc (setting invoice.auto_issue). Chay lai bao nhieu lan cung an
    /// toan: doanh nghiep da co hoa don cua ky thi bo qua (UNIQUE business + period). Doanh nghiep chua
    /// KYB hoac identity dang loi thi de vong sau.
    /// </summary>
    public sealed class InvoiceWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly ILogger<InvoiceWorker> _logger;

        public InvoiceWorker(IServiceScopeFactory scopeFactory, ISettings settings, ILogger<InvoiceWorker> logger)
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
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_settings.DungSai(SettingKeys.InvoiceAutoIssue))
                    {
                        using (IServiceScope scope = _scopeFactory.CreateScope())
                        {
                            int n = await scope.ServiceProvider.GetRequiredService<InvoiceService>().TuXuatThangTruocAsync(stoppingToken);
                            if (n > 0)
                            {
                                _logger.LogInformation("Tu xuat {SoHoaDon} hoa don thang truoc", n);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "InvoiceWorker loi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.InvoiceAutoIssueInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
