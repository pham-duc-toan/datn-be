using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Link.Api.Persistence
{
    /// <summary>Chi dung cho `dotnet ef migrations` — khong chay luc ung dung chay.</summary>
    public sealed class LinkDbContextFactory : IDesignTimeDbContextFactory<LinkDbContext>
    {
        public LinkDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<LinkDbContext> b = new DbContextOptionsBuilder<LinkDbContext>();
            b.UseNpgsql("Host=localhost;Port=5407;Database=link_db;Username=link_user;Password=dev_link_pw");
            return new LinkDbContext(b.Options);
        }
    }
}
