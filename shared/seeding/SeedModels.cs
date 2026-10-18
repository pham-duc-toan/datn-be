using System;
using System.Collections.Generic;
using Crowd.Labeling;

namespace Crowd.Seeding
{
    // Cac lop mo ta DU LIEU cua kich ban — khong co hanh vi. Moi service doc va
    // tu dich sang entity cua minh.
    //
    // `required` + `init`: truong bat buoc phai gan luc khoi tao (quen la loi
    // bien dich), va gan xong thi khong doi duoc nua — kich ban la hang so.

    public sealed class SeedUser
    {
        /// <summary>Ten ngan de tra cuu trong kich ban: "admin", "biz1", "lab1"...</summary>
        public required string Key { get; init; }

        public required Guid Id { get; init; }

        public required string Email { get; init; }

        public required string DisplayName { get; init; }

        public required IReadOnlyList<string> Roles { get; init; }
    }

    /// <summary>Du an seed dung o buoc nao cua vong doi.</summary>
    public enum SeedStage
    {
        /// <summary>Nhap, da cau hinh du — san sang bam publish.</summary>
        Draft,

        /// <summary>Da ky quy, cho admin duyet.</summary>
        PendingApproval,

        /// <summary>Dang chay, labeler vao lam bai duoc.</summary>
        Running,

        /// <summary>
        /// Da cham xong va qua han khieu nai (Hoan thanh): moi bai trong Attempts deu da nop va co Result;
        /// ledger da tra thuong nguoi dat va hoan phan ky quy con lai.
        /// </summary>
        Completed,
    }

    public enum SeedMemberRole
    {
        Labeler,
    }

    public sealed class SeedMember
    {
        public required Guid UserId { get; init; }

        public required SeedMemberRole Role { get; init; }
    }

    /// <summary>
    /// Mot mau. File (anh PNG mot mau, am thanh WAV mot tan so) duoc SINH luc
    /// seed; text / pair thi noi dung nam san trong ContentJson.
    /// </summary>
    public sealed class SeedSample
    {
        public required Guid Id { get; init; }

        /// <summary>So thu tu trong du an, tu 1.</summary>
        public required int Index { get; init; }

        public required string FileName { get; init; }

        public required string Modality { get; init; }

        /// <summary>
        /// Khoa trong bucket datasets. Anh: "{projectId}/{sampleId}.png" (cung cong thuc
        /// Sample.TaoAnhTrongZip). Doan audio cat tu mot file: CAC DOAN DUNG CHUNG khoa.
        /// null voi text / pair.
        /// </summary>
        public string? StorageKey { get; init; }

        /// <summary>Noi dung text / pair: {"text"} hoac {"prompt","a","b"}.</summary>
        public string? ContentJson { get; init; }

        public required SampleMetadata Metadata { get; init; }

        /// <summary>Anh: mau dung (do / xanh_la ...) — dap an de doc kich ban.</summary>
        public string? TrueLabel { get; init; }

        public byte R { get; init; }

        public byte G { get; init; }

        public byte B { get; init; }

        /// <summary>Audio: tan so am (Hz) va do dai CA FILE (giay) de sinh WAV.</summary>
        public double ToneHz { get; init; }

        public double FileSeconds { get; init; }
    }

    public sealed class SeedGold
    {
        public required Guid Id { get; init; }

        public required Guid SampleId { get; init; }

        /// <summary>Dap an — phan du lieu nhan theo cac cong cu, vd {"label":{"labelIds":["do"]}}.</summary>
        public required string PayloadJson { get; init; }

        /// <summary>true = cau hoi bai test dau vao; false = cau kiem tra chat luong.</summary>
        public required bool ForEntranceTest { get; init; }
    }

    /// <summary>Mot cau tra loi trong bai lam (cau that hoac cau vang).</summary>
    public sealed class SeedAnswer
    {
        public required Guid SampleId { get; init; }

        /// <summary>Phan du lieu nhan theo cac cong cu, vd {"label":{"labelIds":["do"]}}.</summary>
        public required string PayloadJson { get; init; }

        /// <summary>Id nhan o annotation-svc (chi cau that cua bai DA NOP).</summary>
        public required Guid AnnotationId { get; init; }
    }

    public enum SeedAttemptState
    {
        /// <summary>Dang lam: cau tra loi la ban nhap (task-svc), chua co nhan o annotation-svc.</summary>
        InProgress,

        Submitted,
    }

    /// <summary>Ket qua cham mot bai (du an Completed) — cung cong thuc quality-svc.</summary>
    public sealed class SeedResult
    {
        public required int ComparableCount { get; init; }

        public required int MatchedCount { get; init; }

        public required int AgreementPercent { get; init; }

        public required bool Passed { get; init; }
    }

    /// <summary>
    /// Mot BAI LAM (docs 3.18): labeler lam het cac cau cua du an roi nop ca bai. Mot dong nay sinh du lieu
    /// o nhieu service: bai lam (task-svc), nhan (annotation-svc), ban sao cham (quality-svc), tien thuong
    /// (ledger) — tat ca dung chung cac ID ben duoi.
    /// </summary>
    public sealed class SeedAttempt
    {
        public required Guid AttemptId { get; init; }

        public required Guid ProjectId { get; init; }

        public required string LabelerKey { get; init; }

        public required Guid LabelerId { get; init; }

        public required SeedAttemptState State { get; init; }

        public required TimeSpan StartedAgo { get; init; }

        /// <summary>Nop cach "bay gio" bao lau. null khi dang lam.</summary>
        public TimeSpan? SubmittedAgo { get; init; }

        /// <summary>Cau tra loi (bai dang lam: chi cac cau da nhap nhap).</summary>
        public required IReadOnlyList<SeedAnswer> Answers { get; init; }

