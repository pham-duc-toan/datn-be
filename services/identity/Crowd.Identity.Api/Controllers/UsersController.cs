using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// Thong tin CONG KHAI cua nguoi dung cho moi nguoi da dang nhap: ten, anh, cap do, huy hieu
    /// (man hinh duyet nhan hien nguoi gan thay vi labelerId), bang xep hang (FL-12).
    /// Khong lo email, vai tro, trang thai khoa.
    /// </summary>
    [ApiController]
    [Route("users")]
    [Authorize]
    public sealed class UsersController : ControllerBase
    {
        private readonly HoSoService _hoSo;
        private readonly CapDoService _capDo;
        private readonly TimeProvider _clock;

        public UsersController(HoSoService hoSo, CapDoService capDo, TimeProvider clock)
        {
            if (hoSo == null)
            {
                throw new ArgumentNullException(nameof(hoSo));
            }

            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _hoSo = hoSo;
            _capDo = capDo;
            _clock = clock;
        }

        /// <summary>GET /users/public?ids=a,b,c — toi da 100 id; id khong ton tai thi bo qua.</summary>
        [HttpGet("public")]
        public async Task<IActionResult> CongKhai([FromQuery] string? ids, CancellationToken ct)
        {
            List<Guid> ds = new List<Guid>();
            if (!string.IsNullOrWhiteSpace(ids))
            {
                foreach (string phan in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    Guid id;
                    if (!Guid.TryParse(phan, out id))
                    {
                        throw new InvalidValueException("id_khong_hop_le", "ids phải là danh sách UUID ngăn cách bằng dấu phẩy.");
                    }

                    ds.Add(id);
                }
            }

            return Ok(await _hoSo.CongKhaiAsync(ds, ct));
        }

        /// <summary>GET /users/leaderboard?period=all|month|week&amp;limit=20</summary>
        [HttpGet("leaderboard")]
        public async Task<IActionResult> XepHang([FromQuery] string? period, [FromQuery] int limit, CancellationToken ct)
        {
            return Ok(await _capDo.BangXepHangAsync(period, limit, _clock.GetUtcNow(), ct));
        }
    }
}
