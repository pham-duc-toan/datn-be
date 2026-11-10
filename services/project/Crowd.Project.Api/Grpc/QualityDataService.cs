using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Grpc
{
    /// <summary>
    /// gRPC noi bo cho quality-svc (kien truc 3.22, contracts/grpc/project/v1/quality_data.proto): de bai, bai
    /// nop va nhan doc THANG tu project_db — quality-svc khong con giu ban sao projects / attempts / labels.
    ///
    /// Chi DOC, AsNoTracking, khong khoa: cham chay sau khi dong thu bai (bai nop khong doi nua), lo DS va xet
    /// gian lan chap nhan thay du lieu tre vai mili-giay. Khoa noi bo kiem o InternalKeyInterceptor.
    /// </summary>
    public sealed class QualityDataService : QualityData.QualityDataBase
    {
        private readonly ProjectDbContext _db;

        public QualityDataService(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public override async Task<ProjectInfo> GetProject(ProjectRequest request, ServerCallContext context)
        {
            Guid projectId = GrpcIds.Doc(request.ProjectId, "project_id");
            CancellationToken ct = context.CancellationToken;

            LabelingProject? duAn = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (duAn == null || duAn.PublishedAt == null || duAn.LabelSchema == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Du an chua publish."));
            }

            ProjectInfo kq = new ProjectInfo
            {
                ProjectId = duAn.Id.ToString(),
                OwnerId = duAn.OwnerId.ToString(),
                Modality = duAn.Modality,
                LabelSchemaJson = duAn.LabelSchema.ToRawJson().Json,
                PassThresholdPercent = duAn.PassThresholdPercent,
                Slots = duAn.Slots,
                QuestionCount = duAn.QuestionCount,
                Status = duAn.Status.ToString(),
                PublishedAt = Timestamp.FromDateTimeOffset(duAn.PublishedAt.Value),
            };
            if (duAn.FillSlotsMinPercent.HasValue)
            {
                kq.FillSlotsMinPercent = duAn.FillSlotsMinPercent.Value;
            }

            CollectionRound? dot = await _db.CollectionRounds.AsNoTracking().FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);
            if (dot != null && dot.FinishReason != null)
            {
                kq.FinishReason = dot.FinishReason;
                if (duAn.GradingStartedAt.HasValue)
                {
                    kq.CollectionFinishedAt = Timestamp.FromDateTimeOffset(duAn.GradingStartedAt.Value);
                }
            }

            List<Guid> biLoai = await _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId && s.ExcludedAt != null)
                .Select(s => s.Id)
                .ToListAsync(ct);
            foreach (Guid id in biLoai)
            {
                kq.ExcludedSampleIds.Add(id.ToString());
            }

            return kq;
        }

        public override async Task<AttemptList> ListSubmittedAttempts(ProjectRequest request, ServerCallContext context)
        {
            Guid projectId = GrpcIds.Doc(request.ProjectId, "project_id");
            CancellationToken ct = context.CancellationToken;

            List<Attempt> bai = await _db.Attempts.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.State == AttemptState.Submitted)
                .OrderBy(a => a.SubmittedAt)
                .ThenBy(a => a.Id)
                .ToListAsync(ct);

            // Cau vang co dinh tu luc roi Draft (KiemTraCoTheSuaCauHoiVang) → "cau that" cua moi bai luon tinh
            // lai dung nhu luc nop: thu tu de tru cau vang.
            HashSet<Guid> vang = (await _db.GoldItems.AsNoTracking()
                .Where(g => g.ProjectId == projectId && g.Purpose == GoldPurpose.QualityCheck)
                .Select(g => g.SampleId)
                .ToListAsync(ct)).ToHashSet();
            HashSet<Guid> coMau = (await _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .Select(s => s.Id)
                .ToListAsync(ct)).ToHashSet();
            Dictionary<Guid, int> soNhan = await _db.Annotations.AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.Source == LabelSource.Professional)
                .GroupBy(x => x.AttemptId)
                .Select(g => new { AttemptId = g.Key, SoNhan = g.Count() })
                .ToDictionaryAsync(x => x.AttemptId, x => x.SoNhan, ct);

            AttemptList kq = new AttemptList();
            foreach (Attempt a in bai)
            {
                int n;
                if (!soNhan.TryGetValue(a.Id, out n))
                {
                    n = 0;
                }

                AttemptInfo info = new AttemptInfo
                {
                    AttemptId = a.Id.ToString(),
                    LabelerId = a.LabelerId.ToString(),
                    LabelCount = n,
                    AnsweredTotal = a.AnsweredCount ?? 0,
                    GoldTotal = a.GoldTotal,
                    GoldCorrect = a.GoldCorrect,
                    StartedAt = Timestamp.FromDateTimeOffset(a.StartedAt),
                    SubmittedAt = Timestamp.FromDateTimeOffset(a.SubmittedAt ?? a.StartedAt),
                    Graded = a.GradedAt.HasValue,
                    ComparableCount = a.ComparableCount ?? 0,
                    MatchedCount = a.MatchedCount ?? 0,
                    AgreementPercent = a.AgreementPercent ?? 0,
                    Passed = a.Passed == true,
                    Rewarded = a.Rewarded == true,
                    AppealState = a.AppealState.ToString(),
                    ClientIp = a.ClientIp ?? string.Empty,
                    DeviceId = a.DeviceId ?? string.Empty,
                };
                foreach (Guid sampleId in a.QuestionOrder)
                {
                    if (!vang.Contains(sampleId) && coMau.Contains(sampleId))
                    {
                        info.SampleIds.Add(sampleId.ToString());
                    }
                }

                kq.Attempts.Add(info);
            }

            return kq;
        }

        public override async Task StreamLabels(LabelsRequest request, IServerStreamWriter<LabelRow> responseStream, ServerCallContext context)
        {
            Guid projectId = GrpcIds.Doc(request.ProjectId, "project_id");
            List<Guid> attemptIds = new List<Guid>();
            foreach (string s in request.AttemptIds)
            {
                attemptIds.Add(GrpcIds.Doc(s, "attempt_ids"));
            }

            // Nhan cua labeler (Professional) chi sinh luc nop bai, nen moi dong deu thuoc bai da nop. Nhan khach
            // vang lai o cong link (LinkGateway, khong co labeler) khong vao cham / DS.
            IQueryable<LabelAnnotation> nhan = _db.Annotations.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.Source == LabelSource.Professional && a.LabelerId != null);
            if (attemptIds.Count > 0)
            {
                nhan = nhan.Where(a => attemptIds.Contains(a.AttemptId));
            }

            // Cung khoa phan vung (project_id) o hai bang → Postgres chi cham MOT phan vung moi ben. Luc nop lay tu
            // bai (annotations khong luu moc thoi gian).
            var dong = from a in nhan
                       join s in _db.Samples.AsNoTracking().Where(x => x.ProjectId == projectId) on a.SampleId equals s.Id
                       join t in _db.Attempts.AsNoTracking() on a.AttemptId equals t.Id
                       orderby t.SubmittedAt, a.AttemptId, a.SampleId
                       select new
                       {
                           a.AttemptId,
                           a.SampleId,
                           a.LabelerId,
                           Data = a.DataJson,
                           s.Metadata,
                           SubmittedAt = t.SubmittedAt ?? t.StartedAt,
                       };

            await foreach (var d in dong.AsAsyncEnumerable().WithCancellation(context.CancellationToken))
            {
                await responseStream.WriteAsync(new LabelRow
                {
                    AttemptId = d.AttemptId.ToString(),
                    SampleId = d.SampleId.ToString(),
                    LabelerId = d.LabelerId.HasValue ? d.LabelerId.Value.ToString() : string.Empty,
                    DataJson = d.Data,
                    SampleMetadataJson = d.Metadata.Json,
                    SubmittedAt = Timestamp.FromDateTimeOffset(d.SubmittedAt),
                });
            }
        }

        public override async Task<SharedSubmittersResponse> FindSharedSubmitters(SharedSubmittersRequest request, ServerCallContext context)
        {
            bool theoIp = !string.IsNullOrEmpty(request.ClientIp);
            bool theoThietBi = !string.IsNullOrEmpty(request.DeviceId);
            if (theoIp == theoThietBi)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Dien dung mot trong client_ip / device_id."));
            }

            if (request.Since == null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Thieu since."));
            }

            DateTimeOffset tu = request.Since.ToDateTimeOffset();
            IQueryable<Attempt> q = _db.Attempts.AsNoTracking()
                .Where(a => a.State == AttemptState.Submitted && a.SubmittedAt > tu);
            if (theoIp)
            {
                string ip = request.ClientIp;
                q = q.Where(a => a.ClientIp == ip);
            }
            else
            {
                string thietBi = request.DeviceId;
                q = q.Where(a => a.DeviceId == thietBi);
            }

            List<Guid> nguoi = await q.Select(a => a.LabelerId).Distinct().ToListAsync(context.CancellationToken);

            SharedSubmittersResponse kq = new SharedSubmittersResponse();
            foreach (Guid id in nguoi)
            {
                kq.LabelerIds.Add(id.ToString());
            }

            return kq;
        }

        public override async Task<LabelerStatsResponse> GetLabelerSubmissionStats(LabelerStatsRequest request, ServerCallContext context)
        {
            List<Guid> ids = new List<Guid>();
            foreach (string s in request.LabelerIds)
            {
                ids.Add(GrpcIds.Doc(s, "labeler_ids"));
            }

            var bai = await _db.Attempts.AsNoTracking()
                .Where(a => ids.Contains(a.LabelerId) && a.State == AttemptState.Submitted)
                .Select(a => new { a.LabelerId, a.StartedAt, a.SubmittedAt, a.AnsweredCount })
                .ToListAsync(context.CancellationToken);

            LabelerStatsResponse kq = new LabelerStatsResponse();
            foreach (var nhom in bai.GroupBy(a => a.LabelerId))
            {
                List<double> giay = nhom
                    .Select(a => GiayMoiCau(a.StartedAt, a.SubmittedAt ?? a.StartedAt, a.AnsweredCount ?? 0))
                    .OrderBy(x => x)
                    .ToList();
                kq.Labelers.Add(new LabelerStats
                {
                    LabelerId = nhom.Key.ToString(),
                    Submissions = giay.Count,
                    MedianSecondsPerQuestion = TrungVi(giay),
                });
            }

            return kq;
        }

        public override async Task<LabelerEvidenceResponse> GetLabelerEvidence(LabelerEvidenceRequest request, ServerCallContext context)
        {
            List<Guid> ids = new List<Guid>();
            foreach (string s in request.LabelerIds)
            {
                ids.Add(GrpcIds.Doc(s, "labeler_ids"));
            }

            // Bai DA CHAM = project.graded da ghi ket qua (GradedAt). Bon so nay chi dat mot lan luc cham, phan xu
            // khieu nai khong doi chung — quality khong can giu ban sao (attempt_grades cu). Chi muc ix_attempts_labeler.
            var tong = await _db.Attempts.AsNoTracking()
                .Where(a => ids.Contains(a.LabelerId) && a.GradedAt != null)
                .GroupBy(a => a.LabelerId)
                .Select(g => new
                {
                    LabelerId = g.Key,
                    SoBai = g.Count(),
                    GoldTotal = g.Sum(a => (long)a.GoldTotal),
                    GoldCorrect = g.Sum(a => (long)a.GoldCorrect),
                    SoDuoc = g.Sum(a => (long)(a.ComparableCount ?? 0)),
                    Giong = g.Sum(a => (long)(a.MatchedCount ?? 0)),
                })
                .ToListAsync(context.CancellationToken);

            LabelerEvidenceResponse kq = new LabelerEvidenceResponse();
            foreach (var t in tong)
            {
                kq.Labelers.Add(new LabelerEvidence
                {
                    LabelerId = t.LabelerId.ToString(),
                    GradedAttempts = t.SoBai,
                    GoldTotal = t.GoldTotal,
                    GoldCorrect = t.GoldCorrect,
                    ComparableTotal = t.SoDuoc,
                    MatchedTotal = t.Giong,
                });
            }

            return kq;
        }

        /// <summary>So giay trung binh moi cau da tra loi — cung cong thuc quality-svc dung khi xet "qua nhanh".</summary>
        public static double GiayMoiCau(DateTimeOffset batDau, DateTimeOffset nop, int soCauDaTraLoi)
        {
            double giay = Math.Max(0, (nop - batDau).TotalSeconds);
            return giay / Math.Max(soCauDaTraLoi, 1);
        }

        /// <param name="daSapXep">Day da sap xep tang dan.</param>
        public static double TrungVi(IReadOnlyList<double> daSapXep)
        {
            if (daSapXep == null)
            {
                throw new ArgumentNullException(nameof(daSapXep));
            }

            if (daSapXep.Count == 0)
            {
                return 0;
            }

            int giua = daSapXep.Count / 2;
            if (daSapXep.Count % 2 == 1)
            {
                return daSapXep[giua];
            }

            return (daSapXep[giua - 1] + daSapXep[giua]) / 2;
        }
    }
}
