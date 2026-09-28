using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Tasking.Infrastructure.Persistence
{
    /// <summary>CHI phuc vu `dotnet ef` sinh migration. Giong project-svc.</summary>
    public sealed class TaskDbContextFactory : IDesignTimeDbContextFactory<TaskDbContext>
    {
        private const string ChuoiKetNoiMacDinh =
            "Host=localhost;Port=5403;Database=task_db;Username=task_user;Password=dev_task_pw";

        public TaskDbContext CreateDbContext(string[] args)
        {
            string? tuBienMoiTruong = Environment.GetEnvironmentVariable("TASK_DB");

            string chuoiKetNoi;
            if (string.IsNullOrWhiteSpace(tuBienMoiTruong))
            {
                chuoiKetNoi = ChuoiKetNoiMacDinh;
            }
            else
            {
                chuoiKetNoi = tuBienMoiTruong;
            }

            DbContextOptionsBuilder<TaskDbContext> builder = new DbContextOptionsBuilder<TaskDbContext>();
            builder.UseNpgsql(chuoiKetNoi);

            return new TaskDbContext(builder.Options);
        }
    }
}
