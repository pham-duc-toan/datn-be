using System;

namespace Crowd.Ledger.Domain.Holds
{
    public enum GateClickState
    {
        /// <summary>Da nhan, cho worker gop lo.</summary>
        Queued,

        /// <summary>Da chi trong lo BatchId.</summary>
        Paid,

        /// <summary>Het ngan sach cong link luc gop lo — khong chi.</summary>
        Rejected,
    }

    /// <summary>
    /// Mot luot vuot link hop le cho che do CHI THEO LO (setting ledger.gate_payout_mode = batched,
    /// VD-M-08). Consumer chi ghi dong nay — khong khoa so cai, khong viet but toan; worker gop
    /// theo du an moi ledger.gate_batch_interval roi viet MOT but toan cho ca lo.
    ///
    /// Khoa chinh ClickId: luot bi giao lai (relay at-least-once phat lai voi eventId moi) khong
    /// duoc chi hai lan — dong da Paid van nam do chan.
    /// </summary>
    public sealed class GateClick
    {
        private GateClick()
        {
        }

        public Guid ClickId { get; private set; }

        public Guid LinkId { get; private set; }

        public Guid SharerId { get; private set; }

        public Guid ProjectId { get; private set; }

        public long SharerAmountVnd { get; private set; }

        public long PlatformAmountVnd { get; private set; }

        public DateTimeOffset ValidatedAt { get; private set; }

        public DateTimeOffset ReceivedAt { get; private set; }

        public GateClickState State { get; private set; }

        public Guid? BatchId { get; private set; }

        public DateTimeOffset? SettledAt { get; private set; }

        public long TongVnd
        {
            get { return SharerAmountVnd + PlatformAmountVnd; }
        }

        public static GateClick Tao(
            Guid clickId, Guid linkId, Guid sharerId, Guid projectId, long sharerAmount, long platformAmount, DateTimeOffset validatedAt, DateTimeOffset luc)
        {
            GateClick c = new GateClick();
            c.ClickId = clickId;
            c.LinkId = linkId;
            c.SharerId = sharerId;
            c.ProjectId = projectId;
            c.SharerAmountVnd = sharerAmount;
            c.PlatformAmountVnd = platformAmount;
            c.ValidatedAt = validatedAt;
            c.ReceivedAt = luc;
            c.State = GateClickState.Queued;
            return c;
        }

        public void DaChi(Guid batchId, DateTimeOffset luc)
        {
            State = GateClickState.Paid;
            BatchId = batchId;
            SettledAt = luc;
        }

        public void TuChoi(Guid batchId, DateTimeOffset luc)
        {
            State = GateClickState.Rejected;
            BatchId = batchId;
            SettledAt = luc;
        }
    }
}
