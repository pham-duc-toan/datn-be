using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Payment;
using Crowd.Payment.Api.Services;
using Crowd.Payment.Domain.Payouts;
using Crowd.Payment.Infrastructure.Persistence;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Payment.Api.Workers
{
    /// <summary>
    /// Chuyen tien ra ngoai theo BA PHA (VD-M-09):
    ///
    ///   PHA 1  transaction ngan: Requested → Sending, COMMIT       ← da ghi y dinh
    ///   PHA 2  goi cong, KHONG transaction nao dang mo              ← co the cham 30 giay
    ///   PHA 3  transaction moi: ghi ket qua + payout.* vao outbox
    ///
    /// Vi sao khong goi cong trong transaction: giu khoa dong va connection suot
    /// thoi gian cho cong; chet giua chung thi DB rollback nhung CONG KHONG
    /// rollback — hai he lech nhau vinh vien.
    ///
    /// Ket qua KHONG RO (timeout) → giu Sending. Vong sau, lenh Sending qua
    /// NguongTraCuu duoc TRA CUU nguoc cong theo Id — khong gui lai mu quang.
    /// </summary>
    public sealed class PayoutWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IPaymentProvider _provider;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<PayoutWorker> _logger;

        public PayoutWorker(
            IServiceScopeFactory scopeFactory,
            IPaymentProvider provider,
            ISettings settings,
            TimeProvider clock,
            ILogger<PayoutWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _scopeFactory = scopeFactory;
            _provider = provider;
            _settings = settings;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await GuiLenhMoiAsync(stoppingToken);
                    await TraCuuLenhKetAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PayoutWorker loi, thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.PaymentPayoutInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task GuiLenhMoiAsync(CancellationToken ct)
        {
            // ---- PHA 1: nhan viec, ghi Sending, COMMIT ----
            List<Payout> nhan = new List<Payout>();
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                PaymentDbContext db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    nhan = await db.Payouts
                        .FromSqlRaw("SELECT * FROM payouts WHERE status = 'Requested' ORDER BY created_at LIMIT 20 FOR UPDATE SKIP LOCKED")
                        .ToListAsync(ct);

                    foreach (Payout p in nhan)
                    {
                        p.BatDauGui(_clock.GetUtcNow());
                    }

                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                finally
                {
                    await tx.DisposeAsync();
                }
            }

            foreach (Payout p in nhan)
            {
                // ---- PHA 2: goi cong, KHONG transaction ----
                ProviderPayoutResult kq;
                try
                {
                    kq = await _provider.ChuyenTienAsync(p.Id, p.NetVnd, p.BankAccount, ct);
                }
                catch (Exception ex)
                {
                    kq = new ProviderPayoutResult(ProviderOutcome.Unknown, null, ex.Message);
                }

                // ---- PHA 3: transaction moi ghi ket qua ----
                await GhiKetQuaAsync(p.Id, kq, ct);
            }
        }

        /// <summary>Lenh ket o Sending qua lau: tra cuu nguoc cong roi chot.</summary>
        private async Task TraCuuLenhKetAsync(CancellationToken ct)
        {
            List<Payout> ket;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                PaymentDbContext db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                DateTimeOffset moc = _clock.GetUtcNow() - _settings.ThoiGian(SettingKeys.PaymentPayoutLookupAfter);
                ket = await db.Payouts.AsNoTracking()
                    .Where(p => p.Status == PayoutStatus.Sending && p.SentAt <= moc)
                    .Take(20)
                    .ToListAsync(ct);
            }

            foreach (Payout p in ket)
            {
                ProviderPayoutResult kq = await _provider.TraCuuAsync(p.Id, p.BankAccount, ct);
                _logger.LogInformation("Tra cuu lenh chuyen ket {PayoutId}: {Outcome}", p.Id, kq.Outcome);
                await GhiKetQuaAsync(p.Id, kq, ct);
            }
        }

        private async Task GhiKetQuaAsync(Guid payoutId, ProviderPayoutResult kq, CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                PaymentDbContext db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                PaymentEventPublisher events = scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>();

                Payout? p = await db.Payouts.FirstOrDefaultAsync(x => x.Id == payoutId, ct);
                if (p == null || p.Status != PayoutStatus.Sending)
                {
                    return;
                }

                DateTimeOffset bayGio = _clock.GetUtcNow();
                Caller heThong = Caller.HeThong(Guid.CreateVersion7(), null);

                if (kq.Outcome == ProviderOutcome.Succeeded)
                {
                    p.ThanhCong(kq.ProviderTxnId ?? string.Empty, bayGio);
                    events.Phat(heThong, new PayoutCompleted
                    {
                        WithdrawalId = p.Id,
                        Provider = _provider.Name,
                        ProviderTxnId = p.ProviderTxnId!,
                    });
                }
                else if (kq.Outcome == ProviderOutcome.Failed)
                {
                    p.ThatBai(kq.Reason ?? "Cong tu choi", bayGio);
                    events.Phat(heThong, new PayoutFailed { WithdrawalId = p.Id, Reason = p.LastError! });
                }
                else
                {
                    // Khong biet ket qua: KHONG doan, KHONG gui lai. Giu Sending cho tra cuu.
                    p.GhiLoiTam(kq.Reason ?? "Khong ro ket qua", bayGio);
                    _logger.LogWarning("Lenh chuyen {PayoutId} chua ro ket qua: {Reason} — se tra cuu lai", p.Id, kq.Reason);
                }

                await db.SaveChangesAsync(ct);
            }
        }
    }
}
