using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Annotation;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;

namespace Crowd.Seeding
{
    /// <summary>
    /// Dung lai CAC EVENT ma he thong that SE PHAT cho kich ban seed.
    ///
    /// Service giu ban sao (task, annotation, ledger) khong tu viet lai cach dung
    /// ban sao — chung PHAT LAI cac event nay vao CHINH processor cua minh (goi
    /// thang XuLyAsync, khong qua RabbitMQ). Nho vay seed di dung code duong that
    /// da duoc test, va ban sao seed giong het ban sao sinh ra tu event that.
    /// </summary>
    public static class SeedEvents
    {
        /// <summary>Producer ghi vao envelope — de log nhin la biet event nay do seed dung, khong phai tu bus.</summary>
        public const string Producer = "seed";

        public static EventEnvelope<T> Boc<T>(T payload, DateTimeOffset occurredAt)
            where T : class, IEventPayload
        {
            return EventEnvelope.Create(
                producer: Producer,
                correlationId: Guid.NewGuid(),
                payload: payload,
                occurredAt: occurredAt);
        }

        /// <summary>member.added cho chu du an + moi thanh vien, theo dung thu tu that.</summary>
        public static List<MemberAdded> ThanhVien(SeedProject p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            List<MemberAdded> ds = new List<MemberAdded>();

            ds.Add(new MemberAdded { ProjectId = p.Id, UserId = p.OwnerId, Role = ProjectMemberRole.Owner });

            foreach (SeedMember m in p.Members)
            {
                ds.Add(new MemberAdded
                {
                    ProjectId = p.Id,
                    UserId = m.UserId,
                    Role = m.Role == SeedMemberRole.Reviewer ? ProjectMemberRole.Reviewer : ProjectMemberRole.Labeler,
                });
            }

            return ds;
        }

        /// <summary>gold_set.updated — null khi du an khong co cau vang.</summary>
        public static GoldSetUpdated? CauVang(SeedProject p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (p.Gold.Count == 0)
            {
                return null;
            }

            List<GoldSetItem> items = new List<GoldSetItem>();
            foreach (SeedGold g in p.Gold)
            {
                items.Add(new GoldSetItem
                {
                    SampleId = g.SampleId,
                    ExpectedLabels = new string[] { g.Label },
                    Purpose = g.ForEntranceTest ? GoldPurpose.EntranceTest : GoldPurpose.QualityCheck,
                });
            }

            return new GoldSetUpdated { ProjectId = p.Id, Items = items };
        }

        /// <summary>project.publish_requested — so tien ky quy = ngan sach (giong ProjectService).</summary>
        public static ProjectPublishRequested YeuCauKyQuy(SeedProject p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            return new ProjectPublishRequested
            {
                ProjectId = p.Id,
                OwnerId = p.OwnerId,
                EscrowAmountVnd = p.BudgetVnd,
            };
        }

        /// <summary>project.published — cung cac truong ProjectService.TaoProjectPublished dien.</summary>
        public static ProjectPublished DaPublish(SeedProject p, DateTimeOffset bayGio)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            return new ProjectPublished
            {
                ProjectId = p.Id,
                OwnerId = p.OwnerId,
                TaskType = ProjectTaskType.ImageClassification,
                LabelClasses = new List<string>(p.Classes),
                AllowMultipleLabels = false,
                UnitPriceVnd = p.UnitPriceVnd,
                PlatformFeeVnd = p.PlatformFeePerLabelVnd,
                Redundancy = p.Redundancy,
                Deadline = p.Deadline(bayGio),
                AllowProfessional = true,
                AllowLinkGateway = false,
                AllowCollaborative = false,
                IsPrivate = p.IsPrivate,
                MinLevel = null,
                MinReputation = null,
                RequireEntranceTest = p.RequireEntranceTest,
                SampleCount = p.Samples.Count,
            };
        }

        /// <summary>annotation.approved — so tien chot theo dieu khoan luc publish.</summary>
        public static AnnotationApproved DaDuyet(SeedSubmission s, SeedProject p)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            return new AnnotationApproved
            {
                AnnotationId = s.AnnotationId,
                TaskId = s.TaskId,
                ProjectId = s.ProjectId,
                LabelerId = s.LabelerId,
                AmountVnd = p.UnitPriceVnd,
                PlatformFeeVnd = p.PlatformFeePerLabelVnd,
                Source = AnnotationSource.Professional,
            };
        }

        /// <summary>deposit.confirmed — chi cho lenh nap da thanh toan.</summary>
        public static DepositConfirmed NapThanhCong(SeedDeposit d)
        {
            if (d == null)
            {
                throw new ArgumentNullException(nameof(d));
            }

            if (d.ProviderTxnId == null)
            {
                throw new InvalidOperationException("Lenh nap " + d.IntentId + " chua thanh toan.");
            }

            return new DepositConfirmed
            {
                IntentId = d.IntentId,
                BusinessId = d.BusinessId,
                AmountVnd = d.AmountVnd,
                Provider = KichBanSeed.CongThanhToan,
                ProviderTxnId = d.ProviderTxnId,
            };
        }
    }
}
