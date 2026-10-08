using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Ledger;
using Crowd.Project.Api.Consumers;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Api.Seeding;
using Crowd.Project.Api.Services;
using Crowd.Project.Api.Settings;
using Crowd.Project.Api.Workers;
using Crowd.Project.Infrastructure.Media;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Seeding;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string chuoiKetNoi = builder.Configuration.GetConnectionString("ProjectDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:ProjectDb trong appsettings.");

builder.Services.AddDbContext<ProjectDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// ---- Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker ----
builder.Services.AddCrowdSettings<ProjectDbContext>(builder.Configuration, "project-svc");

// ---- Ha tang dung chung: outbox (GUI event) + consumer (NHAN event) ----
builder.Services.AddOutbox<ProjectDbContext>(builder.Configuration);

// Saga publish buoc 2: ledger tra loi. Ten queue giu nguyen mai mai — no la ten
// handler trong processed_events.
builder.Services.AddEventConsumer<ProjectDbContext, EscrowReserved, EscrowReservedProcessor>(
    builder.Configuration, "project-svc.escrow-reserved");
builder.Services.AddEventConsumer<ProjectDbContext, EscrowRejected, EscrowRejectedProcessor>(
    builder.Configuration, "project-svc.escrow-rejected");

// ---- Kho anh MinIO ----
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();

// ---- Nghiep vu ----
builder.Services.Configure<ProjectSagaOptions>(builder.Configuration.GetSection(ProjectSagaOptions.SectionName));
builder.Services.AddScoped<ProjectAccessService>();
builder.Services.AddScoped<ProjectEventPublisher>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<DatasetService>();
builder.Services.AddScoped<GioiHanZipFilter>();
builder.Services.AddScoped<UploadService>();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
builder.Services.AddScoped<DatasetIngestor>();
builder.Services.AddHostedService<DatasetIngestWorker>();
builder.Services.AddScoped<GoldSetService>();
builder.Services.AddScoped<EntranceTestService>();
builder.Services.AddHostedService<PendingApprovalTimeoutWorker>();
builder.Services.AddScoped<ProjectSeeder>();

// ---- Kiem token (giong moi service) ----
builder.Services.AddCrowdJwtAuthentication(builder.Configuration);

// ---- HTTP ----
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enum ra/vao dang chuoi camelCase: "imageClassification", "running".
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

WebApplication app = builder.Build();

// Moi service tu migrate luc khoi dong (VD-O-03): khong ai phai nho chay tay
// `dotnet ef database update` cho 15 database.
using (IServiceScope scope = app.Services.CreateScope())
{
    ProjectDbContext db = scope.ServiceProvider.GetRequiredService<ProjectDbContext>();
    await db.Database.MigrateAsync();

    // Du lieu mau (chi Development + Seed:Enabled). Chay TRUOC app.RunAsync nen
    // worker va consumer chua chay — khong ai chen vao giua luc seed.
    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        ProjectSeeder seeder = scope.ServiceProvider.GetRequiredService<ProjectSeeder>();
        await seeder.ChayAsync(CancellationToken.None);
    }
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
