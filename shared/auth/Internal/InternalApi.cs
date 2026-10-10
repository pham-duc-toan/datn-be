using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Crowd.BuildingBlocks.Auth.Internal
{
    /// <summary>
    /// Cau hinh goi HTTP NOI BO giua cac service (muc "InternalApi" trong appsettings).
    ///
    /// Chi dung cho thao tac HIEM, khong nam tren duong nong, ma can hoi CHU du lieu
    /// ngay tai cho (luat muc 4 docs: quyet dinh tien bac hoi lai chu so huu) — vd dong
    /// du an: project-svc hoi task-svc va annotation-svc truoc khi tra ky quy.
    /// </summary>
    public sealed class InternalApiOptions
    {
        public const string Section = "InternalApi";

        /// <summary>Khoa dung chung giua cac service. Rong = moi endpoint noi bo tu choi.</summary>
        public string Key { get; set; } = string.Empty;

        public string TaskUrl { get; set; } = string.Empty;

        public string AnnotationUrl { get; set; } = string.Empty;

        /// <summary>Het gio moi loi goi noi bo.</summary>
        public int TimeoutSeconds { get; set; } = 10;
    }

    public static class InternalApiHeaders
    {
        public const string Key = "X-Internal-Key";
    }

    public static class InternalApiServiceCollectionExtensions
    {
        public static IServiceCollection AddCrowdInternalApi(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            services.Configure<InternalApiOptions>(configuration.GetSection(InternalApiOptions.Section));
            return services;
        }
    }

    /// <summary>
    /// Chi cho goi khi header X-Internal-Key khop khoa noi bo. Endpoint noi bo nam duoi
    /// /internal — gateway KHONG dinh tuyen tien to nay, nen tu ngoai chi toi duoc khi
    /// cham thang cong service; khoa chan ca truong hop do. So khop thoi gian co dinh.
    /// Sai / thieu khoa → 404 (khong tiet lo endpoint ton tai).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class InternalApiAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            InternalApiOptions options = context.HttpContext.RequestServices.GetRequiredService<IOptions<InternalApiOptions>>().Value;
            string? guiLen = context.HttpContext.Request.Headers[InternalApiHeaders.Key];

            if (!KhoaKhop(options.Key, guiLen))
            {
                context.Result = new NotFoundResult();
            }
        }

        public static bool KhoaKhop(string? khoaDung, string? guiLen)
        {
            if (string.IsNullOrEmpty(khoaDung) || string.IsNullOrEmpty(guiLen))
            {
                return false;
            }

            byte[] a = Encoding.UTF8.GetBytes(khoaDung);
            byte[] b = Encoding.UTF8.GetBytes(guiLen);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
