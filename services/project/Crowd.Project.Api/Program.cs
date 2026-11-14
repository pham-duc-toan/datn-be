using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Auth.Internal;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Admin;
using Crowd.Contracts.Gate;
using Crowd.Contracts.Grpc.Identity;
using Crowd.Contracts.Grpc.Quality;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Media;
using Crowd.Contracts.Quality;
using Crowd.Project.Api.Consumers;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Grpc;
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
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddCrowdSettings(builder.Configuration, "project-svc");

// ---- Ha tang dung chung: outbox (GUI event) + consumer (NHAN event) ----
builder.Services.AddOutbox<ProjectDbContext>(builder.Configuration);

// Saga publish buoc 2: ledger tra loi. Ten queue giu nguyen mai mai — no la ten
// handler trong processed_events.
builder.Services.AddEventConsumer<ProjectDbContext, EscrowReserved, EscrowReservedProcessor>(
    builder.Configuration, "project-svc.escrow-reserved");
builder.Services.AddEventConsumer<ProjectDbContext, EscrowRejected, EscrowRejectedProcessor>(
    builder.Configuration, "project-svc.escrow-rejected");

// Lam bai va nhan (truoc la task-svc, annotation-svc — nay cung database): chi con nghe event cua SERVICE KHAC.
// quality-svc cham xong → ghi ket qua bai + nhan, mo han khieu nai.
builder.Services.AddEventConsumer<ProjectDbContext, ProjectGraded, ProjectGradedProcessor>(
    builder.Configuration, "project-svc.project-graded");
// Tai khoan bi khoa → huy bai dang lam. Dieu kien MO bai (khoa, cap, uy tin) hoi identity qua gRPC luc mo
// (3.24) — khong con ban sao labeler_cache, nen khong nghe user.unblocked / level.changed / reputation.changed.
builder.Services.AddEventConsumer<ProjectDbContext, UserBlocked, UserBlockedProcessor>(
    builder.Configuration, "project-svc.user-blocked");
// gate-svc: khach vuot link da gan nhan → nhan Accepted (source LinkGateway).
builder.Services.AddEventConsumer<ProjectDbContext, GateSolved, GateSolvedProcessor>(
    builder.Configuration, "project-svc.gate-solved");

// Email vua xac thuc → nhan loi moi cho gui toi email do.
builder.Services.AddEventConsumer<ProjectDbContext, UserEmailVerified, UserEmailVerifiedProcessor>(
    builder.Configuration, "project-svc.user-email-verified");

// media-svc chuan bi xong video vua nap → ghi trang thai mau; con video dang chuan bi thi chua publish / duyet.
builder.Services.AddEventConsumer<ProjectDbContext, VideoPrepared, VideoPreparedProcessor>(
    builder.Configuration, "project-svc.video-prepared");

// Admin gop the chu de → doi ma the trong nhan du an.
builder.Services.AddEventConsumer<ProjectDbContext, TagMerged, TagMergedProcessor>(
    builder.Configuration, "project-svc.tag-merged");
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
builder.Services.AddScoped<MauService>();
builder.Services.AddScoped<GioiHanZipFilter>();
builder.Services.AddScoped<UploadService>();
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
builder.Services.AddScoped<SampleIngestor>();

// Nhap tu nguon ngoai (FB-11): HttpClient rieng di qua cua chong SSRF (ChanSsrf); link S3 ky san, khong luu khoa bi mat.
builder.Services.Configure<NhapNgoaiOptions>(builder.Configuration.GetSection(NhapNgoaiOptions.SectionName));
NhapNgoaiOptions nhapNgoai = new NhapNgoaiOptions();
builder.Configuration.GetSection(NhapNgoaiOptions.SectionName).Bind(nhapNgoai);
builder.Services.AddHttpClient(NhapNguonNgoaiService.TenHttpClient)
    .ConfigurePrimaryHttpMessageHandler(() => ChanSsrf.TaoHandler(nhapNgoai.AllowedPrivateHosts));
