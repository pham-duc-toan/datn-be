namespace Crowd.Contracts.Project
{
    /// <summary>
    /// Loai bai toan gan nhan — muc 2.2 cua dac ta. Ra JSON thanh chuoi
    /// camelCase ("imageClassification"), khong phai so thu tu, nen chen them
    /// gia tri moi khong lam lech du lieu cu.
    /// </summary>
    public enum ProjectTaskType
    {
        ImageClassification,
        BoundingBox,
        Segmentation,
        TextClassification,
        NamedEntityRecognition,
        Sentiment,
        PairwiseComparison,
        AudioTranscription,
        VideoTracking,
        LlmResponseComparison,
    }

    /// <summary>Vai tro cua mot nguoi TRONG MOT du an (khac vai tro tai khoan).</summary>
    public enum ProjectMemberRole
    {
        /// <summary>Doanh nghiep so huu du an.</summary>
        Owner,

        /// <summary>Nguoi gan nhan duoc phep nhan task cua du an.</summary>
        Labeler,

        /// <summary>Nguoi duyet nhan thay mat doanh nghiep.</summary>
        Reviewer,
    }

    /// <summary>Muc dich cua mot cau hoi vang.</summary>
    public enum GoldPurpose
    {
        /// <summary>Dung trong bai test dau vao (FB-16, FL-03).</summary>
        EntranceTest,

        /// <summary>Tron vao task that de giam sat lien tuc (FQ-04).</summary>
        QualityCheck,
    }
}
