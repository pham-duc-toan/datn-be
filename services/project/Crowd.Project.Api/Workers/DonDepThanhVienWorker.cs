using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Project.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Workers
{
    /// <summary>
    /// Don dep dinh ky. Hang doi bi bo quen: don xin tham gia chu du an khong duyet qua project.join_request_ttl → het
    /// han (bao labeler, labeler gui lai duoc ngay); loi moi cho qua project.invitation_ttl → het han.
    /// Chay lai bao nhieu lan cung an toan (chi dong Pending qua han moi bi doi).
    /// </summary>
    public sealed class DonDepThanhVienWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly ILogger<DonDepThanhVienWorker> _logger;

        public DonDepThanhVienWorker(IServiceScopeFactory scopeFactory, ISettings settings, ILogger<DonDepThanhVienWorker> logger)
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
                    using (IServiceScope scope = _scopeFactory.CreateScope())
                    {
                        int don = await scope.ServiceProvider.GetRequiredService<JoinRequestService>()
                            .HetHanAsync(Caller.HeThong(Guid.CreateVersion7(), null), stoppingToken);
                        int loiMoi = await scope.ServiceProvider.GetRequiredService<InvitationService>().HetHanAsync(stoppingToken);
                        if (don + loiMoi > 0)
                        {
                            _logger.LogInformation("Het han {SoDon} don xin tham gia, {SoLoiMoi} loi moi", don, loiMoi);
                        }

                        // Muc 3.14: xoa file goc cua du an da dong qua han giu — CHI khi doanh nghiep dong y.
                        await scope.ServiceProvider.GetRequiredService<ProjectService>().XoaFileDuAnDaDongAsync(
                            scope.ServiceProvider.GetRequiredService<Crowd.BuildingBlocks.Storage.IObjectStorage>(), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DonDepThanhVienWorker loi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.ProjectHousekeepingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
