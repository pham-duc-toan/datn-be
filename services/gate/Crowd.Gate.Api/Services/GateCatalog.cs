using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Gate.Domain;
using Crowd.Gate.Infrastructure.Persistence;
using Crowd.Labeling;
using Microsoft.EntityFrameworkCore;
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

        /// <summary>Ngan sach cong link luc nap (ban sao tu ledger).</summary>
        public long NganSachVnd { get; }

        public LabelSchema TapNhan { get; }

        public JsonElement TapNhanJson { get; }

        public List<Guid> CauThat { get; }

        public List<Guid> CauVang { get; }

        /// <summary>Dap an cau vang — chi dung de cham, KHONG BAO GIO tra ra client.</summary>
        public Dictionary<Guid, LabelPayload> DapAnVang { get; }
    }

    /// <summary>
    /// Bo nho dem cua DUONG NONG: du an phuc vu duoc, mau, dap an vang, link dang chay.
    /// Nap tu ban sao trong gate_db moi gate.cache_refresh_interval, va ngay khi consumer
    /// vua ghi thay doi (YeuCauNapLai). Request /g, /go chi doc o day — khong cham Postgres.
    ///
    /// Anh chup BAT BIEN, doi ca khoi (Volatile): request dang doc khong bao gio thay nua cu
    /// nua moi. Rieng ngan sach co them phan "da tieu tai cho" giua hai lan nap (ledger moi la
    /// nguon su that) de het tien thi ngung phuc vu som, khong doi toi lan nap sau.
    /// </summary>
    public sealed class GateCatalog : IDisposable
    {
        private readonly ILogger<GateCatalog> _logger;
        private readonly SemaphoreSlim _tinHieu = new SemaphoreSlim(0);
        private readonly ConcurrentDictionary<Guid, long> _daTieuTaiCho = new ConcurrentDictionary<Guid, long>();
        private AnhChup _hienTai = AnhChup.Rong;

        public GateCatalog(ILogger<GateCatalog> logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _logger = logger;
        }

        public bool DaNap { get; private set; }

        public void Dispose()
        {
            _tinHieu.Dispose();
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

        /// <summary>Ngan sach con lai uoc tinh = ban sao tu ledger − phan da tieu tai cho tu lan nap truoc.</summary>
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

        /// <summary>Consumer vua ghi thay doi: nap lai som (worker gop nhieu yeu cau lien tiep).</summary>
        public void YeuCauNapLai()
        {
            _tinHieu.Release();
        }

        /// <summary>Cho toi khi co yeu cau nap lai hoac het thoiGian.</summary>
        public async Task ChoAsync(TimeSpan thoiGian, CancellationToken ct)
        {
            await _tinHieu.WaitAsync(thoiGian, ct);
            while (_tinHieu.CurrentCount > 0)
            {
                _tinHieu.Wait(0, ct);
            }
        }

        public async Task NapLaiAsync(GateDbContext db, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            List<DuAnCong> duAn = await db.Projects.AsNoTracking()
                .Where(p => p.Status == TrangThaiDuAnCong.Running)
                .ToListAsync(ct);
            List<Guid> ids = duAn.Select(p => p.ProjectId).ToList();

            List<MauCong> mau = await db.Samples.AsNoTracking().Where(s => ids.Contains(s.ProjectId)).ToListAsync(ct);
            List<CauVangCong> vang = await db.GoldItems.AsNoTracking().Where(g => ids.Contains(g.ProjectId)).ToListAsync(ct);
            List<LinkCong> link = await db.Links.AsNoTracking().Where(l => l.Active).ToListAsync(ct);
            Dictionary<Guid, long> nganSach = await db.Budgets.AsNoTracking()
                .Where(b => ids.Contains(b.ProjectId))
                .ToDictionaryAsync(b => b.ProjectId, b => b.RemainingVnd, ct);

            Dictionary<Guid, DuAnPhucVu> phucVu = new Dictionary<Guid, DuAnPhucVu>();
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

                Dictionary<Guid, LabelPayload> dapAn = new Dictionary<Guid, LabelPayload>();
                foreach (CauVangCong g in vang.Where(x => x.ProjectId == d.ProjectId))
                {
                    dapAn[g.SampleId] = g.DapAn();
                }

                List<Guid> cauThat = mau
                    .Where(m => m.ProjectId == d.ProjectId && !dapAn.ContainsKey(m.SampleId))
                    .Select(m => m.SampleId)
                    .ToList();

                long conLai;
                nganSach.TryGetValue(d.ProjectId, out conLai);
                phucVu[d.ProjectId] = new DuAnPhucVu(d, conLai, tapNhan, cauThat, dapAn);
            }

            Dictionary<Guid, MauCong> mauTheoId = mau.ToDictionary(m => m.SampleId);
            Dictionary<string, LinkCong> linkTheoMa = new Dictionary<string, LinkCong>(StringComparer.Ordinal);
            foreach (LinkCong l in link)
            {
                linkTheoMa[l.Code] = l;
            }

            Volatile.Write(ref _hienTai, new AnhChup(phucVu, mauTheoId, linkTheoMa));
            _daTieuTaiCho.Clear();
            DaNap = true;
        }

        private sealed class AnhChup
        {
            public static readonly AnhChup Rong = new AnhChup(
                new Dictionary<Guid, DuAnPhucVu>(), new Dictionary<Guid, MauCong>(), new Dictionary<string, LinkCong>(StringComparer.Ordinal));

            public AnhChup(Dictionary<Guid, DuAnPhucVu> duAn, Dictionary<Guid, MauCong> mau, Dictionary<string, LinkCong> linkTheoMa)
            {
                DuAn = duAn;
                Mau = mau;
                LinkTheoMa = linkTheoMa;
            }

            public Dictionary<Guid, DuAnPhucVu> DuAn { get; }

            public Dictionary<Guid, MauCong> Mau { get; }

            public Dictionary<string, LinkCong> LinkTheoMa { get; }
        }
    }
}
