using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Persistence;
using Crowd.Admin.Api.Services;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Admin.Api.Workers
{
    /// <summary>Lan doc gan nhat — controller /admin/ops/queues tra ve, khong goi RabbitMQ moi request.</summary>
    public sealed class TrangThaiDlq
    {
        private readonly object _khoa = new object();
        private IReadOnlyList<KeyValuePair<string, long>> _hangDoi = new List<KeyValuePair<string, long>>();
        private bool _docDuoc;
        private DateTimeOffset? _luc;

        public void Ghi(bool docDuoc, IReadOnlyList<KeyValuePair<string, long>> hangDoi, DateTimeOffset luc)
        {
            lock (_khoa)
            {
                _docDuoc = docDuoc;
                _hangDoi = hangDoi;
                _luc = luc;
            }
        }

        public (bool DocDuoc, IReadOnlyList<KeyValuePair<string, long>> HangDoi, DateTimeOffset? Luc) Doc()
        {
            lock (_khoa)
            {
                return (_docDuoc, _hangDoi, _luc);
            }
        }
    }

    /// <summary>
    /// Canh bao do sau DLQ (VD-D-06, dieu kien bat buoc cua VD-D-12): moi ops.dlq_check_interval doc so message
    /// cua moi queue qua RabbitMQ management API; DLQ (ten ket thuc ".dlq") co tu ops.dlq_alert_threshold
    /// message → log Warning + phat ops.dlq_alert (notification-svc gui email toi ops.alert_email). Nhac lai
    /// sau ops.alert_repeat neu van chua xu ly; DLQ ve 0 thi quen trang thai. Khong doc duoc management API
    /// thi log Error (RabbitMQ sap thi moi service khac cung dang bao loi).
    /// </summary>
    public sealed class DlqMonitorWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _http;
        private readonly RabbitMqOptions _rabbit;
        private readonly string _url;
        private readonly ISettings _settings;
        private readonly TrangThaiDlq _trangThai;
        private readonly TimeProvider _clock;
        private readonly ILogger<DlqMonitorWorker> _logger;
        private readonly Dictionary<string, DateTimeOffset> _daBao = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        public DlqMonitorWorker(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory http,
            IOptions<RabbitMqOptions> rabbit,
            IConfiguration configuration,
            ISettings settings,
            TrangThaiDlq trangThai,
            TimeProvider clock,
            ILogger<DlqMonitorWorker> logger)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (rabbit == null)
            {
                throw new ArgumentNullException(nameof(rabbit));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (trangThai == null)
            {
                throw new ArgumentNullException(nameof(trangThai));
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
            _http = http;
            _rabbit = rabbit.Value;
            _url = configuration["RabbitMqManagement:Url"] ?? ("http://" + _rabbit.Host + ":15672");
            _settings = settings;
            _trangThai = trangThai;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await KiemTraAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _trangThai.Ghi(false, new List<KeyValuePair<string, long>>(), _clock.GetUtcNow());
                    _logger.LogError(ex, "Khong doc duoc RabbitMQ management API ({Url}) — khong biet DLQ co message hay khong", _url);
                }

                try
                {
                    await ChoTheoSetting.ChoAsync(_settings, SettingKeys.OpsDlqCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task KiemTraAsync(CancellationToken ct)
        {
            HttpClient http = _http.CreateClient("rabbitmq-management");
            string vhost = Uri.EscapeDataString(string.IsNullOrEmpty(_rabbit.VirtualHost) ? "/" : _rabbit.VirtualHost);
            using (HttpRequestMessage yc = new HttpRequestMessage(HttpMethod.Get, _url.TrimEnd('/') + "/api/queues/" + vhost + "?columns=name,messages"))
            {
                yc.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_rabbit.UserName + ":" + _rabbit.Password)));
                using (HttpResponseMessage r = await http.SendAsync(yc, ct))
                {
                    r.EnsureSuccessStatusCode();
                    List<KeyValuePair<string, long>> dlq = DocDlq(await r.Content.ReadAsStringAsync(ct));
                    DateTimeOffset bayGio = _clock.GetUtcNow();
                    _trangThai.Ghi(true, dlq, bayGio);
                    await CanhBaoAsync(dlq, bayGio, ct);
                }
            }
        }

        /// <summary>JSON /api/queues → (ten DLQ, so message), DLQ nhieu message nhat truoc. Ham thuan — test duoc.</summary>
        public static List<KeyValuePair<string, long>> DocDlq(string json)
        {
            List<KeyValuePair<string, long>> ra = new List<KeyValuePair<string, long>>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                foreach (JsonElement q in doc.RootElement.EnumerateArray())
                {
                    string? ten = q.GetProperty("name").GetString();
                    if (ten == null || !ten.EndsWith(".dlq", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    JsonElement so;
                    long n = q.TryGetProperty("messages", out so) && so.ValueKind == JsonValueKind.Number ? so.GetInt64() : 0;
                    ra.Add(new KeyValuePair<string, long>(ten, n));
                }
            }

            return ra.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        }

        private async Task CanhBaoAsync(List<KeyValuePair<string, long>> dlq, DateTimeOffset bayGio, CancellationToken ct)
        {
            int nguong = _settings.SoNguyen(SettingKeys.OpsDlqAlertThreshold);
            TimeSpan nhacLai = _settings.ThoiGian(SettingKeys.OpsAlertRepeat);
            List<OpsDlqAlert> canBao = new List<OpsDlqAlert>();
            foreach (KeyValuePair<string, long> q in dlq)
            {
                if (q.Value < nguong)
                {
                    _daBao.Remove(q.Key);
                    continue;
                }

                DateTimeOffset lanTruoc;
                if (_daBao.TryGetValue(q.Key, out lanTruoc) && bayGio - lanTruoc < nhacLai)
                {
                    continue;
                }

                _daBao[q.Key] = bayGio;
                _logger.LogWarning("DLQ {Queue} co {So} message (nguong {Nguong}) — can xu ly / dua lai", q.Key, q.Value, nguong);
                canBao.Add(new OpsDlqAlert { Queue = q.Key, Depth = q.Value, Threshold = nguong });
            }

            if (canBao.Count == 0)
            {
                return;
            }

            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                AdminDbContext db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
                IOutboxWriter outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
                Guid correlationId = Guid.CreateVersion7();
                foreach (OpsDlqAlert a in canBao)
                {
                    outbox.Enqueue(EventEnvelope.Create(SettingService.Producer, correlationId, a, null, null));
                }

                await db.SaveChangesAsync(ct);
            }
        }
    }
}
