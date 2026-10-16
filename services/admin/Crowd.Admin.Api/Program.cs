using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using Crowd.Admin.Api.Consumers;
using Crowd.Admin.Api.Exceptions;
using Crowd.Admin.Api.Persistence;
using Crowd.Admin.Api.Services;
using Crowd.Admin.Api.Workers;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
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

// admin-svc la CHU cua setting: doc thang bang settings (khong co ban sao) — bo nho
// duoc nap luc khoi dong va cap nhat ngay khi admin doi.
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<ISettings>(sp => sp.GetRequiredService<SettingsStore>());

builder.Services.AddOutbox<AdminDbContext>(builder.Configuration);
builder.Services.AddEventConsumer<AdminDbContext, SettingsSnapshotRequested, SnapshotRequestedProcessor>(
    builder.Configuration, "admin-svc.settings-snapshot-requested");

builder.Services.AddScoped<SettingService>();

// Nhat ky audit (moi event tren bus), thong ke, thong bao toan he thong (FP-06, FM-09).
builder.Services.Configure<EventConsumerOptions>(builder.Configuration.GetSection(EventConsumerOptions.SectionName));
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AnnouncementService>();
builder.Services.AddHostedService<AuditConsumer>();
builder.Services.AddHostedService<AuditRetentionWorker>();
builder.Services.AddHostedService<SnapshotWorker>();

// Hang doi duyet chung (read model tu event), mau bai toan (FM-03), canh bao do sau DLQ (VD-D-06/12).
builder.Services.AddScoped<HangDoiDuyet>();
builder.Services.AddScoped<MauBaiToanService>();
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

    // Phat toan bo luc khoi dong: service nao dang cho thi dong bo ngay.
    await service.PhatSnapshotAsync(Guid.CreateVersion7(), null, true, CancellationToken.None);

    app.Logger.LogInformation("Setting: khoi tao {Moi} khoa moi, {Tong} khoa trong danh muc", moi, SettingCatalog.TatCa.Count);

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
