using System;

namespace Crowd.Project.Domain.Members
{
    /// <summary>
    /// Nhung gi project-svc can biet ve MOT nguoi dung de quyet "vao du an duoc khong". Lay tu
    /// identity-svc ngay luc can (POST /internal/users/lookup) — project_db khong giu ban sao
    /// email / cap do / uy tin.
    /// </summary>
    public sealed class HoSoNguoiDung
    {
        public HoSoNguoiDung(Guid userId, bool laLabeler, bool biKhoa, int capDo, int? uyTin)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("userId rong", nameof(userId));
            }

            UserId = userId;
            LaLabeler = laLabeler;
            BiKhoa = biKhoa;
            CapDo = capDo;
            UyTin = uyTin;
        }

        public Guid UserId { get; }

        /// <summary>Tai khoan co vai tro labeler tren nen tang.</summary>
        public bool LaLabeler { get; }

        /// <summary>Bi admin khoa (FM-01).</summary>
        public bool BiKhoa { get; }

        /// <summary>Cap do hien tai (chua co thong ke = 1).</summary>
        public int CapDo { get; }

        /// <summary>null = chua duoc cham uy tin.</summary>
        public int? UyTin { get; }
    }
}
