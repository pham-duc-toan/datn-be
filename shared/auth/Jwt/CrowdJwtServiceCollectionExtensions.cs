using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Crowd.BuildingBlocks.Auth.Jwt
{
    /// <summary>
    /// Mot dong de mot service biet kiem access token.
    ///
    /// Moi service TU KIEM token, khong tin rang request da qua gateway
    /// (VD-S-06). Ai cham duoc vao mang noi bo ma goi thang ledger-svc:8105
    /// thi van bi chan o day.
    /// </summary>
    public static class CrowdJwtServiceCollectionExtensions
    {
        public static IServiceCollection AddCrowdJwtAuthentication(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            CrowdJwtOptions tuyChon = new CrowdJwtOptions();
            configuration.GetSection(CrowdJwtOptions.SectionName).Bind(tuyChon);

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // Tai khoa cong khai tu identity-svc, tu cache, tu tai lai khi
                    // gap kid la (tuc la identity vua xoay khoa).
                    options.MetadataAddress = tuyChon.MetadataAddress;
                    options.RequireHttpsMetadata = tuyChon.RequireHttpsMetadata;

                    // BAY HAY GAP NHAT: mac dinh ASP.NET Core DOI TEN claim luc doc
                    // token — "sub" thanh ClaimTypes.NameIdentifier dai ngoang,
                    // "role" thanh ClaimTypes.Role. Code tim "sub" se khong thay gi.
                    // Tat di de token doc ra dung nhu no duoc ghi.
                    options.MapInboundClaims = false;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = tuyChon.Issuer,

                        ValidateAudience = true,
                        ValidAudience = tuyChon.Audience,

                        ValidateLifetime = true,

                        // Mac dinh la 5 PHUT — voi access token song 15 phut thi
                        // token het han van dung duoc them 1/3 doi. Ha xuong 30 giay,
                        // du bu lech dong ho giua cac container.
                        ClockSkew = TimeSpan.FromSeconds(30),

                        ValidateIssuerSigningKey = true,

                        // CHI chap nhan RS256. Chan tan cong "alg confusion":
                        // ke tan cong doi header thanh HS256 roi ky bang khoa CONG
                        // KHAI (ma ai cung tai duoc) — thu vien nao nhe da tin
                        // header se chap nhan (VD-S-05).
                        ValidAlgorithms = new string[] { SecurityAlgorithms.RsaSha256 },

                        NameClaimType = CrowdClaims.Subject,
                        RoleClaimType = CrowdClaims.Role,
                    };
                });

            services.AddAuthorization();

            return services;
        }
    }
}
