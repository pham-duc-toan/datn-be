using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Admin.Api.Persistence
{
    /// <summary>Chi dung cho `dotnet ef migrations` — khong chay luc ung dung chay.</summary>
    public sealed class AdminDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext>
    {
        public AdminDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<AdminDbContext> b = new DbContextOptionsBuilder<AdminDbContext>();
            b.UseNpgsql("Host=localhost;Port=5411;Database=admin_db;Username=admin_user;Password=dev_admin_pw");
            return new AdminDbContext(b.Options);
        }
    }
}
