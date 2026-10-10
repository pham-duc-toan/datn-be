using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Security;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Link;
using Crowd.Contracts.Project;
using Crowd.Gate.Api.Consumers;
using Crowd.Gate.Api.Helpers;
using Crowd.Gate.Api.Services;
using Crowd.Gate.Api.Workers;
using Crowd.Gate.Infrastructure.ClickHouse;
using Crowd.Gate.Infrastructure.Persistence;
using Crowd.Gate.Infrastructure.Redis;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager cfg = builder.Configuration;

string chuoiKetNoi = cfg.GetConnectionString("GateDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:GateDb trong appsettings.");

GateOptions gate = new GateOptions();
cfg.GetSection(GateOptions.SectionName).Bind(gate);
if (string.IsNullOrWhiteSpace(gate.Redis) || string.IsNullOrWhiteSpace(gate.ClickHouse))
{
    throw new InvalidOperationException("Thieu Gate:Redis / Gate:ClickHouse trong appsettings.");
}

builder.Services.AddDbContext<GateDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker.
builder.Services.AddCrowdSettings<GateDbContext>(cfg, "gate-svc");
builder.Services.AddOutbox<GateDbContext>(cfg);

builder.Services.AddEventConsumer<GateDbContext, ProjectPublished, ProjectPublishedProcessor>(cfg, "gate-svc.project-published");
builder.Services.AddEventConsumer<GateDbContext, ProjectPaused, ProjectStatusProcessors>(cfg, "gate-svc.project-paused");
builder.Services.AddEventConsumer<GateDbContext, ProjectResumed, ProjectStatusProcessors>(cfg, "gate-svc.project-resumed");
builder.Services.AddEventConsumer<GateDbContext, ProjectCancelled, ProjectStatusProcessors>(cfg, "gate-svc.project-cancelled");
builder.Services.AddEventConsumer<GateDbContext, ProjectCompleted, ProjectStatusProcessors>(cfg, "gate-svc.project-completed");
builder.Services.AddEventConsumer<GateDbContext, DatasetIngested, DatasetIngestedProcessor>(cfg, "gate-svc.dataset-ingested");
builder.Services.AddEventConsumer<GateDbContext, GoldSetUpdated, GoldSetUpdatedProcessor>(cfg, "gate-svc.gold-set-updated");
builder.Services.AddEventConsumer<GateDbContext, LinkActivated, LinkProcessors>(cfg, "gate-svc.link-activated");
builder.Services.AddEventConsumer<GateDbContext, LinkDisabled, LinkProcessors>(cfg, "gate-svc.link-disabled");
builder.Services.AddEventConsumer<GateDbContext, GateBudgetChanged, GateBudgetChangedProcessor>(cfg, "gate-svc.gate-budget-changed");

builder.Services.Configure<GateOptions>(cfg.GetSection(GateOptions.SectionName));
builder.Services.Configure<PrivacyOptions>(cfg.GetSection(PrivacyOptions.SectionName));

// ---- Duong nong: Redis rieng cua gate + ClickHouse cho thong ke ----
ConfigurationOptions redisCfg = ConfigurationOptions.Parse(gate.Redis);
redisCfg.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisCfg));
builder.Services.AddSingleton<RedisGateStore>();
builder.Services.AddSingleton(new ClickStore(gate.ClickHouse));

// ---- Anh cua cau hoi: link xem co han tu MinIO ----
builder.Services.Configure<ObjectStorageOptions>(cfg.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();

builder.Services.AddSingleton<GateCatalog>();
builder.Services.AddSingleton<GateTokenService>();
builder.Services.AddHttpClient<TurnstileVerifier>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<GateService>();
builder.Services.AddScoped<GateEventPublisher>();
builder.Services.AddHostedService<CatalogRefreshWorker>();
builder.Services.AddHostedService<EventRelayWorker>();

// Token dang nhap la TUY CHON o trang vuot link: co thi dung de bat chu link tu vuot.
builder.Services.AddCrowdJwtAuthentication(cfg);
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// IP that cua khach nam o X-Forwarded-For do gateway ghi. Tin proxy loopback (mac dinh) va
// cac mang trong ForwardedHeaders:TrustedNetworks (docker compose: mang noi bo).
builder.Services.Configure<ForwardedHeadersOptions>(o => ProxyTinCay.CauHinh(o, builder.Configuration));

WebApplication app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Services.GetRequiredService<IOptions<PrivacyOptions>>().Value.IpSalt))
{
    throw new InvalidOperationException("Thieu Privacy:IpSalt — muoi bam IP (phai trung voi link-svc).");
}

// Kiem khoa ky token ngay luc khoi dong (thieu thi chet som, khong doi toi request dau).
app.Services.GetRequiredService<GateTokenService>();

using (IServiceScope scope = app.Services.CreateScope())
{
    GateDbContext db = scope.ServiceProvider.GetRequiredService<GateDbContext>();
    await db.Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
