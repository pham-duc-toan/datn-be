using System;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.Gateway.Api.Middlewares;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Bang dinh tuyen nam o file rieng ocelot.json (quy uoc cua Ocelot),
// tach khoi appsettings de doc mot cho la thay het route.
// reloadOnChange: sua route luc dang chay khong phai khoi dong lai.
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

// Docker: DownstreamHosts__8101=identity ... ghi de "localhost" trong ocelot.json (xem DownstreamHosts).
DownstreamHosts.GhiDe(builder.Configuration);

// CORS cho frontend: chi cac nguon khai trong Cors:AllowedOrigins (dev: localhost:3000, :5173).
// Dat O GATEWAY vi day la cua duy nhat frontend goi toi; service phia sau khong can biet.
string[]? nguonFe = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
string[] nguonChoPhep = nguonFe == null ? Array.Empty<string>() : nguonFe;
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins(nguonChoPhep)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("X-Correlation-Id");
    });
});

// Kiem token o gateway bang CUNG bo cau hinh voi moi service (shared/auth).
// Route nao trong ocelot.json co AuthenticationOptions "Bearer" thi Ocelot goi
// scheme nay truoc khi chuyen tiep; token sai → 401 ngay tai gateway.
builder.Services.AddCrowdJwtAuthentication(builder.Configuration);

// Setting dong (admin-svc quan ly). Gateway khong co database: giu setting trong
// bo nho, nghe setting.changed qua mot queue tam (vd tran upload dataset.zip_max_bytes).
builder.Services.AddCrowdSettingsInMemory(builder.Configuration, "gateway");

builder.Services.AddOcelot(builder.Configuration);

WebApplication app = builder.Build();

// CORS truoc moi thu: tra loi preflight OPTIONS ngay, khong chuyen tiep vao service.
app.UseCors("frontend");

// Chay TRUOC Ocelot: gan correlationId roi moi chuyen tiep.
app.UseMiddleware<CorrelationIdMiddleware>();

// Nang tran body chi cho endpoint upload dataset.
app.UseMiddleware<UploadLimitMiddleware>();

// Ocelot la khau CUOI cua ong xu ly: request khop route thi chuyen
// tiep, khong khop thi tra 404. Middleware nao can chay truoc
// (vd correlationId) phai dang ky TRUOC dong nay.
await app.UseOcelot();

await app.RunAsync();
