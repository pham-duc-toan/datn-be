using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Crowd.Identity.Api.Services
{
    /// <summary>
    /// Diem uy tin cua labeler — doc THANG tu quality_db qua view v_reputations (kien truc 3.32, role identity_reader
    /// chi SELECT duoc view nay). Thay cho cot ban sao labeler_stats.reputation cap nhat bang reputation.changed:
    /// quality la chu, identity luon doc gia tri moi nhat (tinh cap, tra cuu cho project, "cap do cua toi").
    /// Labeler chua co dong = quality chua cham bai nao cua ho (uy tin null).
    /// </summary>
    public sealed class NguonUyTin : IDisposable
    {
        private readonly NpgsqlDataSource _quality;

        public NguonUyTin(string qualityRead)
        {
            if (string.IsNullOrWhiteSpace(qualityRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view uy tin cua quality-svc.", nameof(qualityRead));
            }

            _quality = NpgsqlDataSource.Create(qualityRead);
        }

        public void Dispose()
        {
            _quality.Dispose();
        }

        /// <summary>Uy tin cua cac labeler (ai chua co thi khong co trong ket qua). DB quality loi → nem ra.</summary>
        public async Task<Dictionary<Guid, int>> LayAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
        {
            Dictionary<Guid, int> d = new Dictionary<Guid, int>();
            if (userIds == null || userIds.Count == 0)
            {
                return d;
            }

            await using (NpgsqlCommand cmd = _quality.CreateCommand(
                "SELECT labeler_id, reputation FROM v_reputations WHERE labeler_id = ANY($1)"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = userIds.ToArray() });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        d[r.GetGuid(0)] = r.GetInt32(1);
                    }
                }
            }

            return d;
        }

        public async Task<int?> CuaMotNguoiAsync(Guid userId, CancellationToken ct)
        {
            Dictionary<Guid, int> d = await LayAsync(new[] { userId }, ct);
            int uyTin;
            return d.TryGetValue(userId, out uyTin) ? uyTin : null;
        }
    }
}
