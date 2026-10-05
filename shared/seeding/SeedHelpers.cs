using System;
using System.Collections.Generic;
using System.Linq;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Crowd.Seeding
{
    /// <summary>Cong tac bat/tat seed. Doc muc "Seed" trong appsettings.</summary>
    public static class SeedSwitch
    {
        /// <summary>
        /// Seed CHI chay khi ca hai dieu kien dung: moi truong Development VA
        /// Seed:Enabled = true. Production quen tat co thi van khong bao gio seed
        /// — mat khau chung "Matkhau@123" khong duoc phep ton tai o do.
        /// </summary>
        public static bool DuocChay(IHostEnvironment env, IConfiguration config)
        {
            if (env == null)
            {
                throw new ArgumentNullException(nameof(env));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            return env.IsDevelopment() && config.GetValue<bool>("Seed:Enabled");
        }
    }

    public static class SeedOutbox
    {
        /// <summary>
        /// Bo cac event ma code nghiep vu vua xep vao outbox trong luc seed.
        ///
        /// Seed goi lai code that (MoneyFlowService, processor...) va code do
        /// phat event — vd ledger giu ky quy thi phat escrow.reserved. Nhung moi
        /// service da TU seed phan cua minh roi: de cac event nay bay len
        /// RabbitMQ thi project-svc nhan escrow.reserved cho mot du an da chay
        /// tu lau, task-svc nhan lai du lieu da co... Nen seed KHONG phat gi ra
        /// ngoai: go cac dong outbox moi them (chua luu) khoi DbContext.
        ///
        /// Phai goi NGAY sau moi buoc co the phat event, truoc lan SaveChanges
        /// tiep theo — vd LedgerWriter tu SaveChanges ben trong.
        /// </summary>
        public static int BoEventChuaGui(DbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            List<EntityEntry<OutboxMessage>> moi = db.ChangeTracker
                .Entries<OutboxMessage>()
                .Where(e => e.State == EntityState.Added)
                .ToList();

            foreach (EntityEntry<OutboxMessage> e in moi)
            {
                e.State = EntityState.Detached;
            }

            return moi.Count;
        }
    }

    /// <summary>
    /// Dong ho DUNG YEN tai mot thoi diem. Seed dung de "lui ve qua khu": vd
    /// ledger chi tra nhan "luc 4 ngay truoc" thi khoan treo tinh tu 4 ngay
    /// truoc — da het han, nen labeler rut duoc ngay. Code nghiep vu nhan
    /// TimeProvider nen khong can sua gi.
    /// </summary>
    public sealed class DongHoCoDinh : TimeProvider
    {
        private readonly DateTimeOffset _luc;

        public DongHoCoDinh(DateTimeOffset luc)
        {
            _luc = luc;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _luc;
        }
    }
}
