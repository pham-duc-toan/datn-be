using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Admin;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Identity.Api.Services;

namespace Crowd.Identity.Api.Consumers
{
    // Event identity-svc nghe. Processor KHONG SaveChanges — IdempotencyGuard luu mot lan cung dau vet.

    /// <summary>
    /// project.grading_applied (project-svc da ghi ket qua cham vao bai lam): labeler co bai dat → hang cho tinh
    /// lai cap. So cau duoc chap nhan doc qua gRPC luc tinh (kien truc 3.24) — khong nghe thang project.graded
    /// cua quality-svc vi luc do project-svc co the chua ghi ket qua, tong se thieu dung du an vua cham.
    /// </summary>
    public sealed class ProjectGradingAppliedProcessor : IEventProcessor<ProjectGradingApplied>
    {
        private readonly CapDoService _capDo;

        public ProjectGradingAppliedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public Task XuLyAsync(EventEnvelope<ProjectGradingApplied> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _capDo.XepTinhLaiAsync(envelope.Payload.ApprovedLabelerIds, envelope.CorrelationId, ct);
        }

        internal static Caller HeThong<T>(EventEnvelope<T> envelope)
            where T : class, IEventPayload
        {
            return Caller.HeThong(envelope.CorrelationId, envelope.EventId);
        }
    }

    /// <summary>attempt.appeal_resolved: khieu nai ca bai duoc chap nhan → bai thanh dat, tinh lai cap nhu tren.</summary>
    public sealed class AttemptAppealResolvedProcessor : IEventProcessor<AttemptAppealResolved>
    {
        private readonly CapDoService _capDo;

        public AttemptAppealResolvedProcessor(CapDoService capDo)
        {
            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _capDo = capDo;
        }

        public Task XuLyAsync(EventEnvelope<AttemptAppealResolved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptAppealResolved p = envelope.Payload;
            if (!p.Accepted || p.AnsweredCount <= 0)
            {
                return Task.CompletedTask;
            }

            return _capDo.XepTinhLaiAsync(new[] { p.LabelerId }, envelope.CorrelationId, ct);
        }
    }

    /// <summary>reputation.changed (quality-svc) → uy tin moi nhat (ban cu den tre bi bo qua), tinh lai cap.</summary>
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
            return _capDo.GhiUyTinAsync(p.UserId, p.Reputation, envelope.OccurredAt, envelope.CorrelationId, ct);
        }
    }

    /// <summary>tag.merged (admin-svc) → doi ma the trong ho so ky nang (goi y du an van khop).</summary>
    public sealed class TagMergedProcessor : IEventProcessor<TagMerged>
    {
        private readonly HoSoService _hoSo;

        public TagMergedProcessor(HoSoService hoSo)
        {
            if (hoSo == null)
            {
                throw new ArgumentNullException(nameof(hoSo));
            }

            _hoSo = hoSo;
        }

        public Task XuLyAsync(EventEnvelope<TagMerged> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            TagMerged p = envelope.Payload;
            return _hoSo.GopTheAsync(p.Kind, p.FromCode, p.ToCode, ct);
        }
    }

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

            Caller heThong = ProjectGradingAppliedProcessor.HeThong(envelope);
            foreach (Guid userId in p.UserIds)
            {
                await _taiKhoan.KhoaTuHeThongAsync(userId, "Gian lan (ho so " + p.CaseId + "): " + p.Reason, heThong, ct);
            }
        }
    }
}
