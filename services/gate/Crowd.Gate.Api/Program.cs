using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Security;
using Crowd.BuildingBlocks.Storage;
using Crowd.Gate.Api.Helpers;
using Crowd.Gate.Api.Services;
using Crowd.Gate.Api.Workers;
using Crowd.Gate.Infrastructure.ClickHouse;
using Crowd.Gate.Infrastructure.Persistence;
using Crowd.Gate.Infrastructure.Redis;
using Crowd.Gate.Infrastructure.Source;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
builder.Services.AddCrowdSettings(cfg, "gate-svc");
builder.Services.AddOutbox<GateDbContext>(cfg);

builder.Services.Configure<GateOptions>(cfg.GetSection(GateOptions.SectionName));

// ---- Du lieu cong link: doc THANG view v_gate_* cua chu du lieu (kien truc 3.32) — khong ban sao, khong gRPC ----
// Role gate_reader chi SELECT duoc cac view do. Nap vao bo nho luc khoi dong (CatalogWarmup) va dinh ky.
string docProject = cfg.GetConnectionString("ProjectRead")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:ProjectRead (role gate_reader doc view cua project-svc).");
string docLedger = cfg.GetConnectionString("LedgerRead")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:LedgerRead (role gate_reader doc view cua ledger-svc).");
string docLink = cfg.GetConnectionString("LinkRead")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:LinkRead (role gate_reader doc view cua link-svc).");
builder.Services.AddSingleton(new NguonCongLink(docProject, docLedger, docLink));

// /internal/catalog (doi ky thuat, e2e) dung khoa noi bo.
builder.Services.AddCrowdInternalApi(cfg);
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
// Link xem mau qua media-svc (FP-02/03/04).
builder.Services.AddMediaLinks(cfg);

builder.Services.AddSingleton<GateCatalog>();
builder.Services.AddSingleton<GateTokenService>();
builder.Services.AddHttpClient<TurnstileVerifier>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<GateService>();
builder.Services.AddScoped<GateEventPublisher>();
// Warm-up: hosted service chay truoc khi Kestrel mo cong — nap xong bo nho moi nhan request (xem them chot 503 duoi).
builder.Services.AddHostedService<CatalogWarmup>();
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

// Chot thu hai cua warm-up: neu vi ly do nao do request toi khi bo nho chua nap xong thi tra 503 (Retry-After),
// khong bao gio phuc vu tu bo nho rong. /health van tra loi de probe biet trang thai.
GateCatalog catalogKhoiDong = app.Services.GetRequiredService<GateCatalog>();
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

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