builder.Services.AddScoped<NhapNguonNgoaiService>();
builder.Services.AddHostedService<SampleIngestWorker>();
builder.Services.AddScoped<GoldSetService>();
builder.Services.AddScoped<EntranceTestService>();
builder.Services.AddHostedService<PendingApprovalTimeoutWorker>();

// Lam bai (FL-04..FL-09) va nhan / ket qua / xuat file (FB-22, FB-25).
builder.Services.AddScoped<BaiLamRevoker>();
builder.Services.AddScoped<ThuBaiService>();
builder.Services.AddScoped<LamBaiService>();
builder.Services.AddScoped<BaoLoiService>();
builder.Services.AddScoped<DonDuAnDaDongService>();
builder.Services.AddScoped<AnnotationService>();
builder.Services.AddHostedService<BaiLamWorker>();
builder.Services.AddHostedService<ClosedProjectPurgeWorker>();
// IP that cua labeler qua X-Forwarded-For cua gateway (chi tin mang noi bo cau hinh) — chong gian lan (FM-07).
builder.Services.Configure<ForwardedHeadersOptions>(o => ProxyTinCay.CauHinh(o, builder.Configuration));

builder.Services.AddScoped<ProjectSeeder>();

// ---- Kiem token (giong moi service) ----
builder.Services.AddCrowdJwtAuthentication(builder.Configuration);
builder.Services.AddCrowdInternalApi(builder.Configuration);

// ---- gRPC noi bo ben GOI (3.24): identity (tra cuu nguoi dung luc moi / tham gia / mo bai), quality (gop nhan
// cho ket qua giao doanh nghiep — thuat toan chi con o quality-svc) ----
InternalApiOptions noiBo = new InternalApiOptions();
builder.Configuration.GetSection(InternalApiOptions.Section).Bind(noiBo);
if (string.IsNullOrWhiteSpace(noiBo.Key) || string.IsNullOrWhiteSpace(noiBo.IdentityGrpcUrl)
    || string.IsNullOrWhiteSpace(noiBo.QualityGrpcUrl))
{
    throw new InvalidOperationException("Thieu InternalApi:Key / IdentityGrpcUrl / QualityGrpcUrl trong appsettings.");
}

builder.Services.AddGrpcClient<IdentityData.IdentityDataClient>(o =>
{
    o.Address = new Uri(noiBo.IdentityGrpcUrl);
});
builder.Services.AddScoped<IdentityClient>();
// Ten doanh nghiep cho watermark noi anh / video: hoi identity, giu trong RAM (Helpers/TenDoanhNghiep.cs).
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TenDoanhNghiep>();
builder.Services.AddGrpcClient<LabelAggregation.LabelAggregationClient>(o =>
{
    o.Address = new Uri(noiBo.QualityGrpcUrl);
});
builder.Services.AddScoped<GopNhanClient>();

// ---- gRPC noi bo ben PHUC VU (quality 3.22, gate 3.23, identity + ledger 3.24): cong rieng HTTP/2
// (Kestrel:Endpoints:Grpc), khoa noi bo ----
builder.Services.AddSingleton<InternalKeyInterceptor>();
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<InternalKeyInterceptor>();
});

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

    // Role chi doc view cho service khac (gate_reader — kien truc 3.32): bat dang nhap + mat khau tu cau hinh.
    await ReaderRoles.DamBaoDangNhapAsync(
        db, app.Configuration, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReadAccess"), CancellationToken.None);

    // Du lieu mau (chi Development + Seed:Enabled). Chay TRUOC app.RunAsync nen
    // worker va consumer chua chay — khong ai chen vao giua luc seed.
    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        ProjectSeeder seeder = scope.ServiceProvider.GetRequiredService<ProjectSeeder>();
        await seeder.ChayAsync(CancellationToken.None);
    }
}

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<QualityDataService>();
app.MapGrpcService<LabelerStatsService>();
app.MapGrpcService<LedgerDataService>();

await app.RunAsync();
