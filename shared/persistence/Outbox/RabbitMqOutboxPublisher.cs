using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Hien thuc IOutboxPublisher bang RabbitMQ.Client truc tiep.
    ///
    /// VI SAO KHONG DUNG MASSTRANSIT O DAY: dispatcher da co san dung khoi byte
    /// can gui (envelope_json) va dung routing key (event_type). Viec cua
    /// MassTransit la serialize va dinh tuyen THEO KIEU .NET — hai thu ta da
    /// tu lam xong. Nhet vao no thi phai thao nguoc envelope ra kieu C#, ma
    /// dispatcher lai co y khong biet payload la kieu gi.
    /// MassTransit van dung o phia CONSUMER va cho saga.
    ///
    /// Dang ky SINGLETON: giu mot ket noi TCP va mot channel dung lai mai.
    /// Mo ket noi moi cho tung message se giet hieu nang — bat tay TCP cong
    /// AMQP ton hang chuc mili giay.
    /// </summary>
    public sealed class RabbitMqOutboxPublisher : IOutboxPublisher, IAsyncDisposable
    {
        private readonly RabbitMqOptions _options;
        private readonly ILogger<RabbitMqOutboxPublisher> _logger;

        /// <summary>
        /// Chi cho MOT message duoc publish tai mot thoi diem.
        ///
        /// IChannel cua RabbitMQ.Client khong an toan khi nhieu luong cung ghi.
        /// Hien tai moi service chi co mot dispatcher nen hiem khi tranh chap,
        /// nhung khoa nay lam dieu do thanh dam bao chu khong phai may man.
        /// </summary>
        private readonly SemaphoreSlim _khoa = new SemaphoreSlim(1, 1);

        private IConnection? _ketNoi;
        private IChannel? _kenh;

        public RabbitMqOutboxPublisher(
            IOptions<RabbitMqOptions> options,
            ILogger<RabbitMqOutboxPublisher> logger)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _options = options.Value;
            _logger = logger;
        }

        public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            await _khoa.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                IChannel kenh = await LayKenhAsync(ct).ConfigureAwait(false);

                BasicProperties thuocTinh = new BasicProperties();

                // Persistent: message duoc ghi xuong dia cua broker. Khong co
                // dong nay, RabbitMQ restart la mat sach message trong queue —
                // dung luc do outbox da danh dau published_at roi.
                thuocTinh.Persistent = true;
                thuocTinh.ContentType = "application/json";

                // Ba truong duoi chi de tra cuu tren giao dien RabbitMQ va trong
                // log; nghiep vu khong doc chung, moi thu that nam trong body.
                thuocTinh.MessageId = message.Id.ToString();
                thuocTinh.CorrelationId = message.CorrelationId.ToString();
                thuocTinh.Type = message.EventType;

                byte[] than = Encoding.UTF8.GetBytes(message.EnvelopeJson);

                // Channel duoc tao voi publisher confirms, nen await o day chi
                // tra ve KHI BROKER DA XAC NHAN ghi nhan message. Day chinh la
                // dieu kien IOutboxPublisher doi hoi.
                await kenh.BasicPublishAsync(
                        exchange: _options.Exchange,
                        routingKey: message.EventType,
                        mandatory: false,
                        basicProperties: thuocTinh,
                        body: than,
                        cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                _khoa.Release();
            }
        }

        /// <summary>
        /// Lay channel dang dung, tao moi neu chua co hoac da chet.
        ///
        /// Ket noi co the dut khi broker restart. Lan publish ke tiep se thay
        /// channel dong va tu dung lai — dispatcher chi thay mot lan that bai
        /// roi lui lich, khong can biet gi them.
        /// </summary>
        private async Task<IChannel> LayKenhAsync(CancellationToken ct)
        {
            if (_kenh != null && _kenh.IsOpen)
            {
                return _kenh;
            }

            await DongKetNoiCuAsync().ConfigureAwait(false);

            ConnectionFactory factory = new ConnectionFactory();
            factory.HostName = _options.Host;
            factory.Port = _options.Port;
            factory.UserName = _options.UserName;
            factory.Password = _options.Password;
            factory.VirtualHost = _options.VirtualHost;

            _ketNoi = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);

            // publisherConfirmationsEnabled: yeu cau broker gui xac nhan.
            // publisherConfirmationTrackingEnabled: thu vien tu doi chieu xac
            // nhan voi tung lan publish, nho vay BasicPublishAsync moi cho duoc.
            CreateChannelOptions tuyChon = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            _kenh = await _ketNoi.CreateChannelAsync(tuyChon, ct).ConfigureAwait(false);

            // Khai bao exchange moi lan noi lai. Thao tac nay idempotent: da ton
            // tai voi cung tham so thi khong lam gi. Nho vay khong phai dung
            // script rieng de dung ha tang truoc khi chay service.
            await _kenh.ExchangeDeclareAsync(
                    exchange: _options.Exchange,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Da noi RabbitMQ {Host}:{Port}, exchange {Exchange}",
                _options.Host,
                _options.Port,
                _options.Exchange);

            return _kenh;
        }

        private async Task DongKetNoiCuAsync()
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

        public async ValueTask DisposeAsync()
        {
            await DongKetNoiCuAsync().ConfigureAwait(false);
            _khoa.Dispose();
        }
    }
}
