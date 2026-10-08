using System;
namespace Crowd.BuildingBlocks.Persistence.Consumers
{
    /// <summary>Tham so van hanh phia NHAN event. Doc tu muc "Consumers".</summary>
    public sealed class EventConsumerOptions
    {
        public const string SectionName = "Consumers";

        /// <summary>
        /// So message broker duoc day toi truoc khi nhan ack. Nho thi mot
        /// message cham khong giu hang loat message khac; lon thi thong luong
        /// cao hon. Consumer xu ly TUAN TU nen 10 la du.
        /// </summary>
        public ushort PrefetchCount { get; set; } = 10;

        /// <summary>
        /// So lan giao toi da truoc khi dua vao DLQ (VD-D-06).
        ///
        /// Quorum queue cua RabbitMQ tu dem so lan giao (x-delivery-count) va
        /// tu dua sang dead-letter khi vuot nguong — khong phai tu viet bo dem.
        /// Khong co tran nay, mot message loi vinh vien se quay vong mai mai
        /// va chan ca hang doi.
        /// </summary>
        public int DeliveryLimit { get; set; } = 5;

        /// <summary>Cho bao lau roi noi lai RabbitMQ (service khong co setting he thong).</summary>
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Exchange nhan message chet. Moi queue co mot queue ".dlq" rieng.</summary>
        public string DeadLetterExchange { get; set; } = "datn.dlx";
    }
}
