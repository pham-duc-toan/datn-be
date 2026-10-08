using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Ledger.Api.Consumers;
using Crowd.Ledger.Api.Exceptions;
using Crowd.Ledger.Api.Seeding;
using Crowd.Ledger.Api.Services;
using Crowd.Ledger.Api.Workers;
using Crowd.Ledger.Infrastructure.Persistence;
using Crowd.Seeding;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager cfg = builder.Configuration;

string chuoiKetNoi = cfg.GetConnectionString("LedgerDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:LedgerDb trong appsettings.");

builder.Services.AddDbContext<LedgerDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// ---- Outbox + consumer. Ten queue giu nguyen mai mai. ----
// Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker.
builder.Services.AddCrowdSettings<LedgerDbContext>(cfg, "ledger-svc");
builder.Services.AddOutbox<LedgerDbContext>(cfg);
builder.Services.AddEventConsumer<LedgerDbContext, ProjectPublishRequested, ProjectPublishRequestedProcessor>(cfg, "ledger-svc.project-publish-requested");
builder.Services.AddEventConsumer<LedgerDbContext, ProjectPublished, ProjectPublishedProcessor>(cfg, "ledger-svc.project-published");
builder.Services.AddEventConsumer<LedgerDbContext, AnnotationApproved, AnnotationApprovedProcessor>(cfg, "ledger-svc.annotation-approved");
builder.Services.AddEventConsumer<LedgerDbContext, ProjectCancelled, ProjectCancelledProcessor>(cfg, "ledger-svc.project-cancelled");
builder.Services.AddEventConsumer<LedgerDbContext, ProjectCompleted, ProjectCompletedProcessor>(cfg, "ledger-svc.project-completed");
builder.Services.AddEventConsumer<LedgerDbContext, DepositConfirmed, DepositConfirmedProcessor>(cfg, "ledger-svc.deposit-confirmed");
builder.Services.AddEventConsumer<LedgerDbContext, PayoutCompleted, PayoutCompletedProcessor>(cfg, "ledger-svc.payout-completed");
builder.Services.AddEventConsumer<LedgerDbContext, PayoutFailed, PayoutFailedProcessor>(cfg, "ledger-svc.payout-failed");
builder.Services.AddEventConsumer<LedgerDbContext, UserBlocked, UserBlockedProcessor>(cfg, "ledger-svc.user-blocked");

// ---- Nghiep vu ----
builder.Services.AddScoped<LedgerWriter>();
builder.Services.AddScoped<LedgerEventPublisher>();
builder.Services.AddScoped<MoneyFlowService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<WithdrawalApprovalService>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddHostedService<HoldReleaseWorker>();
builder.Services.AddScoped<LedgerSeeder>();

// ---- HTTP ----
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
    LedgerDbContext db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
    await db.Database.MigrateAsync();

    // Du lieu mau (chi Development + Seed:Enabled), truoc khi worker/consumer chay.
    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        LedgerSeeder seeder = scope.ServiceProvider.GetRequiredService<LedgerSeeder>();
        await seeder.ChayAsync(CancellationToken.None);
    }
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
