using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Crowd.Gateway.Api.RateLimiting
{
    /// <summary>
    /// Bo dem cua so co dinh DUNG CHUNG giua moi instance gateway (VD-S-12). Moi khoa
    /// "rl:{tang}:{chu the}:{han muc}:{so cua so}" la mot so nguyen trong Redis:
    ///   INCR → lan dau thi dat het han = do dai cua so → tra (so lan, ms con lai).
    /// Ca ba lenh chay trong MOT script Lua nen nguyen tu: hai gateway tang cung luc khong lot so.
    /// </summary>
    public sealed class DemTanSuatRedis
    {
        private const string Script = @"
local n = redis.call('INCR', KEYS[1])
if n == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
local ttl = redis.call('PTTL', KEYS[1])
return {n, ttl}";

        private readonly IConnectionMultiplexer _redis;

        public DemTanSuatRedis(IConnectionMultiplexer redis)
        {
            if (redis == null)
            {
                throw new ArgumentNullException(nameof(redis));
            }

            _redis = redis;
        }

        /// <summary>Tang dem cua khoa trong cua so hien tai; tra (so lan ke ca lan nay, thoi gian toi het cua so).</summary>
        public async Task<KeyValuePair<long, TimeSpan>> DemAsync(string khoa, DateTimeOffset bayGio)
        {
            long soCuaSo = bayGio.ToUnixTimeMilliseconds() / (long)GioiHanTanSuat.CuaSo.TotalMilliseconds;
            RedisKey k = "rl:" + khoa + ":" + soCuaSo;
            RedisResult kq = await _redis.GetDatabase().ScriptEvaluateAsync(
                Script, new RedisKey[] { k }, new RedisValue[] { (long)GioiHanTanSuat.CuaSo.TotalMilliseconds });

            RedisResult[] mang = (RedisResult[])kq!;
            long n = (long)mang[0];
            long ttl = (long)mang[1];
            return new KeyValuePair<long, TimeSpan>(n, TimeSpan.FromMilliseconds(ttl < 0 ? 0 : ttl));
        }
    }

    /// <summary>
    /// Ap cac tang (GioiHanTanSuat.CacTang) bang bo dem Redis. Redis loi → dung bo dem trong bo nho
    /// cua instance cho request nay (log canh bao), khong de request qua mat kiem soat.
    /// </summary>
    public sealed class DemTanSuatRedisMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly DemTanSuatRedis _dem;
        private readonly PartitionedRateLimiter<HttpContext> _boNho;
        private readonly TimeProvider _clock;
        private readonly ILogger<DemTanSuatRedisMiddleware> _logger;

        public DemTanSuatRedisMiddleware(
            RequestDelegate next,
            DemTanSuatRedis dem,
            PartitionedRateLimiter<HttpContext> boNho,
            TimeProvider clock,
            ILogger<DemTanSuatRedisMiddleware> logger)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (dem == null)
            {
                throw new ArgumentNullException(nameof(dem));
            }

            if (boNho == null)
            {
                throw new ArgumentNullException(nameof(boNho));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _next = next;
            _dem = dem;
            _boNho = boNho;
            _clock = clock;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (ctx == null)
            {
                throw new ArgumentNullException(nameof(ctx));
            }

            if (HttpMethods.IsOptions(ctx.Request.Method))
            {
                await _next(ctx);
                return;
            }

            try
            {
                DateTimeOffset bayGio = _clock.GetUtcNow();
                foreach (KeyValuePair<string, int> tang in GioiHanTanSuat.CacTang(ctx))
                {
                    KeyValuePair<long, TimeSpan> kq = await _dem.DemAsync(tang.Key + ":" + tang.Value, bayGio);
                    if (kq.Key > tang.Value)
                    {
                        await GioiHanTanSuat.GhiTuChoiAsync(ctx, kq.Value, ctx.RequestAborted);
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is RedisException || ex is TimeoutException)
            {
                _logger.LogWarning(ex, "Redis gioi han tan suat loi — dem tam trong bo nho instance nay");
                using (RateLimitLease lease = _boNho.AttemptAcquire(ctx))
                {
                    if (!lease.IsAcquired)
                    {
                        TimeSpan sau;
                        TimeSpan? retry = null;
                        if (lease.TryGetMetadata(MetadataName.RetryAfter, out sau))
                        {
                            retry = sau;
                        }

                        await GioiHanTanSuat.GhiTuChoiAsync(ctx, retry, ctx.RequestAborted);
                        return;
                    }
                }
            }

            await _next(ctx);
        }
    }
}
