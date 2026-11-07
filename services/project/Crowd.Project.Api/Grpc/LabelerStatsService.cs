using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Crowd.Contracts.Grpc.Project;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Infrastructure.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Grpc
{
    /// <summary>
    /// gRPC noi bo cho identity-svc (kien truc 3.24, contracts/grpc/project/v1/labeler_stats.proto, LabelerStatsData): so cau
    /// duoc chap nhan cua labeler tinh THANG tren project.attempts — identity khong con giu bo dem cong don.
    ///
    /// Bai duoc chap nhan: passed = true (tinh tu graded_at) hoac khieu nai duoc chap nhan (tu
    /// appeal_resolved_at). Chi muc ix_attempts_chap_nhan (loc san hai dieu kien do). Khoa noi bo kiem o
    /// InternalKeyInterceptor.
    /// </summary>
    public sealed class LabelerStatsService : LabelerStatsData.LabelerStatsDataBase
    {
        public const int ToiDaMoiLan = 1000;

        private readonly ProjectDbContext _db;

        public LabelerStatsService(ProjectDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        public override async Task<ApprovedCountsResponse> GetApprovedCounts(ApprovedCountsRequest request, ServerCallContext context)
        {
            if (request.LabelerIds.Count > ToiDaMoiLan)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Toi da " + ToiDaMoiLan + " labeler moi lan."));
            }

            List<Guid> ids = new List<Guid>();
            foreach (string s in request.LabelerIds)
            {
                ids.Add(GrpcIds.Doc(s, "labeler_ids"));
            }

            var tong = await ChapNhan(_db.Attempts.AsNoTracking())
                .Where(a => ids.Contains(a.LabelerId))
                .GroupBy(a => a.LabelerId)
                .Select(g => new { LabelerId = g.Key, SoCau = g.Sum(a => (long)(a.AnsweredCount ?? 0)) })
                .ToListAsync(context.CancellationToken);
            Dictionary<Guid, long> theoLabeler = tong.ToDictionary(t => t.LabelerId, t => t.SoCau);

            ApprovedCountsResponse kq = new ApprovedCountsResponse();
            foreach (Guid id in ids.Distinct())
            {
                long soCau;
                theoLabeler.TryGetValue(id, out soCau);
                kq.Labelers.Add(new LabelerApproved { LabelerId = id.ToString(), ApprovedCount = soCau });
            }

            return kq;
        }

        public override async Task<LeaderboardResponse> GetLeaderboard(LeaderboardRequest request, ServerCallContext context)
        {
            if (request.Limit < 1 || request.Limit > ToiDaMoiLan || request.Offset < 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "limit 1-" + ToiDaMoiLan + ", offset >= 0."));
            }

            IQueryable<Attempt> q = ChapNhan(_db.Attempts.AsNoTracking());
            if (request.Since != null)
            {
                DateTimeOffset tu = request.Since.ToDateTimeOffset();
                q = q.Where(a => (a.Passed == true && a.GradedAt >= tu)
                                 || (a.Passed != true && a.AppealResolvedAt >= tu));
            }

            var top = await q
                .GroupBy(a => a.LabelerId)
                .Select(g => new { LabelerId = g.Key, SoCau = g.Sum(a => (long)(a.AnsweredCount ?? 0)) })
                .Where(x => x.SoCau > 0)
                .OrderByDescending(x => x.SoCau).ThenBy(x => x.LabelerId)
                .Skip(request.Offset)
                .Take(request.Limit)
                .ToListAsync(context.CancellationToken);

            LeaderboardResponse kq = new LeaderboardResponse();
            foreach (var t in top)
            {
                kq.Labelers.Add(new LabelerApproved { LabelerId = t.LabelerId.ToString(), ApprovedCount = t.SoCau });
            }

            return kq;
        }

        /// <summary>Bai DUOC CHAP NHAN: cham dat, hoac truot nhung khieu nai duoc chap nhan.</summary>
        private static IQueryable<Attempt> ChapNhan(IQueryable<Attempt> q)
        {
            return q.Where(a => a.Passed == true || a.AppealState == AppealState.Accepted);
        }
    }
}
