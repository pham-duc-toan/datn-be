using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Gate.Domain;
using Crowd.Gate.Infrastructure.Source;
using Crowd.Labeling;
using Microsoft.Extensions.Logging;

namespace Crowd.Gate.Api.Services
{
    /// <summary>Mot du an DANG phuc vu duoc tren trang vuot link (da qua LuatCongLink).</summary>
    public sealed class DuAnPhucVu
    {
        public DuAnPhucVu(DuAnCong duAn, long nganSachVnd, LabelSchema tapNhan, List<Guid> cauThat, Dictionary<Guid, LabelPayload> dapAnVang)
        {
            DuAn = duAn;
            NganSachVnd = nganSachVnd;
            TapNhan = tapNhan;
            CauThat = cauThat;
            DapAnVang = dapAnVang;
            CauVang = dapAnVang.Keys.ToList();
            using (JsonDocument d = JsonDocument.Parse(duAn.LabelSchemaJson))
            {
                TapNhanJson = d.RootElement.Clone();
            }
        }

        public DuAnCong DuAn { get; }

        /// <summary>Ngan sach cong link luc nap (ledger tinh trong view v_gate_budgets).</summary>
        public long NganSachVnd { get; }

        public LabelSchema TapNhan { get; }

        public JsonElement TapNhanJson { get; }

        public List<Guid> CauThat { get; }

        public List<Guid> CauVang { get; }

        /// <summary>Dap an cau vang — chi dung de cham, KHONG BAO GIO tra ra client.</summary>
        public Dictionary<Guid, LabelPayload> DapAnVang { get; }
    }

    /// <summary>
    /// Bo nho cua DUONG NONG: du an phuc vu duoc, mau, dap an vang, ngan sach, link dang chay. Nap THANG tu view
    /// v_gate_* cua project / ledger / link (kien truc 3.32 — gate khong giu ban sao trong DB) luc khoi dong
    /// (CatalogWarmup: chua nap xong thi chua nhan request) va moi gate.cache_refresh_interval. Request /g, /go chi
    /// doc o day — khong cham Postgres, khong goi service nao.
    ///
    /// Mau + cau vang sau publish khong doi nen moi du an chi doc MOT lan (giu trong _noiDung); moi vong chi doc lai
    /// phan nho: danh sach du an + trang thai, ngan sach, link. Du an roi khoi view (huy / hoan thanh) → bo khoi bo nho.
    ///
    /// Anh chup BAT BIEN, doi ca khoi (Volatile): request dang doc khong bao gio thay nua cu nua moi. Rieng ngan sach
    /// co them phan "da tieu tai cho" giua hai lan nap (ledger moi la nguon su that) de het tien thi ngung phuc vu som,
    /// khong doi toi lan nap sau.
    /// </summary>
    public sealed class GateCatalog : IDisposable
    {
        private readonly ILogger<GateCatalog> _logger;
        private readonly ConcurrentDictionary<Guid, long> _daTieuTaiCho = new ConcurrentDictionary<Guid, long>();

        // Chi mot luong nap (warmup roi worker, khong chong nhau) doc / ghi.
        private readonly Dictionary<Guid, NoiDungDuAn> _noiDung = new Dictionary<Guid, NoiDungDuAn>();
        private readonly SemaphoreSlim _khoaNap = new SemaphoreSlim(1, 1);
        private AnhChup _hienTai = AnhChup.Rong;

        public GateCatalog(ILogger<GateCatalog> logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _logger = logger;
        }

        public void Dispose()
        {
            _khoaNap.Dispose();
        }

        /// <summary>Da nap xong it nhat mot lan — truoc do gate chua san sang (/health/ready tra 503).</summary>
        public bool DaNap
        {
            get { return Hien.LanNap.HasValue; }
        }

        private AnhChup Hien
        {
            get { return Volatile.Read(ref _hienTai); }
        }

        public LinkCong? LayLink(string code)
        {
            LinkCong? l;
            return Hien.LinkTheoMa.TryGetValue(code, out l) ? l : null;
        }

