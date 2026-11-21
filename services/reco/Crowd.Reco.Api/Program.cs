using System;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Reco.Api.Consumers;
using Crowd.Reco.Api.Helpers;
using Crowd.Reco.Api.Services;
using Crowd.Reco.Api.Workers;
using Crowd.Reco.Infrastructure.ClickHouse;
using Crowd.Reco.Infrastructure.Persistence;
using Crowd.Reco.Infrastructure.Redis;
using Crowd.Reco.Infrastructure.Source;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

// reco-svc (kien truc 3.39): feed cho du an ca nhan hoa theo kha nang kiem tien. Doc du an qua view cua project-svc
// (role reco_reader, nap RAM moi 30 giay), so thich / tap loai tru o Redis rieng, hanh vi o ClickHouse database reco.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager cfg = builder.Configuration;

string chuoiKetNoi = cfg.GetConnectionString("RecoDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:RecoDb trong appsettings.");
string docProject = cfg.GetConnectionString("ProjectRead")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:ProjectRead (role reco_reader doc view cua project-svc).");

RecoOptions reco = new RecoOptions();
cfg.GetSection(RecoOptions.SectionName).Bind(reco);
if (string.IsNullOrWhiteSpace(reco.Redis) || string.IsNullOrWhiteSpace(reco.ClickHouse))
{
    throw new InvalidOperationException("Thieu Reco:Redis / Reco:ClickHouse trong appsettings.");
}

builder.Services.AddDbContext<RecoDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// Setting dong (admin-svc): GOI TRUOC AddOutbox de nap xong truoc moi worker.
builder.Services.AddCrowdSettings(cfg, "reco-svc");
builder.Services.AddOutbox<RecoDbContext>(cfg);
builder.Services.Configure<RecoOptions>(cfg.GetSection(RecoOptions.SectionName));

// ---- Nguon du lieu ----
builder.Services.AddSingleton(new NguonDuAn(docProject));
ConfigurationOptions redisCfg = ConfigurationOptions.Parse(reco.Redis);
redisCfg.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisCfg));
builder.Services.AddSingleton<RecoRedis>();
builder.Services.AddSingleton(new RecoClickStore(reco.ClickHouse));

// File mo hinh do ml-svc ghi (bucket ml-models). Link xem mau xem truoc qua media-svc (FP-02/03).
builder.Services.Configure<ObjectStorageOptions>(cfg.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
builder.Services.AddMediaLinks(cfg);

// Ten doanh nghiep cho watermark noi: hoi identity qua gRPC luc nap bo nho (viec nen).
builder.Services.AddCrowdInternalApi(cfg);
InternalApiOptions noiBo = new InternalApiOptions();
cfg.GetSection(InternalApiOptions.Section).Bind(noiBo);
if (string.IsNullOrWhiteSpace(noiBo.IdentityGrpcUrl))
{
    throw new InvalidOperationException("Thieu InternalApi:IdentityGrpcUrl trong appsettings (ten doanh nghiep cho watermark).");
}

builder.Services.AddGrpcClient<Crowd.Contracts.Grpc.Identity.IdentityData.IdentityDataClient>(o =>
{
    o.Address = new Uri(noiBo.IdentityGrpcUrl);
});
builder.Services.AddSingleton<TenDoanhNghiep>();

// ---- Bo nho + mo hinh (singleton: moi instance mot ban) ----
builder.Services.AddSingleton<CatalogGoiY>();
builder.Services.AddSingleton<MoHinhDangChay>();
builder.Services.AddScoped<HanhViService>();
builder.Services.AddScoped<FeedService>();

builder.Services.AddHostedService<CatalogWarmup>();
builder.Services.AddHostedService<CatalogRefreshWorker>();
builder.Services.AddHostedService<EventRelayWorker>();
builder.Services.AddHostedService<ModelReloadWorker>();

// ---- Hanh vi (bo dem so thich) + tap loai tru ----
builder.Services.AddEventConsumer<RecoDbContext, UserRegistered, UserRegisteredProcessor>(cfg, "reco-svc.user-registered");
builder.Services.AddEventConsumer<RecoDbContext, AttemptStarted, AttemptStartedProcessor>(cfg, "reco-svc.attempt-started");
builder.Services.AddEventConsumer<RecoDbContext, AttemptSubmitted, AttemptSubmittedProcessor>(cfg, "reco-svc.attempt-submitted");
builder.Services.AddEventConsumer<RecoDbContext, AttemptAbandoned, AttemptAbandonedProcessor>(cfg, "reco-svc.attempt-abandoned");
builder.Services.AddEventConsumer<RecoDbContext, ProjectGraded, ProjectGradedProcessor>(cfg, "reco-svc.project-graded");
builder.Services.AddEventConsumer<RecoDbContext, MemberAdded, MemberAddedProcessor>(cfg, "reco-svc.member-added");
builder.Services.AddEventConsumer<RecoDbContext, MemberRemoved, MemberRemovedProcessor>(cfg, "reco-svc.member-removed");
builder.Services.AddEventConsumer<RecoDbContext, MemberBlocked, MemberBlockedProcessor>(cfg, "reco-svc.member-blocked");
builder.Services.AddEventConsumer<RecoDbContext, MemberUnblocked, MemberUnblockedProcessor>(cfg, "reco-svc.member-unblocked");
builder.Services.AddEventConsumer<RecoDbContext, OwnerBlocklistChanged, OwnerBlocklistChangedProcessor>(cfg, "reco-svc.owner-blocklist-changed");

// ---- Du an mo / dong → nap lai bo nho ngay ----
builder.Services.AddEventConsumer<RecoDbContext, ProjectPublished, NapLaiKhiDoiDuAn<ProjectPublished>>(cfg, "reco-svc.project-published");
builder.Services.AddEventConsumer<RecoDbContext, ProjectPaused, NapLaiKhiDoiDuAn<ProjectPaused>>(cfg, "reco-svc.project-paused");
builder.Services.AddEventConsumer<RecoDbContext, ProjectResumed, NapLaiKhiDoiDuAn<ProjectResumed>>(cfg, "reco-svc.project-resumed");
builder.Services.AddEventConsumer<RecoDbContext, ProjectCancelled, NapLaiKhiDoiDuAn<ProjectCancelled>>(cfg, "reco-svc.project-cancelled");
builder.Services.AddEventConsumer<RecoDbContext, ProjectCollectionFinished, NapLaiKhiDoiDuAn<ProjectCollectionFinished>>(cfg, "reco-svc.project-collection-finished");

builder.Services.AddCrowdJwtAuthentication(cfg);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    RecoDbContext db = scope.ServiceProvider.GetRequiredService<RecoDbContext>();
    await db.Database.MigrateAsync();
}

// Chot thu hai cua warm-up: bo nho chua nap thi tra 503 (Retry-After) — khong bao gio xep feed tu bo nho rong.
CatalogGoiY catalogKhoiDong = app.Services.GetRequiredService<CatalogGoiY>();
app.Use(async (context, next) =>
{
    if (!catalogKhoiDong.DaNap && !context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "2";
        return;
    }

    await next(context);
});

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
