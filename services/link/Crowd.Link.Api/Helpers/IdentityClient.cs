using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Link.Api.Exceptions;
using Microsoft.Extensions.Options;

namespace Crowd.Link.Api.Helpers
{
    /// <summary>Phan can dung trong tra loi POST /internal/users/lookup cua identity-svc.</summary>
    public sealed class NguoiDungTraCuu
    {
        public Guid UserId { get; init; }

        public DateTimeOffset RegisteredAt { get; init; }
    }

    /// <summary>
    /// Hoi identity-svc luc dang ky cua nguoi nhap ma gioi thieu — CHU du lieu tra loi ngay luc can thay vi
    /// giu ban sao user.registered (known_users). Chi tren thao tac nhap ma (hiem, mot lan moi tai khoan).
    /// Loi / het gio → 503: khong kiem duoc thi KHONG ghi nhan gioi thieu (gioi thieu sinh hoa hong).
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

        /// <summary>Luc dang ky cua nguoi dung; khong ton tai → null.</summary>
        public async Task<DateTimeOffset?> LucDangKyAsync(Guid userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_options.IdentityUrl))
            {
                throw new LinkException(503, "dich_vu_khong_san_sang", "Chưa cấu hình InternalApi:IdentityUrl cho link-svc.");
            }

            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, _options.IdentityUrl.TrimEnd('/') + "/internal/users/lookup"))
            {
                req.Headers.Add(InternalApiHeaders.Key, _options.Key);
                req.Content = JsonContent.Create(new { userIds = new[] { userId } });
                try
                {
                    using (HttpResponseMessage r = await _http.SendAsync(req, ct))
                    {
                        if (!r.IsSuccessStatusCode)
                        {
                            throw new LinkException(503, "dich_vu_khong_san_sang", "Không kiểm được tài khoản (identity-svc trả " + (int)r.StatusCode + "). Thử lại sau.");
                        }

                        List<NguoiDungTraCuu>? ds = await r.Content.ReadFromJsonAsync<List<NguoiDungTraCuu>>(ct);
                        NguoiDungTraCuu? u = ds == null ? null : ds.FirstOrDefault(x => x.UserId == userId);
                        if (u == null)
                        {
                            return null;
                        }

                        return u.RegisteredAt;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    throw new LinkException(503, "dich_vu_khong_san_sang", "Không gọi được identity-svc. Thử lại sau.");
                }
            }
        }
    }
}
