using System;
using System.Collections.Generic;

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

    /// <summary>Mot anh mau: anh PNG mot mau, sinh ngay luc seed.</summary>
    public sealed class SeedSample
    {
        public required Guid Id { get; init; }

        /// <summary>So thu tu trong du an, tu 1.</summary>
        public required int Index { get; init; }

        public required string FileName { get; init; }

        /// <summary>Khoa trong bucket datasets — cung cong thuc voi Sample.Tao: "{projectId}/{sampleId}.png".</summary>
        public required string StorageKey { get; init; }

        /// <summary>Dap an dung (mau cua anh). Dung cho cau hoi vang va de doc kich ban.</summary>
        public required string TrueLabel { get; init; }

        public required byte R { get; init; }

        public required byte G { get; init; }

        public required byte B { get; init; }
    }

    public sealed class SeedGold
    {
        public required Guid Id { get; init; }

        public required Guid SampleId { get; init; }

        public required string Label { get; init; }

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

        public required string StorageKey { get; init; }

        public required Guid LabelerId { get; init; }

        public required string Label { get; init; }

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

        public required SeedStage Stage { get; init; }

        public required bool IsPrivate { get; init; }

        public required IReadOnlyList<string> Classes { get; init; }

        public required long UnitPriceVnd { get; init; }

        public required int Redundancy { get; init; }

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
