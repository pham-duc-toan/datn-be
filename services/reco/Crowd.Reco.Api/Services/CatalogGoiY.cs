using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Labeling;
using Crowd.Reco.Api.Helpers;
using Crowd.Reco.Domain;
using Crowd.Reco.Infrastructure.Redis;
using Crowd.Reco.Infrastructure.Source;
using Microsoft.Extensions.Logging;

namespace Crowd.Reco.Api.Services
{
    /// <summary>Mau xem truoc trong bo nho (tu v_reco_preview_samples — MOT mau moi du an, he thong tu lay).</summary>
    public sealed class MauXemTruocRam
    {
        public required Guid SampleId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string Modality { get; init; }

        public string? StorageKey { get; init; }

        public string? ContentJson { get; init; }

        public required string MetadataJson { get; init; }

        public string? Question { get; init; }

        /// <summary>Video / am thanh: giay bat dau / ket thuc cua mau trong file (metadata segmentStart / segmentEnd).</summary>
        public double? BatDau { get; init; }

        public double? KetThuc { get; init; }
    }

    /// <summary>Mot ban chup bo nho — KHONG DOI sau khi tao; lan nap moi dung ban moi roi doi con tro mot lan.</summary>
    public sealed class AnhCatalog
    {
        public required IReadOnlyDictionary<Guid, DuAnGoiY> DuAn { get; init; }

        public required ThongKeCho ThongKe { get; init; }

        public required IReadOnlyDictionary<Guid, IReadOnlyList<MauXemTruocRam>> MauXemTruoc { get; init; }

        public required IReadOnlyDictionary<Guid, string?> TenDoanhNghiep { get; init; }

        public required DateTimeOffset NapLuc { get; init; }
    }

    /// <summary>
    /// BO NHO cua reco-svc (moi instance mot ban): du an dang nhan bai + dac trung cham diem, nap tu view v_reco_projects
    /// moi reco.cache_refresh_interval va ngay khi co event mo / dong du an. Request KHONG BAO GIO doi nap: ban moi dung o
    /// cho khac roi doi con tro mot lan (request dang chay van doc ban cu).
    /// </summary>
    public sealed class CatalogGoiY : IDisposable
    {
        private readonly NguonDuAn _nguon;
        private readonly RecoRedis _redis;
        private readonly TenDoanhNghiep _tenDoanhNghiep;
        private readonly TimeProvider _clock;
        private readonly ILogger<CatalogGoiY> _logger;
        private readonly ConcurrentDictionary<Guid, DateTimeOffset> _moLuc = new ConcurrentDictionary<Guid, DateTimeOffset>();
        private readonly ConcurrentDictionary<Guid, string?> _ten = new ConcurrentDictionary<Guid, string?>();
        private readonly SemaphoreSlim _coYeuCau = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _dangNap = new SemaphoreSlim(1, 1);
        private AnhCatalog? _anh;

