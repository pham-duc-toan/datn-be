using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.Contracts.Grpc.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Crowd.Reco.Api.Helpers
{
    /// <summary>Muc "Reco" — ha tang cua reco-svc. Tham so nghiep vu la setting reco.* (admin sua).</summary>
    public sealed class RecoOptions
    {
        public const string SectionName = "Reco";

        /// <summary>Redis RIENG cua reco-svc (vd "localhost:6383").</summary>
        public string Redis { get; set; } = string.Empty;

        /// <summary>ClickHouse database reco (user reco_user).</summary>
        public string ClickHouse { get; set; } = string.Empty;

        /// <summary>Tien to khoa file mo hinh trong bucket ObjectStorage (ml-svc ghi, reco doc): {prefix}manifest.json.</summary>
        public string ModelPrefix { get; set; } = "reco/";
    }

    /// <summary>Loi nghiep vu cua reco-svc → ProblemDetails { code }.</summary>
    public sealed class RecoException : Exception
    {
        public RecoException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }

        public string Code { get; }
    }

    public sealed class ApiExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetails;

        public ApiExceptionHandler(IProblemDetailsService problemDetails)
        {
            if (problemDetails == null)
            {
                throw new ArgumentNullException(nameof(problemDetails));
            }

            _problemDetails = problemDetails;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            RecoException? r = exception as RecoException;
            if (r == null)
            {
                return false;
            }

            httpContext.Response.StatusCode = r.Status;
            ProblemDetails chiTiet = new ProblemDetails();
            chiTiet.Status = r.Status;
            chiTiet.Title = r.Code;
            chiTiet.Detail = r.Message;
            chiTiet.Extensions["code"] = r.Code;
            ProblemDetailsContext ctx = new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = chiTiet,
                Exception = exception,
            };
            return await _problemDetails.TryWriteAsync(ctx);
        }
    }

    /// <summary>
    /// Nhom A/B co dinh theo nguoi: bam(userId) % 100 &lt; ti le → nhom thu (treatment). Mot nguoi luon o cung mot nhom
    /// du doi instance / doi ngay (khong dung Random, khong dung GetHashCode thay doi theo tien trinh).
    /// </summary>
    public static class NhomAb
    {
        public const string DangChay = "control";
        public const string ThuNghiem = "treatment";

        public static int Xo(Guid userId)
        {
            byte[] bam = SHA256.HashData(Encoding.UTF8.GetBytes("reco-ab:" + userId.ToString("N")));
            uint v = BitConverter.ToUInt32(bam, 0);
            return (int)(v % 100);
        }

        public static string Chon(Guid userId, int tiLeThuNghiem)
        {
            return Xo(userId) < Math.Clamp(tiLeThuNghiem, 0, 100) ? ThuNghiem : DangChay;
        }
    }

    /// <summary>
    /// Ten cong ty chu du an cho watermark noi cua mau xem truoc — hoi identity qua gRPC IdentityData LUC NAP BO NHO
    /// (viec nen, kien truc 3.24); request feed chi doc ten da nap.
    /// </summary>
    public sealed class TenDoanhNghiep
    {
        private readonly IServiceProvider _services;
        private readonly InternalApiOptions _options;
        private readonly TimeProvider _clock;

        public TenDoanhNghiep(IServiceProvider services, IOptions<InternalApiOptions> options, TimeProvider clock)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _services = services;
            _options = options.Value;
            _clock = clock;
        }

        public async Task<IReadOnlyDictionary<Guid, string?>> TraCuuAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken ct)
        {
            if (ownerIds == null)
            {
                throw new ArgumentNullException(nameof(ownerIds));
            }

            Dictionary<Guid, string?> kq = new Dictionary<Guid, string?>();
            if (ownerIds.Count == 0)
            {
                return kq;
            }

            IdentityData.IdentityDataClient client = _services.GetRequiredService<IdentityData.IdentityDataClient>();
            LookupUsersRequest req = new LookupUsersRequest();
            req.UserIds.AddRange(ownerIds.Select(x => x.ToString()));
            LookupUsersResponse res = await client.LookupUsersAsync(
                req, InternalGrpc.Khoa(_options), _clock.GetUtcNow().UtcDateTime.AddSeconds(_options.TimeoutSeconds), ct);

            foreach (Guid id in ownerIds)
            {
                kq[id] = null;
            }

            foreach (UserInfo u in res.Users)
            {
                Guid id;
                if (Guid.TryParse(u.UserId, out id))
                {
                    kq[id] = string.IsNullOrWhiteSpace(u.CompanyName) ? null : u.CompanyName.Trim();
                }
            }

            return kq;
        }
    }
}
