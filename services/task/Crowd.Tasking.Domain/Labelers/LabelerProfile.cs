using System;

namespace Crowd.Tasking.Domain.Labelers
{
    /// <summary>
    /// labeler_cache (docs muc 4): ban sao read-only cua identity — cap do, uy
    /// tin, bi khoa hay khong. Dung de loc eligibility bang MOT cau SQL tren DB
    /// cua chinh task-svc.
    ///
    /// Moi truong co moc thoi gian RIENG: reputation.changed va level.changed la
    /// hai dong event doc lap, mot cai den tre khong duoc keo cai kia lui lai.
    /// </summary>
    public sealed class LabelerProfile
    {
        private LabelerProfile()
        {
        }

        public Guid UserId { get; private set; }

        /// <summary>null = chua tung nhan level.changed.</summary>
        public int? Level { get; private set; }

        public DateTimeOffset LevelAt { get; private set; }

        public int? Reputation { get; private set; }

        public DateTimeOffset ReputationAt { get; private set; }

        public bool Blocked { get; private set; }

        public DateTimeOffset BlockedAt { get; private set; }

        public static LabelerProfile Tao(Guid userId)
        {
            LabelerProfile p = new LabelerProfile();
            p.UserId = userId;
            p.LevelAt = DateTimeOffset.MinValue;
            p.ReputationAt = DateTimeOffset.MinValue;
            p.BlockedAt = DateTimeOffset.MinValue;
            return p;
        }

        public bool DatLevel(int level, DateTimeOffset occurredAt)
        {
            if (occurredAt < LevelAt)
            {
                return false;
            }

            Level = level;
            LevelAt = occurredAt;
            return true;
        }

        public bool DatReputation(int reputation, DateTimeOffset occurredAt)
        {
            if (occurredAt < ReputationAt)
            {
                return false;
            }

            Reputation = reputation;
            ReputationAt = occurredAt;
            return true;
        }

        public bool Khoa(DateTimeOffset occurredAt)
        {
            if (occurredAt < BlockedAt)
            {
                return false;
            }

            Blocked = true;
            BlockedAt = occurredAt;
            return true;
        }
    }
}
