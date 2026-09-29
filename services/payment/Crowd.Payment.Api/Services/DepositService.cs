using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.Contracts.Payment;
using Crowd.Payment.Api.Dtos;
using Crowd.Payment.Api.Exceptions;
using Crowd.Payment.Domain.Common;
using Crowd.Payment.Domain.Deposits;
using Crowd.Payment.Infrastructure.Persistence;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Payment.Api.Services
{
    /// <summary>
    /// Nap tien (FB-03): tao lenh nap → doanh nghiep tra tien tren trang cong →
    /// cong goi WEBHOOK co chu ky → phat deposit.confirmed → ledger ghi so.
    ///
    /// Tien CHI vao so khi webhook hop le den. Client khong co cach nao tu bao
    /// "toi da tra roi".
    /// </summary>
    public sealed class DepositService
    {
        private static readonly JsonSerializerOptions JsonWebhook = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly PaymentDbContext _db;
        private readonly IPaymentProvider _provider;
        private readonly PaymentEventPublisher _events;
        private readonly TimeProvider _clock;
        private readonly ILogger<DepositService> _logger;

        public DepositService(
            PaymentDbContext db,
            IPaymentProvider provider,
            PaymentEventPublisher events,
            TimeProvider clock,
            ILogger<DepositService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _provider = provider;
            _events = events;
            _clock = clock;
            _logger = logger;
        }

        public async Task<DepositResponse> TaoAsync(long amountVnd, string? idempotencyKey, Caller caller, CancellationToken ct)
        {
            Guid businessId = caller.LayUserId();
            string khoa = idempotencyKey ?? string.Empty;

            PaymentIntent? cu = await _db.Intents.AsNoTracking().FirstOrDefaultAsync(i => i.BusinessId == businessId && i.IdempotencyKey == khoa, ct);
            if (cu != null)
            {
                return TaoResponse(cu);
            }

            PaymentIntent moi = PaymentIntent.Tao(businessId, amountVnd, _provider.Name, khoa, _clock.GetUtcNow());
            _db.Intents.Add(moi);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_intents_idempotency"))
            {
                _db.ChangeTracker.Clear();
                PaymentIntent benThang = await _db.Intents.AsNoTracking().FirstAsync(i => i.BusinessId == businessId && i.IdempotencyKey == khoa, ct);
                return TaoResponse(benThang);
            }

            return TaoResponse(moi);
        }

        public async Task<DepositResponse> XemAsync(Guid intentId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            PaymentIntent? i = await _db.Intents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == intentId, ct);

            // Lenh nap cua nguoi khac → 404 (BOLA).
            if (i == null || (i.BusinessId != uid && !caller.IsAdmin))
            {
                throw new NotFoundException("Khong tim thay lenh nap.");
            }

            return TaoResponse(i);
        }

        /// <summary>
        /// Xu ly webhook. THU TU QUAN TRONG: kiem chu ky TRUOC khi doc bat cu gi.
        /// Webhook lap lai (cong gui 3 lan) → lan 2, 3 khong lam gi, van tra 200 de
        /// cong ngung gui lai.
        /// </summary>
        public async Task XuLyWebhookAsync(string body, string? chuKy, Guid correlationId, CancellationToken ct)
        {
            if (!_provider.KiemChuKy(body, chuKy))
            {
                _logger.LogWarning("Webhook chu ky SAI — tu choi. Co the la gia mao.");
                throw new ForbiddenException("chu_ky_sai", "Chu ky webhook khong hop le.");
            }

            SandboxWebhookBody? w = JsonSerializer.Deserialize<SandboxWebhookBody>(body, JsonWebhook);
            if (w == null)
            {
                throw new InvalidValueException("webhook_hong", "Webhook khong doc duoc.");
            }

            PaymentIntent? intent = await _db.Intents.FirstOrDefaultAsync(i => i.Id == w.IntentId, ct);
            if (intent == null)
            {
                throw new NotFoundException("Khong tim thay lenh nap.");
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();

            if (!string.Equals(w.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                intent.ThatBai(bayGio);
                await _db.SaveChangesAsync(ct);
                return;
            }

            bool moi = intent.XacNhanThanhCong(w.ProviderTxnId ?? string.Empty, w.AmountVnd, bayGio);
            if (!moi)
            {
                _logger.LogInformation("Webhook lap cho lenh nap {IntentId} — bo qua", intent.Id);
                return;
            }

            _events.Phat(Caller.HeThong(correlationId, null), new DepositConfirmed
            {
                IntentId = intent.Id,
                BusinessId = intent.BusinessId,
                AmountVnd = intent.AmountVnd,
                Provider = intent.Provider,
                ProviderTxnId = intent.ProviderTxnId!,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_intents_provider_txn"))
            {
                // Mot ma giao dich cong dung cho HAI lenh nap: gian lan hoac cong loi.
                throw new RuleViolationException("ma_giao_dich_trung", "Ma giao dich cong da duoc dung cho lenh nap khac.");
            }
        }

        private DepositResponse TaoResponse(PaymentIntent i)
        {
            return new DepositResponse
            {
                IntentId = i.Id,
                AmountVnd = i.AmountVnd,
                Status = i.Status,
                CheckoutUrl = _provider.TaoLinkThanhToan(i.Id, i.AmountVnd),
                CreatedAt = i.CreatedAt,
            };
        }
    }
}
