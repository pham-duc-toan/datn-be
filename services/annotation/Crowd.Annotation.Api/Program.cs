using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.Annotation.Api.Consumers;
using Crowd.Annotation.Api.Exceptions;
using Crowd.Annotation.Api.Seeding;
using Crowd.Annotation.Api.Services;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Gate;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Seeding;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string chuoiKetNoi = builder.Configuration.GetConnectionString("AnnotationDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:AnnotationDb trong appsettings.");

builder.Services.AddDbContext<AnnotationDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// ---- Outbox (gui) + consumer (nhan). Ten queue giu nguyen mai mai. ----
ConfigurationManager cfg = builder.Configuration;
// Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker.
builder.Services.AddCrowdSettings<AnnotationDbContext>(cfg, "annotation-svc");
builder.Services.AddOutbox<AnnotationDbContext>(cfg);
builder.Services.AddEventConsumer<AnnotationDbContext, AssignmentSubmitted, AssignmentSubmittedProcessor>(cfg, "annotation-svc.assignment-submitted");
builder.Services.AddEventConsumer<AnnotationDbContext, ProjectPublished, ProjectPublishedProcessor>(cfg, "annotation-svc.project-published");
builder.Services.AddEventConsumer<AnnotationDbContext, MemberAdded, MemberAddedProcessor>(cfg, "annotation-svc.member-added");
builder.Services.AddEventConsumer<AnnotationDbContext, MemberBlocked, MemberBlockedProcessor>(cfg, "annotation-svc.member-blocked");
builder.Services.AddEventConsumer<AnnotationDbContext, MemberUnblocked, MemberUnblockedProcessor>(cfg, "annotation-svc.member-unblocked");
builder.Services.AddEventConsumer<AnnotationDbContext, MemberRemoved, MemberRemovedProcessor>(cfg, "annotation-svc.member-removed");
builder.Services.AddEventConsumer<AnnotationDbContext, ConsensusReached, ConsensusReachedProcessor>(cfg, "annotation-svc.consensus-reached");
builder.Services.AddEventConsumer<AnnotationDbContext, GateSolved, GateSolvedProcessor>(cfg, "annotation-svc.gate-solved");

// ---- Kho anh (chi sinh link xem) ----
builder.Services.Configure<ObjectStorageOptions>(cfg.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();

// ---- Nghiep vu ----
builder.Services.AddScoped<AnnotationEventPublisher>();
builder.Services.AddScoped<AnnotationService>();
builder.Services.AddScoped<AnnotationSeeder>();

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
    AnnotationDbContext db = scope.ServiceProvider.GetRequiredService<AnnotationDbContext>();
    await db.Database.MigrateAsync();

    // Du lieu mau (chi Development + Seed:Enabled), truoc khi consumer chay.
    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        AnnotationSeeder seeder = scope.ServiceProvider.GetRequiredService<AnnotationSeeder>();
        await seeder.ChayAsync(CancellationToken.None);
    }
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
