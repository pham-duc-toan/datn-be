using System;
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
using Microsoft.Extensions.DependencyInjection;

namespace Crowd.Gateway.Api.RateLimiting
{
    /// <summary>
    /// Gioi han tan suat o gateway (VD-S-12), cua so co dinh 1 phut, ba tang chong nhau:
    ///   - chung: moi nguoi dung (sub trong token) hoac moi IP neu chua dang nhap;
    ///   - xac thuc: dang nhap / dang ky / quen / dat lai mat khau / xac thuc email — theo IP
    ///     (chong do mat khau, spam email);
    ///   - tien: tao lenh nap / rut — theo nguoi dung.
    /// Mot request co the bi tinh o hai tang (vd dang nhap: chung + xac thuc). Vuot → 429
    /// qua_nhieu_yeu_cau kem Retry-After.
    ///
    /// Han muc doc tu setting; gia tri nam trong KHOA ngan (partition) nen admin doi la ngan moi
    /// dung muc moi ngay, ngan cu het han tu bo.
    ///
    /// Gioi han NAM TRONG BO NHO tung instance gateway: chay N instance thi han muc thuc te gap N.
    /// Chap nhan o quy mo do an; production can kho chung (Redis).
    /// </summary>
    public static class GioiHanTanSuat
    {
        private static readonly TimeSpan CuaSo = TimeSpan.FromMinutes(1);

        public static IServiceCollection AddGioiHanTanSuat(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = TuChoiAsync;
                options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    PartitionedRateLimiter.Create<HttpContext, string>(NganChung),
                    PartitionedRateLimiter.Create<HttpContext, string>(NganXacThuc),
                    PartitionedRateLimiter.Create<HttpContext, string>(NganTien));
            });
            return services;
        }

        private static RateLimitPartition<string> NganChung(HttpContext ctx)
        {
            int hanMuc = ctx.RequestServices.GetRequiredService<ISettings>().SoNguyen(SettingKeys.GatewayRateLimitPerMinute);
            return CuaSoCoDinh("chung:" + NguoiHoacIp(ctx) + ":" + hanMuc, hanMuc);
        }

        private static RateLimitPartition<string> NganXacThuc(HttpContext ctx)
        {
            if (!LaXacThuc(ctx.Request))
            {
                return RateLimitPartition.GetNoLimiter("khong");
            }

            int hanMuc = ctx.RequestServices.GetRequiredService<ISettings>().SoNguyen(SettingKeys.GatewayRateLimitAuthPerMinute);
            return CuaSoCoDinh("auth:" + Ip(ctx) + ":" + hanMuc, hanMuc);
        }

        private static RateLimitPartition<string> NganTien(HttpContext ctx)
        {
            if (!LaTaoLenhTien(ctx.Request))
            {
                return RateLimitPartition.GetNoLimiter("khong");
            }

            int hanMuc = ctx.RequestServices.GetRequiredService<ISettings>().SoNguyen(SettingKeys.GatewayRateLimitMoneyPerMinute);
            return CuaSoCoDinh("tien:" + NguoiHoacIp(ctx) + ":" + hanMuc, hanMuc);
        }

        private static RateLimitPartition<string> CuaSoCoDinh(string khoa, int hanMuc)
        {
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
                || duong.Equals("/auth/verify-email", StringComparison.OrdinalIgnoreCase);
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

        private static async ValueTask TuChoiAsync(OnRejectedContext ctx, CancellationToken ct)
        {
            TimeSpan sau;
            if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out sau))
            {
                ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(sau.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            ctx.HttpContext.Response.ContentType = "application/problem+json";
            await ctx.HttpContext.Response.WriteAsync(
                LoiJson.Tao(StatusCodes.Status429TooManyRequests, "qua_nhieu_yeu_cau", "Gui qua nhieu yeu cau — thu lai sau it giay."), ct);
        }
    }
}
