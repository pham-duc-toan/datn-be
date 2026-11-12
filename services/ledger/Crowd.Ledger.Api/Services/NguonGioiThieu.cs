using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Crowd.Ledger.Api.Services
{
    /// <summary>
    /// Quan he gioi thieu — doc THANG tu link_db qua view v_referrals (kien truc 3.32, role ledger_reader chi SELECT
    /// duoc view nay), thay cho bang ban sao ledger.referrals cap nhat bang referral.registered. View chi tra quan he
    /// dang hieu luc (active): ho so nghi gian lan chua duyet / bi tu choi khong duoc hoa hong — luat nam o link-svc.
    /// Chi goi khi chi hoa hong (consumer click.validated / lo GateBatch), khong co request nao cho o day.
    /// </summary>
    public sealed class NguonGioiThieu : IDisposable
    {
        private readonly NpgsqlDataSource _link;

        public NguonGioiThieu(string linkRead)
        {
            if (string.IsNullOrWhiteSpace(linkRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view gioi thieu cua link-svc.", nameof(linkRead));
            }

            _link = NpgsqlDataSource.Create(linkRead);
        }

        public void Dispose()
        {
            _link.Dispose();
        }

        /// <summary>Nguoi da gioi thieu sharer nay; null = khong ai (hoac quan he chua / khong duoc duyet).</summary>
        public async Task<Guid?> NguoiGioiThieuAsync(Guid referredId, CancellationToken ct)
        {
            await using (NpgsqlCommand cmd = _link.CreateCommand("SELECT referrer_id FROM v_referrals WHERE referred_id = $1"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = referredId });
                object? kq = await cmd.ExecuteScalarAsync(ct);
                return kq == null || kq is DBNull ? null : (Guid)kq;
            }
        }
    }
}
