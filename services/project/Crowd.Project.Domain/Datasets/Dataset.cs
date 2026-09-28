using System;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Datasets
{
    /// <summary>
    /// Mot lan nap du lieu (FB-11): mot file ZIP tro thanh nhieu Sample.
    /// Du an co the nap nhieu dataset khi con Nhap.
    /// </summary>
    public sealed class Dataset
    {
        public const int DoDaiTenToiDa = 200;

        private Dataset()
        {
            Name = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public string Name { get; private set; }

        public int SampleCount { get; private set; }

        /// <summary>So file bi bo qua: sai dinh dang, qua lon, hoac trung anh da co.</summary>
        public int SkippedCount { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static Dataset Tao(Guid projectId, string name, DateTimeOffset luc)
        {
            string ten = name == null ? string.Empty : name.Trim();
            if (ten.Length == 0 || ten.Length > DoDaiTenToiDa)
            {
                throw new InvalidValueException(
                    "ten_dataset_khong_hop_le",
                    "Ten dataset phai tu 1 den " + DoDaiTenToiDa + " ky tu.");
            }

            Dataset d = new Dataset();
            d.Id = Guid.CreateVersion7();
            d.ProjectId = projectId;
            d.Name = ten;
            d.CreatedAt = luc;
            return d;
        }

        public void GhiNhanKetQuaNap(int soMau, int soBoQua)
        {
            if (soMau <= 0)
            {
                throw new RuleViolationException(
                    "khong_co_anh_hop_le",
                    "File ZIP khong co anh hop le nao (chi nhan jpg, png, webp).");
            }

            SampleCount = soMau;
            SkippedCount = soBoQua;
        }
    }
}
