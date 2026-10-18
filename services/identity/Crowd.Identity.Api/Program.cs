using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Tasking;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Quality;
using Crowd.Identity.Api.Consumers;
using Crowd.Identity.Api.Entities;
using Crowd.Identity.Api.Exceptions;
using Crowd.Identity.Api.Persistence;
using Crowd.Identity.Api.Seeding;
using Crowd.Identity.Api.Services;
using Crowd.Identity.Api.Settings;
using Crowd.Identity.Api.Workers;
using Crowd.Seeding;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

            // Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker.
            builder.Services.AddCrowdSettings<IdentityDbContext>(builder.Configuration, "identity-svc");

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

            // Khoa bao ve du lieu identity: ma hoa bi mat 2FA, bam co khoa so giay to (IdentityData:Key).
            builder.Services.Configure<KhoaDuLieuOptions>(builder.Configuration.GetSection(KhoaDuLieuOptions.SectionName));
            builder.Services.AddSingleton<KhoaDuLieu>();
            builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
            builder.Services.AddSingleton<MatKhauService>();

            // Nghiep vu dang ky/dang nhap. Scoped vi dung IdentityDbContext.
            builder.Services.AddScoped<AuthService>();

            // Tai khoan: xac thuc email, quen / doi mat khau, thu hoi phien (FC-01, FC-03, VD-S-11).
            builder.Services.AddScoped<MaTaiKhoanService>();
            builder.Services.AddScoped<PhienService>();
            builder.Services.AddScoped<TaiKhoanService>();

            // Ho so, anh dai dien (bucket rieng "avatars"), cap do / huy hieu / xep hang (FC-05, FL-01, FL-12).
            builder.Services.AddSingleton<KhoAnhDaiDien>();
            builder.Services.AddScoped<HoSoService>();
            builder.Services.AddScoped<CapDoService>();

            // Don refresh token het han (chay nen, chu ky theo setting).
            builder.Services.AddScoped<RefreshTokenCleanupService>();
            builder.Services.AddHostedService<RefreshTokenCleanupWorker>();

            // KYB (FB-01/02, FM-01): giay phep nam trong bucket PRIVATE kyb-docs (S-07).
            builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
            builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
            builder.Services.AddScoped<KybService>();

            // 2FA (FC-04) va xac minh danh tinh ca nhan (muc 2.9, FP-05) — anh giay to cung bucket kyb-docs.
            builder.Services.AddScoped<HaiLopService>();
            builder.Services.AddScoped<XacMinhDanhTinhService>();
            builder.Services.AddScoped<TaiKhoanAdminService>();
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<ApiExceptionHandler>();
            builder.Services.AddCrowdInternalApi(builder.Configuration);

            // Event identity nghe: so lieu cap do (project.graded, attempt.appeal_resolved, reputation.changed),
            // khoa tai khoan khi admin xac nhan gian lan (fraud.case_confirmed).
            IConfiguration cfg = builder.Configuration;
            builder.Services.AddEventConsumer<IdentityDbContext, ProjectGraded, ProjectGradedProcessor>(cfg, "identity-svc.project-graded");
            builder.Services.AddEventConsumer<IdentityDbContext, AttemptAppealResolved, AttemptAppealResolvedProcessor>(cfg, "identity-svc.attempt-appeal-resolved");
            builder.Services.AddEventConsumer<IdentityDbContext, ReputationChanged, ReputationChangedProcessor>(cfg, "identity-svc.reputation-changed");
            builder.Services.AddEventConsumer<IdentityDbContext, FraudCaseConfirmed, FraudCaseConfirmedProcessor>(cfg, "identity-svc.fraud-case-confirmed");
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

            app.UseExceptionHandler();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            await app.RunAsync();
        }
    }
}
