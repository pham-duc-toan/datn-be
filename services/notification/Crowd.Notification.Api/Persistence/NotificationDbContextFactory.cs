using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Notification.Api.Persistence
{
    /// <summary>Chi dung cho `dotnet ef migrations` — khong chay luc ung dung chay.</summary>
    public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
    {
        public NotificationDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<NotificationDbContext> b = new DbContextOptionsBuilder<NotificationDbContext>();
            b.UseNpgsql("Host=localhost;Port=5410;Database=notification_db;Username=notification_user;Password=dev_notification_pw");
            return new NotificationDbContext(b.Options);
        }
    }
}
