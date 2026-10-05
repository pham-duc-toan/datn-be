using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Persistence;
using Crowd.Identity.Api.Seeding;
using Crowd.Identity.Api.Services;
using Crowd.Identity.Api.Settings;
using Crowd.Seeding;
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
        public static async Task Main(string[] args)
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
            builder.Services.AddScoped<IdentitySeeder>();

            // Controller nam o thu muc Controllers/, tu duoc tim thay qua
            // [ApiController] + [Route].
            builder.Services.AddControllers();

            // ---- KIEM token (MOI service deu co dong nay) ----
            // identity-svc cung tu kiem token cua chinh no qua JWKS, giong het
            // 16 service kia — khong co duong tat nao rieng.
            builder.Services.AddCrowdJwtAuthentication(builder.Configuration);

            WebApplication app = builder.Build();

            // Tu migrate luc khoi dong nhu moi service khac (VD-O-03), roi seed
            // tai khoan mau neu dang o Development va bat Seed:Enabled.
            using (IServiceScope scope = app.Services.CreateScope())
            {
                IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.MigrateAsync();

                if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
                {
                    IdentitySeeder seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
                    await seeder.ChayAsync(CancellationToken.None);
                }
            }

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            await app.RunAsync();
        }
    }
}
