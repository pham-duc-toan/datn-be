using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Reco.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Crowd.Reco.Infrastructure.Persistence
{
    /// <summary>Mot thay doi bo dem cua MOT labeler (gom nhieu hanh vi cung the thi cong don truoc khi ghi).</summary>
    public sealed class CongBoDem
    {
        public required Guid UserId { get; init; }

        public required string Loai { get; init; }

        public required string Ma { get; init; }

        public required double WA { get; init; }

        public required double WB { get; init; }
    }

    /// <summary>
    /// Ghi / doc bang goc labeler_affinity. Cong don chay HOAN TOAN trong SQL (mot cau cho ca lo):
    ///   a = a_cu × 0,5^(so ngay tu lan truoc / chu ky) + wA   (b tuong tu)
    /// nen nhieu instance / consumer cung cong mot the khong mat lan nao (khong doc-sua-ghi o C#).
    /// Chay trong transaction cua DbContext dang mo (consumer: cung transaction voi processed_events).
    /// </summary>
    public static class BoDemStore
    {
        private const string SqlCong = @"
INSERT INTO labeler_affinity (user_id, tag_kind, tag_code, a, b, updated_at)
SELECT u, k, c, wa, wb, @luc
FROM unnest(@users, @kinds, @codes, @was, @wbs) AS t(u, k, c, wa, wb)
ON CONFLICT (user_id, tag_kind, tag_code) DO UPDATE SET
    a = labeler_affinity.a * power(0.5, GREATEST(0, EXTRACT(EPOCH FROM (EXCLUDED.updated_at - labeler_affinity.updated_at))) / 86400.0 / @chuKy) + EXCLUDED.a,
    b = labeler_affinity.b * power(0.5, GREATEST(0, EXTRACT(EPOCH FROM (EXCLUDED.updated_at - labeler_affinity.updated_at))) / 86400.0 / @chuKy) + EXCLUDED.b,
    updated_at = GREATEST(labeler_affinity.updated_at, EXCLUDED.updated_at);";

        /// <summary>Cong mot lo thay doi (gop trung khoa truoc). Tra ve danh sach user bi doi (de xoa cache Redis).</summary>
        public static async Task<IReadOnlyCollection<Guid>> CongAsync(
            RecoDbContext db, IEnumerable<CongBoDem> thayDoi, DateTimeOffset luc, double chuKyNgay, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (thayDoi == null)
            {
                throw new ArgumentNullException(nameof(thayDoi));
            }

            Dictionary<string, CongBoDem> gop = new Dictionary<string, CongBoDem>(StringComparer.Ordinal);
            foreach (CongBoDem x in thayDoi)
            {
                if (string.IsNullOrWhiteSpace(x.Ma) || (x.WA == 0 && x.WB == 0))
                {
                    continue;
                }

                string ma = x.Ma.Trim();
                if (ma.Length > 64)
                {
                    continue;
                }

                string khoa = x.UserId.ToString("N") + "|" + x.Loai + "|" + ma;
                CongBoDem? cu;
                if (gop.TryGetValue(khoa, out cu))
                {
                    gop[khoa] = new CongBoDem { UserId = x.UserId, Loai = x.Loai, Ma = ma, WA = cu.WA + x.WA, WB = cu.WB + x.WB };
                }
                else
                {
                    gop[khoa] = new CongBoDem { UserId = x.UserId, Loai = x.Loai, Ma = ma, WA = x.WA, WB = x.WB };
                }
            }

            if (gop.Count == 0)
            {
                return Array.Empty<Guid>();
            }

            List<CongBoDem> ds = gop.Values.ToList();
            NpgsqlParameter[] thamSo = new NpgsqlParameter[]
            {
                new NpgsqlParameter("users", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ds.Select(x => x.UserId).ToArray() },
                new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ds.Select(x => x.Loai).ToArray() },
                new NpgsqlParameter("codes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ds.Select(x => x.Ma).ToArray() },
                new NpgsqlParameter("was", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = ds.Select(x => x.WA).ToArray() },
                new NpgsqlParameter("wbs", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = ds.Select(x => x.WB).ToArray() },
                new NpgsqlParameter("luc", NpgsqlDbType.TimestampTz) { Value = luc.UtcDateTime },
                new NpgsqlParameter("chuKy", NpgsqlDbType.Double) { Value = chuKyNgay <= 0 ? 1e9 : chuKyNgay },
            };

            await db.Database.ExecuteSqlRawAsync(SqlCong, thamSo, ct);
            return ds.Select(x => x.UserId).Distinct().ToList();
        }

        /// <summary>Bo dem cua mot labeler (nap Redis khi thieu).</summary>
        public static async Task<SoThich> DocAsync(RecoDbContext db, Guid userId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            List<LabelerAffinity> dong = await db.Affinities.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
            SoThich st = new SoThich();
            foreach (LabelerAffinity d in dong)
            {
                st.Dat(d.TagKind, d.TagCode, new BoDem(Math.Max(0, d.A), Math.Max(0, d.B), d.UpdatedAt));
            }

            return st;
        }
    }
}
