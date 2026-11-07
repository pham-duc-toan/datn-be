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
    /// ngay tai cho (luat muc 4 docs: quyet dinh tien bac hoi lai chu so huu) — vd
    /// ledger-svc hoi identity-svc trang thai khoa luc rut tien.
    /// </summary>
    public sealed class InternalApiOptions
    {
        public const string Section = "InternalApi";

        /// <summary>Khoa dung chung giua cac service. Rong = moi endpoint noi bo tu choi.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>identity-svc: project-svc tra cuu nguoi duoc moi, ledger kiem khoa luc rut, link kiem luc dang ky.</summary>
        public string IdentityUrl { get; set; } = string.Empty;

        /// <summary>
        /// project-svc gRPC (HTTP/2, cong 9102): gate keo mau + cau vang luc du an publish bat cong link; identity
        /// doc so cau duoc chap nhan / bang xep hang; ledger doc ten du an luc xuat hoa don.
        /// </summary>
        public string ProjectGrpcUrl { get; set; } = string.Empty;

        /// <summary>identity-svc gRPC (HTTP/2, cong 9101): project-svc tra cuu nguoi dung (moi / tham gia / mo bai).</summary>
        public string IdentityGrpcUrl { get; set; } = string.Empty;

        /// <summary>quality-svc gRPC (HTTP/2, cong 9201): project-svc nho gop nhan cho ket qua giao doanh nghiep.</summary>
        public string QualityGrpcUrl { get; set; } = string.Empty;

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
