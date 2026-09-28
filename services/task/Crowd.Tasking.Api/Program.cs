using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Project;
using Crowd.Tasking.Api.Consumers;
using Crowd.Tasking.Api.Exceptions;
using Crowd.Tasking.Api.Services;
using Crowd.Tasking.Api.Settings;
using Crowd.Tasking.Api.Workers;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string chuoiKetNoi = builder.Configuration.GetConnectionString("TaskDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:TaskDb trong appsettings.");

builder.Services.AddDbContext<TaskDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// ---- Outbox (gui) ----
builder.Services.AddOutbox<TaskDbContext>(builder.Configuration);

// ---- Consumer (nhan): dung ban sao + sinh task. Ten queue giu nguyen mai mai. ----
ConfigurationManager cfg = builder.Configuration;
builder.Services.AddEventConsumer<TaskDbContext, DatasetIngested, DatasetIngestedProcessor>(cfg, "task-svc.dataset-ingested");
builder.Services.AddEventConsumer<TaskDbContext, ProjectPublished, ProjectPublishedProcessor>(cfg, "task-svc.project-published");
builder.Services.AddEventConsumer<TaskDbContext, ProjectPaused, ProjectPausedProcessor>(cfg, "task-svc.project-paused");
builder.Services.AddEventConsumer<TaskDbContext, ProjectResumed, ProjectResumedProcessor>(cfg, "task-svc.project-resumed");
builder.Services.AddEventConsumer<TaskDbContext, ProjectCancelled, ProjectCancelledProcessor>(cfg, "task-svc.project-cancelled");
builder.Services.AddEventConsumer<TaskDbContext, ProjectCompleted, ProjectCompletedProcessor>(cfg, "task-svc.project-completed");
builder.Services.AddEventConsumer<TaskDbContext, GoldSetUpdated, GoldSetUpdatedProcessor>(cfg, "task-svc.gold-set-updated");
builder.Services.AddEventConsumer<TaskDbContext, MemberAdded, MemberAddedProcessor>(cfg, "task-svc.member-added");
builder.Services.AddEventConsumer<TaskDbContext, MemberBlocked, MemberBlockedProcessor>(cfg, "task-svc.member-blocked");
builder.Services.AddEventConsumer<TaskDbContext, MemberUnblocked, MemberUnblockedProcessor>(cfg, "task-svc.member-unblocked");
builder.Services.AddEventConsumer<TaskDbContext, MemberRemoved, MemberRemovedProcessor>(cfg, "task-svc.member-removed");
builder.Services.AddEventConsumer<TaskDbContext, UserBlocked, UserBlockedProcessor>(cfg, "task-svc.user-blocked");
builder.Services.AddEventConsumer<TaskDbContext, ReputationChanged, ReputationChangedProcessor>(cfg, "task-svc.reputation-changed");
builder.Services.AddEventConsumer<TaskDbContext, LevelChanged, LevelChangedProcessor>(cfg, "task-svc.level-changed");

// ---- Kho anh (chi sinh link xem) ----
builder.Services.Configure<ObjectStorageOptions>(cfg.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();

// ---- Nghiep vu ----
builder.Services.Configure<LeaseOptions>(cfg.GetSection(LeaseOptions.SectionName));
builder.Services.AddScoped<TaskEventPublisher>();
builder.Services.AddScoped<LeaseRevoker>();
builder.Services.AddScoped<LeaseService>();
builder.Services.AddHostedService<LeaseReaper>();

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

// Tu migrate luc khoi dong (VD-O-03).
using (IServiceScope scope = app.Services.CreateScope())
{
    TaskDbContext db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await db.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
