using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Crowd.Reco.Domain;
using StackExchange.Redis;

namespace Crowd.Reco.Infrastructure.Redis
{
    /// <summary>Tap loai tru / duoc thay cua MOT labeler, doc tu Redis ex:{userId}.</summary>
    public sealed class TapLoaiTru
    {
        public required HashSet<Guid> LoaiTru { get; init; }

        public required HashSet<Guid> ThanhVien { get; init; }

        public required HashSet<Guid> DoanhNghiepChan { get; init; }

        /// <summary>Du an da thich (l:{pid}, 3.40) — KHONG loai tru, chi de the hien tim do.</summary>
        public HashSet<Guid> DaThich { get; init; } = new HashSet<Guid>();
    }

    /// <summary>
    /// Redis RIENG cua reco-svc (redis-reco, allkeys-lru — moi khoa deu dung lai duoc tu nguon goc):
    ///   aff:{userId}            Hash  "loai|ma" → "a,b,unixGiay"      ban doc nhanh cua labeler_affinity
    ///   ex:{userId}             Hash  s:{pid} da nop · b:{pid} bi chan · h:{pid} khong quan tam · m:{pid} thanh vien
    ///                                 · o:{ownerId} doanh nghiep chan · l:{pid} da thich · "_" da nap (dung lai tu view + reco_db)
    ///   skip:{userId}           Hash  {pid} → "soLan:unixGiay" du an da LUOT QUA (3.40), het han sau vai ngay
    ///   evd:{eventId}           String hanh vi frontend da nhan (bo trung khi frontend gui lai lo cu), song 2 ngay
    ///   feed:{userId}:{phien}   String nhi phan — danh sach 200 the da xep, KHONG SUA
    ///   served:{userId}:{phien} Set   id da tra trong phien
    ///   seen:{projectId}        HyperLogLog — so labeler da that su thay the
    ///   reco:events             Stream — luot hien / mo / hanh vi → relay ghi ClickHouse + cong bo dem theo lo
    /// </summary>
    public sealed class RecoRedis
    {
        public const string Stream = "reco:events";
        public const string NhomRelay = "relay";
        private const string DaNap = "_";

        // Chi sua tap loai tru neu da nap (chua nap thi lan doc sau nap du tu nguon goc — khong tao tap thieu).
        private const string LuaDatNeuCo = "if redis.call('EXISTS', KEYS[1]) == 1 then redis.call('HSET', KEYS[1], ARGV[1], '1') end return 1";
        private const string LuaXoaNeuCo = "if redis.call('EXISTS', KEYS[1]) == 1 then redis.call('HDEL', KEYS[1], ARGV[1]) end return 1";

        private readonly IConnectionMultiplexer _redis;

        public RecoRedis(IConnectionMultiplexer redis)
        {
            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _redis = redis;
        }

        private IDatabase Db
        {
            get { return _redis.GetDatabase(); }
        }

        // ---------------------------------------------------------------- so thich

        public async Task<SoThich?> DocSoThichAsync(Guid userId)
        {
            HashEntry[] e = await Db.HashGetAllAsync("aff:" + userId.ToString("N"));
            if (e.Length == 0)
            {
                return null;
            }

            SoThich st = new SoThich();
            foreach (HashEntry h in e)
            {
                string k = h.Name.ToString();
                if (k == DaNap)
                {
                    continue;
                }

                int tach = k.IndexOf('|', StringComparison.Ordinal);
                BoDem? d = BoDem.GiaiMa(h.Value.ToString());
                if (tach <= 0 || d == null)
                {
                    continue;
                }

                st.Dat(k.Substring(0, tach), k.Substring(tach + 1), d);
            }

            return st;
        }

