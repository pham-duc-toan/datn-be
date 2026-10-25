using System;
using Crowd.Tasking.Domain.Common;

namespace Crowd.Tasking.Domain.Attempts
{
    /// <summary>
    /// Mot cau tra loi trong bai lam — MOT dong moi (bai, cau) de luu nhap tung cau rat nhe (upsert mot
    /// dong, khong ghi lai ca bai). Ban nhap la JSON tho (chua kiem luat); kiem day du khi nop.
    /// </summary>
    public sealed class AttemptAnswer
    {
        /// <summary>Mot cau toi da 256 KB — du cho hang tram khung / doan, chan nhoi rac vao DB.</summary>
        public const int KichThuocToiDa = 256 * 1024;

        private AttemptAnswer()
        {
            DataJson = "{}";
        }

        public Guid AttemptId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>Phan du lieu nhan theo cac cong cu, vd {"label":{"labelIds":["do"]}}.</summary>
        public string DataJson { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static AttemptAnswer Tao(Guid attemptId, Guid sampleId, string dataJson, DateTimeOffset luc)
        {
            AttemptAnswer a = new AttemptAnswer();
            a.AttemptId = attemptId;
            a.SampleId = sampleId;
            a.Ghi(dataJson, luc);
            return a;
        }

        public void Ghi(string dataJson, DateTimeOffset luc)
        {
            if (string.IsNullOrWhiteSpace(dataJson))
            {
                throw new ArgumentException("Thieu du lieu.", nameof(dataJson));
            }

            if (System.Text.Encoding.UTF8.GetByteCount(dataJson) > KichThuocToiDa)
            {
                throw new InvalidValueException("cau_tra_loi_qua_lon", "Mỗi câu trả lời tối đa " + (KichThuocToiDa / 1024) + " KB.");
            }

            DataJson = dataJson;
            UpdatedAt = luc;
        }
    }
}
