using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Media;
using Crowd.Contracts.Tasking;
using Crowd.Project.Api.Consumers;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Api.Seeding;
using Crowd.Project.Api.Services;
using Crowd.Project.Api.Settings;
using Crowd.Project.Api.Workers;
using Crowd.Project.Infrastructure.Import;
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

// Du an lam trung tam (docs 3.18): task-svc dong thu bai → Dang cham; het han khieu nai → Hoan thanh.
builder.Services.AddEventConsumer<ProjectDbContext, ProjectCollectionFinished, CollectionFinishedProcessor>(
    builder.Configuration, "project-svc.collection-finished");
builder.Services.AddEventConsumer<ProjectDbContext, ProjectSettled, ProjectSettledProcessor>(
    builder.Configuration, "project-svc.project-settled");

// Email vua xac thuc → nhan loi moi cho gui toi email do.
builder.Services.AddEventConsumer<ProjectDbContext, UserEmailVerified, UserEmailVerifiedProcessor>(
    builder.Configuration, "project-svc.user-email-verified");

// media-svc chuan bi xong video vua nap → ghi trang thai mau; con video dang chuan bi thi chua publish / duyet.
builder.Services.AddEventConsumer<ProjectDbContext, VideoPrepared, VideoPreparedProcessor>(
    builder.Configuration, "project-svc.video-prepared");
// ---- Kho anh MinIO ----
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
// Link xem mau qua media-svc (FP-02/03): chu du an xem ban goc, labeler (bai test) xem ban co watermark.
builder.Services.AddMediaLinks(builder.Configuration);

// ---- Nghiep vu ----
builder.Services.Configure<ProjectSagaOptions>(builder.Configuration.GetSection(ProjectSagaOptions.SectionName));
builder.Services.AddScoped<ProjectAccessService>();
builder.Services.AddScoped<ProjectEventPublisher>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<InviteCodeService>();
builder.Services.AddScoped<JoinRequestService>();

// Bao ve (muc 2.9, FP-01, FP-05), loi moi cho, quan he doanh nghiep - labeler (FB-23), goi y du an (FL-02).
builder.Services.AddScoped<BaoVeService>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<QuanHeLabelerService>();
builder.Services.AddScoped<GoiYService>();
builder.Services.AddHostedService<DonDepThanhVienWorker>();
builder.Services.AddScoped<DatasetService>();
builder.Services.AddScoped<GioiHanZipFilter>();
builder.Services.AddScoped<UploadService>();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
builder.Services.AddScoped<DatasetIngestor>();

// Nhap tu nguon ngoai (FB-11): HttpClient rieng di qua cua chong SSRF (ChanSsrf), bi mat S3 ma hoa tam.
builder.Services.Configure<NhapNgoaiOptions>(builder.Configuration.GetSection(NhapNgoaiOptions.SectionName));
NhapNgoaiOptions nhapNgoai = new NhapNgoaiOptions();
builder.Configuration.GetSection(NhapNgoaiOptions.SectionName).Bind(nhapNgoai);
builder.Services.AddHttpClient(NhapNguonNgoaiService.TenHttpClient)
    .ConfigurePrimaryHttpMessageHandler(() => ChanSsrf.TaoHandler(nhapNgoai.AllowedPrivateHosts));
builder.Services.AddSingleton<BiMatNguon>();
builder.Services.AddScoped<NhapNguonNgoaiService>();
builder.Services.AddHostedService<DatasetIngestWorker>();
builder.Services.AddScoped<GoldSetService>();
builder.Services.AddScoped<EntranceTestService>();
builder.Services.AddHostedService<PendingApprovalTimeoutWorker>();
builder.Services.AddScoped<ProjectSeeder>();

// ---- Kiem token (giong moi service) ----
builder.Services.AddCrowdJwtAuthentication(builder.Configuration);
builder.Services.AddCrowdInternalApi(builder.Configuration);
builder.Services.AddHttpClient<DongSoClient>();
builder.Services.AddHttpClient<IdentityClient>();

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
