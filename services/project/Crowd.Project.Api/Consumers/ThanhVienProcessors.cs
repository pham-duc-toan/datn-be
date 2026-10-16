using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Identity;
using Crowd.Project.Api.Services;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Consumers
{
    /// <summary>
    /// user.email_verified: tai khoan vua chung minh so huu email → nhan loi moi cho gui toi email do
    /// (moi nguoi chua co tai khoan). Du an khong can dong y rieng va du dieu kien thi vao luon.
    /// Khong o user.registered: dang ky bang email nguoi khac thi khong bam duoc link xac thuc.
    /// </summary>
    public sealed class UserEmailVerifiedProcessor : IEventProcessor<UserEmailVerified>
    {
        private readonly InvitationService _loiMoi;
        private readonly ILogger<UserEmailVerifiedProcessor> _logger;

        public UserEmailVerifiedProcessor(InvitationService loiMoi, ILogger<UserEmailVerifiedProcessor> logger)
        {
            if (loiMoi == null)
            {
                throw new ArgumentNullException(nameof(loiMoi));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _loiMoi = loiMoi;
            _logger = logger;
        }

        public async Task XuLyAsync(EventEnvelope<UserEmailVerified> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            int vao = await _loiMoi.GanTaiKhoanAsync(
                envelope.Payload.UserId, Caller.HeThong(envelope.CorrelationId, envelope.EventId), ct);
            if (vao > 0)
            {
                _logger.LogInformation("User {UserId} xac thuc email: vao {SoDuAn} du an tu loi moi cho", envelope.Payload.UserId, vao);
            }
        }
    }
}