        /// <summary>Cac link con phuc vu duoc, thu tu ngau nhien (de chon "link khac" cho khach vua truot).</summary>
        public List<LinkCong> LinkDangPhucVu(DateTimeOffset luc)
        {
            List<LinkCong> ds = new List<LinkCong>();
            foreach (LinkCong l in Hien.LinkTheoMa.Values)
            {
                if (l.PhucVuDuoc(luc))
                {
                    ds.Add(l);
                }
            }

            return ds.OrderBy(x => Random.Shared.Next()).ToList();
        }

        public MauCong? LayMau(Guid id)
        {
            MauCong? m;
            return Hien.Mau.TryGetValue(id, out m) ? m : null;
        }

        public DuAnPhucVu? LayDuAn(Guid id)
        {
            DuAnPhucVu? d;
            return Hien.DuAn.TryGetValue(id, out d) ? d : null;
        }

        /// <summary>Ngan sach con lai uoc tinh = so ledger tinh (view) − phan da tieu tai cho tu lan nap truoc.</summary>
        public long NganSachConLai(Guid projectId)
        {
            DuAnPhucVu? d = LayDuAn(projectId);
            if (d == null)
            {
                return 0;
            }

            long daTieu;
            _daTieuTaiCho.TryGetValue(projectId, out daTieu);
            return d.NganSachVnd - daTieu;
        }

        public void TruNganSach(Guid projectId, long soTien)
        {
            _daTieuTaiCho.AddOrUpdate(projectId, soTien, (k, cu) => cu + soTien);
        }

        /// <summary>Du an con phuc vu duoc, thu tu ngau nhien (chia deu traffic giua cac du an).</summary>
        public List<DuAnPhucVu> UngVien(int soCauThat, int soCauVang)
        {
            List<DuAnPhucVu> ds = new List<DuAnPhucVu>();
            foreach (DuAnPhucVu d in Hien.DuAn.Values)
            {
                if (d.DuAn.Status != TrangThaiDuAnCong.Running || d.CauVang.Count < soCauVang || d.CauThat.Count == 0)
                {
                    continue;
                }

                long chiPhi = LuatCongLink.ChiPhiMotLuot(soCauThat, d.DuAn.UnitPriceVnd, d.DuAn.PlatformFeeVnd);
                if (NganSachConLai(d.DuAn.ProjectId) < chiPhi)
                {
                    continue;
                }

                ds.Add(d);
            }

            return ds.OrderBy(x => Random.Shared.Next()).ToList();
        }

        /// <summary>Tom tat bo nho (cho /internal/catalog) — KHONG co dap an vang.</summary>
        public TomTatCatalog TomTat()
        {
            AnhChup a = Hien;
            List<TomTatDuAn> duAn = new List<TomTatDuAn>();
            foreach (DuAnPhucVu d in a.DuAn.Values)
            {
                duAn.Add(new TomTatDuAn
                {
                    ProjectId = d.DuAn.ProjectId,
                    Status = d.DuAn.Status.ToString(),
                    RealSamples = d.CauThat.Count,
                    GoldItems = d.CauVang.Count,
                    BudgetVnd = NganSachConLai(d.DuAn.ProjectId),
                });
            }

            return new TomTatCatalog
            {
                Ready = a.LanNap.HasValue,
                LoadedAt = a.LanNap,
                Projects = duAn.OrderBy(x => x.ProjectId).ToList(),
                Links = a.LinkTheoMa.Count,
            };
        }

        /// <summary>
        /// Doc lai tu view cua chu du lieu. Loi (DB chu tam khong tra loi) → nem ra, anh chup cu giu nguyen.
        /// </summary>
        public async Task NapLaiAsync(NguonCongLink nguon, CancellationToken ct)
        {
            if (nguon == null)
            {
                throw new ArgumentNullException(nameof(nguon));
            }

            await _khoaNap.WaitAsync(ct);
            try
            {
                await NapAsync(nguon, ct);
            }
            finally
            {
                _khoaNap.Release();
            }
        }

