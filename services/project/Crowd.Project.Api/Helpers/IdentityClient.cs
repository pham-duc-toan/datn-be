using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Members;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>Tra loi cua identity-svc: POST /internal/users/lookup.</summary>
    public sealed class NguoiDungTraCuu
    {
        public Guid UserId { get; init; }

        public string Email { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public IReadOnlyList<string> Roles { get; init; } = new List<string>();

        public bool Locked { get; init; }

        public int Level { get; init; }

        public int? Reputation { get; init; }

        /// <summary>Da xac minh danh tinh (FP-05).</summary>
        public bool IdentityVerified { get; init; }

        /// <summary>Da xac minh va du tuoi (muc 2.9).</summary>
        public bool Adult { get; init; }

        public IReadOnlyList<string> Domains { get; init; } = new List<string>();

        public IReadOnlyList<string> Languages { get; init; } = new List<string>();

        public IReadOnlyList<string> Modalities { get; init; } = new List<string>();

        public HoSoNguoiDung HoSo()
        {
            return new HoSoNguoiDung(UserId, Roles.Contains(CrowdRoles.Labeler, StringComparer.Ordinal), Locked, Level, Reputation);
        }
    }

    /// <summary>
    /// Hoi identity-svc ve nguoi duoc moi / nguoi xin vao du an (email → userId, vai tro, khoa,
    /// cap do, uy tin). Hoi CHU du lieu ngay luc can thay vi giu ban sao email trong project_db.
    /// Chi tren thao tac moi / tham gia (hiem), khong tren duong nong. Loi (tat, het gio, ma loi)
    /// → DichVuKhongSanSangException (503): khong kiem duoc thi KHONG cho vao — cung huong voi
    /// task-svc (thieu du lieu thi tu choi).
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
        }

        public async Task<IReadOnlyList<NguoiDungTraCuu>> TraCuuAsync(
            IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<string> emails,
            CancellationToken ct)
        {
            if (userIds == null)
            {
                throw new ArgumentNullException(nameof(userIds));
            }

            if (emails == null)
            {
                throw new ArgumentNullException(nameof(emails));
            }

            if (userIds.Count == 0 && emails.Count == 0)
            {
                return new List<NguoiDungTraCuu>();
            }

            if (string.IsNullOrWhiteSpace(_options.IdentityUrl))
            {
                throw new DichVuKhongSanSangException("Chua cau hinh InternalApi:IdentityUrl cho project-svc.", null);
            }

            string goc = _options.IdentityUrl.EndsWith('/') ? _options.IdentityUrl : _options.IdentityUrl + "/";
            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(goc), "internal/users/lookup")))
            {
                req.Headers.Add(InternalApiHeaders.Key, _options.Key);
                req.Content = JsonContent.Create(new { userIds = userIds, emails = emails });

                try
                {
                    using (CancellationTokenSource hetGio = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        hetGio.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                        HttpResponseMessage res = await _http.SendAsync(req, hetGio.Token);
                        if (!res.IsSuccessStatusCode)
                        {
                            throw new DichVuKhongSanSangException(
                                "identity-svc tra " + (int)res.StatusCode + " khi tra cuu nguoi dung. Thu lai sau.", null);
                        }

                        List<NguoiDungTraCuu>? kq = await res.Content.ReadFromJsonAsync<List<NguoiDungTraCuu>>(hetGio.Token);
                        if (kq == null)
                        {
                            throw new DichVuKhongSanSangException("identity-svc tra ve rong khi tra cuu nguoi dung.", null);
                        }

                        return kq;
                    }
                }
                catch (HttpRequestException ex)
                {
                    throw new DichVuKhongSanSangException("Khong goi duoc identity-svc de tra cuu nguoi dung. Thu lai sau.", ex);
                }
                catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    throw new DichVuKhongSanSangException("identity-svc khong tra loi kip khi tra cuu nguoi dung. Thu lai sau.", ex);
                }
            }
        }

        /// <summary>Ho so mot nguoi; khong ton tai → null.</summary>
        public async Task<NguoiDungTraCuu?> LayAsync(Guid userId, CancellationToken ct)
        {
            IReadOnlyList<NguoiDungTraCuu> ds = await TraCuuAsync(new[] { userId }, Array.Empty<string>(), ct);
            return ds.FirstOrDefault(x => x.UserId == userId);
        }
    }
}
