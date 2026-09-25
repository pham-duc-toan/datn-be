using System.Collections.Generic;
using System.Security.Claims;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Identity.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>
    /// "Toi la ai" — chung minh viec kiem token hoat dong: can dang nhap, vai
    /// tro nao cung duoc.
    ///
    /// Moi thong tin lay tu TOKEN (thuoc tinh User cua ControllerBase), khong
    /// doc database.
    /// </summary>
    [ApiController]
    [Route("me")]
    [Authorize]
    public sealed class MeController : ControllerBase
    {
        /// <summary>GET /me</summary>
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
    }
}
