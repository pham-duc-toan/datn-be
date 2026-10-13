using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Quality;
using Crowd.Identity.Api.Services;

namespace Crowd.Identity.Api.Consumers
{
    // Event identity-svc nghe. Processor KHONG SaveChanges — IdempotencyGuard luu mot lan cung dau vet.

    /// <summary>annotation.approved (kenh chuyen nghiep) → +1 nhan duoc duyet, tinh lai cap / huy hieu.</summary>
    public sealed class AnnotationApprovedProcessor : IEventProcessor<AnnotationApproved>
    {
        private readonly CapDoService _capDo;

        public AnnotationApprovedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public Task XuLyAsync(EventEnvelope<AnnotationApproved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AnnotationApproved p = envelope.Payload;

            // Nhan cong link: khach vang lai, khong co tai khoan de tinh cap.
            if (!p.LabelerId.HasValue || p.Source != AnnotationSource.Professional)
            {
                return Task.CompletedTask;
            }

            return _capDo.GhiNhanDuyetAsync(p.LabelerId.Value, 1, envelope.OccurredAt, HeThong(envelope), ct);
        }

        internal static Caller HeThong<T>(EventEnvelope<T> envelope)
            where T : class, IEventPayload
        {
            return Caller.HeThong(envelope.CorrelationId, envelope.EventId);
        }
    }

    /// <summary>reputation.changed (quality-svc) → uy tin moi nhat; ban cu den tre bi bo qua.</summary>
    public sealed class ReputationChangedProcessor : IEventProcessor<ReputationChanged>
    {
        private readonly CapDoService _capDo;

        public ReputationChangedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public Task XuLyAsync(EventEnvelope<ReputationChanged> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ReputationChanged p = envelope.Payload;
            return _capDo.GhiUyTinAsync(p.UserId, p.Reputation, envelope.OccurredAt, AnnotationApprovedProcessor.HeThong(envelope), ct);
        }
    }

    /// <summary>dispute.resolved tra tien doanh nghiep → nhan bi loai: -1 nhan duoc duyet.</summary>
    public sealed class DisputeResolvedProcessor : IEventProcessor<DisputeResolved>
    {
        private readonly CapDoService _capDo;

        public DisputeResolvedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public Task XuLyAsync(EventEnvelope<DisputeResolved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            DisputeResolved p = envelope.Payload;
            if (p.Outcome != DisputeOutcome.RefundBusiness)
            {
                return Task.CompletedTask;
            }

            return _capDo.GhiNhanDuyetAsync(p.LabelerId, -1, envelope.OccurredAt, AnnotationApprovedProcessor.HeThong(envelope), ct);
        }
    }

    /// <summary>fraud.case_confirmed (admin xac nhan gian lan o quality-svc) → khoa cac tai khoan neu BlockAccounts.</summary>
    public sealed class FraudCaseConfirmedProcessor : IEventProcessor<FraudCaseConfirmed>
    {
        private readonly TaiKhoanAdminService _taiKhoan;

        public FraudCaseConfirmedProcessor(TaiKhoanAdminService taiKhoan)
        {
            if (taiKhoan == null)
            {
                throw new ArgumentNullException(nameof(taiKhoan));
            }

            _taiKhoan = taiKhoan;
        }

        public async Task XuLyAsync(EventEnvelope<FraudCaseConfirmed> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            FraudCaseConfirmed p = envelope.Payload;
            if (!p.BlockAccounts)
            {
                return;
            }

            Caller heThong = AnnotationApprovedProcessor.HeThong(envelope);
            foreach (Guid userId in p.UserIds)
            {
                await _taiKhoan.KhoaTuHeThongAsync(userId, "Gian lan (ho so " + p.CaseId + "): " + p.Reason, heThong, ct);
            }
        }
    }
}
