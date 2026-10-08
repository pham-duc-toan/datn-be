using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crowd.Settings
{
    /// <summary>
    /// Ban dong bo setting cho tien trinh KHONG co database (gateway): nghe
    /// setting.changed / settings.snapshot qua mot queue rieng tam thoi (tu xoa khi tat),
    /// ghi thang vao bo nho. Luc noi xin admin-svc phat lai toan bo.
    ///
    /// Khong can chong xu ly trung: ghi theo version nen giao lai hai lan van dung.
    /// </summary>
    public sealed class SettingsInMemoryListener : BackgroundService
    {
        private readonly SettingsStore _store;
        private readonly RabbitMqOptions _rabbit;
        private readonly string _tenService;
        private readonly ILogger<SettingsInMemoryListener> _logger;
        private IConnection? _ketNoi;
        private IChannel? _kenh;

        public SettingsInMemoryListener(
            SettingsStore store, IOptions<RabbitMqOptions> rabbit, string tenService, ILogger<SettingsInMemoryListener> logger)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (rabbit == null)
            {
                throw new ArgumentNullException(nameof(rabbit));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _store = store;
            _rabbit = rabbit.Value;
            _tenService = tenService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await NoiVaNgheAsync(stoppingToken);
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Mat ket noi nghe setting, noi lai sau 5 giay");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            if (_kenh != null)
            {
                await _kenh.DisposeAsync();
            }

            if (_ketNoi != null)
            {
                await _ketNoi.DisposeAsync();
            }
        }

        private async Task NoiVaNgheAsync(CancellationToken ct)
        {
            ConnectionFactory f = new ConnectionFactory();
            f.HostName = _rabbit.Host;
            f.Port = _rabbit.Port;
            f.UserName = _rabbit.UserName;
            f.Password = _rabbit.Password;
            f.VirtualHost = _rabbit.VirtualHost;

            _ketNoi = await f.CreateConnectionAsync(_tenService + ".settings", ct);
            _kenh = await _ketNoi.CreateChannelAsync(null, ct);
            await _kenh.ExchangeDeclareAsync(_rabbit.Exchange, ExchangeType.Topic, true, false, null, false, false, ct);

            // Queue rieng cua tien trinh nay: ten do broker dat, xoa khi ngat ket noi.
            QueueDeclareOk q = await _kenh.QueueDeclareAsync(string.Empty, false, true, true, null, false, false, ct);
            await _kenh.QueueBindAsync(q.QueueName, _rabbit.Exchange, SettingChanged.EventType, null, false, ct);
            await _kenh.QueueBindAsync(q.QueueName, _rabbit.Exchange, SettingsSnapshot.EventType, null, false, ct);

            AsyncEventingBasicConsumer c = new AsyncEventingBasicConsumer(_kenh);
            c.ReceivedAsync += KhiNhanAsync;
            await _kenh.BasicConsumeAsync(q.QueueName, true, c, ct);

            // Xin phat lai toan bo (khong co outbox — gui thang, mat thi lan snapshot dinh ky bu).
            EventEnvelope<SettingsSnapshotRequested> xin = EventEnvelope.Create(
                _tenService, Guid.CreateVersion7(), new SettingsSnapshotRequested { Service = _tenService });
            BasicProperties p = new BasicProperties { ContentType = "application/json", Type = SettingsSnapshotRequested.EventType };
            await _kenh.BasicPublishAsync(_rabbit.Exchange, SettingsSnapshotRequested.EventType, false, p,
                Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(xin, CrowdJson.Options)), ct);

            _logger.LogInformation("Dang nghe setting (khong DB), da xin admin-svc phat lai");
        }

        private Task KhiNhanAsync(object sender, BasicDeliverEventArgs ea)
        {
            try
            {
                if (ea.RoutingKey == SettingChanged.EventType)
                {
                    SettingChanged p = EventEnvelope.Deserialize<SettingChanged>(ea.Body.ToArray()).Payload;
                    _store.ApDung(p.Key, p.Value.Json, p.SettingVersion);
                }
                else if (ea.RoutingKey == SettingsSnapshot.EventType)
                {
                    IReadOnlyList<SettingSnapshotItem> items = EventEnvelope.Deserialize<SettingsSnapshot>(ea.Body.ToArray()).Payload.Items;
                    foreach (SettingSnapshotItem i in items)
                    {
                        _store.ApDung(i.Key, i.Value.Json, i.SettingVersion);
                    }
                }
            }
            catch (EventContractException ex)
            {
                _logger.LogError(ex, "Message setting sai hop dong — bo qua");
            }

            return Task.CompletedTask;
        }
    }
}
