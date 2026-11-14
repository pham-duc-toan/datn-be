using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Project.Api.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>
    /// Ten cong ty cua chu du an — in vao watermark noi cua anh / video ("nguồn: &lt;nguoi xem&gt; - &lt;doanh nghiep&gt;
    /// - &lt;domain&gt;"). Chu du lieu la identity (ho so doanh nghiep), hoi qua gRPC IdentityData — project_db khong giu
    /// ban sao (kien truc 3.24). Moi lan mo bai deu can ten nen giu trong RAM (MoiLanGiu); doi ten thi link moi sau
    /// toi da MoiLanGiu moi doi theo.
    ///
    /// identity khong tra loi → null (chu bo phan doanh nghiep) va KHONG nho: xem mau van chay, lan sau hoi lai.
    /// </summary>
    public sealed class TenDoanhNghiep
    {
        /// <summary>Giu ten trong RAM bao lau.</summary>
        public static readonly TimeSpan MoiLanGiu = TimeSpan.FromMinutes(10);

        private const string TienTo = "ten_doanh_nghiep:";

        private readonly IdentityClient _identity;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TenDoanhNghiep> _logger;

        public TenDoanhNghiep(IdentityClient identity, IMemoryCache cache, ILogger<TenDoanhNghiep> logger)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _identity = identity;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>Ten cong ty cua MOT chu du an; khong co ho so / identity khong tra loi → null.</summary>
        public async Task<string?> LayAsync(Guid ownerId, CancellationToken ct)
        {
            IReadOnlyDictionary<Guid, string?> ds = await LayNhieuAsync(new[] { ownerId }, ct);
            string? ten;
            return ds.TryGetValue(ownerId, out ten) ? ten : null;
        }

        /// <summary>Ten cong ty cua nhieu chu du an — chi hoi identity phan chua co trong RAM, MOT lan goi.</summary>
        public async Task<IReadOnlyDictionary<Guid, string?>> LayNhieuAsync(IEnumerable<Guid> ownerIds, CancellationToken ct)
        {
            if (ownerIds == null)
            {
                throw new ArgumentNullException(nameof(ownerIds));
            }

            Dictionary<Guid, string?> kq = new Dictionary<Guid, string?>();
            List<Guid> thieu = new List<Guid>();
            foreach (Guid id in ownerIds.Distinct())
            {
                string? daNho;
                if (_cache.TryGetValue(TienTo + id, out daNho))
                {
                    kq[id] = string.IsNullOrEmpty(daNho) ? null : daNho;
                }
                else
                {
                    thieu.Add(id);
                }
            }

            if (thieu.Count == 0)
            {
                return kq;
            }

            IReadOnlyList<NguoiDungTraCuu> nguoi;
            try
            {
                nguoi = await _identity.TraCuuAsync(thieu, Array.Empty<string>(), ct);
            }
            catch (DichVuKhongSanSangException ex)
            {
                _logger.LogWarning(ex, "Chua hoi duoc ten doanh nghiep tu identity-svc — watermark tam bo ten doanh nghiep");
                foreach (Guid id in thieu)
                {
                    kq[id] = null;
                }

                return kq;
            }

            foreach (Guid id in thieu)
            {
                NguoiDungTraCuu? n = nguoi.FirstOrDefault(x => x.UserId == id);
                string? ten = n == null || string.IsNullOrWhiteSpace(n.CompanyName) ? null : n.CompanyName.Trim();
                // Nho ca "khong co ten" (chuoi rong) de khong hoi lai moi lan mo bai.
                _cache.Set(TienTo + id, ten ?? string.Empty, MoiLanGiu);
                kq[id] = ten;
            }

            return kq;
        }
    }
}
