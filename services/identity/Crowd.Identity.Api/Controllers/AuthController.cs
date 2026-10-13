using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Settings;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Helpers;
using Crowd.Identity.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// Dang ky, dang nhap, lam moi token, dang xuat; xac thuc email, quen / dat lai mat khau.
    /// Moi endpoint o day CONG KHAI (chua dang nhap).
    ///
    /// Controller chi lam 3 viec: kiem dau vao (qua helper), goi service, doi
    /// ket qua cua service thanh ma HTTP. Nghiep vu nam het o AuthService / TaiKhoanService.
    /// Loi tra ProblemDetails co "code" nhu moi service khac.
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
        private readonly TaiKhoanService _taiKhoan;
        private readonly ISettings _settings;

        public AuthController(AuthService authService, TaiKhoanService taiKhoan, ISettings settings)
        {
            if (authService == null)
            {
                throw new ArgumentNullException(nameof(authService));
            }

            if (taiKhoan == null)
            {
                throw new ArgumentNullException(nameof(taiKhoan));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _authService = authService;
            _taiKhoan = taiKhoan;
            _settings = settings;
        }

        /// <summary>POST /auth/register — tao tai khoan va gui email xac thuc.</summary>
        [HttpPost("register")]
        public async Task<IActionResult> DangKy([FromBody] RegisterRequest body, CancellationToken ct)
        {
            Dictionary<string, string[]> loi = AuthRequestValidator.KiemDangKy(body, _settings);
            if (loi.Count > 0)
            {
                ValidationProblemDetails chiTiet = new ValidationProblemDetails(loi);
                chiTiet.Extensions["code"] = "du_lieu_khong_hop_le";
                return ValidationProblem(chiTiet);
            }

            Guid correlationId = CorrelationIdHelper.LayTuRequest(Request);

            DangKyKetQua ketQua = await _authService.DangKyAsync(body, correlationId, ct);

            if (ketQua.TrangThai == DangKyTrangThai.EmailDaTonTai)
            {
                return Loi(StatusCodes.Status409Conflict, "email_da_ton_tai", "Email da duoc dang ky.");
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
                return Loi(StatusCodes.Status401Unauthorized, "sai_thong_tin", SaiThongTin);
            }

            DangNhapKetQua ketQua = await _authService.DangNhapAsync(body, ct);

            if (ketQua.TrangThai == DangNhapTrangThai.SaiThongTin)
            {
                return Loi(StatusCodes.Status401Unauthorized, "sai_thong_tin", SaiThongTin);
            }

            if (ketQua.TrangThai == DangNhapTrangThai.BiKhoa)
            {
                return Loi(StatusCodes.Status403Forbidden, "tai_khoan_bi_khoa", "Tai khoan dang bi khoa.");
            }

            return Ok(ketQua.Token);
        }

        /// <summary>POST /auth/refresh</summary>
        [HttpPost("refresh")]
        public async Task<IActionResult> LamMoi([FromBody] RefreshRequest body, CancellationToken ct)
        {
            TokenResponse? cap = null;
            if (body != null && !string.IsNullOrWhiteSpace(body.RefreshToken))
            {
                cap = await _authService.LamMoiAsync(body.RefreshToken, ct);
            }

            if (cap == null)
            {
                return Loi(StatusCodes.Status401Unauthorized, "phien_khong_hop_le", "Phien dang nhap khong con hieu luc — hay dang nhap lai.");
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

        /// <summary>POST /auth/verify-email — nguoi dung bam link trong email (token lay tu query cua link).</summary>
        [HttpPost("verify-email")]
        public async Task<IActionResult> XacThucEmail([FromBody] TokenOnlyRequest body, CancellationToken ct)
        {
            return Ok(await _taiKhoan.XacThucEmailAsync(body == null ? null : body.Token, KhachVangLai(), ct));
        }

        /// <summary>POST /auth/forgot-password — luon 202 (khong tiet lo email nao co tai khoan).</summary>
        [HttpPost("forgot-password")]
        public async Task<IActionResult> QuenMatKhau([FromBody] ForgotPasswordRequest body, CancellationToken ct)
        {
            await _taiKhoan.QuenMatKhauAsync(body == null ? null : body.Email, KhachVangLai(), ct);
            return Accepted();
        }

        /// <summary>POST /auth/reset-password — dat mat khau moi bang token trong email; moi phien cu bi thu hoi.</summary>
        [HttpPost("reset-password")]
        public async Task<IActionResult> DatLaiMatKhau([FromBody] ResetPasswordRequest body, CancellationToken ct)
        {
            if (body == null)
            {
                return Loi(StatusCodes.Status400BadRequest, "ma_khong_hop_le", "Thieu du lieu.");
            }

            await _taiKhoan.DatLaiMatKhauAsync(body.Token, body.NewPassword, KhachVangLai(), ct);
            return NoContent();
        }

        /// <summary>Chua dang nhap: Caller he thong chi de mang correlationId vao event.</summary>
        private Caller KhachVangLai()
        {
            return Caller.HeThong(CorrelationIdHelper.LayTuRequest(Request), null);
        }

        private static ObjectResult Loi(int status, string code, string detail)
        {
            ProblemDetails chiTiet = new ProblemDetails { Status = status, Title = code, Detail = detail };
            chiTiet.Extensions["code"] = code;
            return new ObjectResult(chiTiet) { StatusCode = status };
        }
    }
}
