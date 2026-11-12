using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using Crowd.Admin.Api.Exceptions;
using Crowd.Admin.Api.Persistence;
using Crowd.Admin.Api.Services;
using Crowd.Admin.Api.Workers;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Persistence.ReadAccess;
using Crowd.BuildingBlocks.Settings;
using Crowd.Contracts.Admin;
using Crowd.Seeding;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string chuoiKetNoi = builder.Configuration.GetConnectionString("AdminDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:AdminDb trong appsettings.");

builder.Services.AddDbContext<AdminDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// admin-svc la CHU cua setting: doc thang bang settings — bo nho duoc nap luc khoi dong va cap nhat ngay
// khi admin doi. Service khac doc view v_settings bang role settings_reader (kien truc 3.32).
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<ISettings>(sp => sp.GetRequiredService<SettingsStore>());

builder.Services.AddOutbox<AdminDbContext>(builder.Configuration);

builder.Services.AddScoped<SettingService>();

// Nhat ky audit (moi event tren bus), thong ke, thong bao toan he thong (FP-06, FM-09).
builder.Services.Configure<EventConsumerOptions>(builder.Configuration.GetSection(EventConsumerOptions.SectionName));
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AnnouncementService>();
builder.Services.AddHostedService<AuditConsumer>();
builder.Services.AddHostedService<AuditRetentionWorker>();

// Viec can xu ly tay (kien truc 3.29; hang doi duyet chung da bo o 3.33), mau bai toan (FM-03).
// Loi ky thuat (message vao DLQ): DlqMonitorWorker dem → the "Tinh trang he thong" + email doi ky thuat; chi tiet o Kibana.
builder.Services.AddScoped<ViecTayService>();
builder.Services.AddScoped<MauBaiToanService>();
builder.Services.AddScoped<TheChuDeService>();
builder.Services.AddSingleton<TrangThaiDlq>();
builder.Services.AddHttpClient("rabbitmq-management", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<DlqMonitorWorker>();

builder.Services.AddCrowdJwtAuthentication(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    AdminDbContext db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    await db.Database.MigrateAsync();

    // Khoa moi trong danh muc → them voi gia tri khoi tao. Development (bat seed) dung
    // vai gia tri de test nhanh — chi khi THEM MOI, khong ghi de gia tri admin da dat.
    Dictionary<string, JsonNode> giaTriDev = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        giaTriDev[SettingKeys.LedgerHoldDuration] = JsonValue.Create(120);
    }

    SettingService service = scope.ServiceProvider.GetRequiredService<SettingService>();
    int moi = await service.KhoiTaoAsync(giaTriDev, CancellationToken.None);

    // Role chi doc view cho service khac (settings_reader — kien truc 3.32): bat dang nhap + mat khau tu cau hinh.
    await ReaderRoles.DamBaoDangNhapAsync(
        db, app.Configuration, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ReadAccess"), CancellationToken.None);

    app.Logger.LogInformation("Setting: khoi tao {Moi} khoa moi, {Tong} khoa trong danh muc", moi, SettingCatalog.TatCa.Count);

    // Danh muc the chu de mac dinh (linh vuc / ngon ngu) — du lieu tham chieu, moi moi truong.
    int soThe = await scope.ServiceProvider.GetRequiredService<TheChuDeService>().SeedAsync(CancellationToken.None);
    app.Logger.LogInformation("Danh muc the: them {So}", soThe);

    // Mau bai toan co san (FM-03) cho moi truong dev.
    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        int soMau = await scope.ServiceProvider.GetRequiredService<MauBaiToanService>().SeedAsync(KichBanSeed.User("admin").Id, CancellationToken.None);
        app.Logger.LogInformation("Seed mau bai toan: them {So}", soMau);
    }
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
