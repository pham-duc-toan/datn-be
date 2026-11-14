using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Contracts.Grpc.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Crowd.Gate.Api.Helpers
{
    /// <summary>
    /// Ten cong ty cua chu du an — in vao watermark noi cua anh phat cho khach vuot link ("nguồn: &lt;ma phien&gt; -
    /// &lt;doanh nghiep&gt; - &lt;domain&gt;"). Hoi identity qua gRPC IdentityData (kien truc 3.24). CHI GateCatalog goi
    /// luc nap bo nho (viec nen) — request /g, /go chi doc ten da nap, khong goi mang.
    /// </summary>
    public sealed class TenDoanhNghiepCong
    {
        private readonly IServiceProvider _services;
        private readonly InternalApiOptions _options;
        private readonly TimeProvider _clock;

        public TenDoanhNghiepCong(IServiceProvider services, IOptions<InternalApiOptions> options, TimeProvider clock)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _services = services;
            _options = options.Value;
            _clock = clock;
        }

        /// <summary>
        /// owner → ten cong ty (null = khong co ho so doanh nghiep). identity khong tra loi → nem RpcException, noi
        /// goi giu nguyen phan da biet va thu lai lan nap sau.
        /// </summary>
        public async Task<IReadOnlyDictionary<Guid, string?>> TraCuuAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken ct)
        {
            if (ownerIds == null)
            {
                throw new ArgumentNullException(nameof(ownerIds));
            }

            Dictionary<Guid, string?> kq = new Dictionary<Guid, string?>();
            if (ownerIds.Count == 0)
            {
                return kq;
            }

            // Client gRPC tu factory la transient: lay moi lan goi (handler duoc factory xoay vong).
            IdentityData.IdentityDataClient client = _services.GetRequiredService<IdentityData.IdentityDataClient>();
            LookupUsersRequest req = new LookupUsersRequest();
            req.UserIds.AddRange(ownerIds.Select(x => x.ToString()));
            LookupUsersResponse res = await client.LookupUsersAsync(
                req, InternalGrpc.Khoa(_options), _clock.GetUtcNow().UtcDateTime.AddSeconds(_options.TimeoutSeconds), ct);

            foreach (Guid id in ownerIds)
            {
                kq[id] = null;
            }

            foreach (UserInfo u in res.Users)
            {
                Guid id;
                if (Guid.TryParse(u.UserId, out id))
                {
                    kq[id] = string.IsNullOrWhiteSpace(u.CompanyName) ? null : u.CompanyName.Trim();
                }
            }

            return kq;
        }
    }
}
