using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Contracts.Grpc.Project;
using Crowd.Labeling;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Grpc
{
    /// <summary>
    /// gRPC noi bo cho gate-svc (kien truc 3.23, contracts/grpc/project/v1/gate_data.proto): gate keo mau + cau
    /// vang kiem tra MOT lan khi du an publish bat cong link, thay cho viec chep moi du an tu dataset.ingested /
    /// gold_set.updated. Khong phai duong nong cua gate — gate phuc vu khach tu ban sao cua no.
    ///
    /// Chi DOC, AsNoTracking. Sau publish mau va cau vang khong doi nua (cau vang khoa tu luc roi Draft), nen
    /// doc mot lan la du. Khoa noi bo kiem o InternalKeyInterceptor.
    /// </summary>
    public sealed class GateDataService : GateData.GateDataBase
    {
        private readonly ProjectDbContext _db;

        public GateDataService(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public override async Task StreamGateContent(GateContentRequest request, IServerStreamWriter<GateContentItem> responseStream, ServerCallContext context)
        {
            Guid projectId;
            if (!Guid.TryParse(request.ProjectId, out projectId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "project_id khong phai uuid."));
            }

            CancellationToken ct = context.CancellationToken;
            LabelingProject? duAn = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (duAn == null || duAn.PublishedAt == null || !duAn.AllowLinkGateway)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Du an chua publish hoac khong bat cong link."));
            }

            // Cau cua bai test dau vao KHONG len trang cong khai (giong ban sao cu tu gold_set.updated).
            List<GoldItem> vang = await _db.GoldItems.AsNoTracking()
                .Where(g => g.ProjectId == projectId && g.Purpose == GoldPurpose.QualityCheck)
                .ToListAsync(ct);
            foreach (GoldItem g in vang)
            {
                LabelPayload dapAn = g.ExpectedPayload;
                await responseStream.WriteAsync(new GateContentItem
                {
                    Gold = new GateGoldItem
                    {
                        SampleId = g.SampleId.ToString(),
                        TaskType = dapAn.TaskType,
                        SchemaVersion = dapAn.SchemaVersion,
                        DataJson = dapAn.DataJson,
                    },
                }, ct);
            }

            // Mot phan vung (project_id) — doc dan theo stream, du an lon khong nap ca vao bo nho.
            IAsyncEnumerable<Sample> mau = _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.Id)
                .AsAsyncEnumerable();
            await foreach (Sample s in mau.WithCancellation(ct))
            {
                GateSample g = new GateSample
                {
                    SampleId = s.Id.ToString(),
                    Modality = s.Modality,
                    MetadataJson = s.Metadata.Json,
                };
                if (s.StorageKey != null)
                {
                    g.StorageKey = s.StorageKey;
                }

                if (s.Content != null)
                {
                    g.ContentJson = s.Content.Json;
                }

                await responseStream.WriteAsync(new GateContentItem { Sample = g }, ct);
            }
        }
    }
}
