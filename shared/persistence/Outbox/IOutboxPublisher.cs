using System.Threading;
using System.Threading.Tasks;

namespace Crowd.BuildingBlocks.Persistence.Outbox
{
    /// <summary>
    /// Thu ma dispatcher can de day mot dong outbox len bus.
    ///
    /// Tach thanh interface vi hai ly do:
    ///
    /// 1. Dispatcher KHONG can biet bus la RabbitMQ hay gi khac. No chi biet
    ///    "dua dong nay di, xong thi bao lai".
    ///
    /// 2. Sau nay test dispatcher ma khong can broker that: thay bang mot ban
    ///    gia luon thanh cong, hoac mot ban gia luon nem loi de kiem nhanh
    ///    backoff.
    /// </summary>
    public interface IOutboxPublisher
    {
        /// <summary>
        /// Day mot message len bus va CHO broker xac nhan da nhan.
        ///
        /// Phai cho xac nhan that, khong duoc "gui roi quen" — neu tra ve truoc
        /// khi broker nhan, dispatcher se danh dau da gui cho mot message chua
        /// bao gio toi noi, va no mat vinh vien.
        ///
        /// Nem ngoai le neu khong gui duoc; dispatcher bat va lui lich thu lai.
        /// </summary>
        Task PublishAsync(OutboxMessage message, CancellationToken ct);
    }
}
