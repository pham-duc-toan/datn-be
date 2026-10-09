using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Crowd.Gate.Infrastructure.Redis
{
    /// <summary>Bo cau hoi da phat cho mot khach. Dap an cau vang KHONG nam o day — chi danh sach id.</summary>
    public sealed class PhienVuot
    {
        public Guid SessionId { get; set; }

        public Guid LinkId { get; set; }

        public string Code { get; set; } = string.Empty;

        public Guid OwnerId { get; set; }

        public Guid? CampaignId { get; set; }

        /// <summary>null = khong co du an nao phuc vu duoc: chi dem nguoc.</summary>
        public Guid? ProjectId { get; set; }

        public List<Guid> GoldIds { get; set; } = new List<Guid>();

        public List<Guid> RealIds { get; set; } = new List<Guid>();

        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>Server kiem: nop truoc luc nay (het dem nguoc) bi tu choi.</summary>
        public DateTimeOffset AnswerableAt { get; set; }

        public string IpHash { get; set; } = string.Empty;

        public string IpHashDay { get; set; } = string.Empty;

        public string ReferrerHost { get; set; } = string.Empty;
    }

    /// <summary>
    /// Moi thu cua DUONG NONG cong link nam o Redis rieng (redis-gate, AOF bat):
    ///   gate:session:{id}           bo cau hoi da phat (TTL gate.session_ttl)
    ///   gate:jti:{jti}              token mo link chua dung (TTL gate.token_ttl) — VD-L-02
    ///   gate:ip:{link}:{ipNgay}     IP da tinh tien cho link trong cua so — dac ta "1 luot/IP/24h"
    ///   gate:sample:{id}:n          so nhan cong link da thu cho mau (gioi han gate.max_labels_per_sample)
    ///   gate:events                 Redis Stream: luot xem / nop — relay chuyen sang outbox + ClickHouse
    /// </summary>
    public sealed class RedisGateStore
    {
        public const string Stream = "gate:events";
        public const string NhomRelay = "relay";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly IConnectionMultiplexer _redis;

        public RedisGateStore(IConnectionMultiplexer redis)
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

        // ---------------- Phien ----------------

        public async Task LuuPhienAsync(PhienVuot p, TimeSpan ttl)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            await Db.StringSetAsync("gate:session:" + p.SessionId.ToString("N"), JsonSerializer.Serialize(p, Json), ttl);
        }

        public async Task<PhienVuot?> DocPhienAsync(Guid id)
        {
            RedisValue v = await Db.StringGetAsync("gate:session:" + id.ToString("N"));
            if (v.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<PhienVuot>(v.ToString(), Json);
        }

        /// <summary>Xoa phien — CHI MOT request thang (true). Hai request nop cung luc: request sau nhan false.</summary>
        public async Task<bool> XoaPhienAsync(Guid id)
        {
            return await Db.KeyDeleteAsync("gate:session:" + id.ToString("N"));
        }

        // ---------------- Token dung mot lan ----------------

        public async Task GhiJtiAsync(string jti, TimeSpan ttl)
        {
            await Db.StringSetAsync("gate:jti:" + jti, "1", ttl);
        }

        /// <summary>GETDEL nguyen tu: token chi doi duoc link MOT lan, ke ca hai request cung luc.</summary>
        public async Task<bool> DungJtiAsync(string jti)
        {
            RedisValue v = await Db.StringGetDeleteAsync("gate:jti:" + jti);
            return !v.IsNullOrEmpty;
        }

        // ---------------- Chong trung IP ----------------

        /// <summary>SET NX: true = IP NAY LAN DAU trong cua so (duoc tinh tien), false = da tinh roi.</summary>
        public async Task<bool> GiuLuotIpAsync(Guid linkId, string ipHashNgay, TimeSpan cuaSo)
        {
            if (cuaSo <= TimeSpan.Zero)
            {
                return true;
            }

            return await Db.StringSetAsync("gate:ip:" + linkId.ToString("N") + ":" + ipHashNgay, "1", cuaSo, When.NotExists);
        }

        // ---------------- Dem nhan moi mau ----------------

        public async Task<long[]> DemNhanMauAsync(IReadOnlyList<Guid> ids)
        {
            if (ids == null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            RedisKey[] khoa = new RedisKey[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                khoa[i] = "gate:sample:" + ids[i].ToString("N") + ":n";
            }

            RedisValue[] v = await Db.StringGetAsync(khoa);
            long[] kq = new long[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                long n;
                kq[i] = v[i].IsNullOrEmpty || !long.TryParse(v[i].ToString(), out n) ? 0 : n;
            }

            return kq;
        }

        public async Task TangNhanMauAsync(IEnumerable<Guid> ids)
        {
            if (ids == null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            foreach (Guid id in ids)
            {
                await Db.StringIncrementAsync("gate:sample:" + id.ToString("N") + ":n");
            }
        }

        // ---------------- Stream su kien ----------------

        public async Task GhiSuKienAsync(string json)
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
                // Nhom da co tu lan chay truoc — binh thuong.
            }
        }

        /// <summary>
        /// Doc mot lo cho relay. batDauTuDau = true: doc lai cac muc DA GIAO nhung chua ACK
        /// (relay chet giua chung lan truoc); false: muc moi.
        /// </summary>
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
