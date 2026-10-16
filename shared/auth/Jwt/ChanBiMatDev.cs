using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Crowd.BuildingBlocks.Auth.Jwt
{
    /// <summary>
    /// S-08: mat khau / khoa DEV (dev_*_pw, khoa trong appsettings.Development.json, khoa noi bo demo) nam trong
    /// repo cho che do chay service tren may dev. Chung chi an toan khi KHONG bao gio len moi truong that.
    /// Service chay voi moi truong khac Development ma con gia tri dev nao trong cau hinh → DUNG NGAY luc khoi dong
    /// (fail closed), liet ke khoa cau hinh vi pham (khong in gia tri).
    /// Dang ky san trong AddCrowdJwtAuthentication — moi service C# deu goi.
    /// </summary>
    public sealed class ChanBiMatDev : IHostedService
    {
        /// <summary>Khoa dev cong khai trong repo (appsettings.Development.json, deploy/tao_env.py --dev).</summary>
        private static readonly string[] KhoaDev = new string[]
        {
            "pgZwtqPVxGvu3oA5esvFaUio+ltJ2sSfgOWqYQNHwyU=",
            "TUmrnIsOHHdHl7BEEngcDqPfPQW6v956RTfgOEOlILo=",
            "q5Ec0b2l2b0mW3Yw2kOQ7mR6x8H1sZ9vT4nJ3cP0aUo=",
            "demo_internal_key",
        };

        private readonly IConfiguration _cfg;
        private readonly IHostEnvironment _env;

        public ChanBiMatDev(IConfiguration cfg, IHostEnvironment env)
        {
            if (cfg == null)
            {
                throw new ArgumentNullException(nameof(cfg));
            }

            if (env == null)
            {
                throw new ArgumentNullException(nameof(env));
            }

            _cfg = cfg;
            _env = env;
        }

        private static readonly Regex MatKhauDev = new Regex(@"dev_[a-z0-9]+_pw", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_env.IsDevelopment())
            {
                return Task.CompletedTask;
            }

            List<string> viPham = TimViPham(_cfg.AsEnumerable());
            if (viPham.Count > 0)
            {
                throw new InvalidOperationException(
                    "S-08: moi truong " + _env.EnvironmentName + " dang dung mat khau / khoa DEV o: " + string.Join(", ", viPham)
                    + ". Dat bi mat that qua bien moi truong / secret manager (xem deploy/tao_env.py).");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>Cac khoa cau hinh co gia tri dev. Ham thuan — test duoc.</summary>
        public static List<string> TimViPham(IEnumerable<KeyValuePair<string, string?>> cauHinh)
        {
            List<string> ra = new List<string>();
            foreach (KeyValuePair<string, string?> kv in cauHinh)
            {
                if (string.IsNullOrEmpty(kv.Value))
                {
                    continue;
                }

                bool dev = MatKhauDev.IsMatch(kv.Value);
                foreach (string k in KhoaDev)
                {
                    if (kv.Value.Contains(k, StringComparison.Ordinal))
                    {
                        dev = true;
                    }
                }

                if (dev)
                {
                    ra.Add(kv.Key);
                }
            }

            ra.Sort(StringComparer.Ordinal);
            return ra;
        }
    }
}
