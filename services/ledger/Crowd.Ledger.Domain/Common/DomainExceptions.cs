using System;

namespace Crowd.Ledger.Domain.Common
{
    /// <summary>
    /// Goc cua moi loi nghiep vu. Mang mot MA (Code) on dinh de frontend dich ra
    /// thong bao da ngon ngu (FC-08), kem mot cau tieng Viet de doc khi debug.
    ///
    /// Domain chi NEM loi; tang Api quyet dinh doi thanh ma HTTP nao. Domain
    /// khong biet HTTP la gi.
    /// </summary>
    public abstract class DomainException : Exception
    {
        protected DomainException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        /// <summary>Vd "chuyen_trang_thai_khong_hop_le".</summary>
        public string Code { get; }
    }

    /// <summary>
    /// Gia tri dau vao sai: ten rong, don gia am, lop nhan trung... Api doi
    /// thanh 400.
    /// </summary>
    public sealed class InvalidValueException : DomainException
    {
        public InvalidValueException(string code, string message)
            : base(code, message)
        {
        }
    }

    /// <summary>
    /// Dau vao dung nhung TRANG THAI hien tai khong cho phep: publish du an da
    /// huy, chan chu so huu... Api doi thanh 409.
    /// </summary>
    public sealed class RuleViolationException : DomainException
    {
        public RuleViolationException(string code, string message)
            : base(code, message)
        {
        }
    }
}
