using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Crowd.Notification.Api.Services
{
    /// <summary>Dia chi lien lac cua mot nguoi dung — doc tu view v_contacts cua identity, KHONG luu o notification.</summary>
    public sealed class LienHe
    {
        public LienHe(Guid userId, string email, string displayName)
        {
            if (email == null)
            {
                throw new ArgumentNullException(nameof(email));
            }

            if (displayName == null)
            {
                throw new ArgumentNullException(nameof(displayName));
            }

            UserId = userId;
            Email = email;
            DisplayName = displayName;
        }

        public Guid UserId { get; }

        public string Email { get; }

        public string DisplayName { get; }
    }

    /// <summary>
    /// Doc THANG dia chi lien lac tu identity_db qua view v_contacts (kien truc 3.32, role notification_reader chi
    /// SELECT duoc view nay) — thay cho bang ban sao contacts cap nhat bang user.registered. Chi viec nen dung
    /// (gui email theo lo, thong bao toan he thong), khong co request nao cho o day. Email / ten / vai tro luon
    /// la gia tri MOI NHAT cua identity (ban sao cu chi cap nhat luc dang ky).
    /// </summary>
    public sealed class NguonLienHe : IDisposable
    {
        private readonly NpgsqlDataSource _identity;

        public NguonLienHe(string identityRead)
        {
            if (string.IsNullOrWhiteSpace(identityRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view lien lac cua identity-svc.", nameof(identityRead));
            }

            _identity = NpgsqlDataSource.Create(identityRead);
        }

        public void Dispose()
        {
            _identity.Dispose();
        }

        /// <summary>Lien lac cua cac nguoi dung (ai khong con thi khong co trong ket qua).</summary>
        public async Task<Dictionary<Guid, LienHe>> LayAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
        {
            Dictionary<Guid, LienHe> d = new Dictionary<Guid, LienHe>();
            if (userIds == null || userIds.Count == 0)
            {
                return d;
            }

            await using (NpgsqlCommand cmd = _identity.CreateCommand(
                "SELECT user_id, email, display_name FROM v_contacts WHERE user_id = ANY($1)"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = userIds.ToArray() });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        d[r.GetGuid(0)] = new LienHe(r.GetGuid(0), r.GetString(1), r.GetString(2));
                    }
                }
            }

            return d;
        }

        /// <summary>Nguoi nhan thong bao toan he thong: vaiTro null = moi nguoi; khac = ai co vai tro do.</summary>
        public async Task<List<Guid>> NguoiNhanAsync(string? vaiTro, CancellationToken ct)
        {
            List<Guid> ds = new List<Guid>();
            await using (NpgsqlCommand cmd = _identity.CreateCommand(vaiTro == null
                ? "SELECT user_id FROM v_contacts"
                : "SELECT user_id FROM v_contacts WHERE $1 = ANY(roles)"))
            {
                if (vaiTro != null)
                {
                    cmd.Parameters.Add(new NpgsqlParameter { Value = vaiTro });
                }

                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        ds.Add(r.GetGuid(0));
                    }
                }
            }

            return ds;
        }
    }
}
