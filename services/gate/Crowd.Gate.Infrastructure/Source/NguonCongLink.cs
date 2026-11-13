using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Gate.Domain;
using Crowd.Labeling;
using Npgsql;

namespace Crowd.Gate.Infrastructure.Source
{
    /// <summary>
    /// Doc THANG du lieu cong link tu DB cua chu du lieu (kien truc 3.32) — thay cho cac bang ban sao gate_* cap nhat
    /// bang event. Moi nguon la mot connection string voi role gate_reader: role nay chi SELECT duoc cac view v_gate_*
    /// do chinh service chu tao (luat loc nam o phia chu, gate khong biet bang that).
    ///
    /// Chi GateCatalog goi (nap bo nho luc khoi dong + dinh ky) — request /g, /go KHONG BAO GIO cham toi day.
    /// </summary>
    public sealed class NguonCongLink : IDisposable
    {
        private readonly NpgsqlDataSource _project;
        private readonly NpgsqlDataSource _ledger;
        private readonly NpgsqlDataSource _link;

        public NguonCongLink(string projectRead, string ledgerRead, string linkRead)
        {
            if (string.IsNullOrWhiteSpace(projectRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view cua project-svc.", nameof(projectRead));
            }

            if (string.IsNullOrWhiteSpace(ledgerRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view cua ledger-svc.", nameof(ledgerRead));
            }

            if (string.IsNullOrWhiteSpace(linkRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view cua link-svc.", nameof(linkRead));
            }

            _project = NpgsqlDataSource.Create(projectRead);
            _ledger = NpgsqlDataSource.Create(ledgerRead);
            _link = NpgsqlDataSource.Create(linkRead);
        }

        public void Dispose()
        {
            _project.Dispose();
            _ledger.Dispose();
            _link.Dispose();
        }

        /// <summary>Du an dang len cong (da publish, bat cong link, chua dong).</summary>
        public async Task<List<DuAnCong>> DocDuAnAsync(CancellationToken ct)
        {
            List<DuAnCong> ds = new List<DuAnCong>();
            await using (NpgsqlCommand cmd = _project.CreateCommand(
                "SELECT project_id, owner_id, modality, label_schema::text, unit_price_vnd, platform_fee_vnd, status FROM v_gate_projects"))
            await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    TrangThaiDuAnCong trangThai = r.GetString(6) == "Paused" ? TrangThaiDuAnCong.Paused : TrangThaiDuAnCong.Running;
                    ds.Add(new DuAnCong(
                        r.GetGuid(0),
                        r.GetGuid(1),
                        r.GetString(2),
                        r.IsDBNull(3) ? "{}" : r.GetString(3),
                        r.GetInt64(4),
                        r.GetInt64(5),
                        trangThai));
                }
            }

            return ds;
        }

        /// <summary>Mau + cau vang kiem tra cua MOT du an (sau publish khong doi — moi du an nap mot lan).</summary>
        public async Task<NoiDungDuAn> DocNoiDungAsync(Guid projectId, CancellationToken ct)
        {
            List<MauCong> mau = new List<MauCong>();
            await using (NpgsqlCommand cmd = _project.CreateCommand(
                "SELECT sample_id, modality, storage_key, content::text, metadata::text, question FROM v_gate_samples WHERE project_id = $1"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = projectId });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        mau.Add(new MauCong(
                            r.GetGuid(0),
                            projectId,
                            r.GetString(1),
                            r.IsDBNull(2) ? null : r.GetString(2),
                            r.IsDBNull(3) ? null : RawJson.Tu(r.GetString(3)),
                            RawJson.Tu(r.GetString(4)),
                            r.IsDBNull(5) ? null : r.GetString(5)));
                    }
                }
            }

            List<CauVangCong> vang = new List<CauVangCong>();
            await using (NpgsqlCommand cmd = _project.CreateCommand(
                "SELECT sample_id, task_type, schema_version, expected_payload::text FROM v_gate_gold WHERE project_id = $1"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = projectId });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        vang.Add(new CauVangCong(
                            r.GetGuid(0),
                            projectId,
                            LabelPayload.TuLuuTru(r.GetString(1), r.GetInt32(2), r.GetString(3))));
                    }
                }
            }

            return new NoiDungDuAn(mau, vang);
        }

        /// <summary>Ngan sach cong link con lai (ledger tinh trong view) cua cac du an can.</summary>
        public async Task<Dictionary<Guid, long>> DocNganSachAsync(IReadOnlyList<Guid> projectIds, CancellationToken ct)
        {
            Dictionary<Guid, long> d = new Dictionary<Guid, long>();
            if (projectIds.Count == 0)
            {
                return d;
            }

            Guid[] ids = new Guid[projectIds.Count];
            for (int i = 0; i < projectIds.Count; i++)
            {
                ids[i] = projectIds[i];
            }

            await using (NpgsqlCommand cmd = _ledger.CreateCommand(
                "SELECT project_id, remaining_vnd FROM v_gate_budgets WHERE project_id = ANY($1)"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = ids });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        d[r.GetGuid(0)] = r.GetInt64(1);
                    }
                }
            }

            return d;
        }

        /// <summary>Link dang chay (Active).</summary>
        public async Task<List<LinkCong>> DocLinkAsync(CancellationToken ct)
        {
            List<LinkCong> ds = new List<LinkCong>();
            await using (NpgsqlCommand cmd = _link.CreateCommand(
                "SELECT link_id, code, owner_id, destination_url, password_hash, expires_at, campaign_id, creator_ip_hash FROM v_gate_links"))
            await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    ds.Add(new LinkCong(
                        r.GetGuid(0),
                        r.GetString(1),
                        r.GetGuid(2),
                        r.GetString(3),
                        r.IsDBNull(4) ? null : r.GetString(4),
                        r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
                        r.IsDBNull(6) ? null : r.GetGuid(6),
                        r.IsDBNull(7) ? null : r.GetString(7)));
                }
            }

            return ds;
        }
    }

    /// <summary>Mau + cau vang cua mot du an.</summary>
    public sealed class NoiDungDuAn
    {
        public NoiDungDuAn(List<MauCong> mau, List<CauVangCong> vang)
        {
            if (mau == null)
            {
                throw new ArgumentNullException(nameof(mau));
            }

            if (vang == null)
            {
                throw new ArgumentNullException(nameof(vang));
            }

            Mau = mau;
            Vang = vang;
        }

        public List<MauCong> Mau { get; }

        public List<CauVangCong> Vang { get; }
    }
}