        public CatalogGoiY(NguonDuAn nguon, RecoRedis redis, TenDoanhNghiep tenDoanhNghiep, TimeProvider clock, ILogger<CatalogGoiY> logger)
        {
            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            if (tenDoanhNghiep == null)
            {
                throw new ArgumentNullException(nameof(tenDoanhNghiep));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _nguon = nguon;
            _redis = redis;
            _tenDoanhNghiep = tenDoanhNghiep;
            _clock = clock;
            _logger = logger;
        }

        public void Dispose()
        {
            _coYeuCau.Dispose();
            _dangNap.Dispose();
        }

        public bool DaNap
        {
            get { return Volatile.Read(ref _anh) != null; }
        }

        /// <summary>Ban chup hien tai (chua nap → ban rong).</summary>
        public AnhCatalog Anh
        {
            get
            {
                AnhCatalog? a = Volatile.Read(ref _anh);
                if (a != null)
                {
                    return a;
                }

                return new AnhCatalog
                {
                    DuAn = new Dictionary<Guid, DuAnGoiY>(),
                    ThongKe = ThongKeCho.Rong(),
                    MauXemTruoc = new Dictionary<Guid, IReadOnlyList<MauXemTruocRam>>(),
                    TenDoanhNghiep = new Dictionary<Guid, string?>(),
                    NapLuc = DateTimeOffset.MinValue,
                };
            }
        }

        public DuAnGoiY? Tim(Guid id)
        {
            DuAnGoiY? p;
            return Anh.DuAn.TryGetValue(id, out p) ? p : null;
        }

        /// <summary>Event mo / dong du an: danh thuc worker nap lai ngay (gop nhieu yeu cau thanh mot lan nap).</summary>
        public void YeuCauNapLai()
        {
            if (_coYeuCau.CurrentCount == 0)
            {
                try
                {
                    _coYeuCau.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Da co yeu cau dang cho — mot lan nap la du.
                }
            }
        }

        /// <summary>Cho toi khi co yeu cau nap lai hoac het thoi gian.</summary>
        public async Task ChoYeuCauAsync(TimeSpan toiDa, CancellationToken ct)
        {
            await _coYeuCau.WaitAsync(toiDa, ct);
        }

        private static MauXemTruocRam TaoMau(DongMauXemTruoc x)
        {
            double? batDau = null;
            double? ketThuc = null;
            if (x.Modality == Modalities.Video || x.Modality == Modalities.Audio)
            {
                SampleMetadata md = SampleMetadata.Tu(RawJson.Tu(string.IsNullOrWhiteSpace(x.MetadataJson) ? "{}" : x.MetadataJson));
                batDau = md.SegmentStart ?? 0;
                ketThuc = md.SegmentEnd ?? md.DurationSec;
            }

            return new MauXemTruocRam
            {
                SampleId = x.SampleId,
                ProjectId = x.ProjectId,
                Modality = x.Modality,
                StorageKey = x.StorageKey,
                ContentJson = x.ContentJson,
                MetadataJson = x.MetadataJson,
                Question = x.Question,
                BatDau = batDau,
                KetThuc = ketThuc,
            };
        }

        /// <summary>Nap ban chup moi tu view cua project-svc. Loi → nem ra, giu ban cu.</summary>
        public async Task<int> NapAsync(bool laKhoiDong, CancellationToken ct)
        {
            await _dangNap.WaitAsync(ct);
            try
            {
                DateTimeOffset bayGio = _clock.GetUtcNow();
                List<DongDuAn> dong = await _nguon.DocDangNhanBaiAsync(ct);
                List<DongMauXemTruoc> mau = await _nguon.DocMauXemTruocAsync(ct);
                Dictionary<Guid, long> daThay = await _redis.DemDaThayAsync(dong.Select(x => x.Id).ToList());

                // Ten doanh nghiep cho watermark mau xem truoc: chi hoi chu du an co mau xem truoc ma chua biet ten.
                List<Guid> canTen = mau.Where(x => x.StorageKey != null)
                    .Select(x => dong.FirstOrDefault(d => d.Id == x.ProjectId))
                    .Where(d => d != null && !_ten.ContainsKey(d.OwnerId))
                    .Select(d => d!.OwnerId)
                    .Distinct()
                    .ToList();
                if (canTen.Count > 0)
                {
                    try
                    {
                        IReadOnlyDictionary<Guid, string?> ten = await _tenDoanhNghiep.TraCuuAsync(canTen, ct);
                        foreach (KeyValuePair<Guid, string?> cap in ten)
                        {
                            _ten[cap.Key] = cap.Value;
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // Thieu ten thi watermark chi bo phan ten cong ty — lan nap sau hoi lai.
                        _logger.LogWarning(ex, "Khong hoi duoc ten doanh nghiep cho watermark mau xem truoc");
                    }
                }

                Dictionary<Guid, DuAnGoiY> duAn = new Dictionary<Guid, DuAnGoiY>();
                HashSet<Guid> conMo = new HashSet<Guid>();
                foreach (DongDuAn d in dong)
                {
                    conMo.Add(d.Id);
                    // Luc reco thay du an DANG NHAN BAI lan dau. Luc khoi dong: lay luc publish (du an co tu truoc).
                    DateTimeOffset moLuc = _moLuc.GetOrAdd(d.Id, laKhoiDong ? d.PublishedAt : bayGio);
                    long thay;
                    daThay.TryGetValue(d.Id, out thay);
                    duAn[d.Id] = new DuAnGoiY
                    {
                        Id = d.Id,
                        OwnerId = d.OwnerId,
                        Name = d.Name,
                        Description = d.Description,
                        Modality = d.Modality,
                        Domains = d.Domains,
                        Languages = d.Languages,
                        ToolKinds = d.ToolKinds,
                        RewardVnd = d.RewardVnd,
                        Slots = d.Slots,
                        QuestionCount = d.QuestionCount,
                        Deadline = d.Deadline,
                        PublishedAt = d.PublishedAt,
                        IsPrivate = d.IsPrivate,
                        MinLevel = d.MinLevel,
                        MinReputation = d.MinReputation,
                        RequireEntranceTest = d.RequireEntranceTest,
                        RequireVerifiedIdentity = d.RequireVerifiedIdentity,
                        SensitiveContent = d.SensitiveContent,
                        NdaRequired = d.NdaRequired,
                        Accepting = d.Accepting,
                        SubmittedCount = d.SubmittedCount,
                        InProgressCount = d.InProgressCount,
                        StartedCount = d.StartedCount,
                        MedianSecondsPerQuestion = d.MedianSecondsPerQuestion,
                        Tools = d.Tools,
                        SeenCount = thay,
                        OpenedAt = moLuc,
                    };
                }

                // Du an da dong khong con trong view: bo moc "mo luc" (mo lai sau nay tinh la moi mo).
                foreach (Guid id in _moLuc.Keys.ToList())
                {
                    if (!conMo.Contains(id))
                    {
                        DateTimeOffset bo;
                        _moLuc.TryRemove(id, out bo);
                    }
                }

                Dictionary<Guid, IReadOnlyList<MauXemTruocRam>> xemTruoc = new Dictionary<Guid, IReadOnlyList<MauXemTruocRam>>();
                foreach (IGrouping<Guid, DongMauXemTruoc> nhom in mau.GroupBy(x => x.ProjectId))
                {
                    if (!duAn.ContainsKey(nhom.Key))
                    {
                        continue;
                    }

                    List<MauXemTruocRam> ds = nhom
                        .Take(1)
                        .Select(TaoMau)
                        .ToList();
                    xemTruoc[nhom.Key] = ds;
                }

                AnhCatalog moi = new AnhCatalog
                {
                    DuAn = duAn,
                    ThongKe = ThongKeCho.Tinh(duAn.Values),
                    MauXemTruoc = xemTruoc,
                    TenDoanhNghiep = new Dictionary<Guid, string?>(_ten),
                    NapLuc = bayGio,
                };
                Volatile.Write(ref _anh, moi);
                return duAn.Count;
            }
            finally
            {
                _dangNap.Release();
            }
        }
    }
}