        public required int GoldTotal { get; init; }

        public required int GoldCorrect { get; init; }

        /// <summary>Chi du an Completed.</summary>
        public SeedResult? Result { get; init; }
    }

    public sealed class SeedProject
    {
        public required string Key { get; init; }

        public required Guid Id { get; init; }

        public required Guid OwnerId { get; init; }

        public required Guid DatasetId { get; init; }

        public required string Name { get; init; }

        public required string Description { get; init; }

        public required string GuidelineMarkdown { get; init; }

        /// <summary>image | text | audio | video | pair.</summary>
        public required string Modality { get; init; }

        /// <summary>Tap nhan (Crowd.Labeling.LabelSchema) dang JSON.</summary>
        public required string LabelSchemaJson { get; init; }

        public required SeedStage Stage { get; init; }

        public required bool IsPrivate { get; init; }

        /// <summary>Thuong cho MOT nguoi lam dat ca du an.</summary>
        public required long RewardVnd { get; init; }

        public required int Slots { get; init; }

        /// <summary>t — % cau giong da so toi thieu de dat.</summary>
        public required int PassThresholdPercent { get; init; }

        /// <summary>Deadline = 0h UTC cua ngay (hom nay + so ngay nay). Tinh theo NGAY de moi service ra cung mot gia tri.</summary>
        public required int DeadlineInDays { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required int EntranceQuestionCount { get; init; }

        public required int EntrancePassPercent { get; init; }

        public required TimeSpan CreatedAgo { get; init; }

        /// <summary>Luc bam publish va ledger giu tien. null = con Nhap.</summary>
        public TimeSpan? EscrowedAgo { get; init; }

        /// <summary>Luc admin duyet. null = chua chay.</summary>
        public TimeSpan? PublishedAgo { get; init; }

        /// <summary>Du an Completed: luc dong thu bai + cham, va luc het han khieu nai (hoan thanh).</summary>
        public TimeSpan? GradedAgo { get; init; }

        public TimeSpan? CompletedAgo { get; init; }

        public required IReadOnlyList<SeedSample> Samples { get; init; }

        public required IReadOnlyList<SeedMember> Members { get; init; }

        public required IReadOnlyList<SeedGold> Gold { get; init; }

        public required IReadOnlyList<SeedAttempt> Attempts { get; init; }

        /// <summary>Phi nen tang tren MOI nguoi dat — cung cong thuc LabelingProject.TinhPhiVnd.</summary>
        public long PlatformFeePerPassVnd
        {
            get { return RewardVnd * KichBanSeed.PhanTramPhi / 100; }
        }

        /// <summary>Ky quy = so suat x (thuong + phi) — cung cong thuc LabelingProject.ChiPhiUocTinhVnd (seed khong bat cong link).</summary>
        public long EscrowVnd
        {
            get { return Slots * (RewardVnd + PlatformFeePerPassVnd); }
        }

        /// <summary>So cau that = so mau khong phai cau vang.</summary>
        public int QuestionCount
        {
            get { return Samples.Count - Gold.Count; }
        }

        /// <summary>Da qua buoc ky quy chua (Cho duyet, Dang chay, Hoan thanh).</summary>
        public bool DaKyQuy
        {
            get { return Stage == SeedStage.PendingApproval || Stage == SeedStage.Running || Stage == SeedStage.Completed; }
        }

        /// <summary>Da tung chay (co ban sao o task / annotation / quality).</summary>
        public bool DaChay
        {
            get { return Stage == SeedStage.Running || Stage == SeedStage.Completed; }
        }

        public bool LaCauVang(Guid sampleId)
        {
            foreach (SeedGold g in Gold)
            {
                if (g.SampleId == sampleId)
                {
                    return true;
                }
            }

            return false;
        }

        public LabelSchema TapNhan()
        {
            return LabelSchema.Doc(LabelSchemaJson);
        }

        public SeedSample Mau(Guid sampleId)
        {
            foreach (SeedSample s in Samples)
            {
                if (s.Id == sampleId)
                {
                    return s;
                }
            }

            throw new InvalidOperationException("Du an seed '" + Key + "' khong co mau " + sampleId + ".");
        }

        public DateTimeOffset Deadline(DateTimeOffset bayGio)
        {
            DateTime ngay = bayGio.UtcDateTime.Date.AddDays(DeadlineInDays);
            return new DateTimeOffset(ngay, TimeSpan.Zero);
        }

        /// <summary>Luc bam publish va ledger giu tien. Goi khi du an chua ky quy la loi kich ban.</summary>
        public DateTimeOffset LucKyQuy(DateTimeOffset bayGio)
        {
            if (!EscrowedAgo.HasValue)
            {
                throw new InvalidOperationException("Du an seed '" + Key + "' chua ky quy.");
            }

            return bayGio - EscrowedAgo.Value;
        }

        /// <summary>Luc admin duyet (bat dau chay).</summary>
        public DateTimeOffset LucDuyet(DateTimeOffset bayGio)
        {
            if (!PublishedAgo.HasValue)
            {
                throw new InvalidOperationException("Du an seed '" + Key + "' chua duoc duyet.");
            }

            return bayGio - PublishedAgo.Value;
        }
    }

    /// <summary>Lenh nap tien. ProviderTxnId null = chua thanh toan (de test trang sandbox).</summary>
    public sealed class SeedDeposit
    {
        public required Guid IntentId { get; init; }

        public required Guid BusinessId { get; init; }

        public required long AmountVnd { get; init; }

        public required string IdempotencyKey { get; init; }

        public string? ProviderTxnId { get; init; }

        public required TimeSpan CreatedAgo { get; init; }

        public bool DaThanhToan
        {
            get { return ProviderTxnId != null; }
        }
    }
}
