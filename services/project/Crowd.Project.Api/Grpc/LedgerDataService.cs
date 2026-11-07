using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Crowd.Contracts.Grpc.Project;
using Crowd.Project.Infrastructure.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Grpc
{
    /// <summary>
    /// gRPC noi bo cho ledger-svc (kien truc 3.24, contracts/grpc/project/v1/ledger_data.proto): ten du an in
    /// len hoa don VAT — ledger khong con chep ten vao project_escrows. Khoa noi bo kiem o InternalKeyInterceptor.
    /// </summary>
    public sealed class LedgerDataService : LedgerData.LedgerDataBase
    {
        public const int ToiDaMoiLan = 1000;

        private readonly ProjectDbContext _db;

        public LedgerDataService(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public override async Task<ProjectNamesResponse> GetProjectNames(ProjectNamesRequest request, ServerCallContext context)
        {
            if (request.ProjectIds.Count > ToiDaMoiLan)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Toi da " + ToiDaMoiLan + " du an moi lan."));
            }

            List<Guid> ids = new List<Guid>();
            foreach (string s in request.ProjectIds)
            {
                ids.Add(GrpcIds.Doc(s, "project_ids"));
            }

            var ten = await _db.Projects.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync(context.CancellationToken);

            ProjectNamesResponse kq = new ProjectNamesResponse();
            foreach (var t in ten)
            {
                kq.Projects.Add(new ProjectName { ProjectId = t.Id.ToString(), Name = t.Name });
            }

            return kq;
        }
    }
}
