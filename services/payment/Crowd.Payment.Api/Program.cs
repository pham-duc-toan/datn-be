using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Ledger;
using Crowd.Payment.Api.Consumers;
using Crowd.Payment.Api.Controllers;
using Crowd.Payment.Api.Exceptions;
using Crowd.Payment.Api.Seeding;
using Crowd.Payment.Api.Services;
using Crowd.Payment.Api.Workers;
using Crowd.Payment.Infrastructure.Persistence;
using Crowd.Payment.Infrastructure.Providers;
using Crowd.Seeding;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager cfg = builder.Configuration;

string chuoiKetNoi = cfg.GetConnectionString("PaymentDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:PaymentDb trong appsettings.");

builder.Services.AddDbContext<PaymentDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker.
builder.Services.AddCrowdSettings<PaymentDbContext>(cfg, "payment-svc");
builder.Services.AddOutbox<PaymentDbContext>(cfg);
builder.Services.AddEventConsumer<PaymentDbContext, PayoutRequested, PayoutRequestedProcessor>(cfg, "payment-svc.payout-requested");

// ---- Cong thanh toan ----
// Sandbox: vua la cong NAP gia lap vua la cong CHI tien ra (lenh rut).
builder.Services.Configure<SandboxOptions>(cfg.GetSection(SandboxOptions.SectionName));
builder.Services.AddSingleton<SandboxPaymentProvider>();
builder.Services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<SandboxPaymentProvider>());
builder.Services.AddSingleton<ICongNap>(sp => sp.GetRequiredService<SandboxPaymentProvider>());

// VNPay / MoMo (FB-03): chi dang ky khi CO DU khoa (bien moi truong / appsettings). Thieu khoa thi
// cong do khong xuat hien trong GET /payments/deposits/providers.
builder.Services.Configure<VnPayOptions>(cfg.GetSection(VnPayOptions.SectionName));
builder.Services.Configure<MoMoOptions>(cfg.GetSection(MoMoOptions.SectionName));
VnPayOptions vnpay = new VnPayOptions();
cfg.GetSection(VnPayOptions.SectionName).Bind(vnpay);
if (vnpay.DuCauHinh)
{
    builder.Services.AddHttpClient<VnPayProvider>(c => c.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddTransient<ICongNap>(sp => sp.GetRequiredService<VnPayProvider>());
}

MoMoOptions momo = new MoMoOptions();
cfg.GetSection(MoMoOptions.SectionName).Bind(momo);
if (momo.DuCauHinh)
{
    builder.Services.AddHttpClient<MoMoProvider>(c => c.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddTransient<ICongNap>(sp => sp.GetRequiredService<MoMoProvider>());
}

builder.Services.AddScoped<DanhBaCongNap>();

// Cong gia lap VNPay / MoMo (chi Development — xem CongGiaLapController).
builder.Services.AddSingleton<KhoCongGiaLap>();
builder.Services.AddHttpClient();

builder.Services.AddScoped<PaymentEventPublisher>();
builder.Services.AddScoped<DepositService>();
builder.Services.AddHostedService<PayoutWorker>();

// Doi soat lenh nap qua cong con treo — IPN bi lac / den tre (VD-M-09).
builder.Services.AddHostedService<DepositReconcileWorker>();
builder.Services.AddScoped<PaymentSeeder>();

builder.Services.AddCrowdJwtAuthentication(cfg);

// IP that cua doanh nghiep tu X-Forwarded-For cua gateway (VNPay bat buoc vnp_IpAddr).
builder.Services.Configure<ForwardedHeadersOptions>(o => ProxyTinCay.CauHinh(o, cfg));
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    PaymentDbContext db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.MigrateAsync();

    // Du lieu mau (chi Development + Seed:Enabled), truoc khi worker/consumer chay.
    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        PaymentSeeder seeder = scope.ServiceProvider.GetRequiredService<PaymentSeeder>();
        await seeder.ChayAsync(CancellationToken.None);
    }
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
