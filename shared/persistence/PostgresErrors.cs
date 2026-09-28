using System;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Crowd.BuildingBlocks.Persistence
{
    /// <summary>
    /// Nhan dien loi cua Postgres ma nghiep vu phai xu ly, khong phai loi he thong.
    /// </summary>
    public static class PostgresErrors
    {
        /// <summary>Ma loi cua Postgres cho vi pham rang buoc duy nhat.</summary>
        public const string UniqueViolation = "23505";

        /// <summary>
        /// Loi co phai do dung rang buoc duy nhat khong.
        ///
        /// Day la cach CHUAN de chan TOCTOU: khong kiem "da ton tai chua" roi moi
        /// ghi, ma cu ghi, de database tu choi dong trung, roi bat loi o day.
        /// Dung o: processed_events (chong xu ly trung), users.email (hai nguoi
        /// dang ky cung email cung luc).
        /// </summary>
        public static bool IsUniqueViolation(DbUpdateException ex)
        {
            if (ex == null)
            {
                throw new ArgumentNullException(nameof(ex));
            }

            PostgresException? loiPg = ex.InnerException as PostgresException;

            if (loiPg == null)
            {
                return false;
            }

            return string.Equals(loiPg.SqlState, UniqueViolation, StringComparison.Ordinal);
        }

        /// <summary>
        /// Loi co phai do dung DUNG rang buoc duy nhat <paramref name="tenRangBuoc"/>
        /// khong. Dung khi mot SaveChanges co the vi pham NHIEU rang buoc khac nhau
        /// va moi cai phai xu ly mot kieu — xem IdempotencyGuard.
        /// </summary>
        public static bool IsUniqueViolation(DbUpdateException ex, string tenRangBuoc)
        {
            if (!IsUniqueViolation(ex))
            {
                return false;
            }

            PostgresException loiPg = (PostgresException)ex.InnerException!;
            return string.Equals(loiPg.ConstraintName, tenRangBuoc, StringComparison.Ordinal);
        }
    }
}
