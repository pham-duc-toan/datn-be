using System;
using System.Collections.Generic;
using System.Reflection;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Gate;

namespace Crowd.Admin.Api.Services
{
    /// <summary>
    /// Moi loai event cua he thong (quet IEventPayload trong Crowd.Contracts) — lua chon cua setting
    /// admin.audit_excluded_types tren man hinh admin. Catalog setting nam o building-blocks (khong tham chieu
    /// Crowd.Contracts) nen danh sach duoc dien o day.
    /// </summary>
    public static class LoaiEventHeThong
    {
        private static readonly Lazy<IReadOnlyList<string>> DanhSach = new Lazy<IReadOnlyList<string>>(Quet);

        public static IReadOnlyList<string> TatCa
        {
            get { return DanhSach.Value; }
        }

        private static List<string> Quet()
        {
            SortedSet<string> ds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Type t in typeof(GateSolved).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(IEventPayload).IsAssignableFrom(t))
                {
                    continue;
                }

                PropertyInfo? p = t.GetProperty("EventType", BindingFlags.Public | BindingFlags.Static);
                string? loai = p == null ? null : p.GetValue(null) as string;
                if (!string.IsNullOrEmpty(loai))
                {
                    ds.Add(loai);
                }
            }

            return new List<string>(ds);
        }
    }
}
