using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crowd.Gateway.Api.Sessions
{
    /// <summary>Dia chi identity-svc de nap lai moc thu hoi luc khoi dong (muc "SessionRevocation").</summary>
    public sealed class ThuHoiPhienOptions
    {
        public const string SectionName = "SessionRevocation";

        public string IdentityUrl { get; set; } = "http://localhost:8101";
    }

    /// <summary>
    /// Giu ThuHoiPhienStore luon moi:
    ///   1. Noi RabbitMQ, nghe user.sessions_revoked qua queue tam rieng (tu xoa khi tat) — giong
    ///      SettingsInMemoryListener.
    ///   2. SAU KHI da nghe, xin identity danh sach moc con tac dung (GET /internal/session-revocations)
    ///      — nghe truoc roi moi xin nen khong lot moc nao xay ra giua hai buoc. Identity chua len thi
    ///      thu lai moi 10 giay.
    ///   3. Moi phut don moc het tac dung.
    /// Ghi theo "giu moc muon nhat" nen nhan trung / lech thu tu van dung, khong can chong trung.
    /// </summary>
    public sealed class ThuHoiPhienListener : BackgroundService
    {
        private static readonly TimeSpan ChoThuLai = TimeSpan.FromSeconds(10);

        private readonly ThuHoiPhienStore _store;
        private readonly RabbitMqOptions _rabbit;
        private readonly ThuHoiPhienOptions _options;
        private readonly InternalApiOptions _noiBo;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<ThuHoiPhienListener> _logger;
        private IConnection? _ketNoi;
        private IChannel? _kenh;

        public ThuHoiPhienListener(
            ThuHoiPhienStore store,
            IOptions<RabbitMqOptions> rabbit,
            IOptions<ThuHoiPhienOptions> options,
            IOptions<InternalApiOptions> noiBo,
            IHttpClientFactory http,
            ILogger<ThuHoiPhienListener> logger)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (rabbit == null)
            {
                throw new ArgumentNullException(nameof(rabbit));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (noiBo == null)
            {
                throw new ArgumentNullException(nameof(noiBo));
            }

            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _store = store;
            _rabbit = rabbit.Value;
            _options = options.Value;
            _noiBo = noiBo.Value;
            _http = http;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await NoiVaNgheAsync(stoppingToken);
                    await NapLaiMocAsync(stoppingToken);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        _store.DonMocCu();
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Mat ket noi nghe thu hoi phien, noi lai sau {Giay} giay", ChoThuLai.TotalSeconds);
                    await DongAsync();
                    try
                    {
                        await Task.Delay(ChoThuLai, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            await DongAsync();
        }

        private async Task NoiVaNgheAsync(CancellationToken ct)
        {
            ConnectionFactory f = new ConnectionFactory();
            f.HostName = _rabbit.Host;
            f.Port = _rabbit.Port;
            f.UserName = _rabbit.UserName;
            f.Password = _rabbit.Password;
            f.VirtualHost = _rabbit.VirtualHost;

            _ketNoi = await f.CreateConnectionAsync("gateway.session-revocations", ct);
            _kenh = await _ketNoi.CreateChannelAsync(null, ct);
            await _kenh.ExchangeDeclareAsync(_rabbit.Exchange, ExchangeType.Topic, true, false, null, false, false, ct);

            QueueDeclareOk q = await _kenh.QueueDeclareAsync(string.Empty, false, true, true, null, false, false, ct);
            await _kenh.QueueBindAsync(q.QueueName, _rabbit.Exchange, SessionsRevoked.EventType, null, false, ct);

            AsyncEventingBasicConsumer c = new AsyncEventingBasicConsumer(_kenh);
            c.ReceivedAsync += KhiNhanAsync;
            await _kenh.BasicConsumeAsync(q.QueueName, true, c, ct);
        }

        private async Task NapLaiMocAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    HttpClient client = _http.CreateClient();
                    using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, _options.IdentityUrl.TrimEnd('/') + "/internal/session-revocations"))
                    {
                        req.Headers.Add(InternalApiHeaders.Key, _noiBo.Key);
                        using (HttpResponseMessage res = await client.SendAsync(req, ct))
                        {
                            res.EnsureSuccessStatusCode();
                            List<MocDto>? ds = await res.Content.ReadFromJsonAsync<List<MocDto>>(CrowdJson.Options, ct);
                            int so = 0;
                            if (ds != null)
                            {
                                foreach (MocDto m in ds)
                                {
                                    _store.Ghi(m.UserId, m.RevokedAt);
                                    so = so + 1;
                                }
                            }

                            _logger.LogInformation("Thu hoi phien: nap lai {So} moc tu identity, dang nghe user.sessions_revoked", so);
                            return;
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("Chua nap duoc moc thu hoi tu identity ({Loi}) — thu lai sau {Giay} giay", ex.Message, ChoThuLai.TotalSeconds);
                }

                await Task.Delay(ChoThuLai, ct);
            }
        }

        private Task KhiNhanAsync(object sender, BasicDeliverEventArgs ea)
        {
            try
            {
                SessionsRevoked p = EventEnvelope.Deserialize<SessionsRevoked>(ea.Body.ToArray()).Payload;
                _store.Ghi(p.UserId, p.RevokedAt);
            }
            catch (EventContractException ex)
            {
                _logger.LogError(ex, "Message user.sessions_revoked sai hop dong — bo qua");
            }

            return Task.CompletedTask;
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

        private sealed class MocDto
        {
            public Guid UserId { get; init; }

            public DateTimeOffset RevokedAt { get; init; }
        }
    }
}
