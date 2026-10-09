using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Security;
using Crowd.Link.Api.Exceptions;
using Crowd.Link.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Crowd.Link.Api.Helpers
{
    /// <summary>
    /// Ai dang tao link: token cua sharer, HOAC API key (FS-05 / FS-02 — cong cu tu dong
    /// hoa khong cam token 15 phut). Gateway khong chan route /links bang token cho rieng
    /// truong hop nay; endpoint can token van gan [Authorize].
    /// </summary>
    public static class NguoiGoi
    {
        public const string HeaderApiKey = "X-Api-Key";

        public static async Task<Caller> SharerAsync(HttpContext http, LinkService links, string? apiKeyQuery, CancellationToken ct)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (links == null)
            {
                throw new ArgumentNullException(nameof(links));
            }

            if (http.User.Identity != null && http.User.Identity.IsAuthenticated)
            {
                if (!http.User.IsInRole(CrowdRoles.Sharer))
                {
                    throw new LinkException(403, "khong_phai_sharer", "Tai khoan chua co vai tro nguoi chia se link (sharer).");
                }

                return Caller.TuHttp(http, ActorRole.Sharer);
            }

            string? key = http.Request.Headers[HeaderApiKey];
            if (string.IsNullOrWhiteSpace(key))
            {
                key = apiKeyQuery;
            }

            Guid? chu = await links.XacThucApiKeyAsync(key, ct);
            if (chu == null)
            {
                throw new LinkException(401, "chua_xac_thuc", "Can token dang nhap hoac API key hop le.");
            }

            return Caller.TuNguoiDung(http, chu.Value, ActorRole.Sharer);
        }

        /// <summary>IP nguoi goi (sau ForwardedHeaders: IP that phia sau gateway), da bam co muoi.</summary>
        public static string IpHash(HttpContext http, string muoi)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            string ip = http.Connection.RemoteIpAddress == null ? "unknown" : http.Connection.RemoteIpAddress.ToString();
            return BamIp.Bam(ip, muoi);
        }
    }
}
