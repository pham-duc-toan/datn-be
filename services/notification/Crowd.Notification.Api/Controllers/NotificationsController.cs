using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Notification.Api.Dtos;
using Crowd.Notification.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Notification.Api.Controllers
{
    /// <summary>
    /// Thong bao cua NGUOI DANG DANG NHAP (FC-06). Gateway route "/notifications/{everything}".
    /// FE hoi /me/unread-count dinh ky (vd 30 giay) de hien so chua doc.
    /// </summary>
    [ApiController]
    [Route("notifications/me")]
    [Authorize]
    public sealed class NotificationsController : ControllerBase
    {
        private readonly ThongBaoService _service;

        public NotificationsController(ThongBaoService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _service = service;
        }

        /// <summary>GET /notifications/me?unreadOnly=&amp;page=&amp;pageSize=</summary>
        [HttpGet]
        public async Task<IActionResult> DanhSach([FromQuery] bool unreadOnly, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        {
            return Ok(await _service.DanhSachAsync(NguoiDung(), unreadOnly, page, pageSize, ct));
        }

        /// <summary>GET /notifications/me/unread-count</summary>
        [HttpGet("unread-count")]
        public async Task<IActionResult> ChuaDoc(CancellationToken ct)
        {
            return Ok(new UnreadCountResponse { Count = await _service.DemChuaDocAsync(NguoiDung(), ct) });
        }

        /// <summary>POST /notifications/me/{id}/read — thong bao cua nguoi khac → 404.</summary>
        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> DaDoc(Guid id, CancellationToken ct)
        {
            await _service.DaDocAsync(NguoiDung(), id, ct);
            return NoContent();
        }

        /// <summary>POST /notifications/me/read-all</summary>
        [HttpPost("read-all")]
        public async Task<IActionResult> DocHet(CancellationToken ct)
        {
            return Ok(new ReadAllResponse { Updated = await _service.DocHetAsync(NguoiDung(), ct) });
        }

        /// <summary>GET /notifications/me/preferences — moi loai: co gui email khong, co bat buoc khong.</summary>
        [HttpGet("preferences")]
        public async Task<IActionResult> TuyChon(CancellationToken ct)
        {
            return Ok(await _service.TuyChonAsync(NguoiDung(), ct));
        }

        /// <summary>PUT /notifications/me/preferences { "items": [ { "type", "emailEnabled" } ] }</summary>
        [HttpPut("preferences")]
        public async Task<IActionResult> DatTuyChon([FromBody] UpdatePreferencesRequest body, CancellationToken ct)
        {
            return Ok(await _service.DatTuyChonAsync(NguoiDung(), body, ct));
        }

        private Guid NguoiDung()
        {
            return CrowdClaims.GetUserId(User);
        }
    }
}
