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
    /// Ai dang tao link: token dang nhap, HOAC API key (FS-05 / FS-02 — cong cu tu dong
    /// hoa khong cam token 15 phut). Gateway khong chan route /links bang token cho rieng
    /// truong hop nay; endpoint can token van gan [Authorize].
    ///
    /// Chia se link la CHUC NANG cua labeler (nguoi kiem tien), khong phai vai tro rieng.
    /// Doanh nghiep / admin khong chia se link: doanh nghiep khong duoc nhan tien tu chinh ngan
    /// sach minh chi, admin la ben kiem duyet link. Tai khoan cu dang ky voi vai tro "sharer"
    /// van dung duoc. API key chi tao duoc bang token cua tai khoan hop le nen khong kiem lai.
    /// </summary>
    public static class NguoiGoi
    {
        public const string HeaderApiKey = "X-Api-Key";

        /// <summary>Gia tri cho [Authorize(Roles = ...)]: ai duoc chia se link.</summary>
        public const string VaiTroChiaSe = CrowdRoles.Labeler + "," + CrowdRoles.Sharer;

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
                if (!http.User.IsInRole(CrowdRoles.Labeler) && !http.User.IsInRole(CrowdRoles.Sharer))
                {
                    throw new LinkException(403, "khong_duoc_chia_se_link", "Chỉ tài khoản người gán nhãn (labeler) mới chia sẻ link kiếm tiền.");
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
                throw new LinkException(401, "chua_xac_thuc", "Cần token đăng nhập hoặc API key hợp lệ.");
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
