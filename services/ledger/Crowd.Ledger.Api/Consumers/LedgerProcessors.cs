using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.Contracts.Gate;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Link;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Api.Consumers
{
    // Moi processor chi chuyen tiep sang MoneyFlowService — logic tien nam MOT cho.
    // Chay ben trong transaction cua IdempotencyGuard: but toan, khoan treo va
    // event phat sinh commit CUNG processed_events (VD-D-02). Event phat sinh mang
    // causationId = eventId goc: tra loi duoc "vi sao labeler nay nhan tien".

    internal static class Nguon
    {
        public static Caller Tu<T>(EventEnvelope<T> e)
            where T : class, IEventPayload
        {
            return Caller.HeThong(e.CorrelationId, e.EventId);
        }
    }

    /// <summary>
    /// Kien truc 3.29: tien KHONG chi duoc vi trang thai ky quy / lenh rut khong khop (khong phai loi ky thuat) →
    /// LOAI 2, admin xu ly tay trong hang doi duyet. Ma con lai (vd chua_co_dieu_khoan = project.published chua toi)
    /// van nem tiep de thu lai (loai 3).
    /// </summary>
    internal static class ViecTien
    {
        public static async Task BocAsync(Func<Task> lam, Func<RuleViolationException, ViecCanXuLyTay?> chuyen)
        {
            try
            {
                await lam();
            }
            catch (RuleViolationException ex)
            {
                ViecCanXuLyTay? viec = chuyen(ex);
                if (viec == null)
                {
                    throw;
                }

                throw new CanXuLyTayException(viec);
            }
        }

        /// <summary>Ly do khong tra duoc thuong bang loi thuong; null = khong phai ca can xu ly tay.</summary>
        public static string? LyDoKhongTraThuong(string code)
        {
            switch (code)
            {
                case "chua_ky_quy":
                    return "dự án không có tiền ký quỹ trong sổ";
                case "ky_quy_da_dong":
                    return "tiền ký quỹ của dự án đã được hoàn về doanh nghiệp";
                case "vuot_so_suat":
                    return "dự án đã trả đủ số suất thưởng";
                default:
                    return null;
            }
        }
    }

    public sealed class ProjectPublishRequestedProcessor : IEventProcessor<ProjectPublishRequested>
    {
        private readonly MoneyFlowService _flow;

        public ProjectPublishRequestedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectPublishRequested> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.DatKyQuyAsync(envelope.Payload, Nguon.Tu(envelope), ct);
        }
    }

    public sealed class ProjectPublishedProcessor : IEventProcessor<ProjectPublished>
    {
        private readonly MoneyFlowService _flow;

        public ProjectPublishedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectPublished> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.GhiDieuKhoanAsync(envelope.Payload, Nguon.Tu(envelope), ct);
        }
    }

    /// <summary>project.graded (quality-svc): tra thuong cho moi bai dat.</summary>
    public sealed class ProjectGradedProcessor : IEventProcessor<ProjectGraded>
    {
        private readonly MoneyFlowService _flow;

        public ProjectGradedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectGraded> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ProjectGraded p = envelope.Payload;
            return ViecTien.BocAsync(
                () => _flow.ChiThuongDuAnAsync(p, Nguon.Tu(envelope), ct),
                ex =>
                {
                    string? lyDo = ViecTien.LyDoKhongTraThuong(ex.Code);
                    if (lyDo == null)
                    {
                        return null;
                    }

                    int soBai = p.Attempts.Count(a => a.Rewarded);
                    return new ViecCanXuLyTay
                    {
                        Loai = "thuong_khong_chi_duoc",
                        TieuDe = "Không trả được thưởng cho bài đạt",
                        MoTa = "Dự án đã chấm xong với " + soBai + " bài đạt được thưởng nhưng " + lyDo
                            + ", nên chưa bài nào được trả. Kiểm tra dự án rồi trả tay cho người làm (hoặc thu lại tiền từ doanh nghiệp).",
                        ProjectId = p.ProjectId,
                        UserId = p.OwnerId,
                    };
                });
        }
    }

    /// <summary>attempt.appeal_resolved (project-svc): khieu nai ca bai duoc chap nhan → tra thuong.</summary>
    public sealed class AttemptAppealResolvedProcessor : IEventProcessor<AttemptAppealResolved>
    {
        private readonly MoneyFlowService _flow;

        public AttemptAppealResolvedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<AttemptAppealResolved> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            AttemptAppealResolved p = envelope.Payload;
            return ViecTien.BocAsync(
                () => _flow.ChiThuongKhieuNaiAsync(p, Nguon.Tu(envelope), ct),
                ex =>
                {
                    string? lyDo = ViecTien.LyDoKhongTraThuong(ex.Code);
                    if (lyDo == null)
                    {
                        return null;
                    }

                    return new ViecCanXuLyTay
                    {
                        Loai = "thuong_khong_chi_duoc",
                        TieuDe = "Không trả được thưởng cho khiếu nại được chấp nhận",
                        MoTa = "Khiếu nại của người làm đã được chấp nhận và bài được thưởng nhưng " + lyDo
                            + ", nên chưa trả. Kiểm tra dự án rồi trả tay cho người làm.",
                        ProjectId = p.ProjectId,
                        UserId = p.LabelerId,
                    };
                });
        }
    }

    public sealed class ProjectCancelledProcessor : IEventProcessor<ProjectCancelled>
    {
        private readonly MoneyFlowService _flow;

        public ProjectCancelledProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectCancelled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            // Chi huy duoc khi chua co bai nop nao (project-svc kiem) → khong con bai nao cho tra.
            return _flow.TraKyQuyAsync(envelope.Payload.ProjectId, true, 0, Nguon.Tu(envelope), ct);
        }
    }

    public sealed class ProjectCompletedProcessor : IEventProcessor<ProjectCompleted>
    {
        private readonly MoneyFlowService _flow;

        public ProjectCompletedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ProjectCompleted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.TraKyQuyAsync(envelope.Payload.ProjectId, false, envelope.Payload.PassedAttemptCount, Nguon.Tu(envelope), ct);
        }
    }

    public sealed class DepositConfirmedProcessor : IEventProcessor<DepositConfirmed>
    {
        private readonly MoneyFlowService _flow;

        public DepositConfirmedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<DepositConfirmed> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.NapTienAsync(envelope.Payload, ct);
        }
    }

    public sealed class PayoutCompletedProcessor : IEventProcessor<PayoutCompleted>
    {
        private readonly MoneyFlowService _flow;

        public PayoutCompletedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<PayoutCompleted> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            PayoutCompleted p = envelope.Payload;
            return ViecTien.BocAsync(
                () => _flow.HoanTatRutAsync(p, ct),
                ex => ex.Code != "khong_co_lenh_rut" ? null : new ViecCanXuLyTay
                {
                    Loai = "doi_soat_rut_tien",
                    TieuDe = "Cổng thanh toán báo đã chuyển tiền cho lệnh rút không có trong hệ thống",
                    MoTa = "Cổng thanh toán " + p.Provider + " báo đã chuyển " + ViecCanXuLyTay.Tien(p.NetAmountVnd) + " (mã giao dịch "
                        + p.ProviderTxnId + ") cho một lệnh rút mà hệ thống không tìm thấy. Đối soát với cổng thanh toán và ví của người nhận.",
                    UserId = p.LabelerId,
                    AmountVnd = p.NetAmountVnd,
                });
        }
    }

    public sealed class PayoutFailedProcessor : IEventProcessor<PayoutFailed>
    {
        private readonly MoneyFlowService _flow;

        public PayoutFailedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<PayoutFailed> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            PayoutFailed p = envelope.Payload;
            return ViecTien.BocAsync(
                () => _flow.ThatBaiRutAsync(p, ct),
                ex => ex.Code != "khong_co_lenh_rut" ? null : new ViecCanXuLyTay
                {
                    Loai = "doi_soat_rut_tien",
                    TieuDe = "Cổng thanh toán báo chuyển tiền thất bại cho lệnh rút không có trong hệ thống",
                    MoTa = "Cổng thanh toán báo chuyển " + ViecCanXuLyTay.Tien(p.NetAmountVnd) + " thất bại (" + p.Reason
                        + ") cho một lệnh rút mà hệ thống không tìm thấy. Đối soát với cổng thanh toán và ví của người nhận.",
                    UserId = p.LabelerId,
                    AmountVnd = p.NetAmountVnd,
                });
        }
    }

    /// <summary>click.validated (gate-svc): tra nguoi chia se link theo luot vuot hop le.</summary>
    public sealed class ClickValidatedProcessor : IEventProcessor<ClickValidated>
    {
        private readonly MoneyFlowService _flow;

        public ClickValidatedProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ClickValidated> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            ClickValidated p = envelope.Payload;
            return ViecTien.BocAsync(
                () => _flow.ChiTraCongLinkAsync(p, Nguon.Tu(envelope), ct),
                ex => ex.Code != "chua_ky_quy" ? null : new ViecCanXuLyTay
                {
                    Loai = "tien_cong_link_khong_chi_duoc",
                    TieuDe = "Không trả được tiền lượt vượt link",
                    MoTa = "Một lượt vượt link hợp lệ (người chia sẻ được " + ViecCanXuLyTay.Tien(p.SharerAmountVnd)
                        + ") thuộc dự án không có tiền ký quỹ trong sổ, nên chưa trả. Kiểm tra dự án và trả tay cho người chia sẻ nếu cần.",
                    ProjectId = p.ProjectId,
                    UserId = p.SharerId,
                    AmountVnd = p.SharerAmountVnd,
                });
        }
    }

    /// <summary>referral.registered (link-svc): ghi quan he gioi thieu de tinh hoa hong.</summary>
    public sealed class ReferralRegisteredProcessor : IEventProcessor<ReferralRegistered>
    {
        private readonly MoneyFlowService _flow;

        public ReferralRegisteredProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<ReferralRegistered> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.GhiGioiThieuAsync(envelope.Payload, ct);
        }
    }

    /// <summary>link.disabled (link-svc): vi pham da xac nhan → giu doanh thu dang treo cua link.</summary>
    public sealed class LinkDisabledProcessor : IEventProcessor<LinkDisabled>
    {
        private readonly MoneyFlowService _flow;

        public LinkDisabledProcessor(MoneyFlowService flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            _flow = flow;
        }

        public Task XuLyAsync(EventEnvelope<LinkDisabled> envelope, CancellationToken ct)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            return _flow.GiuDoanhThuLinkAsync(envelope.Payload, Nguon.Tu(envelope), ct);
        }
    }
}
