using System;
using System.Collections.Generic;

namespace Crowd.Notification.Api.Entities
{
    /// <summary>
    /// Dia chi lien lac (tu user.registered) — notification-svc khong hoi identity luc gui.
    /// Giu ca vai tro de thong bao toan he thong loc theo doi tuong (FM-09).
    /// </summary>
    public sealed class LienHe
    {
        private List<string> _roles;

        private LienHe()
        {
            Email = string.Empty;
            DisplayName = string.Empty;
            _roles = new List<string>();
        }

        public Guid UserId { get; private set; }

        public string Email { get; private set; }

        public string DisplayName { get; private set; }

        public IReadOnlyList<string> Roles
        {
            get { return _roles; }
        }

        public static LienHe Tao(Guid userId, string email, string displayName, IEnumerable<string> roles)
        {
            LienHe l = new LienHe();
            l.UserId = userId;
            l.Email = email;
            l.DisplayName = displayName;
            l._roles = new List<string>(roles);
            return l;
        }

        /// <summary>Dong co tu truoc khi luu vai tro (hoac seed) — bo sung vai tro.</summary>
        public void DatVaiTro(IEnumerable<string> roles)
        {
            _roles = new List<string>(roles);
        }
    }

    /// <summary>Nguoi dung tat / bat EMAIL cho mot loai thong bao. Khong co dong = mac dinh cua loai.</summary>
    public sealed class TuyChonEmail
    {
        private TuyChonEmail()
        {
            Type = string.Empty;
        }

        public Guid UserId { get; private set; }

        public string Type { get; private set; }

        public bool EmailEnabled { get; private set; }

        public static TuyChonEmail Tao(Guid userId, string loai, bool bat)
        {
            TuyChonEmail t = new TuyChonEmail();
            t.UserId = userId;
            t.Type = loai;
            t.EmailEnabled = bat;
            return t;
        }

        public void Dat(bool bat)
        {
            EmailEnabled = bat;
        }
    }

    /// <summary>Chu du an (tu project.publish_requested): escrow.rejected chi mang ProjectId.</summary>
    public sealed class ChuDuAn
    {
        private ChuDuAn()
        {
        }

        public Guid ProjectId { get; private set; }

        public Guid OwnerId { get; private set; }

        public static ChuDuAn Tao(Guid projectId, Guid ownerId)
        {
            ChuDuAn c = new ChuDuAn();
            c.ProjectId = projectId;
            c.OwnerId = ownerId;
            return c;
        }
    }

    /// <summary>Chu lenh rut (tu payout.requested): payout.completed / failed chi mang WithdrawalId.</summary>
    public sealed class ChuLenhRut
    {
        private ChuLenhRut()
        {
        }

        public Guid WithdrawalId { get; private set; }

        public Guid LabelerId { get; private set; }

        public long NetAmountVnd { get; private set; }

        public static ChuLenhRut Tao(Guid withdrawalId, Guid labelerId, long netAmountVnd)
        {
            ChuLenhRut c = new ChuLenhRut();
            c.WithdrawalId = withdrawalId;
            c.LabelerId = labelerId;
            c.NetAmountVnd = netAmountVnd;
            return c;
        }
    }
}
