using System;
using Crowd.Labeling;

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
    ///
    /// Dap an luu o DINH DANG NHAN CHUNG (LabelPayload): cung hinh dang voi nhan
    /// labeler nop, o moi loai bai toan — phan loai hay bounding box deu vua.
    /// Service da KIEM dap an theo tap nhan va thong tin mau (LabelPayload.Tao)
    /// truoc khi goi Tao.
    /// </summary>
    public sealed class GoldItem
    {
        // Ba truong luu xuong ba cot (expected_task_type, expected_schema_version,
        // expected_payload jsonb). Ben ngoai chi thay ExpectedPayload.
        private string _expectedTaskType;
        private int _expectedSchemaVersion;
        private string _expectedPayloadJson;

        private GoldItem()
        {
            _expectedTaskType = string.Empty;
            _expectedPayloadJson = string.Empty;
        }

        public Guid Id { get; private set; }

        public Guid ProjectId { get; private set; }

        public Guid SampleId { get; private set; }

        /// <summary>Dap an dung, dung lai tu ba cot moi lan doc (da kiem luc tao).</summary>
        public LabelPayload ExpectedPayload
        {
            get { return LabelPayload.TuLuuTru(_expectedTaskType, _expectedSchemaVersion, _expectedPayloadJson); }
        }

        public GoldPurpose Purpose { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static GoldItem Tao(
            Guid projectId,
            Guid sampleId,
            LabelPayload dapAn,
            GoldPurpose purpose,
            DateTimeOffset luc)
        {
            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            GoldItem g = new GoldItem();
            g.Id = Guid.CreateVersion7();
            g.ProjectId = projectId;
            g.SampleId = sampleId;
            g._expectedTaskType = dapAn.TaskType;
            g._expectedSchemaVersion = dapAn.SchemaVersion;
            g._expectedPayloadJson = dapAn.DataJson;
            g.Purpose = purpose;
            g.CreatedAt = luc;
            return g;
        }
    }
}
