using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Crowd.Settings
{
    /// <summary>
    /// Doc setting THANG tu admin_db qua view v_settings (kien truc 3.32) bang role settings_reader — role nay chi
    /// SELECT duoc view do. Thay cho bang settings_replica o moi service + event setting.changed / settings.snapshot.
    /// Chi warm-up va worker nap lai goi; code nghiep vu doc ISettings (bo nho).
    /// </summary>
    public sealed class SettingsSource : IDisposable
    {
        private readonly NpgsqlDataSource _admin;

        public SettingsSource(string settingsRead)
        {
            if (string.IsNullOrWhiteSpace(settingsRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view setting cua admin-svc.", nameof(settingsRead));
            }

            _admin = NpgsqlDataSource.Create(settingsRead);
        }

        public void Dispose()
        {
            _admin.Dispose();
        }

        /// <summary>Nap moi setting vao bo nho (version moi hon moi ghi). Tra so dong doc duoc.</summary>
        public async Task<int> NapAsync(SettingsStore store, CancellationToken ct)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            int so = 0;
            await using (NpgsqlCommand cmd = _admin.CreateCommand("SELECT key, value::text, version FROM v_settings"))
            await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    store.ApDung(r.GetString(0), r.GetString(1), r.GetInt64(2));
                    so++;
                }
            }

            return so;
        }
    }
}