        public async Task GhiSoThichAsync(Guid userId, SoThich st, TimeSpan han)
        {
            if (st == null)
            {
                throw new ArgumentNullException(nameof(st));
            }

            string khoa = "aff:" + userId.ToString("N");
            List<HashEntry> ds = new List<HashEntry>();
            ds.Add(new HashEntry(DaNap, "1"));
            foreach (KeyValuePair<string, BoDem> cap in st.TatCa)
            {
                ds.Add(new HashEntry(cap.Key, cap.Value.MaHoa()));
            }

            ITransaction tx = Db.CreateTransaction();
            Task xoa = tx.KeyDeleteAsync(khoa);
            Task ghi = tx.HashSetAsync(khoa, ds.ToArray());
            Task hetHan = tx.KeyExpireAsync(khoa, han);
            await tx.ExecuteAsync();
            await Task.WhenAll(xoa, ghi, hetHan);
        }

        /// <summary>Bo cache so thich cua cac labeler vua doi bo dem — lan xep feed sau nap lai tu Postgres.</summary>
        public async Task XoaSoThichAsync(IEnumerable<Guid> userIds)
        {
            RedisKey[] khoa = userIds.Distinct().Select(u => (RedisKey)("aff:" + u.ToString("N"))).ToArray();
            if (khoa.Length > 0)
            {
                await Db.KeyDeleteAsync(khoa);
            }
        }

        // ---------------------------------------------------------------- tap loai tru

        public async Task<TapLoaiTru?> DocLoaiTruAsync(Guid userId)
        {
            HashEntry[] e = await Db.HashGetAllAsync("ex:" + userId.ToString("N"));
            if (e.Length == 0)
            {
                return null;
            }

            TapLoaiTru t = new TapLoaiTru
            {
                LoaiTru = new HashSet<Guid>(),
                ThanhVien = new HashSet<Guid>(),
                DoanhNghiepChan = new HashSet<Guid>(),
            };
            foreach (HashEntry h in e)
            {
                string k = h.Name.ToString();
                Guid id;
                if (k.Length < 3 || k[1] != ':' || !Guid.TryParse(k.AsSpan(2), out id))
                {
                    continue;
                }

                char loai = k[0];
                if (loai == 's' || loai == 'b' || loai == 'h')
                {
                    t.LoaiTru.Add(id);
                }
                else if (loai == 'm')
                {
                    t.ThanhVien.Add(id);
                }
                else if (loai == 'o')
                {
                    t.DoanhNghiepChan.Add(id);
                }
                else if (loai == 'l')
                {
                    t.DaThich.Add(id);
                }
            }

            return t;
        }

        public async Task GhiLoaiTruAsync(Guid userId, IEnumerable<string> truong, TimeSpan han)
        {
            string khoa = "ex:" + userId.ToString("N");
            List<HashEntry> ds = new List<HashEntry>();
            ds.Add(new HashEntry(DaNap, "1"));
            foreach (string f in truong)
            {
                ds.Add(new HashEntry(f, "1"));
            }

            ITransaction tx = Db.CreateTransaction();
            Task xoa = tx.KeyDeleteAsync(khoa);
            Task ghi = tx.HashSetAsync(khoa, ds.ToArray());
            Task hetHan = tx.KeyExpireAsync(khoa, han);
            await tx.ExecuteAsync();
            await Task.WhenAll(xoa, ghi, hetHan);
        }

        /// <summary>Dat mot muc tap loai tru (vd "s:{pid}") — chi khi tap da nap. Idempotent (event giao lai khong sao).</summary>
        public async Task DatLoaiTruAsync(Guid userId, string truong)
        {
            await Db.ScriptEvaluateAsync(LuaDatNeuCo, new RedisKey[] { "ex:" + userId.ToString("N") }, new RedisValue[] { truong });
        }

        public async Task XoaLoaiTruAsync(Guid userId, string truong)
        {
            await Db.ScriptEvaluateAsync(LuaXoaNeuCo, new RedisKey[] { "ex:" + userId.ToString("N") }, new RedisValue[] { truong });
        }

        public static string TruongDaNop(Guid projectId)
        {
            return "s:" + projectId.ToString("N");
        }

