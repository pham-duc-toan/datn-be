using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crowd.BuildingBlocks.Persistence.Consumers
{
    /// <summary>
    /// Nua NHAN cua bus: mot tien trinh nen nghe MOT queue, doc tung message
    /// thanh EventEnvelope, chay handler DUNG MOT LAN, roi ack.
    ///
    /// Topology tu khai bao khi khoi dong (idempotent):
    ///
    ///   datn.events (topic) ──routing key = eventType──► [queue]  (quorum)
    ///                                                      │ loi qua DeliveryLimit lan
    ///                                                      ▼
    ///   datn.dlx (direct) ──routing key = ten queue──► [queue.dlq]
    ///
    /// Ba ket cuc cua mot message:
    ///   - Doc khong ra (sai hop dong)  → reject, vao DLQ NGAY. Thu lai vo ich.
    ///   - Handler nem loi              → nack + requeue, thu lai toi DeliveryLimit
    ///                                    lan roi vao DLQ. Loi tam thoi (DB mat
    ///                                    ket noi) se tu het o lan sau.
    ///   - Thanh cong / da xu ly roi    → ack.
    ///
    /// Khong bao gio drop im lang: moi message hoac duoc xu ly, hoac nam trong
    /// DLQ cho nguoi xem (VD-D-06).
    /// </summary>
    public sealed class EventConsumer<TDbContext, TPayload, THandler> : BackgroundService
        where TDbContext : DbContext
        where TPayload : class, IEventPayload
        where THandler : class, IEventProcessor<TPayload>
    {
        private static readonly TimeSpan ChoNoiLai = TimeSpan.FromSeconds(5);

        private readonly string _tenQueue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RabbitMqOptions _rabbit;
        private readonly EventConsumerOptions _options;
        private readonly ILogger<EventConsumer<TDbContext, TPayload, THandler>> _logger;

        private IConnection? _ketNoi;
        private IChannel? _kenh;

        public EventConsumer(
            string tenQueue,
            IServiceScopeFactory scopeFactory,
            IOptions<RabbitMqOptions> rabbit,
            IOptions<EventConsumerOptions> options,
            ILogger<EventConsumer<TDbContext, TPayload, THandler>> logger)
        {
            if (string.IsNullOrWhiteSpace(tenQueue))
            {
                throw new ArgumentException("ten queue khong duoc rong", nameof(tenQueue));
            }

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

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _tenQueue = tenQueue;
            _scopeFactory = scopeFactory;
            _rabbit = rabbit.Value;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Chi can KET NOI THANH CONG MOT LAN. Sau do RabbitMQ.Client tu noi
            // lai va tu dang ky lai consumer khi broker restart
            // (AutomaticRecoveryEnabled mac dinh bat).
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await BatDauNgheAsync(stoppingToken).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Consumer {Queue} chua noi duoc RabbitMQ, thu lai sau {Delay}",
                        _tenQueue,
                        ChoNoiLai);

                    await DongAsync().ConfigureAwait(false);

                    try
                    {
                        await Task.Delay(ChoNoiLai, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            // Giu tien trinh song toi khi ung dung tat. Viec that nam o callback.
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ung dung dang tat — binh thuong
            }
        }

        private async Task BatDauNgheAsync(CancellationToken ct)
        {
            ConnectionFactory factory = new ConnectionFactory();
            factory.HostName = _rabbit.Host;
            factory.Port = _rabbit.Port;
            factory.UserName = _rabbit.UserName;
            factory.Password = _rabbit.Password;
            factory.VirtualHost = _rabbit.VirtualHost;

            _ketNoi = await factory.CreateConnectionAsync(_tenQueue, ct).ConfigureAwait(false);
            _kenh = await _ketNoi.CreateChannelAsync(null, ct).ConfigureAwait(false);

            await KhaiBaoTopologyAsync(_kenh, ct).ConfigureAwait(false);

            await _kenh.BasicQosAsync(0, _options.PrefetchCount, false, ct).ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(_kenh);
            consumer.ReceivedAsync += KhiNhanMessageAsync;

            // autoAck: false — broker chi xoa message khi TA ack. Chet giua chung
            // thi message duoc giao lai cho lan sau.
            await _kenh.BasicConsumeAsync(_tenQueue, false, consumer, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Consumer {Queue} dang nghe {EventType}",
                _tenQueue,
                TPayload.EventType);
        }

        private async Task KhaiBaoTopologyAsync(IChannel kenh, CancellationToken ct)
        {
            await kenh.ExchangeDeclareAsync(
                    exchange: _rabbit.Exchange,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            await kenh.ExchangeDeclareAsync(
                    exchange: _options.DeadLetterExchange,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            string tenDlq = _tenQueue + ".dlq";

            await kenh.QueueDeclareAsync(
                    queue: tenDlq,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            await kenh.QueueBindAsync(tenDlq, _options.DeadLetterExchange, _tenQueue, null, false, ct)
                .ConfigureAwait(false);

            // Quorum queue: sao chep an toan va, quan trong nhat o day, TU DEM so
            // lan giao lai. Vuot x-delivery-limit thi broker tu day sang DLX.
            Dictionary<string, object?> thamSo = new Dictionary<string, object?>();
            thamSo["x-queue-type"] = "quorum";
            thamSo["x-delivery-limit"] = _options.DeliveryLimit;
            thamSo["x-dead-letter-exchange"] = _options.DeadLetterExchange;
            thamSo["x-dead-letter-routing-key"] = _tenQueue;

            await kenh.QueueDeclareAsync(
                    queue: _tenQueue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: thamSo,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            // Routing key = eventType: queue nay chi nhan dung MOT loai event.
            await kenh.QueueBindAsync(_tenQueue, _rabbit.Exchange, TPayload.EventType, null, false, ct)
                .ConfigureAwait(false);
        }

        private async Task KhiNhanMessageAsync(object sender, BasicDeliverEventArgs ea)
        {
            IChannel kenh = ((AsyncEventingBasicConsumer)sender).Channel;
            CancellationToken ct = ea.CancellationToken;

            EventEnvelope<TPayload> envelope;

            try
            {
                envelope = EventEnvelope.Deserialize<TPayload>(ea.Body.ToArray());
            }
            catch (EventContractException ex)
            {
                // Sai hop dong thi thu lai bao nhieu lan cung sai. Vao DLQ ngay.
                _logger.LogError(
                    ex,
                    "Queue {Queue}: message {MessageId} sai hop dong, dua vao DLQ",
                    _tenQueue,
                    ea.BasicProperties.MessageId);

                await kenh.BasicRejectAsync(ea.DeliveryTag, false, ct).ConfigureAwait(false);
                return;
            }

            try
            {
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    IIdempotencyGuard guard = scope.ServiceProvider.GetRequiredService<IIdempotencyGuard>();
                    THandler handler = scope.ServiceProvider.GetRequiredService<THandler>();

                    // Ten queue lam ten handler trong processed_events: mot event co
                    // the duoc NHIEU queue cua cung service nghe, moi queue xu ly
                    // mot lan rieng.
                    bool vuaXuLy = await guard.XuLyMotLanAsync(
                            envelope.EventId,
                            _tenQueue,
                            token => handler.XuLyAsync(envelope, token),
                            ct)
                        .ConfigureAwait(false);

                    if (!vuaXuLy)
                    {
                        _logger.LogInformation(
                            "Queue {Queue}: event {EventId} da xu ly tu truoc, bo qua",
                            _tenQueue,
                            envelope.EventId);
                    }
                }

                await kenh.BasicAckAsync(ea.DeliveryTag, false, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Queue {Queue}: xu ly event {EventId} that bai, tra lai hang doi",
                    _tenQueue,
                    envelope.EventId);

                await kenh.BasicNackAsync(ea.DeliveryTag, false, true, ct).ConfigureAwait(false);
            }
        }

        private async Task DongAsync()
        {
            if (_kenh != null)
            {
                await _kenh.DisposeAsync().ConfigureAwait(false);
                _kenh = null;
            }

            if (_ketNoi != null)
            {
                await _ketNoi.DisposeAsync().ConfigureAwait(false);
                _ketNoi = null;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
            await DongAsync().ConfigureAwait(false);
        }
    }
}
