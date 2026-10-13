using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Persistence;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crowd.Admin.Api.Workers
{
    /// <summary>
    /// Nhat ky audit (FP-06, VD-S-10): nghe MOI event tren bus (routing key "#") va ghi ban sao chi
    /// ghi them vao audit_events. Moi thao tac co nghia cua he thong deu da di qua outbox thanh event
    /// kem actor (ai lam, vai tro nao) — nen day la nhat ky day du ma khong phai sua service nao.
    ///
    /// Cung topology voi EventConsumer dung chung: quorum queue, gioi han so lan giao, DLQ rieng.
    /// KHONG dung EventConsumer vi no doc MOT loai payload co kieu; o day doc envelope tho (JSON).
    /// Idempotent bang khoa chinh event_id (ON CONFLICT DO NOTHING) — khong can processed_events.
    /// Loai event luu luong lon (setting admin.audit_excluded_types) duoc ack ma khong ghi.
    /// </summary>
    public sealed class AuditConsumer : BackgroundService
    {
        public const string TenQueue = "admin-svc.audit";

        /// <summary>Payload lon hon nguong nay chi ghi ghi chu kich thuoc (vd dataset.ingested hang nghin mau).</summary>
        public const int PayloadToiDa = 64 * 1024;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RabbitMqOptions _rabbit;
        private readonly EventConsumerOptions _options;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;
        private readonly ILogger<AuditConsumer> _logger;
        private IConnection? _ketNoi;
        private IChannel? _kenh;

        public AuditConsumer(
            IServiceScopeFactory scopeFactory,
            IOptions<RabbitMqOptions> rabbit,
            IOptions<EventConsumerOptions> options,
            ISettings settings,
            TimeProvider clock,
            ILogger<AuditConsumer> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (rabbit == null)
            {
                throw new ArgumentNullException(nameof(rabbit));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
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
            _rabbit = rabbit.Value;
            _options = options.Value;
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
                    await NgheAsync(stoppingToken);
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Audit: mat ket noi RabbitMQ, noi lai sau");
                    await DongAsync();
                    try
                    {
                        await Task.Delay(_settings.ThoiGian(SettingKeys.ConsumersReconnectDelay), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            await DongAsync();
        }

        private async Task NgheAsync(CancellationToken ct)
        {
            ConnectionFactory f = new ConnectionFactory();
            f.HostName = _rabbit.Host;
            f.Port = _rabbit.Port;
            f.UserName = _rabbit.UserName;
            f.Password = _rabbit.Password;
            f.VirtualHost = _rabbit.VirtualHost;

            _ketNoi = await f.CreateConnectionAsync(TenQueue, ct);
            _kenh = await _ketNoi.CreateChannelAsync(null, ct);

            await _kenh.ExchangeDeclareAsync(_rabbit.Exchange, ExchangeType.Topic, true, false, null, false, false, ct);
            await _kenh.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Direct, true, false, null, false, false, ct);
            await _kenh.QueueDeclareAsync(TenQueue + ".dlq", true, false, false, null, false, false, ct);
            await _kenh.QueueBindAsync(TenQueue + ".dlq", _options.DeadLetterExchange, TenQueue, null, false, ct);

            Dictionary<string, object?> thamSo = new Dictionary<string, object?>();
            thamSo["x-queue-type"] = "quorum";
            thamSo["x-delivery-limit"] = _options.DeliveryLimit;
            thamSo["x-dead-letter-exchange"] = _options.DeadLetterExchange;
            thamSo["x-dead-letter-routing-key"] = TenQueue;
            await _kenh.QueueDeclareAsync(TenQueue, true, false, false, thamSo, false, false, ct);

            // "#" = moi routing key (moi loai event).
            await _kenh.QueueBindAsync(TenQueue, _rabbit.Exchange, "#", null, false, ct);
            await _kenh.BasicQosAsync(0, (ushort)_settings.SoNguyen(SettingKeys.ConsumersPrefetchCount), false, ct);

            AsyncEventingBasicConsumer c = new AsyncEventingBasicConsumer(_kenh);
            c.ReceivedAsync += KhiNhanAsync;
            await _kenh.BasicConsumeAsync(TenQueue, false, c, ct);
            _logger.LogInformation("Audit: dang nghe moi event (queue {Queue})", TenQueue);
        }

        private async Task KhiNhanAsync(object sender, BasicDeliverEventArgs ea)
        {
            IChannel kenh = ((AsyncEventingBasicConsumer)sender).Channel;
            CancellationToken ct = ea.CancellationToken;

            DongAudit? dong;
            try
            {
                dong = DocEnvelope(ea.Body.ToArray());
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                _logger.LogError(ex, "Audit: message khong phai envelope hop le, dua vao DLQ");
                await kenh.BasicRejectAsync(ea.DeliveryTag, false, ct);
                return;
            }

            try
            {
                if (!BiLoaiTru(dong.EventType))
                {
                    await GhiAsync(dong, ct);
                }

                await kenh.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit: ghi event {EventId} that bai, tra lai hang doi", dong.EventId);
                await kenh.BasicNackAsync(ea.DeliveryTag, false, true, ct);
            }
        }

        private async Task GhiAsync(DongAudit d, CancellationToken ct)
        {
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                AdminDbContext db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO audit_events (event_id, event_type, producer, occurred_at, correlation_id, causation_id, actor_id, actor_role, payload, received_at)
                    VALUES ({d.EventId}, {d.EventType}, {d.Producer}, {d.OccurredAt}, {d.CorrelationId}, {d.CausationId},
                            {d.ActorId}, {d.ActorRole}, CAST({d.PayloadJson} AS jsonb), {_clock.GetUtcNow()})
                    ON CONFLICT (event_id) DO NOTHING", ct);
            }
        }

        private bool BiLoaiTru(string eventType)
        {
            foreach (string loai in _settings.Chuoi(SettingKeys.AdminAuditExcludedTypes).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(loai, eventType, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Doc cac truong chung cua envelope (khong can biet kieu payload). Ham thuan — test duoc.</summary>
        public static DongAudit DocEnvelope(byte[] body)
        {
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                JsonElement goc = doc.RootElement;
                JsonElement actor;
                Guid? actorId = null;
                string? actorRole = null;
                if (goc.TryGetProperty("actor", out actor) && actor.ValueKind == JsonValueKind.Object)
                {
                    actorId = actor.GetProperty("userId").GetGuid();
                    actorRole = actor.GetProperty("role").GetString();
                }

                JsonElement causation;
                Guid? causationId = goc.TryGetProperty("causationId", out causation) && causation.ValueKind == JsonValueKind.String
                    ? causation.GetGuid()
                    : null;

                string payload = goc.GetProperty("payload").GetRawText();
                if (Encoding.UTF8.GetByteCount(payload) > PayloadToiDa)
                {
                    payload = "{\"_truncated\":true,\"bytes\":" + Encoding.UTF8.GetByteCount(payload) + "}";
                }

                return new DongAudit
                {
                    EventId = goc.GetProperty("eventId").GetGuid(),
                    EventType = goc.GetProperty("eventType").GetString() ?? throw new FormatException("thieu eventType"),
                    Producer = goc.GetProperty("producer").GetString() ?? string.Empty,
                    OccurredAt = goc.GetProperty("occurredAt").GetDateTimeOffset(),
                    CorrelationId = goc.GetProperty("correlationId").GetGuid(),
                    CausationId = causationId,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    PayloadJson = payload,
                };
            }
        }

        private async Task DongAsync()
        {
            if (_kenh != null)
            {
                await _kenh.DisposeAsync();
                _kenh = null;
            }

            if (_ketNoi != null)
            {
                await _ketNoi.DisposeAsync();
                _ketNoi = null;
            }
        }
    }

    /// <summary>Cac truong cua envelope can cho audit.</summary>
    public sealed class DongAudit
    {
        public Guid EventId { get; init; }

        public string EventType { get; init; } = string.Empty;

        public string Producer { get; init; } = string.Empty;

        public DateTimeOffset OccurredAt { get; init; }

        public Guid CorrelationId { get; init; }

        public Guid? CausationId { get; init; }

        public Guid? ActorId { get; init; }

        public string? ActorRole { get; init; }

        public string PayloadJson { get; init; } = "{}";
    }
}