        public static string TruongBiChan(Guid projectId)
        {
            return "b:" + projectId.ToString("N");
        }

        public static string TruongKhongQuanTam(Guid projectId)
        {
            return "h:" + projectId.ToString("N");
        }

        public static string TruongThanhVien(Guid projectId)
        {
            return "m:" + projectId.ToString("N");
        }

        public static string TruongDoanhNghiepChan(Guid ownerId)
        {
            return "o:" + ownerId.ToString("N");
        }

        public static string TruongDaThich(Guid projectId)
        {
            return "l:" + projectId.ToString("N");
        }

        // ---------------------------------------------------------------- du an da luot qua (3.40)

        // Tang so lan + ghi lan cuoi; gia han ca khoa (mot nguoi mot khoa).
        private const string LuaLuotQua =
            "local v = redis.call('HGET', KEYS[1], ARGV[1]) local n = 0 "
            + "if v then n = tonumber(string.match(v, '^(%d+)')) or 0 end "
            + "redis.call('HSET', KEYS[1], ARGV[1], (n + 1) .. ':' .. ARGV[2]) redis.call('EXPIRE', KEYS[1], ARGV[3]) return n + 1";

        private static string KhoaLuotQua(Guid userId)
        {
            return "skip:" + userId.ToString("N");
        }

