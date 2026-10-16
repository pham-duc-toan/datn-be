using System;

namespace Crowd.Admin.Api.Entities
{
    /// <summary>
    /// Danh muc loai bai toan / template workspace (FM-03). Admin soan san: loai du lieu + tap nhan (cong cu,
    /// lop) + huong dan mau + goi y gia. Doanh nghiep chon mot mau khi tao du an roi sua tiep — frontend chep
    /// tap nhan / huong dan sang du an qua API cua project-svc (du an KHONG tham chieu mau: sua mau khong lam
    /// doi du an da tao). Tap nhan duoc kiem bang chinh bo doc LabelSchema cua project-svc.
    /// </summary>
    public sealed class MauBaiToan
    {
        public const int DoDaiMaToiDa = 60;
        public const int DoDaiTenToiDa = 120;
        public const int DoDaiMoTaToiDa = 1000;
        public const int DoDaiHuongDanToiDa = 20000;

        private MauBaiToan()
        {
            Code = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Modality = string.Empty;
            LabelSchemaJson = string.Empty;
            GuidelineMarkdown = string.Empty;
        }

        public Guid Id { get; private set; }

        /// <summary>Ma ngan, duy nhat (vd "ner-tieng-viet") — frontend dung lam slug.</summary>
        public string Code { get; private set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public string Modality { get; private set; }

        public string LabelSchemaJson { get; private set; }

        public string GuidelineMarkdown { get; private set; }

        public long? SuggestedUnitPriceVnd { get; private set; }

        public int? SuggestedRedundancy { get; private set; }

        public bool Active { get; private set; }

        /// <summary>Tang moi lan sua — frontend biet mau da doi tu lan xem truoc.</summary>
        public int Version { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public Guid UpdatedBy { get; private set; }

        public static MauBaiToan Tao(
            string code, string name, string description, string modality, string labelSchemaJson, string guideline,
            long? giaGoiY, int? redundancyGoiY, Guid adminId, DateTimeOffset luc)
        {
            MauBaiToan m = new MauBaiToan();
            m.Id = Guid.CreateVersion7();
            m.Code = code;
            m.Active = true;
            m.Version = 0;
            m.CreatedAt = luc;
            m.Sua(name, description, modality, labelSchemaJson, guideline, giaGoiY, redundancyGoiY, adminId, luc);
            return m;
        }

        public void Sua(
            string name, string description, string modality, string labelSchemaJson, string guideline,
            long? giaGoiY, int? redundancyGoiY, Guid adminId, DateTimeOffset luc)
        {
            Name = name;
            Description = description;
            Modality = modality;
            LabelSchemaJson = labelSchemaJson;
            GuidelineMarkdown = guideline;
            SuggestedUnitPriceVnd = giaGoiY;
            SuggestedRedundancy = redundancyGoiY;
            Version = Version + 1;
            UpdatedAt = luc;
            UpdatedBy = adminId;
        }

        public void DatHoatDong(bool bat, Guid adminId, DateTimeOffset luc)
        {
            if (Active == bat)
            {
                return;
            }

            Active = bat;
            Version = Version + 1;
            UpdatedAt = luc;
            UpdatedBy = adminId;
        }
    }
}
