using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Crowd.Contracts.Grpc.Identity;
using Crowd.Identity.Api.Dtos;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Services;
using Grpc.Core;

namespace Crowd.Identity.Api.Grpc
{
    /// <summary>
    /// gRPC noi bo (kien truc 3.24, contracts/grpc/identity/v1/identity_data.proto): project-svc tra cuu nguoi
    /// dung luc moi / tham gia du an va luc MO BAI (cap do, uy tin, khoa) — thay bang ban sao labeler_cache.
    /// Khoa noi bo kiem o InternalKeyInterceptor.
    /// </summary>
    public sealed class IdentityDataService : IdentityData.IdentityDataBase
    {
        private readonly TraCuuNguoiDungService _traCuu;

        public IdentityDataService(TraCuuNguoiDungService traCuu)
        {
            if (traCuu == null)
            {
                throw new ArgumentNullException(nameof(traCuu));
            }

            _traCuu = traCuu;
        }

        public override async Task<LookupUsersResponse> LookupUsers(LookupUsersRequest request, ServerCallContext context)
        {
            List<Guid> ids = new List<Guid>();
            foreach (string s in request.UserIds)
            {
                Guid id;
                if (!Guid.TryParse(s, out id))
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "user_ids khong phai uuid."));
                }

                ids.Add(id);
            }

            List<UserLookupDto> ds;
            try
            {
                ds = await _traCuu.TraCuuAsync(ids, request.Emails, request.Usernames, context.CancellationToken);
            }
            catch (InvalidValueException ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }

            LookupUsersResponse kq = new LookupUsersResponse();
            foreach (UserLookupDto u in ds)
            {
                UserInfo info = new UserInfo
                {
                    UserId = u.UserId.ToString(),
                    Email = u.Email,
                    Username = u.Username,
                    DisplayName = u.DisplayName,
                    Locked = u.Locked,
                    Level = u.Level,
                    IdentityVerified = u.IdentityVerified,
                    Adult = u.Adult,
                    CompanyName = u.CompanyName ?? string.Empty,
                };
                if (u.Reputation.HasValue)
                {
                    info.Reputation = u.Reputation.Value;
                }

                info.Roles.AddRange(u.Roles);
                info.Domains.AddRange(u.Domains);
                info.Languages.AddRange(u.Languages);
                info.Modalities.AddRange(u.Modalities);
                kq.Users.Add(info);
            }

            return kq;
        }
    }
}
