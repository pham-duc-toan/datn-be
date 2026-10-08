using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Helpers;
using Crowd.Identity.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// Dang ky, dang nhap, lam moi token, dang xuat.
    ///
    /// Controller chi lam 3 viec: kiem dau vao (qua helper), goi service, doi
    /// ket qua cua service thanh ma HTTP. Nghiep vu nam het o AuthService.
    /// </summary>
    [ApiController]
    [Route("auth")]
    public sealed class AuthController : ControllerBase
    {
        /// <summary>
        /// MOT thong bao cho moi truong hop dang nhap sai — email khong ton tai
        /// hay mat khau sai deu y het. Tach hai thong bao thi ke tan cong thu
        /// email la biet tai khoan nao co that (VD-S-13).
        /// </summary>
        private const string SaiThongTin = "Email hoac mat khau khong dung.";

        private readonly AuthService _authService;
        private readonly ISettings _settings;

        public AuthController(AuthService authService, ISettings settings)
        {
            if (authService == null)
            {
                throw new ArgumentNullException(nameof(authService));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _authService = authService;
            _settings = settings;
        }

        /// <summary>POST /auth/register</summary>
        [HttpPost("register")]
        public async Task<IActionResult> DangKy([FromBody] RegisterRequest body, CancellationToken ct)
        {
            Dictionary<string, string[]> loi = AuthRequestValidator.KiemDangKy(body, _settings);
            if (loi.Count > 0)
            {
                return ValidationProblem(new ValidationProblemDetails(loi));
            }

            Guid correlationId = CorrelationIdHelper.LayTuRequest(Request);

            DangKyKetQua ketQua = await _authService.DangKyAsync(body, correlationId, ct);

            if (ketQua.TrangThai == DangKyTrangThai.EmailDaTonTai)
            {
                return Conflict(new ErrorResponse { Error = "Email da duoc dang ky." });
            }

            RegisterResponse phanHoi = new RegisterResponse { UserId = ketQua.UserId };
            return Created("/users/" + ketQua.UserId, phanHoi);
        }

        /// <summary>POST /auth/login</summary>
        [HttpPost("login")]
        public async Task<IActionResult> DangNhap([FromBody] LoginRequest body, CancellationToken ct)
        {
            if (!AuthRequestValidator.LaDangNhapHopLe(body, _settings))
            {
                return Problem(detail: SaiThongTin, statusCode: StatusCodes.Status401Unauthorized);
            }

            DangNhapKetQua ketQua = await _authService.DangNhapAsync(body, ct);

            if (ketQua.TrangThai == DangNhapTrangThai.SaiThongTin)
            {
                return Problem(detail: SaiThongTin, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (ketQua.TrangThai == DangNhapTrangThai.BiKhoa)
            {
                return Problem(detail: "Tai khoan dang bi khoa.", statusCode: StatusCodes.Status403Forbidden);
            }

            return Ok(ketQua.Token);
        }

        /// <summary>POST /auth/refresh</summary>
        [HttpPost("refresh")]
        public async Task<IActionResult> LamMoi([FromBody] RefreshRequest body, CancellationToken ct)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.RefreshToken))
            {
                return Unauthorized();
            }

            TokenResponse? cap = await _authService.LamMoiAsync(body.RefreshToken, ct);

            if (cap == null)
            {
                return Unauthorized();
            }

            return Ok(cap);
        }

        /// <summary>
        /// POST /auth/logout — luon 204, ke ca khi token khong ton tai: tra loi
        /// khac nhau se cho biet token nao tung ton tai.
        /// </summary>
        [HttpPost("logout")]
        public async Task<IActionResult> DangXuat([FromBody] RefreshRequest body, CancellationToken ct)
        {
            string? refreshToken = null;
            if (body != null)
            {
                refreshToken = body.RefreshToken;
            }

            await _authService.DangXuatAsync(refreshToken, ct);

            return NoContent();
        }
    }
}
