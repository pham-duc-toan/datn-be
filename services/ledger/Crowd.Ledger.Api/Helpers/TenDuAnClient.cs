using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Contracts.Grpc.Project;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Crowd.Ledger.Api.Helpers
{
    /// <summary>
    /// Ten du an in len hoa don VAT — hoi project-svc qua gRPC LedgerData (kien truc 3.24) luc xuat / tam tinh
    /// hoa don; ledger khong con chep ten vao project_escrows. Ten duoc CHUP vao dong hoa don (doi ten du an sau
    /// nay khong sua hoa don cu). project-svc khong tra loi → DichVuKhongSanSangException (503; worker xuat tu
    /// dong thu lai vong sau).
    /// </summary>
    public sealed class TenDuAnClient
    {
        private readonly LedgerData.LedgerDataClient _client;
        private readonly InternalApiOptions _options;
        private readonly TimeProvider _clock;

        public TenDuAnClient(LedgerData.LedgerDataClient client, IOptions<InternalApiOptions> options, TimeProvider clock)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _client = client;
            _options = options.Value;
            _clock = clock;
        }

        /// <summary>projectId → ten; du an khong ton tai thi khong co trong ket qua.</summary>
        public async Task<Dictionary<Guid, string>> LayAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
        {
            if (projectIds == null)
            {
                throw new ArgumentNullException(nameof(projectIds));
            }

            Dictionary<Guid, string> kq = new Dictionary<Guid, string>();
            if (projectIds.Count == 0)
            {
                return kq;
            }

            ProjectNamesRequest req = new ProjectNamesRequest();
            req.ProjectIds.AddRange(projectIds.Distinct().Select(x => x.ToString()));
            ProjectNamesResponse res;
            try
            {
                res = await _client.GetProjectNamesAsync(
                    req, InternalGrpc.Khoa(_options), _clock.GetUtcNow().UtcDateTime.AddSeconds(_options.TimeoutSeconds), ct);
            }
            catch (RpcException ex) when (InternalGrpc.LaLoiTamThoi(ex))
            {
                throw new DichVuKhongSanSangException("Khong goi duoc project-svc lay ten du an: " + ex.Status.Detail);
            }

            foreach (ProjectName p in res.Projects)
            {
                kq[Guid.Parse(p.ProjectId)] = p.Name;
            }

            return kq;
        }
    }
}
