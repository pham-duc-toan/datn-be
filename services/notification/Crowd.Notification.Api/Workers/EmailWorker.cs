using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Persistence;
using Crowd.Notification.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Notification.Api.Workers
{
    /// <summary>
    /// Gui email cho thong bao dang cho (email_state = Pending, toi han email_next_at). Moi lo mot
    /// transaction, khoa dong FOR UPDATE SKIP LOCKED — chay nhieu instance khong gui trung.
    /// Loi SMTP → hen gui lai (backoff luy thua), qua notification.email_max_attempts thi bo.
    /// Tat notification.email_enabled → worker ngung gui (thu van cho, bat lai thi gui tiep).
    ///
    /// Gioi han: SMTP thanh cong nhung commit loi → lan sau gui LAI (at-least-once). Chap nhan:
    /// trung mot email it hai hon mat email (rut tien, khoa tai khoan).
    /// </summary>
    public sealed class EmailWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISettings _settings;
        private readonly IGuiEmail _email;
        private readonly TimeProvider _clock;
        private readonly ILogger<EmailWorker> _logger;

        public EmailWorker(IServiceScopeFactory scopeFactory, ISettings settings, IGuiEmail email, TimeProvider clock, ILogger<EmailWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (email == null)
            {
                throw new ArgumentNullException(nameof(email));
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
            _settings = settings;
            _email = email;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_settings.DungSai(SettingKeys.NotificationEmailEnabled))
                    {
                        int so;
                        do
                        {
                            so = await MotLoAsync(stoppingToken);
                        }
                        while (so >= _settings.SoNguyen(SettingKeys.NotificationEmailBatchSize));
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gui email thong bao loi — thu lai vong sau");
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.NotificationEmailInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> MotLoAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                NotificationDbContext db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    DateTimeOffset bayGio = _clock.GetUtcNow();
                    List<ThongBao> lo = await db.ThongBaos
                        .FromSqlRaw(
                            "SELECT * FROM notifications WHERE email_state = 'Pending' AND email_next_at <= {0} " +
                            "ORDER BY email_next_at LIMIT {1} FOR UPDATE SKIP LOCKED",
                            bayGio, _settings.SoNguyen(SettingKeys.NotificationEmailBatchSize))
                        .ToListAsync(ct);

                    if (lo.Count > 0)
                    {
                        List<Guid> ids = lo.Select(t => t.UserId).Distinct().ToList();
                        Dictionary<Guid, LienHe> lienHe = await db.LienHes.AsNoTracking()
                            .Where(l => ids.Contains(l.UserId)).ToDictionaryAsync(l => l.UserId, ct);
                        string tu = _settings.Chuoi(SettingKeys.NotificationEmailFrom);
                        int toiDa = _settings.SoNguyen(SettingKeys.NotificationEmailMaxAttempts);

                        foreach (ThongBao t in lo)
                        {
                            await GuiMotAsync(t, lienHe, tu, toiDa, ct);
                        }
                    }

                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return lo.Count;
                }
                finally
                {
                    await tx.DisposeAsync();
                }
            }
        }

        private async Task GuiMotAsync(ThongBao t, Dictionary<Guid, LienHe> lienHe, string tu, int toiDa, CancellationToken ct)
        {
            LienHe? lh;
            if (!lienHe.TryGetValue(t.UserId, out lh))
            {
                t.BoEmail("Khong co dia chi email (chua nhan user.registered).");
                return;
            }

            try
            {
                string noiDung = "Xin chào " + lh.DisplayName + ",\n\n" + t.Body + "\n\n— Crowd Label\n"
                                 + "(Bạn có thể tắt email cho loại thông báo này trong phần cài đặt.)";
                await _email.GuiAsync(tu, lh.Email, lh.DisplayName, t.Title, noiDung, ct);
                t.DaGuiEmail(_clock.GetUtcNow());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                t.GuiEmailLoi(ex.GetType().Name + ": " + ex.Message, toiDa, _clock.GetUtcNow());
                _logger.LogWarning("Gui email thong bao {Id} loi lan {Lan}: {Loi}", t.Id, t.EmailAttempts, ex.Message);
            }
        }
    }
}
