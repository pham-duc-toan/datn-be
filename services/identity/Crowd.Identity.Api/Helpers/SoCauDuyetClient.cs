using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Grpc.Project;
using Crowd.Identity.Api.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Crowd.Identity.Api.Helpers
{
    /// <summary>Mot dong bang xep hang project-svc tra ve.</summary>
    public sealed class DongSoCau
    {
        public DongSoCau(Guid userId, long soCau)
        {
            UserId = userId;
            SoCau = soCau;
        }

        public Guid UserId { get; }

        public long SoCau { get; }
    }

    /// <summary>
    /// So cau DUOC CHAP NHAN cua labeler — hoi project-svc qua gRPC LabelerStatsData (kien truc 3.24), identity khong
    /// con giu bo dem cong don. Doc tren trang ca nhan / bang xep hang co BO NHO DEM trong tien trinh (setting
    /// identity.stats_cache_ttl): doc nhieu, doi it (chi doi khi du an cham xong / khieu nai duoc chap nhan).
    /// Worker tinh cap (DemAsync) luon hoi moi va ghi de bo nho dem cua nguoi vua tinh.
    ///
    /// project-svc tat / qua han → DichVuKhongSanSangException (503); worker bat loi do va thu lai vong sau.
    /// </summary>
    public sealed class SoCauDuyetClient
    {
        private static readonly TimeSpan HetGio = TimeSpan.FromSeconds(10);

        private readonly LabelerStatsData.LabelerStatsDataClient _client;
        private readonly InternalApiOptions _internal;
        private readonly IMemoryCache _cache;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public SoCauDuyetClient(
            LabelerStatsData.LabelerStatsDataClient client,
            IOptions<InternalApiOptions> internalApi,
            IMemoryCache cache,
            ISettings settings,
            TimeProvider clock)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (internalApi == null)
            {
                throw new ArgumentNullException(nameof(internalApi));
            }

            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _client = client;
            _internal = internalApi.Value;
            _cache = cache;
            _settings = settings;
            _clock = clock;
        }

        /// <summary>So cau duoc chap nhan cua tung nguoi — luon hoi project-svc, roi cap nhat bo nho dem.</summary>
        public async Task<Dictionary<Guid, long>> DemAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
        {
            if (userIds == null)
            {
                throw new ArgumentNullException(nameof(userIds));
            }

            Dictionary<Guid, long> kq = new Dictionary<Guid, long>();
            if (userIds.Count == 0)
            {
                return kq;
            }

            ApprovedCountsRequest req = new ApprovedCountsRequest();
            req.LabelerIds.AddRange(userIds.Select(x => x.ToString()));
            ApprovedCountsResponse res = await GoiAsync(
                () => _client.GetApprovedCountsAsync(req, InternalGrpc.Khoa(_internal), Han(), ct).ResponseAsync);

            foreach (LabelerApproved l in res.Labelers)
            {
                Guid id = Guid.Parse(l.LabelerId);
                kq[id] = l.ApprovedCount;
                GhiDem(KhoaMotNguoi(id), l.ApprovedCount);
            }

            return kq;
        }

        /// <summary>So cau duoc chap nhan cua mot nguoi (trang "cap do cua toi") — qua bo nho dem.</summary>
        public async Task<long> CuaMotNguoiAsync(Guid userId, CancellationToken ct)
        {
            long co;
            if (_cache.TryGetValue(KhoaMotNguoi(userId), out co))
            {
                return co;
            }

            Dictionary<Guid, long> dem = await DemAsync(new[] { userId }, ct);
            long soCau;
            dem.TryGetValue(userId, out soCau);
            return soCau;
        }

        /// <summary>Mot trang bang xep hang tu project-svc (chua loc tai khoan bi khoa — noi goi loc).</summary>
        /// <param name="tu">null = moi luc.</param>
        public async Task<IReadOnlyList<DongSoCau>> XepHangAsync(DateTimeOffset? tu, int limit, int offset, CancellationToken ct)
        {
            LeaderboardRequest req = new LeaderboardRequest { Limit = limit, Offset = offset };
            if (tu.HasValue)
            {
                req.Since = Timestamp.FromDateTimeOffset(tu.Value);
            }

            LeaderboardResponse res = await GoiAsync(
                () => _client.GetLeaderboardAsync(req, InternalGrpc.Khoa(_internal), Han(), ct).ResponseAsync);
            return res.Labelers.Select(l => new DongSoCau(Guid.Parse(l.LabelerId), l.ApprovedCount)).ToList();
        }

        /// <summary>Thoi gian giu ket qua trong bo nho dem (setting identity.stats_cache_ttl); 0 = khong giu.</summary>
        public TimeSpan ThoiGianDem()
        {
            return _settings.ThoiGian(SettingKeys.IdentityStatsCacheTtl);
        }

        private void GhiDem(string khoa, long giaTri)
        {
            TimeSpan giu = ThoiGianDem();
            if (giu > TimeSpan.Zero)
            {
                _cache.Set(khoa, giaTri, giu);
            }
        }

        private DateTime Han()
        {
            return _clock.GetUtcNow().UtcDateTime.Add(HetGio);
        }

        private static string KhoaMotNguoi(Guid userId)
        {
            return "so_cau_duyet:" + userId.ToString("N");
        }

        private static async Task<T> GoiAsync<T>(Func<Task<T>> goi)
        {
            try
            {
                return await goi();
            }
            catch (RpcException ex) when (InternalGrpc.LaLoiTamThoi(ex))
            {
                throw new DichVuKhongSanSangException("project-svc tam thoi khong tra loi so cau duoc chap nhan. Thu lai sau.", ex);
            }
        }
    }
}
