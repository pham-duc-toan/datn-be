using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// Moi thu "cua toi": danh tinh tu token, ho so (FC-05), ky nang (FL-01), anh dai dien,
    /// mat khau (FC-03), xac thuc email (FC-01), dang xuat moi thiet bi, cap do / huy hieu (FL-12).
    /// Can dang nhap, vai tro nao cung duoc.
    /// </summary>
    [ApiController]
    [Route("me")]
    [Authorize]
    public sealed class MeController : ControllerBase
    {
        private readonly HoSoService _hoSo;
        private readonly TaiKhoanService _taiKhoan;
        private readonly CapDoService _capDo;
        private readonly HaiLopService _haiLop;
        private readonly XacMinhDanhTinhService _xacMinh;

        public MeController(HoSoService hoSo, TaiKhoanService taiKhoan, CapDoService capDo, HaiLopService haiLop, XacMinhDanhTinhService xacMinh)
        {
            if (haiLop == null)
            {
                throw new ArgumentNullException(nameof(haiLop));
            }

            if (xacMinh == null)
            {
                throw new ArgumentNullException(nameof(xacMinh));
            }

            _haiLop = haiLop;
            _xacMinh = xacMinh;

            if (hoSo == null)
            {
                throw new ArgumentNullException(nameof(hoSo));
            }

            if (taiKhoan == null)
            {
                throw new ArgumentNullException(nameof(taiKhoan));
            }

            if (capDo == null)
            {
                throw new ArgumentNullException(nameof(capDo));
            }

            _hoSo = hoSo;
            _taiKhoan = taiKhoan;
            _capDo = capDo;
        }

        /// <summary>GET /me — doc tu TOKEN, khong cham database.</summary>
        [HttpGet]
        public IActionResult ToiLaAi()
        {
            List<string> vaiTro = new List<string>();
            foreach (Claim c in User.FindAll(CrowdClaims.Role))
            {
                vaiTro.Add(c.Value);
            }

            MeResponse phanHoi = new MeResponse
            {
                UserId = CrowdClaims.GetUserId(User),
                Email = User.FindFirstValue(CrowdClaims.Email),
                Roles = vaiTro,
                Name = User.FindFirstValue(CrowdClaims.Name),
            };

            return Ok(phanHoi);
        }

        /// <summary>GET /me/profile</summary>
        [HttpGet("profile")]
        public async Task<IActionResult> HoSo(CancellationToken ct)
        {
            return Ok(await _hoSo.CuaToiAsync(UserId(), ct));
        }

        /// <summary>PUT /me/profile { displayName, bio }</summary>
        [HttpPut("profile")]
        public async Task<IActionResult> SuaHoSo([FromBody] UpdateProfileRequest body, CancellationToken ct)
        {
            return Ok(await _hoSo.CapNhatAsync(UserId(), body, ct));
        }

        /// <summary>PUT /me/skills { domains, languages, modalities }</summary>
        [HttpPut("skills")]
        public async Task<IActionResult> SuaKyNang([FromBody] SkillsDto body, CancellationToken ct)
        {
            return Ok(await _hoSo.CapNhatKyNangAsync(UserId(), body, ct));
        }

        /// <summary>POST /me/avatar/upload-url { fileName, sizeBytes } → link PUT anh len kho.</summary>
        [HttpPost("avatar/upload-url")]
        public async Task<IActionResult> XinLinkAvatar([FromBody] AvatarUploadRequest body)
        {
            return Ok(await _hoSo.XinLinkAvatarAsync(UserId(), body));
        }

        /// <summary>PUT /me/avatar { key } — gan anh vua tai len; key null = go anh.</summary>
        [HttpPut("avatar")]
        public async Task<IActionResult> DatAvatar([FromBody] SetAvatarRequest body, CancellationToken ct)
        {
            return Ok(await _hoSo.DatAvatarAsync(UserId(), body == null ? null : body.Key, ct));
        }

        /// <summary>POST /me/password { currentPassword, newPassword } → cap token MOI (moi phien cu bi thu hoi).</summary>
        [HttpPost("password")]
        public async Task<IActionResult> DoiMatKhau([FromBody] ChangePasswordRequest body, CancellationToken ct)
        {
            return Ok(await _taiKhoan.DoiMatKhauAsync(
                body == null ? null : body.CurrentPassword,
                body == null ? null : body.NewPassword,
                Nguoi(),
                ct));
        }

        /// <summary>POST /me/verify-email/resend — gui lai link xac thuc email.</summary>
        [HttpPost("verify-email/resend")]
        public async Task<IActionResult> GuiLaiXacThuc(CancellationToken ct)
        {
            await _taiKhoan.GuiLaiXacThucEmailAsync(Nguoi(), ct);
            return Accepted();
        }

        /// <summary>POST /me/logout-all — dang xuat moi thiet bi (ca thiet bi nay).</summary>
        [HttpPost("logout-all")]
        public async Task<IActionResult> DangXuatMoiThietBi(CancellationToken ct)
        {
            await _taiKhoan.DangXuatMoiThietBiAsync(Nguoi(), ct);
            return NoContent();
        }

        /// <summary>GET /me/level — cap do, so nhan duoc duyet, uy tin, dieu kien cap ke, huy hieu.</summary>
        [HttpGet("level")]
        public async Task<IActionResult> CapDo(CancellationToken ct)
        {
            return Ok(await _capDo.CuaToiAsync(UserId(), ct));
        }

        // ---------------- Xac thuc hai lop (FC-04) ----------------

        /// <summary>GET /me/2fa — dang bat chua, con bao nhieu ma du phong.</summary>
        [HttpGet("2fa")]
        public async Task<IActionResult> TrangThaiHaiLop(CancellationToken ct)
        {
            return Ok(await _haiLop.TrangThaiAsync(UserId(), ct));
        }

        /// <summary>POST /me/2fa/setup → { secret, otpauthUri } (CHUA bat — phai xac nhan bang enable).</summary>
        [HttpPost("2fa/setup")]
        public async Task<IActionResult> CaiDatHaiLop(CancellationToken ct)
        {
            return Ok(await _haiLop.CaiDatAsync(UserId(), ct));
        }

        /// <summary>POST /me/2fa/enable { code } → bat, tra 10 ma du phong (hien mot lan).</summary>
        [HttpPost("2fa/enable")]
        public async Task<IActionResult> BatHaiLop([FromBody] TwoFactorCodeRequest body, CancellationToken ct)
        {
            return Ok(await _haiLop.BatAsync(body == null ? null : body.Code, Nguoi(), ct));
        }

        /// <summary>POST /me/2fa/disable { password, code | recoveryCode }</summary>
        [HttpPost("2fa/disable")]
        public async Task<IActionResult> TatHaiLop([FromBody] TwoFactorDisableRequest body, CancellationToken ct)
        {
            await _haiLop.TatAsync(
                body == null ? null : body.Password,
                body == null ? null : body.Code,
                body == null ? null : body.RecoveryCode,
                Nguoi(),
                ct);
            return NoContent();
        }

        /// <summary>POST /me/2fa/recovery-codes { code } → bo ma du phong moi (bo cu het hieu luc).</summary>
        [HttpPost("2fa/recovery-codes")]
        public async Task<IActionResult> TaoLaiMaDuPhong([FromBody] TwoFactorCodeRequest body, CancellationToken ct)
        {
            return Ok(await _haiLop.TaoLaiMaDuPhongAsync(body == null ? null : body.Code, Nguoi(), ct));
        }

        // ---------------- Xac minh danh tinh (muc 2.9, FP-05) ----------------

        /// <summary>GET /me/identity-verification</summary>
        [HttpGet("identity-verification")]
        public async Task<IActionResult> XacMinhDanhTinh(CancellationToken ct)
        {
            return Ok(await _xacMinh.CuaToiAsync(UserId(), ct));
        }

        /// <summary>PUT /me/identity-verification { fullName, dateOfBirth, idNumber }</summary>
        [HttpPut("identity-verification")]
        public async Task<IActionResult> SuaXacMinhDanhTinh([FromBody] UpdateIdentityVerificationRequest body, CancellationToken ct)
        {
            return Ok(await _xacMinh.CapNhatAsync(UserId(), body, ct));
        }

        /// <summary>POST /me/identity-verification/upload-url { side: front|back, fileName, sizeBytes }</summary>
        [HttpPost("identity-verification/upload-url")]
        public async Task<IActionResult> XinLinkAnhGiayTo([FromBody] IdentityDocumentUploadRequest body, CancellationToken ct)
        {
            return Ok(await _xacMinh.XinLinkTaiAnhAsync(UserId(), body, ct));
        }

        /// <summary>POST /me/identity-verification/submit — nop cho admin duyet.</summary>
        [HttpPost("identity-verification/submit")]
        public async Task<IActionResult> NopXacMinhDanhTinh(CancellationToken ct)
        {
            return Ok(await _xacMinh.NopAsync(Nguoi(), ct));
        }

        private Guid UserId()
        {
            return CrowdClaims.GetUserId(User);
        }

        /// <summary>Tu cach ghi vao event (audit): vai tro cao nhat trong token.</summary>
        private Caller Nguoi()
        {
            ActorRole vaiTro = ActorRole.Labeler;
            if (User.IsInRole(CrowdRoles.Admin))
            {
                vaiTro = ActorRole.Admin;
            }
            else if (User.IsInRole(CrowdRoles.Business))
            {
                vaiTro = ActorRole.Business;
            }
            else if (User.IsInRole(CrowdRoles.Sharer) && !User.IsInRole(CrowdRoles.Labeler))
            {
                vaiTro = ActorRole.Sharer;
            }

            return Caller.TuHttp(HttpContext, vaiTro);
        }
    }
}