        public async Task<Dictionary<Guid, LuotQua>> DocLuotQuaAsync(Guid userId)
        {
            Dictionary<Guid, LuotQua> kq = new Dictionary<Guid, LuotQua>();
            foreach (HashEntry h in await Db.HashGetAllAsync(KhoaLuotQua(userId)))
            {
                Guid id;
                string[] phan = h.Value.ToString().Split(':');
                long giay;
                int n;
                if (Guid.TryParse(h.Name.ToString(), out id) && phan.Length == 2
                    && int.TryParse(phan[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n)
                    && long.TryParse(phan[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out giay))
                {
                    kq[id] = new LuotQua { SoLan = n, LanCuoi = DateTimeOffset.FromUnixTimeSeconds(giay) };
                }
            }

            return kq;
        }

        public async Task GhiLuotQuaAsync(IReadOnlyCollection<KeyValuePair<Guid, Guid>> nguoiVaDuAn, DateTimeOffset luc, TimeSpan han)
        {
            List<Task> viec = new List<Task>();
            foreach (KeyValuePair<Guid, Guid> x in nguoiVaDuAn)
            {
                viec.Add(Db.ScriptEvaluateAsync(LuaLuotQua, new RedisKey[] { KhoaLuotQua(x.Key) }, new RedisValue[]
                {
                    x.Value.ToString("N"), luc.ToUnixTimeSeconds(), (long)Math.Ceiling(han.TotalSeconds),
                }));
            }

            await Task.WhenAll(viec);
        }

        public async Task XoaLuotQuaAsync(IReadOnlyCollection<KeyValuePair<Guid, Guid>> nguoiVaDuAn)
        {
            List<Task> viec = new List<Task>();
            foreach (KeyValuePair<Guid, Guid> x in nguoiVaDuAn)
            {
                viec.Add(Db.HashDeleteAsync(KhoaLuotQua(x.Key), x.Value.ToString("N")));
            }

            await Task.WhenAll(viec);
        }

        /// <summary>Danh dau hanh vi frontend da nhan. false = da nhan truoc do (frontend gui lai lo cu) → bo qua.</summary>
        public async Task<bool> NhanLanDauAsync(Guid eventId, TimeSpan han)
        {
            return await Db.StringSetAsync("evd:" + eventId.ToString("N"), "1", han, When.NotExists);
        }

        // ---------------------------------------------------------------- phien luot

        public async Task<byte[]?> DocDanhSachAsync(Guid userId, Guid phien)
        {
            RedisValue v = await Db.StringGetAsync(KhoaDanhSach(userId, phien));
            return v.IsNullOrEmpty ? null : (byte[]?)v;
        }

        public async Task GhiDanhSachAsync(Guid userId, Guid phien, byte[] duLieu, TimeSpan han)
        {
            await Db.StringSetAsync(KhoaDanhSach(userId, phien), duLieu, han);
        }

        public async Task<HashSet<Guid>> DocDaTraAsync(Guid userId, Guid phien)
        {
            RedisValue[] v = await Db.SetMembersAsync(KhoaDaTra(userId, phien));
            HashSet<Guid> kq = new HashSet<Guid>();
            foreach (RedisValue x in v)
            {
                Guid id;
                if (Guid.TryParse(x.ToString(), out id))
                {
                    kq.Add(id);
                }
            }

            return kq;
        }

        public async Task ThemDaTraAsync(Guid userId, Guid phien, IEnumerable<Guid> ids, TimeSpan han)
        {
            RedisValue[] v = ids.Select(x => (RedisValue)x.ToString("N")).ToArray();
            if (v.Length == 0)
            {
                return;
            }

            string khoa = KhoaDaTra(userId, phien);
            ITransaction tx = Db.CreateTransaction();
            Task them = tx.SetAddAsync(khoa, v);
            Task hetHan = tx.KeyExpireAsync(khoa, han);
            await tx.ExecuteAsync();
            await Task.WhenAll(them, hetHan);
        }

        private static string KhoaDanhSach(Guid userId, Guid phien)
        {
            return "feed:" + userId.ToString("N") + ":" + phien.ToString("N");
        }

        private static string KhoaDaTra(Guid userId, Guid phien)
        {
            return "served:" + userId.ToString("N") + ":" + phien.ToString("N");
        }

        // ---------------------------------------------------------------- be du an moi

        public async Task GhiDaThayAsync(Guid projectId, Guid userId)
        {
            await Db.HyperLogLogAddAsync("seen:" + projectId.ToString("N"), userId.ToString("N"));
        }

        /// <summary>So nguoi da thay cua nhieu du an (pipeline — mot vong mang cho ca lo).</summary>
        public async Task<Dictionary<Guid, long>> DemDaThayAsync(IReadOnlyCollection<Guid> projectIds)
        {
            IDatabase db = Db;
            IBatch lo = db.CreateBatch();
            List<KeyValuePair<Guid, Task<long>>> viec = new List<KeyValuePair<Guid, Task<long>>>();
            foreach (Guid id in projectIds)
            {
                viec.Add(new KeyValuePair<Guid, Task<long>>(id, lo.HyperLogLogLengthAsync("seen:" + id.ToString("N"))));
            }

            lo.Execute();
            await Task.WhenAll(viec.Select(x => x.Value));
            Dictionary<Guid, long> kq = new Dictionary<Guid, long>();
            foreach (KeyValuePair<Guid, Task<long>> x in viec)
            {
                kq[x.Key] = x.Value.Result;
            }

            return kq;
        }

        // ---------------------------------------------------------------- stream hanh vi

        public async Task GhiHanhViAsync(string json)
        {
            await Db.StreamAddAsync(Stream, "e", json);
        }

        public async Task TaoNhomRelayAsync()
        {
            try
            {
                await Db.StreamCreateConsumerGroupAsync(Stream, NhomRelay, "0-0", true);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
                // Nhom da co tu lan chay truoc.
            }
        }

        public async Task<StreamEntry[]> DocLoAsync(string tenConsumer, int soLuong, bool batDauTuDau)
        {
            return await Db.StreamReadGroupAsync(Stream, NhomRelay, tenConsumer, batDauTuDau ? "0-0" : ">", soLuong);
        }

        public async Task XacNhanAsync(RedisValue[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return;
            }

            await Db.StreamAcknowledgeAsync(Stream, NhomRelay, ids);
            await Db.StreamDeleteAsync(Stream, ids);
        }
    }
}
