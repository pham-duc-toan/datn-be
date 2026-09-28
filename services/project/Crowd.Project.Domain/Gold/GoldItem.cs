using System;
using System.Collections.Generic;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Projects;

namespace Crowd.Project.Domain.Gold
{
    public enum GoldPurpose
    {
        /// <summary>Dung trong bai test dau vao (FB-16, FL-03).</summary>
        EntranceTest,

        /// <summary>Tron vao task that de giam sat chat luong lien tuc (FQ-04).</summary>
        QualityCheck,
    }

    /// <summary>
    /// Cau hoi vang: mot mau DA BIET dap an dung. Dung de cham labeler ma khong
    /// can nguoi duyet.
    ///
    /// Hai muc dich tach rieng: cau dung cho bai test dau vao se bi nhin thay
    /// nhieu lan (moi nguoi lam test deu thay). Neu cung cau do tron vao task
    /// that thi labeler da tung lam test se nho dap an — va cau vang mat tac dung.
    /// </summary>
    public sealed class GoldItem
    {
        private List<string> _expectedLabels;

        private GoldItem()
        {
            _expectedLabels = new List<string>();
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        public IReadOnlyList<string> ExpectedLabels
        {
            get { return _expectedLabels; }
        }

        public GoldPurpose Purpose { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static GoldItem Tao(
            Guid projectId,
            Guid sampleId,
            IReadOnlyCollection<string> expectedLabels,
            GoldPurpose purpose,
            LabelSchema schema,
            DateTimeOffset luc)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (!schema.LaBoNhanHopLe(expectedLabels))
            {
                throw new InvalidValueException(
                    "dap_an_vang_khong_hop_le",
                    "Dap an cua cau hoi vang phai la lop co trong tap nhan"
                    + (schema.AllowMultiple ? "." : ", va chi mot lop."));
            }

            GoldItem g = new GoldItem();
            g.Id = Guid.CreateVersion7();
            g.ProjectId = projectId;
            g.SampleId = sampleId;
            g._expectedLabels = new List<string>(expectedLabels);
            g.Purpose = purpose;
            g.CreatedAt = luc;
            return g;
        }
    }
}
