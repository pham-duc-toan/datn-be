namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>Thong so ket noi RabbitMQ cho phia GUI (producer).</summary>
    public sealed class RabbitMqOptions
    {
        public const string SectionName = "RabbitMq";

        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 5672;

        public string UserName { get; set; } = "datn";

        public string Password { get; set; } = string.Empty;

        public string VirtualHost { get; set; } = "/";

        /// <summary>
        /// Exchange kieu "topic" ma moi event di qua.
        ///
        /// Routing key chinh la eventType, vd "attempt.submitted". Nho dau
        /// cham trong eventType ma consumer binh queue theo mau duoc:
        ///     attempt.*       nhan moi event ve bai lam
        ///     *.submitted     nhan moi event ket thuc bang submitted
        /// Do la ly do eventType bat buoc co dang &lt;aggregate&gt;.&lt;qua_khu&gt;.
        /// </summary>
        public string Exchange { get; set; } = "datn.events";
    }
}
