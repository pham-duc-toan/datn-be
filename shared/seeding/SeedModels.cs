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

        /// <summary>Dang chay, labeler nhan task duoc.</summary>
        Running,
    }

    public enum SeedMemberRole
    {
        Labeler,
        Reviewer,
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

    /// <summary>Ket qua duyet cua mot luot nop.</summary>
    public enum SeedReview
    {
        PendingReview,
        Approved,
        Rejected,

        /// <summary>Bi tu choi roi labeler khieu nai — cho admin phan xu.</summary>
        Appealed,
    }

    /// <summary>
    /// Mot luot labeler nop nhan. Mot dong nay sinh ra du lieu o BA service:
    /// assignment (task-svc), annotation (annotation-svc), va neu duoc duyet thi
    /// but toan chi tra (ledger-svc) — ca ba dung chung cac ID ben duoi.
    /// </summary>
    public sealed class SeedSubmission
    {
        public required Guid AssignmentId { get; init; }

        public required Guid TaskId { get; init; }

        public required Guid AnnotationId { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        public string? StorageKey { get; init; }

        public required Guid LabelerId { get; init; }

        /// <summary>Nhan da nop — phan du lieu theo cac cong cu cua tap nhan.</summary>
        public required string PayloadJson { get; init; }

        public required SeedReview Review { get; init; }

        /// <summary>Nguoi duyet (chu du an hoac reviewer). null khi con cho duyet.</summary>
        public Guid? ReviewerId { get; init; }

        public string? RejectReason { get; init; }

        public string? AppealMessage { get; init; }

        /// <summary>Nop cach "bay gio" bao lau.</summary>
        public required TimeSpan SubmittedAgo { get; init; }

        public TimeSpan? ReviewedAgo { get; init; }

        public TimeSpan? AppealedAgo { get; init; }
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

        public required long UnitPriceVnd { get; init; }

        public required int Redundancy { get; init; }

        /// <summary>Tran redundancy thich ung. null = bang Redundancy (khong thich ung).</summary>
        public int? MaxRedundancy { get; init; }

        /// <summary>Tran co hieu luc — cung cong thuc LabelingProject.TranRedundancy.</summary>
        public int TranRedundancy
        {
            get { return MaxRedundancy.HasValue && MaxRedundancy.Value > Redundancy ? MaxRedundancy.Value : Redundancy; }
        }

        /// <summary>Ngan sach = so tien ky quy khi publish.</summary>
        public required long BudgetVnd { get; init; }

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

        public required IReadOnlyList<SeedSample> Samples { get; init; }

        public required IReadOnlyList<SeedMember> Members { get; init; }

        public required IReadOnlyList<SeedGold> Gold { get; init; }

        public required IReadOnlyList<SeedSubmission> Submissions { get; init; }

        /// <summary>Phi nen tang cho MOT nhan — cung cong thuc LabelingProject.PhiMoiNhanVnd.</summary>
        public long PlatformFeePerLabelVnd
        {
            get { return UnitPriceVnd * KichBanSeed.PhanTramPhi / 100; }
        }

        /// <summary>Da qua buoc ky quy chua (Cho duyet hoac Dang chay).</summary>
        public bool DaKyQuy
        {
            get { return Stage == SeedStage.PendingApproval || Stage == SeedStage.Running; }
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
