using System;
using System.Threading;
using Crowd.BuildingBlocks.Auth.Jwt;
using Crowd.BuildingBlocks.Persistence.Consumers;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Identity;
using Crowd.Contracts.Ledger;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Notification.Api.Consumers;
using Crowd.Notification.Api.Exceptions;
using Crowd.Notification.Api.Persistence;
using Crowd.Notification.Api.Seeding;
using Crowd.Notification.Api.Services;
using Crowd.Notification.Api.Workers;
using Crowd.Seeding;
using Crowd.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// notification-svc (FC-06): nghe event → thong bao trong ung dung + email (SMTP, dev: Mailpit).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigurationManager cfg = builder.Configuration;

string chuoiKetNoi = cfg.GetConnectionString("NotificationDb")
    ?? throw new InvalidOperationException("Thieu ConnectionStrings:NotificationDb trong appsettings.");

builder.Services.AddDbContext<NotificationDbContext>(options =>
{
    options.UseNpgsql(chuoiKetNoi);
});

// Setting dong (admin-svc quan ly): GOI TRUOC AddOutbox de nap xong truoc moi worker.
builder.Services.AddCrowdSettings<NotificationDbContext>(cfg, "notification-svc");
builder.Services.AddOutbox<NotificationDbContext>(cfg);

// ---- Ban sao (khong tao thong bao) ----
builder.Services.AddEventConsumer<NotificationDbContext, UserRegistered, UserRegisteredProcessor>(cfg, "notification-svc.user-registered");
builder.Services.AddEventConsumer<NotificationDbContext, ProjectPublishRequested, PublishRequestedProcessor>(cfg, "notification-svc.project-publish-requested");
builder.Services.AddEventConsumer<NotificationDbContext, PayoutRequested, PayoutRequestedProcessor>(cfg, "notification-svc.payout-requested");

// ---- Tai khoan, KYB ----
builder.Services.AddEventConsumer<NotificationDbContext, BusinessVerified, BusinessVerifiedProcessor>(cfg, "notification-svc.business-verified");
builder.Services.AddEventConsumer<NotificationDbContext, BusinessKybRejected, BusinessKybRejectedProcessor>(cfg, "notification-svc.business-kyb-rejected");
builder.Services.AddEventConsumer<NotificationDbContext, UserBlocked, UserBlockedProcessor>(cfg, "notification-svc.user-blocked");
builder.Services.AddEventConsumer<NotificationDbContext, UserUnblocked, UserUnblockedProcessor>(cfg, "notification-svc.user-unblocked");

// ---- Doanh nghiep: tien, du an ----
builder.Services.AddEventConsumer<NotificationDbContext, DepositConfirmed, DepositConfirmedProcessor>(cfg, "notification-svc.deposit-confirmed");
builder.Services.AddEventConsumer<NotificationDbContext, ProjectPublished, ProjectPublishedProcessor>(cfg, "notification-svc.project-published");
builder.Services.AddEventConsumer<NotificationDbContext, EscrowRejected, EscrowRejectedProcessor>(cfg, "notification-svc.escrow-rejected");
builder.Services.AddEventConsumer<NotificationDbContext, ProjectCancelled, ProjectCancelledProcessor>(cfg, "notification-svc.project-cancelled");
builder.Services.AddEventConsumer<NotificationDbContext, EscrowReleased, EscrowReleasedProcessor>(cfg, "notification-svc.escrow-released");
builder.Services.AddEventConsumer<NotificationDbContext, RefundIssued, RefundIssuedProcessor>(cfg, "notification-svc.refund-issued");

// ---- Labeler: nhan, tien ----
builder.Services.AddEventConsumer<NotificationDbContext, AnnotationApproved, AnnotationApprovedProcessor>(cfg, "notification-svc.annotation-approved");
builder.Services.AddEventConsumer<NotificationDbContext, AnnotationRejected, AnnotationRejectedProcessor>(cfg, "notification-svc.annotation-rejected");
builder.Services.AddEventConsumer<NotificationDbContext, HoldExpired, HoldExpiredProcessor>(cfg, "notification-svc.hold-expired");
builder.Services.AddEventConsumer<NotificationDbContext, PayoutCompleted, PayoutCompletedProcessor>(cfg, "notification-svc.payout-completed");
builder.Services.AddEventConsumer<NotificationDbContext, PayoutFailed, PayoutFailedProcessor>(cfg, "notification-svc.payout-failed");
builder.Services.AddEventConsumer<NotificationDbContext, WithdrawalRejected, WithdrawalRejectedProcessor>(cfg, "notification-svc.withdrawal-rejected");

// ---- Gui email ----
builder.Services.Configure<SmtpOptions>(cfg.GetSection(SmtpOptions.SectionName));
builder.Services.AddSingleton<IGuiEmail, SmtpGuiEmail>();
builder.Services.AddHostedService<EmailWorker>();

builder.Services.AddScoped<ThongBaoWriter>();
builder.Services.AddScoped<ThongBaoService>();
builder.Services.AddScoped<NotificationSeeder>();

builder.Services.AddCrowdJwtAuthentication(cfg);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    NotificationDbContext db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.MigrateAsync();

    if (SeedSwitch.DuocChay(app.Environment, app.Configuration))
    {
        await scope.ServiceProvider.GetRequiredService<NotificationSeeder>().ChayAsync(CancellationToken.None);
    }
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
