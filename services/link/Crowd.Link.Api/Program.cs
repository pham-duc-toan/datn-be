using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Security;
using Crowd.Contracts.Identity;
using Crowd.Link.Api.Consumers;
using Crowd.Link.Api.Exceptions;
using Crowd.Link.Api.Helpers;
using Crowd.Link.Api.Persistence;
using Crowd.Link.Api.Services;
using Crowd.Link.Api.Workers;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager cfg = builder.Configuration;

string chuoiKetNoi = cfg.GetConnectionString("LinkDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:LinkDb trong appsettings.");

builder.Services.AddDbContext<LinkDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker.
builder.Services.AddCrowdSettings<LinkDbContext>(cfg, "link-svc");
builder.Services.AddOutbox<LinkDbContext>(cfg);
builder.Services.AddEventConsumer<LinkDbContext, UserRegistered, UserRegisteredProcessor>(cfg, "link-svc.user-registered");

builder.Services.Configure<ShortLinkOptions>(cfg.GetSection(ShortLinkOptions.SectionName));
builder.Services.Configure<PrivacyOptions>(cfg.GetSection(PrivacyOptions.SectionName));
builder.Services.Configure<UrlSafetyOptions>(cfg.GetSection(UrlSafetyOptions.SectionName));

// Quet link dich: co khoa Google thi goi Safe Browsing that, khong thi bo kiem dev.
UrlSafetyOptions quet = new UrlSafetyOptions();
cfg.GetSection(UrlSafetyOptions.SectionName).Bind(quet);
if (string.IsNullOrWhiteSpace(quet.GoogleApiKey))
{
    builder.Services.AddSingleton<IUrlSafetyChecker, DevUrlSafetyChecker>();
}
else
{
    builder.Services.AddHttpClient<IUrlSafetyChecker, GoogleSafeBrowsingChecker>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(10);
    });
}

builder.Services.AddScoped<LinkEventPublisher>();
builder.Services.AddScoped<LinkService>();
builder.Services.AddScoped<ModerationService>();
builder.Services.AddScoped<ReferralService>();
builder.Services.AddHostedService<LinkScanWorker>();

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

string muoi = app.Services.GetRequiredService<IOptions<PrivacyOptions>>().Value.IpSalt;
if (string.IsNullOrWhiteSpace(muoi))
{
    throw new InvalidOperationException("Thieu Privacy:IpSalt — muoi bam IP (phai trung voi gate-svc).");
}

using (IServiceScope scope = app.Services.CreateScope())
{
    LinkDbContext db = scope.ServiceProvider.GetRequiredService<LinkDbContext>();
    await db.Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
