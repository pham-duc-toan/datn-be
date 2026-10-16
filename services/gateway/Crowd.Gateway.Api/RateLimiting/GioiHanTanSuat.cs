using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Settings;
using Crowd.Gateway.Api.Middlewares;
using Crowd.Gateway.Api.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Crowd.Gateway.Api.RateLimiting
{
    /// <summary>
    /// Gioi han tan suat o gateway (VD-S-12), cua so co dinh 1 phut, ba tang chong nhau:
    ///   - chung: moi nguoi dung (sub trong token) hoac moi IP neu chua dang nhap;
    ///   - xac thuc: dang nhap / dang ky / quen / dat lai mat khau / xac thuc email / ma 2FA — theo IP
    ///     (chong do mat khau, do ma OTP, spam email). Ma 2FA con bi gioi han them o identity: moi
    ///     lan dang nhap chi duoc sai identity.two_factor_max_attempts lan;
    ///   - tien: tao lenh nap / rut — theo nguoi dung.
    /// Mot request co the bi tinh o hai tang (vd dang nhap: chung + xac thuc). Vuot → 429
    /// qua_nhieu_yeu_cau kem Retry-After.
    ///
    /// Han muc doc tu setting; gia tri nam trong KHOA dem nen admin doi la khoa moi dung muc moi
    /// ngay, khoa cu het han tu bo.
    ///
    /// Hai che do dem:
    ///   - co ConnectionStrings:GatewayRedis → dem CHUNG trong Redis (DemTanSuatRedisMiddleware): chay
    ///     N instance gateway thi han muc van dung, khong gap N;
    ///   - khong co (stack demo mot instance) → dem trong bo nho tung instance (RateLimiter cua ASP.NET).
    /// Redis loi giua chung thi middleware lui ve bo dem trong bo nho cua instance — van chan,
    /// chi long hon (khong mo toang endpoint dang nhap khi Redis sap).
    /// </summary>
    public static class GioiHanTanSuat
    {
        public static readonly TimeSpan CuaSo = TimeSpan.FromMinutes(1);

        public static IServiceCollection AddGioiHanTanSuat(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string? redis = configuration.GetConnectionString("GatewayRedis");
            if (!string.IsNullOrWhiteSpace(redis))
            {
                // abortConnect=false: Redis chua len luc gateway khoi dong thi van chay (lui ve bo nho), tu noi lai sau.
                ConfigurationOptions tuyChon = ConfigurationOptions.Parse(redis);
                tuyChon.AbortOnConnectFail = false;
                services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(tuyChon));
                services.AddSingleton<DemTanSuatRedis>();
            }

            // Bo dem trong bo nho: che do chinh khi khong co Redis, va duong lui khi Redis loi.
            services.AddSingleton<PartitionedRateLimiter<HttpContext>>(sp => TaoBoDemBoNho());

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = TuChoiAsync;
                options.GlobalLimiter = TaoBoDemBoNho();
            });
            return services;
        }

        /// <summary>Co Redis thi dem chung qua middleware rieng; khong thi dung RateLimiter trong bo nho.</summary>
        public static IApplicationBuilder UseGioiHanTanSuat(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            if (app.ApplicationServices.GetService<DemTanSuatRedis>() != null)
            {
                return app.UseMiddleware<DemTanSuatRedisMiddleware>();
            }

            return app.UseRateLimiter();
        }

        /// <summary>
        /// Cac tang ap cho request nay: (ten tang + chu the, han muc). Dung chung cho ca hai che do de
        /// luat chi viet mot cho.
        /// </summary>
        public static List<KeyValuePair<string, int>> CacTang(HttpContext ctx)
        {
            ISettings settings = ctx.RequestServices.GetRequiredService<ISettings>();
            List<KeyValuePair<string, int>> ds = new List<KeyValuePair<string, int>>();

            ds.Add(new KeyValuePair<string, int>("chung:" + NguoiHoacIp(ctx), settings.SoNguyen(SettingKeys.GatewayRateLimitPerMinute)));

            if (LaXacThuc(ctx.Request))
            {
                ds.Add(new KeyValuePair<string, int>("auth:" + Ip(ctx), settings.SoNguyen(SettingKeys.GatewayRateLimitAuthPerMinute)));
            }

            if (LaTaoLenhTien(ctx.Request))
            {
                ds.Add(new KeyValuePair<string, int>("tien:" + NguoiHoacIp(ctx), settings.SoNguyen(SettingKeys.GatewayRateLimitMoneyPerMinute)));
            }

            return ds;
        }

        public static PartitionedRateLimiter<HttpContext> TaoBoDemBoNho()
        {
            return PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => TangThu(ctx, 0)),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => TangThu(ctx, 1)),
                PartitionedRateLimiter.Create<HttpContext, string>(ctx => TangThu(ctx, 2)));
        }

        private static RateLimitPartition<string> TangThu(HttpContext ctx, int thuTu)
        {
            List<KeyValuePair<string, int>> tang = CacTang(ctx);
            if (thuTu >= tang.Count)
            {
                return RateLimitPartition.GetNoLimiter("khong");
            }

            string khoa = tang[thuTu].Key + ":" + tang[thuTu].Value;
            int hanMuc = tang[thuTu].Value;
            return RateLimitPartition.GetFixedWindowLimiter(khoa, k => new FixedWindowRateLimiterOptions
            {
                PermitLimit = hanMuc,
                Window = CuaSo,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        }

        public static bool LaXacThuc(HttpRequest request)
        {
            if (!HttpMethods.IsPost(request.Method))
            {
                return false;
            }

            string duong = request.Path.Value ?? string.Empty;
            return duong.Equals("/auth/login", StringComparison.OrdinalIgnoreCase)
                || duong.Equals("/auth/register", StringComparison.OrdinalIgnoreCase)
                || duong.Equals("/auth/forgot-password", StringComparison.OrdinalIgnoreCase)
                || duong.Equals("/auth/reset-password", StringComparison.OrdinalIgnoreCase)
                || duong.Equals("/auth/verify-email", StringComparison.OrdinalIgnoreCase)
                || duong.Equals("/auth/2fa/verify", StringComparison.OrdinalIgnoreCase);
        }

        public static bool LaTaoLenhTien(HttpRequest request)
        {
            if (!HttpMethods.IsPost(request.Method))
            {
                return false;
            }

            string duong = request.Path.Value ?? string.Empty;
            return duong.Equals("/payments/deposits", StringComparison.OrdinalIgnoreCase)
                || duong.Equals("/payments/deposits/manual", StringComparison.OrdinalIgnoreCase)
                || duong.Equals("/ledger/withdrawals", StringComparison.OrdinalIgnoreCase);
        }

        private static string NguoiHoacIp(HttpContext ctx)
        {
            TokenChuaKiem? token = TokenChuaKiem.Doc(ctx.Request);
            return token == null ? "ip:" + Ip(ctx) : "u:" + token.UserId;
        }

        private static string Ip(HttpContext ctx)
        {
            return ctx.Connection.RemoteIpAddress == null ? "khong-ro" : ctx.Connection.RemoteIpAddress.ToString();
        }

        public static async Task GhiTuChoiAsync(HttpContext ctx, TimeSpan? sau, CancellationToken ct)
        {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            if (sau.HasValue)
            {
                ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(sau.Value.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsync(
                LoiJson.Tao(StatusCodes.Status429TooManyRequests, "qua_nhieu_yeu_cau", "Gui qua nhieu yeu cau — thu lai sau it giay."), ct);
        }

        private static async ValueTask TuChoiAsync(OnRejectedContext ctx, CancellationToken ct)
        {
            TimeSpan sau;
            TimeSpan? retry = null;
            if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out sau))
            {
                retry = sau;
            }

            await GhiTuChoiAsync(ctx.HttpContext, retry, ct);
        }
    }
}
