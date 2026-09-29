using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Ledger.Infrastructure.Persistence
{
    /// <summary>CHI phuc vu `dotnet ef` sinh migration.</summary>
    public sealed class LedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
    {
        private const string ChuoiKetNoiMacDinh =
            "Host=localhost;Port=5405;Database=ledger_db;Username=ledger_user;Password=dev_ledger_pw";

        public LedgerDbContext CreateDbContext(string[] args)
        {
            string? tuBienMoiTruong = Environment.GetEnvironmentVariable("LEDGER_DB");

            string chuoiKetNoi;
            if (string.IsNullOrWhiteSpace(tuBienMoiTruong))
            {
                chuoiKetNoi = ChuoiKetNoiMacDinh;
            }
            else
            {
                chuoiKetNoi = tuBienMoiTruong;
            }

            DbContextOptionsBuilder<LedgerDbContext> builder = new DbContextOptionsBuilder<LedgerDbContext>();
            builder.UseNpgsql(chuoiKetNoi);

            return new LedgerDbContext(builder.Options);
        }
    }
}
