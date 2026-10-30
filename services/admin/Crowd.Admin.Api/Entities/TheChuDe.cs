using System;

namespace Crowd.Admin.Api.Entities
{
    /// <summary>
    /// The chu de (linh vuc / ngon ngu) — danh muc admin quan ly. Labeler chon the cho ho so ky nang, doanh nghiep
    /// chon the cho du an; goi y du an khop theo MA the (Crowd.BuildingBlocks.Tags.MaThe). Ma khong doi sau khi tao:
    /// doi ten chi doi ten hien thi. An = khong cho chon moi (noi da gan van giu). Gop = the nay het dung, moi noi
    /// dang gan duoc doi sang the dich (event tag.merged).
    /// </summary>
    public sealed class TheChuDe
    {
        public const int DoDaiTenToiDa = 60;

        private TheChuDe()
        {
            Kind = string.Empty;
            Code = string.Empty;
            Name = string.Empty;
        }

        public Guid Id { get; private set; }

        /// <summary>domain | language.</summary>
        public string Kind { get; private set; }

        /// <summary>Duy nhat trong cung loai.</summary>
        public string Code { get; private set; }

        public string Name { get; private set; }

        public bool Hidden { get; private set; }

        /// <summary>Ma the dich khi da gop; null = chua gop.</summary>
        public string? MergedInto { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public Guid? UpdatedBy { get; private set; }

        public bool DaGop
        {
            get { return MergedInto != null; }
        }

        public static TheChuDe Tao(string kind, string code, string name, Guid? adminId, DateTimeOffset luc)
        {
            TheChuDe t = new TheChuDe();
            t.Id = Guid.CreateVersion7();
            t.Kind = kind;
            t.Code = code;
            t.Name = name;
            t.CreatedAt = luc;
            t.UpdatedAt = luc;
            t.UpdatedBy = adminId;
            return t;
        }

        public void DoiTen(string name, Guid adminId, DateTimeOffset luc)
        {
            Name = name;
            UpdatedAt = luc;
            UpdatedBy = adminId;
        }

        public void DatAn(bool an, Guid adminId, DateTimeOffset luc)
        {
            Hidden = an;
            UpdatedAt = luc;
            UpdatedBy = adminId;
        }

        public void GopVao(string maDich, Guid adminId, DateTimeOffset luc)
        {
            MergedInto = maDich;
            Hidden = true;
            UpdatedAt = luc;
            UpdatedBy = adminId;
        }
    }
}
