using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Crowd.BuildingBlocks.Persistence.ReadAccess
{
    /// <summary>
    /// ROLE CHI DOC VIEW cho service khac (kien truc 3.32): service CHU tao view (luat loc nam o phia chu) va mot
    /// role chi duoc SELECT tren dung cac view do. Service doc noi thang toi DB cua chu bang role nay de NAP BO NHO
    /// (khong phai moi request), thay cho bang ban sao cap nhat bang event.
    ///
    /// Hai buoc:
    ///   1. Migration cua chu: SqlTaoRole(role) + CREATE VIEW + GRANT SELECT — role NOLOGIN, chua co mat khau.
    ///   2. Luc khoi dong (sau MigrateAsync): DamBaoDangNhapAsync bat LOGIN + dat mat khau tu cau hinh
    ///      ReadAccess:Readers:{role} (mat khau khong nam trong migration / repo).
    /// </summary>
    public static class ReaderRoles
    {
        public const string Section = "ReadAccess:Readers";

        private static readonly Regex TenHopLe = new Regex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant);

        /// <summary>SQL cho migration: tao role NOLOGIN neu chua co (de GRANT duoc ngay trong migration).</summary>
        public static string SqlTaoRole(string role)
        {
            KiemTen(role);
            return "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '" + role + "') THEN "
                   + "CREATE ROLE " + role + " NOLOGIN; END IF; END $$;";
        }

        /// <summary>
        /// Bat LOGIN + dat mat khau cho moi role khai trong ReadAccess:Readers. Role chua co (migration chua tao) thi
        /// tao luon — GRANT tren view van do migration lam. Mat khau rong thi bo qua (dev chua cau hinh doc cheo).
        /// </summary>
        public static async Task<int> DamBaoDangNhapAsync(DbContext db, IConfiguration configuration, ILogger logger, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            int so = 0;
            foreach (IConfigurationSection muc in configuration.GetSection(Section).GetChildren())
            {
                string role = muc.Key;
                string? matKhau = muc.Value;
                if (string.IsNullOrWhiteSpace(matKhau))
                {
                    continue;
                }

                KiemTen(role);
                string mk = matKhau.Replace("'", "''", StringComparison.Ordinal);

                // DDL khong nhan tham so — ten da kiem bang regex, mat khau da thoat dau nhay.
                string sql = "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '" + role + "') THEN "
                             + "CREATE ROLE " + role + " LOGIN PASSWORD '" + mk + "'; "
                             + "ELSE ALTER ROLE " + role + " LOGIN PASSWORD '" + mk + "'; END IF; END $$;";
                await db.Database.ExecuteSqlRawAsync(sql, Array.Empty<object>(), ct);
                so++;
            }

            if (so > 0)
            {
                logger.LogInformation("Doc cheo: bat dang nhap cho {So} role chi doc view", so);
            }

            return so;
        }

        private static void KiemTen(string role)
        {
            if (string.IsNullOrWhiteSpace(role) || !TenHopLe.IsMatch(role))
            {
                throw new ArgumentException("Ten role doc cheo khong hop le: '" + role + "'.", nameof(role));
            }
        }
    }
}
