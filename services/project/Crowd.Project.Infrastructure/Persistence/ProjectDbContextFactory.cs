using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Project.Infrastructure.Persistence
{
    /// <summary>
    /// CHI phuc vu cong cu `dotnet ef` (sinh migration). Khong chay luc ung dung
    /// chay — luc do chuoi ket noi lay tu appsettings. Giong annotation-svc.
    /// </summary>
    public sealed class ProjectDbContextFactory : IDesignTimeDbContextFactory<ProjectDbContext>
    {
        private const string ChuoiKetNoiMacDinh =
            "Host=localhost;Port=5402;Database=project_db;" +
            "Username=project_user;Password=dev_project_pw";

        public ProjectDbContext CreateDbContext(string[] args)
        {
            string? tuBienMoiTruong = Environment.GetEnvironmentVariable("PROJECT_DB");

            string chuoiKetNoi;
            if (string.IsNullOrWhiteSpace(tuBienMoiTruong))
            {
                chuoiKetNoi = ChuoiKetNoiMacDinh;
            }
            else
            {
                chuoiKetNoi = tuBienMoiTruong;
            }

            DbContextOptionsBuilder<ProjectDbContext> builder = new DbContextOptionsBuilder<ProjectDbContext>();
            builder.UseNpgsql(chuoiKetNoi);

            return new ProjectDbContext(builder.Options);
        }
    }
}
