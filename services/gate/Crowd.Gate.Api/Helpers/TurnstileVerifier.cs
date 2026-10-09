using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crowd.Gate.Api.Helpers
{
    /// <summary>Kiem token Turnstile phia server (siteverify). Token client tu bao "da qua" khong co gia tri.</summary>
    public sealed class TurnstileVerifier
    {
        private const string DiaChi = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

        private readonly HttpClient _http;
        private readonly TurnstileOptions _options;
        private readonly ILogger<TurnstileVerifier> _logger;

        public TurnstileVerifier(HttpClient http, IOptions<GateOptions> options, ILogger<TurnstileVerifier> logger)
        {
            if (http == null)
            {
                throw new ArgumentNullException(nameof(http));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _http = http;
            _options = options.Value.Turnstile;
            _logger = logger;
        }

        public bool BatBuoc
        {
            get { return _options.Enabled; }
        }

        public string SiteKey
        {
            get { return _options.SiteKey; }
        }

        /// <summary>
        /// true = dat. Loi mang toi Cloudflare: TU CHOI (fail closed) — mo cua khi Turnstile
        /// sap chinh la luc bot tran vao.
        /// </summary>
        public async Task<bool> KiemAsync(string? token, string? ip, CancellationToken ct)
        {
            if (!_options.Enabled)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            {
                return false;
            }

            Dictionary<string, string> form = new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = token,
            };
            if (!string.IsNullOrEmpty(ip))
            {
                form["remoteip"] = ip;
            }

            try
            {
                using (FormUrlEncodedContent body = new FormUrlEncodedContent(form))
                using (HttpResponseMessage r = await _http.PostAsync(DiaChi, body, ct))
                {
                    if (!r.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Turnstile tra HTTP {Status}", (int)r.StatusCode);
                        return false;
                    }

                    JsonNode? kq = await r.Content.ReadFromJsonAsync<JsonNode>(ct);
                    JsonNode? ok = kq == null ? null : kq["success"];
                    return ok != null && ok.GetValue<bool>();
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Khong goi duoc Turnstile — tu choi luot nay");
                return false;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnstile qua thoi gian — tu choi luot nay");
                return false;
            }
        }
    }
}
