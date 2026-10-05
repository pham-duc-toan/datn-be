using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Ledger;
using Crowd.Payment.Api.Consumers;
using Crowd.Payment.Api.Exceptions;
using Crowd.Payment.Api.Seeding;
using Crowd.Payment.Api.Services;
using Crowd.Payment.Api.Settings;
using Crowd.Payment.Api.Workers;
using Crowd.Payment.Infrastructure.Persistence;
using Crowd.Payment.Infrastructure.Providers;
using Crowd.Seeding;
using Microsoft.AspNetCore.Builder;
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

builder.Services.AddOutbox<PaymentDbContext>(cfg);
builder.Services.AddEventConsumer<PaymentDbContext, PayoutRequested, PayoutRequestedProcessor>(cfg, "payment-svc.payout-requested");

// ---- Cong thanh toan: doi sang VNPay chi can dang ky adapter khac o day ----
builder.Services.Configure<SandboxOptions>(cfg.GetSection(SandboxOptions.SectionName));
builder.Services.AddSingleton<IPaymentProvider, SandboxPaymentProvider>();

builder.Services.Configure<PayoutOptions>(cfg.GetSection(PayoutOptions.SectionName));
builder.Services.AddScoped<PaymentEventPublisher>();
builder.Services.AddScoped<DepositService>();
builder.Services.AddHostedService<PayoutWorker>();
builder.Services.AddScoped<PaymentSeeder>();

builder.Services.AddCrowdJwtAuthentication(cfg);
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

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
