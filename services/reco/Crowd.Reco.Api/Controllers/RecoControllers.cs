using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Reco.Api.Dtos;
using Crowd.Reco.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Crowd.Reco.Api.Controllers
{
    /// <summary>
    /// Feed cho du an cua LABELER (kien truc 3.39). Gateway chuyen GET /projects/feed toi day; reco-svc sap thi frontend
    /// tu chuyen sang GET /projects/market cua project-svc (danh sach moi nhat, khong ca nhan hoa).
    /// </summary>
    [ApiController]
    [Authorize(Roles = CrowdRoles.Labeler)]
    public sealed class FeedController : ControllerBase
    {
        private readonly FeedService _feed;

        public FeedController(FeedService feed)
        {
            if (feed == null)
            {
                throw new ArgumentNullException(nameof(feed));
            }

            _feed = feed;
        }

        /// <summary>
        /// GET /projects/feed?cursor=&amp;pageSize=&amp;order= — order = for_you (mac dinh, xep theo goi y; khong co cursor = luot
        /// moi) | newest (du an lam duoc ngay, moi mo truoc — cung dang the, khong qua mo hinh).
        /// </summary>
        [HttpGet("projects/feed")]
        public async Task<IActionResult> Feed([FromQuery] string? cursor, [FromQuery] int pageSize, [FromQuery] string? order, CancellationToken ct)
        {
            if (string.Equals(order, "newest", StringComparison.Ordinal))
            {
                return Ok(await _feed.LayMoiNhatAsync(User, cursor, pageSize, ct));
            }

            return Ok(await _feed.LayAsync(User, cursor, pageSize, ct));
        }
    }

    /// <summary>POST /reco/events — frontend gui theo lo luot hien the (that su tren man hinh), mo the, "khong quan tam".</summary>
    [ApiController]
    [Authorize]
    public sealed class RecoEventsController : ControllerBase
    {
        private readonly HanhViService _hanhVi;

        public RecoEventsController(HanhViService hanhVi)
        {
            if (hanhVi == null)
            {
                throw new ArgumentNullException(nameof(hanhVi));
            }

            _hanhVi = hanhVi;
        }

        [HttpPost("reco/events")]
        public async Task<IActionResult> Ghi([FromBody] RecoEventsRequest body, CancellationToken ct)
        {
            int n = await _hanhVi.GhiTuFrontendAsync(CrowdClaims.GetUserId(User), body, ct);
            return Ok(new RecoEventsResponse { Accepted = n });
        }
    }

    /// <summary>
    /// Suc khoe (probe K8s / docker, doi ky thuat) — gateway khong dinh tuyen toi day.
    ///   /health/live  : tien trinh con chay;  /health/ready : bo nho du an da nap (warm-up).
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class HealthController : ControllerBase
    {
        private readonly CatalogGoiY _catalog;

        public HealthController(CatalogGoiY catalog)
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

    /// <summary>
    /// GET /reco/internal/catalog — tom tat bo nho (doi ky thuat, E2E). POST /reco/internal/refresh — nap lai ngay.
    /// Khoa noi bo (X-Internal-Key), khong co so lieu tung nguoi.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [InternalApi]
    public sealed class InternalRecoController : ControllerBase
    {
        private readonly CatalogGoiY _catalog;
        private readonly MoHinhDangChay _moHinh;

        public InternalRecoController(CatalogGoiY catalog, MoHinhDangChay moHinh)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (moHinh == null)
            {
                throw new ArgumentNullException(nameof(moHinh));
            }

            _catalog = catalog;
            _moHinh = moHinh;
        }

        [HttpGet("reco/internal/catalog")]
        public IActionResult Catalog()
        {
            AnhCatalog a = _catalog.Anh;
            return Ok(new CatalogSummary
            {
                OpenProjects = a.DuAn.Count,
                ProjectsWithPreview = a.MauXemTruoc.Count,
                LoadedAt = a.NapLuc,
                ProductionModel = _moHinh.Production.PhienBan,
                CandidateModel = _moHinh.Candidate == null ? null : _moHinh.Candidate.PhienBan,
            });
        }

        [HttpPost("reco/internal/refresh")]
        public async Task<IActionResult> NapLai(CancellationToken ct)
        {
            int so = await _catalog.NapAsync(false, ct);
            await _moHinh.KiemTraAsync(ct);
            return Ok(new { openProjects = so, productionModel = _moHinh.Production.PhienBan });
        }
    }
}
