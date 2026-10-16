using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Settings;
using Crowd.Notification.Api.Dtos;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Persistence;
using Crowd.Notification.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Notification.Api.Controllers
{
    /// <summary>
    /// Day thong bao realtime qua WebSocket (FC-06) + ngon ngu thong bao (FC-08).
    ///   POST /notifications/me/realtime-ticket   (JWT) → { ticket, expiresAt }
    ///   GET  ws://.../notifications/realtime?ticket=...  → moi thong bao moi la mot khung JSON
    ///        { "kind": "notification", "notification": { id, type, title, body, link, createdAt } }
    ///   GET / PUT /notifications/me/language { "language": "vi" | "en" }
    /// Frontend van giu /me/unread-count lam du phong khi WebSocket rot (proxy cat, mang chap chon).
    /// </summary>
    [ApiController]
    public sealed class RealtimeController : ControllerBase
    {
        private readonly NotificationDbContext _db;
        private readonly RealtimeHub _hub;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public RealtimeController(NotificationDbContext db, RealtimeHub hub, ISettings settings, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (hub == null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _hub = hub;
            _settings = settings;
            _clock = clock;
        }

        [HttpPost("notifications/me/realtime-ticket")]
        [Authorize]
        public async Task<IActionResult> XinVe(CancellationToken ct)
        {
            string token = VeRealtime.TaoToken();
            DateTimeOffset het = _clock.GetUtcNow() + _settings.ThoiGian(SettingKeys.NotificationRealtimeTicketTtl);
            _db.VeRealtimes.Add(VeRealtime.Tao(VeRealtime.Bam(token), CrowdClaims.GetUserId(User), het));
            await _db.SaveChangesAsync(ct);
            return Ok(new RealtimeTicketResponse { Ticket = token, ExpiresAt = het });
        }

        /// <summary>
        /// Mo WebSocket bang ve (dung mot lan). Khong phai yeu cau WebSocket → 400. Ve sai / het han / da dung →
        /// NHAN ket noi roi dong ngay voi ma 4401 "ve_khong_hop_le": tu choi bang HTTP 401 luc bat tay thi
        /// proxy WebSocket (Ocelot) doi thanh 500, client khong phan biet duoc voi loi he thong.
        /// </summary>
        [HttpGet("notifications/realtime")]
        [AllowAnonymous]
        public async Task KetNoi([FromQuery] string? ticket, CancellationToken ct)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            Guid? userId = await DoiVeAsync(ticket, ct);
            using (WebSocket ws = await HttpContext.WebSockets.AcceptWebSocketAsync())
            {
                if (!userId.HasValue)
                {
                    await ws.CloseAsync(MaVeKhongHopLe, "ve_khong_hop_le", CancellationToken.None);
                    return;
                }

                Guid id = _hub.Them(userId.Value, ws);
                try
                {
                    // Chao: frontend biet da ket noi, tai lai so chua doc mot lan.
                    await _hub.GuiAsync(userId.Value, "{\"kind\":\"connected\"}", ct);
                    byte[] dem = new byte[1024];
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        // Client khong can gui gi; doc de nhan khung dong / ping.
                        WebSocketReceiveResult r = await ws.ReceiveAsync(new ArraySegment<byte>(dem), ct);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        }
                    }
                }
                catch (WebSocketException)
                {
                    // client rot ket noi
                }
                catch (OperationCanceledException)
                {
                    // dang tat service / request bi huy
                }
                finally
                {
                    _hub.Bo(userId.Value, id);
                }
            }
        }

        /// <summary>Ma dong WebSocket (khoang 4000-4999 danh cho ung dung) khi ve khong hop le.</summary>
        public const WebSocketCloseStatus MaVeKhongHopLe = (WebSocketCloseStatus)4401;

        /// <summary>Doi ve lay userId — xoa ngay khi doi (DELETE ... RETURNING): hai ket noi cung ve chi mot thang.</summary>
        private async Task<Guid?> DoiVeAsync(string? ticket, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ticket) || ticket.Length > 100)
            {
                return null;
            }

            string bam = VeRealtime.Bam(ticket);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            System.Collections.Generic.List<Guid> ra = await _db.Database
                .SqlQuery<Guid>($"DELETE FROM realtime_tickets WHERE token_hash = {bam} AND expires_at > {bayGio} RETURNING user_id AS \"Value\"")
                .ToListAsync(ct);
            return ra.Count == 1 ? ra[0] : null;
        }

        // ---- ngon ngu (FC-08) ----

        [HttpGet("notifications/me/language")]
        [Authorize]
        public async Task<IActionResult> NgonNguHienTai(CancellationToken ct)
        {
            Guid uid = CrowdClaims.GetUserId(User);
            NgonNguNguoiDung? n = await _db.NgonNgus.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid, ct);
            return Ok(new LanguageResponse { Language = n == null ? NgonNgu.TiengViet : n.Language });
        }

        [HttpPut("notifications/me/language")]
        [Authorize]
        public async Task<IActionResult> DatNgonNgu([FromBody] LanguageResponse body, CancellationToken ct)
        {
            string ma = body == null || body.Language == null ? string.Empty : body.Language.Trim().ToLowerInvariant();
            if (!NgonNgu.HopLe(ma))
            {
                return Problem(statusCode: 400, title: "ngon_ngu_khong_hop_le", detail: "language: vi | en.");
            }

            Guid uid = CrowdClaims.GetUserId(User);
            DateTimeOffset bayGio = _clock.GetUtcNow();
            NgonNguNguoiDung? n = await _db.NgonNgus.FirstOrDefaultAsync(x => x.UserId == uid, ct);
            if (n == null)
            {
                _db.NgonNgus.Add(NgonNguNguoiDung.Tao(uid, ma, bayGio));
            }
            else
            {
                n.Dat(ma, bayGio);
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new LanguageResponse { Language = ma });
        }
    }
}
