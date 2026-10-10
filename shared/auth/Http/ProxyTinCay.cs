using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Crowd.BuildingBlocks.Auth.Http
{
    /// <summary>
    /// Doc IP that cua khach tu X-Forwarded-For do gateway ghi. Mac dinh ASP.NET chi tin proxy
    /// LOOPBACK — dung khi gateway va service chay chung may. Trong docker compose gateway la
    /// mot container khac (IP trong mang noi bo), header bi bo qua va moi khach thanh IP cua
    /// gateway → chong trung IP / chong tu vuot link sai het.
    ///
    /// "ForwardedHeaders:TrustedNetworks" = danh sach CIDR proxy duoc tin (vd mang noi bo cua
    /// compose 172.30.0.0/16). Chi dat mang ma ben ngoai KHONG gui thang vao duoc.
    /// </summary>
    public static class ProxyTinCay
    {
        public const string Section = "ForwardedHeaders:TrustedNetworks";

        public static void CauHinh(ForwardedHeadersOptions options, IConfiguration configuration)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;

            string[]? mang = configuration.GetSection(Section).Get<string[]>();
            if (mang == null)
            {
                return;
            }

            foreach (string cidr in mang)
            {
                if (!string.IsNullOrWhiteSpace(cidr))
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr.Trim()));
                }
            }
        }
    }
}
