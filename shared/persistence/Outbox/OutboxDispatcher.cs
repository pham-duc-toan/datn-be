using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Nửa GỬI của outbox: một tiến trình nền chạy BÊN TRONG chính service,
    /// liên tục đọc bảng outbox rồi đẩy lên bus.
    ///
    /// Generic theo TDbContext vì mỗi service có DbContext riêng, nhưng toàn bộ
    /// logic thì dùng chung — 13 service không ai phải viết lại.
    ///
    /// Đăng ký:
    ///     services.AddHostedService(OutboxDispatcher của AnnotationDbContext);
    /// </summary>
    public sealed class OutboxDispatcher<TDbContext> : BackgroundService
        where TDbContext : DbContext
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OutboxOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<OutboxDispatcher<TDbContext>> _logger;

        public OutboxDispatcher(
            IServiceScopeFactory scopeFactory,
            IOptions<OutboxOptions> options,
            TimeProvider clock,
            ILogger<OutboxDispatcher<TDbContext>> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
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
            _options = options.Value;
            _clock = clock;
            _logger = logger;
        }

        /// <summary>
        /// Setting he thong (admin sua luc chay) neu service co dang ky ISettings; khong
        /// co thi dung OutboxOptions (test, cong cu). Doc moi vong — doi la co tac dung ngay.
        /// </summary>
        private ISettings? Settings()
        {
            if (_settings == null)
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    _settings = scope.ServiceProvider.GetService<ISettings>() ?? (ISettings)KhongCoSetting.Instance;
                }
            }

            return _settings == KhongCoSetting.Instance ? null : _settings;
        }

        private ISettings? _settings;

        private TimeSpan ThoiGianNghi()
        {
            ISettings? s = Settings();
            return s == null ? _options.PollInterval : s.ThoiGian(SettingKeys.OutboxPollInterval);
        }

        private int KichThuocLo()
        {
            ISettings? s = Settings();
            return s == null ? _options.BatchSize : s.SoNguyen(SettingKeys.OutboxBatchSize);
        }

        private TimeSpan TranGianCachThuLai()
        {
            ISettings? s = Settings();
            return s == null ? _options.RetryMaxDelay : s.ThoiGian(SettingKeys.OutboxRetryMaxDelay);
        }

        private TimeSpan ThoiGianChoGui()
        {
            ISettings? s = Settings();
            return s == null ? _options.PublishTimeout : s.ThoiGian(SettingKeys.OutboxPublishTimeout);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "OutboxDispatcher khoi dong: lo {BatchSize} dong, nghi {PollInterval} khi rong",
                KichThuocLo(),
                ThoiGianNghi());

            while (!stoppingToken.IsCancellationRequested)
            {
                int soDaXuLy;

                try
                {
                    soDaXuLy = await XuLyMotLoAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // KHONG de ngoai le thoat khoi vong lap. BackgroundService ma
                    // nem ra ngoai la chet han, va no chet IM LANG — service van
                    // tra ve healthy, chi co dieu khong event nao duoc gui nua.
                    _logger.LogError(
                        ex,
                        "Lo outbox that bai, se thu lai sau {PollInterval}",
                        ThoiGianNghi());

                    soDaXuLy = 0;
                }

                // Lay duoc day lo nghia la con viec — vao lo tiep NGAY, khong nghi.
                // Nho vay luc tai cao PollInterval khong he lam cham.
                if (soDaXuLy >= KichThuocLo())
                {
                    continue;
                }

                try
                {
                    await Task.Delay(ThoiGianNghi(), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("OutboxDispatcher dung.");
        }

        /// <summary>
        /// Doc mot lo, gui tung dong, ghi lai ket qua. Tra ve so dong da xu ly.
        ///
        /// VI SAO PUBLISH NAM BEN TRONG TRANSACTION:
        ///
        /// Neu commit truoc roi moi publish, publish that bai la message MAT
        /// VINH VIEN — da danh dau da gui nhung chua bao gio toi noi.
        /// Neu publish truoc roi moi commit, commit that bai la message duoc
        /// gui LAI o vong sau — trung lap, va consumer da co idempotency de
        /// chiu duoc (manh 3).
        ///
        /// Chon mat an toan: tha trung con hon mat.
        ///
        /// Dieu nay KHONG mau thuan voi VD-M-09 ("khong goi mang trong
        /// transaction"). Cho do lo la GIU KHOA TREN DONG VI khi goi VNPay mat
        /// 30 giay. O day: dong bi khoa la dong outbox ma khong ai khac muon
        /// (SKIP LOCKED cho worker khac di tiep), va publish len RabbitMQ noi
        /// bo chi mat vai mili giay. Con PublishTimeout dat tran cho truong
        /// hop broker treo.
        /// </summary>
        private async Task<int> XuLyMotLoAsync(CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                // BackgroundService la singleton, con DbContext la scoped. Phai tu
                // mo scope moi vong; tiem thang DbContext vao constructor se giu
                // mot context song suot doi ung dung va phinh change tracker.
                TDbContext db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                IOutboxPublisher publisher =
                    scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

                DateTimeOffset bayGio = _clock.GetUtcNow();

                var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                try
                {
                    List<OutboxMessage> lo = await LayLoAsync(db, bayGio, ct)
                        .ConfigureAwait(false);

                    if (lo.Count == 0)
                    {
                        return 0;
                    }

                    foreach (OutboxMessage dong in lo)
                    {
                        await GuiMotDongAsync(publisher, dong, bayGio, ct).ConfigureAwait(false);
                    }

                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);

                    return lo.Count;
                }
                finally
                {
                    await tx.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Lay cac dong cho gui va KHOA chung lai.
        ///
        /// Phai viet SQL tho vi EF Core khong sinh duoc FOR UPDATE SKIP LOCKED.
        ///
        /// SKIP LOCKED la mau chot khi service chay nhieu ban sao: dong dang bi
        /// worker khac giu thi BO QUA de lay dong ke, thay vi dung cho. Khong co
        /// no, ba dispatcher se xep hang sau nhau va scale len 3 ban ma thong
        /// luong van nhu 1.
        ///
        /// ORDER BY id chinh la sap theo thoi diem tao, vi id la UUIDv7.
        /// </summary>
        private Task<List<OutboxMessage>> LayLoAsync(
            TDbContext db, DateTimeOffset bayGio, CancellationToken ct)
        {
            const string Sql =
                "SELECT * FROM outbox " +
                " WHERE published_at IS NULL " +
                "   AND next_attempt_at <= {0} " +
                " ORDER BY id " +
                " LIMIT {1} " +
                "   FOR UPDATE SKIP LOCKED";

            return db.Set<OutboxMessage>()
                .FromSqlRaw(Sql, bayGio, KichThuocLo())
                .ToListAsync(ct);
        }

        /// <summary>
        /// Gui mot dong. That bai thi ghi nhan va lui lich, KHONG nem ra ngoai —
        /// mot message hong khong duoc lam hong ca lo.
        /// </summary>
        private async Task GuiMotDongAsync(
            IOutboxPublisher publisher,
            OutboxMessage dong,
            DateTimeOffset bayGio,
            CancellationToken ct)
        {
            using (var timeout = new CancellationTokenSource(ThoiGianChoGui()))
            using (var ketHop = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
            {
                try
                {
                    await publisher.PublishAsync(dong, ketHop.Token).ConfigureAwait(false);

                    dong.MarkPublished(bayGio);

                    _logger.LogDebug(
                        "Da gui {EventType} {EventId} (correlation {CorrelationId})",
                        dong.EventType,
                        dong.Id,
                        dong.CorrelationId);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    dong.MarkFailed(ex.Message, bayGio, TranGianCachThuLai());

                    _logger.LogWarning(
                        ex,
                        "Gui {EventType} {EventId} that bai lan {AttemptCount}, thu lai luc {NextAttemptAt}",
                        dong.EventType,
                        dong.Id,
                        dong.AttemptCount,
                        dong.NextAttemptAt);
                }
            }
        }
    }
}
