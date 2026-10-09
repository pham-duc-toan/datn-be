using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Crowd.Link.Api.Helpers
{
    /// <summary>Cau hinh quet link dich. Muc "UrlSafety" — khoa API la bi mat nen o appsettings, khong o setting.</summary>
    public sealed class UrlSafetyOptions
    {
        public const string SectionName = "UrlSafety";

        /// <summary>Khoa Google Safe Browsing API v4. Trong = dung bo kiem dev (danh sach ten mien gia lap).</summary>
        public string GoogleApiKey { get; set; } = string.Empty;

        /// <summary>Chi dung khi khong co khoa: ten mien coi nhu doc hai (de test luong chan link).</summary>
        public List<string> DevUnsafeHosts { get; set; } = new List<string>();
    }

    /// <summary>Quet link dich (VD-L-01). Tra ve LY DO neu nguy hiem, null neu sach.</summary>
    public interface IUrlSafetyChecker
    {
        Task<string?> KiemAsync(string url, string tenMien, CancellationToken ct);
    }

    /// <summary>Google Safe Browsing Lookup API v4 (threatMatches:find).</summary>
    public sealed class GoogleSafeBrowsingChecker : IUrlSafetyChecker
    {
        private readonly HttpClient _http;
        private readonly UrlSafetyOptions _options;

        public GoogleSafeBrowsingChecker(HttpClient http, IOptions<UrlSafetyOptions> options)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _http = http;
            _options = options.Value;
        }

        public async Task<string?> KiemAsync(string url, string tenMien, CancellationToken ct)
        {
            JsonObject body = new JsonObject
            {
                ["client"] = new JsonObject { ["clientId"] = "crowd-link", ["clientVersion"] = "1.0" },
                ["threatInfo"] = new JsonObject
                {
                    ["threatTypes"] = new JsonArray("MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE", "POTENTIALLY_HARMFUL_APPLICATION"),
                    ["platformTypes"] = new JsonArray("ANY_PLATFORM"),
                    ["threatEntryTypes"] = new JsonArray("URL"),
                    ["threatEntries"] = new JsonArray(new JsonObject { ["url"] = url }),
                },
            };

            using (HttpResponseMessage r = await _http.PostAsJsonAsync(
                "https://safebrowsing.googleapis.com/v4/threatMatches:find?key=" + Uri.EscapeDataString(_options.GoogleApiKey), body, ct))
            {
                // Loi mang / het han muc: NEM de worker thu lai vong sau — khong "coi nhu sach".
                r.EnsureSuccessStatusCode();
                JsonNode? kq = await r.Content.ReadFromJsonAsync<JsonNode>(ct);
                JsonArray? khop = kq == null ? null : kq["matches"] as JsonArray;
                if (khop == null || khop.Count == 0)
                {
                    return null;
                }

                JsonNode? loai = khop[0]!["threatType"];
                return "Google Safe Browsing: " + (loai == null ? "nguy hiem" : loai.ToString());
            }
        }
    }

    /// <summary>Bo kiem dev: khong goi mang, coi cac ten mien trong DevUnsafeHosts la doc hai.</summary>
    public sealed class DevUrlSafetyChecker : IUrlSafetyChecker
    {
        private readonly UrlSafetyOptions _options;

        public DevUrlSafetyChecker(IOptions<UrlSafetyOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options.Value;
        }

        public Task<string?> KiemAsync(string url, string tenMien, CancellationToken ct)
        {
            string? chan = UrlRules.TenMienBiChan(tenMien, _options.DevUnsafeHosts);
            string? ketQua = chan == null ? null : "Quet (dev): ten mien " + chan + " bi danh dau doc hai";
            return Task.FromResult(ketQua);
        }
    }
}
