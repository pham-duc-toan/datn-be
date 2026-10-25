using System;
using System.Collections.Generic;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Payment;
using Crowd.Contracts.Project;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Labeling;

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
                    Role = ProjectMemberRole.Labeler,
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
                    ExpectedPayload = LabelPayload.Tao(p.TapNhan(), g.PayloadJson, p.Mau(g.SampleId).Metadata),
                    Purpose = g.ForEntranceTest ? GoldPurpose.EntranceTest : GoldPurpose.QualityCheck,
                });
            }

            return new GoldSetUpdated { ProjectId = p.Id, Items = items };
        }

        /// <summary>project.publish_requested — ky quy = so suat x (thuong + phi) (giong ProjectService).</summary>
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
                EscrowAmountVnd = p.EscrowVnd,
                ProjectName = p.Name,
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
                Modality = p.Modality,
                LabelSchema = p.TapNhan().ToRawJson(),
                RewardVnd = p.RewardVnd,
                PlatformFeeVnd = p.PlatformFeePerPassVnd,
                Slots = p.Slots,
                PassThresholdPercent = p.PassThresholdPercent,
                QuestionCount = p.QuestionCount,
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

        /// <summary>Nhan cua mot cau tra loi — KIEM theo tap nhan va thong tin mau nhu khi labeler nop that.</summary>
        public static LabelPayload NhanCua(SeedProject p, SeedAnswer a)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            return LabelPayload.Tao(p.TapNhan(), a.PayloadJson, p.Mau(a.SampleId).Metadata);
        }

        /// <summary>attempt.submitted — chi cau THAT (cau vang chi mang so dung / tong), giong LamBaiService.</summary>
        public static AttemptSubmitted BaiNop(SeedProject p, SeedAttempt b, DateTimeOffset bayGio)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            if (b.State != SeedAttemptState.Submitted || !b.SubmittedAgo.HasValue)
            {
                throw new InvalidOperationException("Bai " + b.AttemptId + " chua nop.");
            }

            List<AttemptAnswer> ds = new List<AttemptAnswer>();
            foreach (SeedSample s in p.Samples)
            {
                if (p.LaCauVang(s.Id))
                {
                    continue;
                }

                SeedAnswer? tl = null;
                foreach (SeedAnswer a in b.Answers)
                {
                    if (a.SampleId == s.Id)
                    {
                        tl = a;
                    }
                }

                ds.Add(new AttemptAnswer
                {
                    SampleId = s.Id,
                    StorageKey = s.StorageKey,
                    SampleContent = s.ContentJson == null ? null : RawJson.Tu(s.ContentJson),
                    SampleMetadata = s.Metadata.ToRawJson(),
                    LabelPayload = tl == null ? null : NhanCua(p, tl),
                });
            }

            return new AttemptSubmitted
            {
                AttemptId = b.AttemptId,
                ProjectId = p.Id,
                LabelerId = b.LabelerId,
                Answers = ds,
                GoldTotal = b.GoldTotal,
                GoldCorrect = b.GoldCorrect,
                StartedAt = bayGio - b.StartedAgo,
                SubmittedAt = bayGio - b.SubmittedAgo.Value,
            };
        }

        /// <summary>project.collection_finished — du an Completed: moi bai trong kich ban da nop.</summary>
        public static ProjectCollectionFinished DongThuBai(SeedProject p, DateTimeOffset bayGio)
        {
            if (p == null || !p.GradedAgo.HasValue)
            {
                throw new InvalidOperationException("Du an seed chua cham.");
            }

            List<SubmittedAttemptRef> ds = new List<SubmittedAttemptRef>();
            foreach (SeedAttempt b in p.Attempts)
            {
                ds.Add(new SubmittedAttemptRef { AttemptId = b.AttemptId, LabelerId = b.LabelerId, GoldTotal = b.GoldTotal, GoldCorrect = b.GoldCorrect });
            }

            return new ProjectCollectionFinished
            {
                ProjectId = p.Id,
                Reason = ds.Count >= p.Slots ? "du_suat" : "het_han",
                Attempts = ds,
                ExcludedSampleIds = Array.Empty<Guid>(),
                FinishedAt = bayGio - p.GradedAgo.Value,
            };
        }

        /// <summary>project.graded — ket qua tinh san trong kich ban (cung luat quality-svc).</summary>
        public static ProjectGraded DaCham(SeedProject p, DateTimeOffset bayGio)
        {
            if (p == null || !p.GradedAgo.HasValue)
            {
                throw new InvalidOperationException("Du an seed chua cham.");
            }

            List<GradedAttempt> ds = new List<GradedAttempt>();
            foreach (SeedAttempt b in p.Attempts)
            {
                if (b.Result == null)
                {
                    throw new InvalidOperationException("Bai " + b.AttemptId + " chua co ket qua cham.");
                }

                ds.Add(new GradedAttempt
                {
                    AttemptId = b.AttemptId,
                    LabelerId = b.LabelerId,
                    ComparableCount = b.Result.ComparableCount,
                    MatchedCount = b.Result.MatchedCount,
                    AgreementPercent = b.Result.AgreementPercent,
                    GoldTotal = b.GoldTotal,
                    GoldCorrect = b.GoldCorrect,
                    Passed = b.Result.Passed,
                    Rewarded = b.Result.Passed,
                    AnsweredCount = b.Answers.Count,
                });
            }

            return new ProjectGraded
            {
                ProjectId = p.Id,
                PassThresholdPercent = p.PassThresholdPercent,
                MajorityUsed = p.Attempts.Count >= 3,
                Attempts = ds,
                GradedAt = bayGio - p.GradedAgo.Value,
            };
        }

        public static int SoNguoiDat(SeedProject p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            int n = 0;
            foreach (SeedAttempt b in p.Attempts)
            {
                if (b.Result != null && b.Result.Passed)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>project.completed — so nguoi dat cuoi cung.</summary>
        public static ProjectCompleted HoanThanh(SeedProject p)
        {
            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            return new ProjectCompleted { ProjectId = p.Id, OwnerId = p.OwnerId, PassedAttemptCount = SoNguoiDat(p) };
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
