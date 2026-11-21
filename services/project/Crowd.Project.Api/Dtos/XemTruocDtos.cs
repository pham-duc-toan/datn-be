using System;
using System.Collections.Generic;
using Crowd.Labeling;

namespace Crowd.Project.Api.Dtos
{
    // ---- Mau xem truoc tren cho du an (P1, kien truc 3.39) ----

    /// <summary>PUT /projects/{id}/preview — bat / tat mau xem truoc (he thong tu lay MOT mau, khong chon tay).</summary>
    public sealed class SetPreviewRequest
    {
        public bool Enabled { get; init; }
    }

    public sealed class PreviewSettingResponse
    {
        /// <summary>Doanh nghiep bat xem truoc.</summary>
        public required bool Enabled { get; init; }

        /// <summary>Ma ly do du an KHONG duoc hien mau (NDA / nhay cam / rieng tu); null = duoc.</summary>
        public string? BlockedReason { get; init; }

        /// <summary>Mau THAT SU hien tren the cho (Enabled va khong bi chan).</summary>
        public required bool Showing { get; init; }
    }

    // ---- "Xem nhu labeler" bang du lieu that (Q2, kien truc 3.39) ----

    /// <summary>
    /// GET /projects/{id}/trial — bai XEM THU dung y bai that (cung tap nhan, mau that, cau vang kiem tra xen vao, thu tu
    /// xao) nhung KHONG ghi DB: khong giu suat, khong tinh nguoi lam, khong hien voi ai khac.
    /// </summary>
    public sealed class TrialAttemptResponse
    {
        public required Guid ProjectId { get; init; }

        public required RawJson LabelSchema { get; init; }

        public required int SchemaVersion { get; init; }

        public required long RewardVnd { get; init; }

        public required int PassThresholdPercent { get; init; }

        public required bool SensitiveContent { get; init; }

        /// <summary>So cau cua bai that (cau that + cau vang) — bai xem thu chi lay mot phan.</summary>
        public required int RealQuestionCount { get; init; }

        /// <summary>Mau he thong lay lam mau xem truoc tren the cho (mau that tao som nhat) — co trong Questions. null = chua co mau.</summary>
        public Guid? PreviewSampleId { get; init; }

        public required int QuestionCount { get; init; }

        /// <summary>Cung dinh dang cau hoi bai that. Answer luon null.</summary>
        public required IReadOnlyList<AttemptQuestion> Questions { get; init; }
    }

    /// <summary>Ket qua mot cau vang trong bai xem thu (chu du an tu dat dap an nen duoc xem).</summary>
    public sealed class TrialGoldResult
    {
        public required Guid SampleId { get; init; }

        /// <summary>false khi bo trong.</summary>
        public required bool Answered { get; init; }

        public required bool Correct { get; init; }

        public required RawJson Expected { get; init; }
    }

    /// <summary>POST /projects/{id}/trial/check — cham NGAY cau vang cua bai xem thu, cung luat voi luc nop bai that.</summary>
    public sealed class TrialCheckResponse
    {
        public required int GoldTotal { get; init; }

        public required int GoldCorrect { get; init; }

        public required int AnsweredCount { get; init; }

        /// <summary>Cau sai dinh dang (giong loi 400 bai_chua_hop_le cua bai that).</summary>
        public required IReadOnlyList<InvalidAnswer> InvalidAnswers { get; init; }

        public required IReadOnlyList<TrialGoldResult> Gold { get; init; }

        /// <summary>Bai that cung cac cau tra loi nay co bi truot vi sai cau kiem tra khong.</summary>
        public required bool WouldFailGold { get; init; }
    }
}
