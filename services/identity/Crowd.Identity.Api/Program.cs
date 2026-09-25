using System;
using System.IO;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence;
using Crowd.Identity.Api.Services;
using Crowd.Identity.Api.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crowd.Identity.Api
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            string chuoiKetNoi = builder.Configuration.GetConnectionString("IdentityDb")
                ?? throw new InvalidOperationException(
                    "Thieu ConnectionStrings:IdentityDb trong appsettings.");

            builder.Services.AddDbContext<IdentityDbContext>(options =>
            {
                options.UseNpgsql(chuoiKetNoi);
            });

            // Ha tang dung chung: outbox + chong xu ly trung.
            builder.Services.AddOutbox<IdentityDbContext>(builder.Configuration);

            // ---- PHAT token (chi identity-svc co) ----
            builder.Services.Configure<JwtIssuerOptions>(
                builder.Configuration.GetSection(JwtIssuerOptions.SectionName));

            builder.Services.AddSingleton<SigningKeyProvider>(sp =>
            {
                JwtIssuerOptions tuyChon = new JwtIssuerOptions();
                builder.Configuration.GetSection(JwtIssuerOptions.SectionName).Bind(tuyChon);

                string duongDan = Path.Combine(
                    builder.Environment.ContentRootPath, tuyChon.SigningKeyPath);

                ILogger logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Crowd.Identity.SigningKey");

                return SigningKeyProvider.NapHoacSinh(duongDan, logger);
            });

            builder.Services.AddSingleton<TokenIssuer>();
            builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
            builder.Services.AddSingleton<MatKhauService>();

            // Nghiep vu dang ky/dang nhap. Scoped vi dung IdentityDbContext.
            builder.Services.AddScoped<AuthService>();

            // Controller nam o thu muc Controllers/, tu duoc tim thay qua
            // [ApiController] + [Route].
            builder.Services.AddControllers();

            // ---- KIEM token (MOI service deu co dong nay) ----
            // identity-svc cung tu kiem token cua chinh no qua JWKS, giong het
            // 16 service kia — khong co duong tat nao rieng.
            builder.Services.AddCrowdJwtAuthentication(builder.Configuration);

            WebApplication app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
