using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Payment.Domain.Deposits;
using Crowd.Payment.Infrastructure.Persistence;
using Crowd.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Payment.Api.Seeding
{
    /// <summary>
    /// Seed lenh nap tien cua kich ban:
    ///   - biz1: 2.000.000d DA THANH TOAN — khop but toan nap tien ma ledger-svc seed.
    ///   - biz2: 1.000.000d CHUA THANH TOAN — mo trang sandbox de tra tien that.
    ///
    /// Di qua PaymentIntent.Tao + XacNhanThanhCong nhu luong webhook that, nhung
    /// KHONG phat deposit.confirmed: ledger-svc da tu seed phan tien cua no.
    /// </summary>
    public sealed class PaymentSeeder
    {
        private readonly PaymentDbContext _db;
        private readonly TimeProvider _clock;
        private readonly ILogger<PaymentSeeder> _logger;
        private readonly ISettings _settings;

        public PaymentSeeder(PaymentDbContext db, TimeProvider clock, ILogger<PaymentSeeder> logger, ISettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _settings = settings;

            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
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
            _clock = clock;
            _logger = logger;
        }

        public async Task ChayAsync(CancellationToken ct)
        {
            Guid moc = KichBanSeed.Deposits[0].IntentId;
            if (await _db.Intents.AnyAsync(i => i.Id == moc, ct))
            {
                _logger.LogInformation("Seed payment: da co du lieu seed — bo qua");
                return;
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            QuyDinhNap quyDinh = new QuyDinhNap
            {
                ToiThieuVnd = _settings.SoLon(SettingKeys.PaymentDepositMinVnd),
                ToiDaVnd = _settings.SoLon(SettingKeys.PaymentDepositMaxVnd),
            };

            foreach (SeedDeposit d in KichBanSeed.Deposits)
            {
                DateTimeOffset lucTao = bayGio - d.CreatedAgo;

                PaymentIntent lenh = PaymentIntent.Tao(d.BusinessId, d.AmountVnd, KichBanSeed.CongThanhToan, d.IdempotencyKey, quyDinh, lucTao);
                SeedIds.GanId(lenh, d.IntentId);

                if (d.ProviderTxnId != null)
                {
                    // Cong bao thanh cong 2 phut sau khi tao lenh.
                    lenh.XacNhanThanhCong(d.ProviderTxnId, d.AmountVnd, lucTao + TimeSpan.FromMinutes(2));
                }

                _db.Intents.Add(lenh);
            }

            SeedOutbox.BoEventChuaGui(_db);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Seed payment: tao {So} lenh nap", KichBanSeed.Deposits.Count);
        }
    }
}
