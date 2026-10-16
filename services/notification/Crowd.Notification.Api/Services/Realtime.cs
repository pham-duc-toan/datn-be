using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Notification.Api.Dtos;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Crowd.Notification.Api.Services
{
    /// <summary>
    /// Cac WebSocket dang mo tren INSTANCE nay, theo nguoi dung. Mot nguoi nhieu tab = nhieu ket noi.
    /// </summary>
    public sealed class RealtimeHub
    {
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, WebSocket>> _ketNoi =
            new ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, WebSocket>>();

        public Guid Them(Guid userId, WebSocket ws)
        {
            Guid id = Guid.NewGuid();
            _ketNoi.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, WebSocket>())[id] = ws;
            return id;
        }

        public void Bo(Guid userId, Guid id)
        {
            ConcurrentDictionary<Guid, WebSocket>? ds;
            if (_ketNoi.TryGetValue(userId, out ds))
            {
                WebSocket? bo;
                ds.TryRemove(id, out bo);
                if (ds.IsEmpty)
                {
                    _ketNoi.TryRemove(userId, out ds);
                }
            }
        }

        public bool CoKetNoi(Guid userId)
        {
            ConcurrentDictionary<Guid, WebSocket>? ds;
            return _ketNoi.TryGetValue(userId, out ds) && !ds.IsEmpty;
        }

        public int SoKetNoi
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<Guid, ConcurrentDictionary<Guid, WebSocket>> k in _ketNoi)
                {
                    n += k.Value.Count;
                }

                return n;
            }
        }

        /// <summary>Gui mot khung text toi moi ket noi cua nguoi nay; ket noi hong thi bo.</summary>
        public async Task GuiAsync(Guid userId, string json, CancellationToken ct)
        {
            ConcurrentDictionary<Guid, WebSocket>? ds;
            if (!_ketNoi.TryGetValue(userId, out ds))
            {
                return;
            }

            byte[] du = Encoding.UTF8.GetBytes(json);
            foreach (KeyValuePair<Guid, WebSocket> k in ds)
            {
                try
                {
                    if (k.Value.State == WebSocketState.Open)
                    {
                        await k.Value.SendAsync(new ArraySegment<byte>(du), WebSocketMessageType.Text, true, ct);
                    }
                    else
                    {
                        Bo(userId, k.Key);
                    }
                }
                catch (WebSocketException)
                {
                    Bo(userId, k.Key);
                }
            }
        }
    }

    /// <summary>
    /// LISTEN thong_bao_moi (ThongBaoWriter pg_notify khi ghi thong bao, phat luc COMMIT): moi instance nhan
    /// MOI thong bao moi, chi day toi WebSocket nguoi nhan neu nguoi do dang ket noi vao CHINH instance nay.
    /// Nhieu instance sau load balancer khong can Redis / sticky session. Mat ket noi DB thi noi lai; thong
    /// bao trong luc mat van nam trong DB (frontend tai lai danh sach khi WebSocket mo lai).
    /// </summary>
    public sealed class RealtimeListener : BackgroundService
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly string _chuoiKetNoi;
        private readonly RealtimeHub _hub;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RealtimeListener> _logger;

        public RealtimeListener(IConfiguration cfg, RealtimeHub hub, IServiceScopeFactory scopeFactory, ILogger<RealtimeListener> logger)
        {
            if (cfg == null)
            {
                throw new ArgumentNullException(nameof(cfg));
            }

            if (hub == null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _chuoiKetNoi = cfg.GetConnectionString("NotificationDb") ?? throw new InvalidOperationException("Thieu ConnectionStrings:NotificationDb.");
            _hub = hub;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using (NpgsqlConnection kn = new NpgsqlConnection(_chuoiKetNoi))
                    {
                        await kn.OpenAsync(stoppingToken);
                        kn.Notification += KhiCoThongBao;
                        await using (NpgsqlCommand lenh = new NpgsqlCommand("LISTEN " + ThongBaoWriter.KenhRealtime, kn))
                        {
                            await lenh.ExecuteNonQueryAsync(stoppingToken);
                        }

                        _logger.LogInformation("Realtime: LISTEN {Kenh}", ThongBaoWriter.KenhRealtime);
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            await kn.WaitAsync(stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Realtime: mat ket noi LISTEN, noi lai sau 3 giay");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private void KhiCoThongBao(object sender, NpgsqlNotificationEventArgs e)
        {
            Guid userId;
            Guid thongBaoId;
            if (!TachNoiDung(e.Payload, out userId, out thongBaoId) || !_hub.CoKetNoi(userId))
            {
                return;
            }

            // Khong chan vong WaitAsync: doc DB + gui WebSocket o task rieng.
            _ = DayAsync(userId, thongBaoId);
        }

        /// <summary>"userIdN:thongBaoIdN" → hai Guid. Ham thuan — test duoc.</summary>
        public static bool TachNoiDung(string noiDung, out Guid userId, out Guid thongBaoId)
        {
            userId = Guid.Empty;
            thongBaoId = Guid.Empty;
            if (string.IsNullOrEmpty(noiDung))
            {
                return false;
            }

            string[] p = noiDung.Split(':');
            return p.Length == 2 && Guid.TryParseExact(p[0], "N", out userId) && Guid.TryParseExact(p[1], "N", out thongBaoId);
        }

        private async Task DayAsync(Guid userId, Guid thongBaoId)
        {
            try
            {
                ThongBao? t;
                using (IServiceScope scope = _scopeFactory.CreateScope())
                {
                    NotificationDbContext db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                    t = await db.ThongBaos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == thongBaoId && x.UserId == userId);
                }

                if (t == null)
                {
                    return;
                }

                NotificationResponse r = new NotificationResponse
                {
                    Id = t.Id,
                    Type = t.Type,
                    Title = t.Title,
                    Body = t.Body,
                    Link = t.Link,
                    CreatedAt = t.CreatedAt,
                    ReadAt = t.ReadAt,
                };
                string json = JsonSerializer.Serialize(new RealtimeMessage { Kind = "notification", Notification = r }, Json);
                await _hub.GuiAsync(userId, json, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Realtime: day thong bao {Id} loi", thongBaoId);
            }
        }
    }
}
