using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Crowd.Gateway.Api.Middlewares
{
    /// <summary>
    /// ocelot.json ghi dia chi service kieu chay tren may: localhost:8101, localhost:8102...
    /// Trong docker compose moi service la mot host rieng (identity, project...). Thay vi giu hai
    /// ban ocelot.json, cau hinh "DownstreamHosts:&lt;cong&gt;" = ten host (vd bien moi truong
    /// DownstreamHosts__8101=identity) se GHI DE Host cua moi route dung cong do.
    /// Khong co muc DownstreamHosts → giu nguyen ocelot.json.
    /// </summary>
    public static class DownstreamHosts
    {
        public const string Section = "DownstreamHosts";

        public static int GhiDe(ConfigurationManager configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            IConfigurationSection doiHost = configuration.GetSection(Section);
            if (!doiHost.Exists())
            {
                return 0;
            }

            Dictionary<string, string?> ghiDe = new Dictionary<string, string?>();
            foreach (IConfigurationSection route in configuration.GetSection("Routes").GetChildren())
            {
                foreach (IConfigurationSection hostPort in route.GetSection("DownstreamHostAndPorts").GetChildren())
                {
                    string? cong = hostPort["Port"];
                    if (cong == null)
                    {
                        continue;
                    }

                    string? host = doiHost[cong];
                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        ghiDe[hostPort.Path + ":Host"] = host;
                    }
                }
            }

            configuration.AddInMemoryCollection(ghiDe);
            return ghiDe.Count;
        }
    }
}
