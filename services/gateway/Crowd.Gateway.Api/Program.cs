using Crowd.Gateway.Api.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Bang dinh tuyen nam o file rieng ocelot.json (quy uoc cua Ocelot),
// tach khoi appsettings de doc mot cho la thay het route.
// reloadOnChange: sua route luc dang chay khong phai khoi dong lai.
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddOcelot(builder.Configuration);

WebApplication app = builder.Build();

// Chay TRUOC Ocelot: gan correlationId roi moi chuyen tiep.
app.UseMiddleware<CorrelationIdMiddleware>();

// Ocelot la khau CUOI cua ong xu ly: request khop route thi chuyen
// tiep, khong khop thi tra 404. Middleware nao can chay truoc
// (vd correlationId) phai dang ky TRUOC dong nay.
await app.UseOcelot();

await app.RunAsync();
