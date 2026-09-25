using Microsoft.AspNetCore.Mvc;

namespace Crowd.Identity.Api.Controllers
{
    /// <summary>GET / — tra ten service, de biet nhanh service dang song.</summary>
    [ApiController]
    [Route("")]
    public sealed class HomeController : ControllerBase
    {
        [HttpGet]
        public IActionResult TenService()
        {
            return Ok("identity-svc");
        }
    }
}
