using System;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Gate.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Gate.Api.Controllers
{
    /// <summary>
    /// Suc khoe cua gate (kien truc 3.32). Gateway khong dinh tuyen toi day — chi probe cua K8s / docker va doi ky thuat.
    ///   /health/live  : tien trinh con chay.
    ///   /health/ready : bo nho cong link da nap xong (warm-up) — chua xong thi 503, load balancer khong gui khach toi.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class HealthController : ControllerBase
    {
        private readonly GateCatalog _catalog;

        public HealthController(GateCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _catalog = catalog;
        }

        [HttpGet("health/live")]
        public IActionResult Live()
        {
            return Ok(new { status = "live" });
        }

        [HttpGet("health/ready")]
        public IActionResult Ready()
        {
            if (!_catalog.DaNap)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "warming_up" });
            }

            return Ok(new { status = "ready" });
        }
    }

    /// <summary>GET /internal/catalog — tom tat bo nho cong link (khong co dap an vang). Khoa noi bo.</summary>
    [ApiController]
    [AllowAnonymous]
    [InternalApi]
    public sealed class InternalCatalogController : ControllerBase
    {
        private readonly GateCatalog _catalog;

        public InternalCatalogController(GateCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _catalog = catalog;
        }

        [HttpGet("internal/catalog")]
        public IActionResult Catalog()
        {
            return Ok(_catalog.TomTat());
        }
    }
}
