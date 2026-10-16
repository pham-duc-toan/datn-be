using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Microsoft.Extensions.Options;

namespace Crowd.Ledger.Api.Helpers
{
    /// <summary>Tra loi cua identity-svc: GET /internal/businesses/{id}.</summary>
    public sealed class HoSoDoanhNghiep
    {
        public Guid BusinessId { get; init; }

        /// <summary>draft | submitted | verified | rejected</summary>
        public string Status { get; init; } = string.Empty;

        public string CompanyName { get; init; } = string.Empty;

        public string TaxCode { get; init; } = string.Empty;

        public string Address { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;
    }

    /// <summary>
    /// Hoi identity-svc ho so KYB (ten, ma so thue, dia chi) luc XUAT hoa don — thao tac hiem, khong
    /// tren duong nong; ket qua duoc CHUP vao hoa don. Loi / het gio → DichVuKhongSanSangException (503):
    /// khong lay duoc nguoi mua thi khong xuat (hoa don thieu ma so thue la hoa don sai).
    /// </summary>
    public sealed class IdentityClient
    {
        private readonly HttpClient _http;
        private readonly InternalApiOptions _options;

        public IdentityClient(HttpClient http, IOptions<InternalApiOptions> options)
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
            _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds <= 0 ? 10 : _options.TimeoutSeconds);
        }

        /// <summary>null = doanh nghiep chua co ho so KYB.</summary>
        public async Task<HoSoDoanhNghiep?> LayDoanhNghiepAsync(Guid businessId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_options.IdentityUrl))
            {
                throw new DichVuKhongSanSangException("Chua cau hinh InternalApi:IdentityUrl.");
            }

            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, _options.IdentityUrl.TrimEnd('/') + "/internal/businesses/" + businessId))
            {
                req.Headers.Add(InternalApiHeaders.Key, _options.Key);
                try
                {
                    using (HttpResponseMessage r = await _http.SendAsync(req, ct))
                    {
                        if (r.StatusCode == HttpStatusCode.NotFound)
                        {
                            return null;
                        }

                        if (!r.IsSuccessStatusCode)
                        {
                            throw new DichVuKhongSanSangException("identity-svc tra " + (int)r.StatusCode + ".");
                        }

                        return await r.Content.ReadFromJsonAsync<HoSoDoanhNghiep>(ct);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    throw new DichVuKhongSanSangException("Khong goi duoc identity-svc: " + ex.Message);
                }
            }
        }
    }

    /// <summary>Dich vu phu thuoc khong tra loi → 503.</summary>
    public sealed class DichVuKhongSanSangException : Exception
    {
        public DichVuKhongSanSangException(string message)
            : base(message)
        {
        }
    }
}