        private async Task NapAsync(NguonCongLink nguon, CancellationToken ct)
        {
            List<DuAnCong> duAn = await nguon.DocDuAnAsync(ct);
            List<LinkCong> link = await nguon.DocLinkAsync(ct);
            List<Guid> ids = duAn.Select(p => p.ProjectId).ToList();
            Dictionary<Guid, long> nganSach = await nguon.DocNganSachAsync(ids, ct);

            // Mau + cau vang: du an moi len cong thi doc mot lan; du an da roi khoi view thi bo.
            HashSet<Guid> conTrongView = new HashSet<Guid>(ids);
            foreach (Guid cu in _noiDung.Keys.Where(k => !conTrongView.Contains(k)).ToList())
            {
                _noiDung.Remove(cu);
            }

            foreach (Guid id in ids)
            {
                if (!_noiDung.ContainsKey(id))
                {
                    NoiDungDuAn nd = await nguon.DocNoiDungAsync(id, ct);
                    _noiDung[id] = nd;
                    _logger.LogInformation("Cong link: nap du an {ProjectId} — {SoMau} mau, {SoVang} cau vang", id, nd.Mau.Count, nd.Vang.Count);
                }
            }

            Dictionary<Guid, DuAnPhucVu> phucVu = new Dictionary<Guid, DuAnPhucVu>();
            Dictionary<Guid, MauCong> mauTheoId = new Dictionary<Guid, MauCong>();
            foreach (DuAnCong d in duAn)
            {
                LabelSchema tapNhan;
                try
                {
                    tapNhan = LabelSchema.Doc(d.LabelSchemaJson);
                }
                catch (LabelFormatException ex)
                {
                    _logger.LogError(ex, "Tap nhan du an {ProjectId} hong — bo qua", d.ProjectId);
                    continue;
                }

                if (LuatCongLink.LyDoKhongHoTro(d.Modality, tapNhan) != null)
                {
                    continue;
                }

                NoiDungDuAn noiDung = _noiDung[d.ProjectId];
                Dictionary<Guid, LabelPayload> dapAn = new Dictionary<Guid, LabelPayload>();
                foreach (CauVangCong g in noiDung.Vang)
                {
                    dapAn[g.SampleId] = g.DapAn;
                }

                List<Guid> cauThat = new List<Guid>();
                foreach (MauCong m in noiDung.Mau)
                {
                    mauTheoId[m.SampleId] = m;
                    if (!dapAn.ContainsKey(m.SampleId))
                    {
                        cauThat.Add(m.SampleId);
                    }
                }

                long conLaiVnd;
                nganSach.TryGetValue(d.ProjectId, out conLaiVnd);
                phucVu[d.ProjectId] = new DuAnPhucVu(d, conLaiVnd, tapNhan, cauThat, dapAn);
            }

            Dictionary<string, LinkCong> linkTheoMa = new Dictionary<string, LinkCong>(StringComparer.Ordinal);
            foreach (LinkCong l in link)
            {
                linkTheoMa[l.Code] = l;
            }

            Volatile.Write(ref _hienTai, new AnhChup(phucVu, mauTheoId, linkTheoMa, DateTimeOffset.UtcNow));
            _daTieuTaiCho.Clear();
        }

        private sealed class AnhChup
        {
            public static readonly AnhChup Rong = new AnhChup(
                new Dictionary<Guid, DuAnPhucVu>(), new Dictionary<Guid, MauCong>(), new Dictionary<string, LinkCong>(StringComparer.Ordinal), null);

            public AnhChup(Dictionary<Guid, DuAnPhucVu> duAn, Dictionary<Guid, MauCong> mau, Dictionary<string, LinkCong> linkTheoMa, DateTimeOffset? lanNap)
            {
                DuAn = duAn;
                Mau = mau;
                LinkTheoMa = linkTheoMa;
                LanNap = lanNap;
            }

            public Dictionary<Guid, DuAnPhucVu> DuAn { get; }

            public Dictionary<Guid, MauCong> Mau { get; }

            public Dictionary<string, LinkCong> LinkTheoMa { get; }

            /// <summary>null = chua nap lan nao.</summary>
            public DateTimeOffset? LanNap { get; }
        }
    }

    public sealed class TomTatCatalog
    {
        public required bool Ready { get; init; }

        public DateTimeOffset? LoadedAt { get; init; }

        public required IReadOnlyList<TomTatDuAn> Projects { get; init; }

        public required int Links { get; init; }
    }

    public sealed class TomTatDuAn
    {
        public required Guid ProjectId { get; init; }

        public required string Status { get; init; }

        public required int RealSamples { get; init; }

        public required int GoldItems { get; init; }

        public required long BudgetVnd { get; init; }
    }
}
