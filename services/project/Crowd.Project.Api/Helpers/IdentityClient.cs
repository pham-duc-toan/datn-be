using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Contracts.Grpc.Identity;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Members;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>Tra loi cua identity-svc (gRPC IdentityData.LookupUsers).</summary>
    public sealed class NguoiDungTraCuu
    {
        public Guid UserId { get; init; }

        public string Email { get; init; } = string.Empty;

        public string Username { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public IReadOnlyList<string> Roles { get; init; } = new List<string>();

        public bool Locked { get; init; }

        public int Level { get; init; }

        public int? Reputation { get; init; }

        /// <summary>Da xac minh danh tinh (FP-05).</summary>
        public bool IdentityVerified { get; init; }

        /// <summary>Da xac minh va du tuoi (muc 2.9).</summary>
        public bool Adult { get; init; }

        public IReadOnlyList<string> Domains { get; init; } = new List<string>();

        public IReadOnlyList<string> Languages { get; init; } = new List<string>();

        public IReadOnlyList<string> Modalities { get; init; } = new List<string>();

        /// <summary>Ten cong ty trong ho so doanh nghiep (rong = khong co) — in vao watermark noi.</summary>
        public string CompanyName { get; init; } = string.Empty;

        public HoSoNguoiDung HoSo()
        {
            return new HoSoNguoiDung(UserId, Roles.Contains(CrowdRoles.Labeler, StringComparer.Ordinal), Locked, Level, Reputation);
        }
    }

    /// <summary>
    /// Hoi identity-svc qua gRPC IdentityData (kien truc 3.24) ve nguoi duoc moi / nguoi xin vao du an / nguoi
    /// MO BAI (email → userId, vai tro, khoa, cap do, uy tin). Hoi CHU du lieu ngay luc can — project_db khong
    /// giu ban sao (ca email lan labeler_cache cu). Loi tam thoi (tat, het gio) → DichVuKhongSanSangException
    /// (503): khong kiem duoc thi KHONG cho vao / khong cho mo bai (thieu du lieu thi tu choi).
    /// </summary>
    public sealed class IdentityClient
    {
        private readonly IdentityData.IdentityDataClient _client;
        private readonly InternalApiOptions _options;
        private readonly TimeProvider _clock;

        public IdentityClient(IdentityData.IdentityDataClient client, IOptions<InternalApiOptions> options, TimeProvider clock)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _client = client;
            _options = options.Value;
            _clock = clock;
        }

        public Task<IReadOnlyList<NguoiDungTraCuu>> TraCuuAsync(
            IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<string> emails,
            CancellationToken ct)
        {
            return TraCuuAsync(userIds, emails, new List<string>(), ct);
        }

        /// <param name="usernames">Username (chu du an moi theo username thay cho userId).</param>
        public async Task<IReadOnlyList<NguoiDungTraCuu>> TraCuuAsync(
            IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<string> emails,
            IReadOnlyCollection<string> usernames,
            CancellationToken ct)
        {
            if (usernames == null)
            {
                throw new ArgumentNullException(nameof(usernames));
            }

            if (userIds == null)
            {
                throw new ArgumentNullException(nameof(userIds));
            }

            if (emails == null)
            {
                throw new ArgumentNullException(nameof(emails));
            }

            if (userIds.Count == 0 && emails.Count == 0 && usernames.Count == 0)
            {
                return new List<NguoiDungTraCuu>();
            }

            LookupUsersRequest req = new LookupUsersRequest();
            req.UserIds.AddRange(userIds.Select(x => x.ToString()));
            req.Emails.AddRange(emails);
            req.Usernames.AddRange(usernames);

            LookupUsersResponse res;
            try
            {
                res = await _client.LookupUsersAsync(
                    req, InternalGrpc.Khoa(_options), _clock.GetUtcNow().UtcDateTime.AddSeconds(_options.TimeoutSeconds), ct);
            }
            catch (RpcException ex) when (InternalGrpc.LaLoiTamThoi(ex))
            {
                throw new DichVuKhongSanSangException("Khong goi duoc identity-svc de tra cuu nguoi dung. Thu lai sau.", ex);
            }

            List<NguoiDungTraCuu> kq = new List<NguoiDungTraCuu>();
            foreach (UserInfo u in res.Users)
            {
                kq.Add(new NguoiDungTraCuu
                {
                    UserId = Guid.Parse(u.UserId),
                    Email = u.Email,
                    Username = u.Username,
                    DisplayName = u.DisplayName,
                    Roles = u.Roles.ToList(),
                    Locked = u.Locked,
                    Level = u.Level,
                    Reputation = u.HasReputation ? u.Reputation : null,
                    IdentityVerified = u.IdentityVerified,
                    Adult = u.Adult,
                    Domains = u.Domains.ToList(),
                    Languages = u.Languages.ToList(),
                    Modalities = u.Modalities.ToList(),
                    CompanyName = u.CompanyName,
                });
            }

            return kq;
        }

        /// <summary>Ho so mot nguoi; khong ton tai → null.</summary>
        public async Task<NguoiDungTraCuu?> LayAsync(Guid userId, CancellationToken ct)
        {
            IReadOnlyList<NguoiDungTraCuu> ds = await TraCuuAsync(new[] { userId }, Array.Empty<string>(), ct);
            return ds.FirstOrDefault(x => x.UserId == userId);
        }
    }
}
