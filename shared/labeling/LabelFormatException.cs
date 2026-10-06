using System;

namespace Crowd.Labeling
{
    /// <summary>
    /// Nhan sai dinh dang: thieu truong, sai kieu, loai nhan chua ho tro...
    /// API cua moi service doi ngoai le nay thanh 400 kem Code.
    /// </summary>
    public sealed class LabelFormatException : Exception
    {
        public LabelFormatException()
            : base("Nhan sai dinh dang.")
        {
            Code = "nhan_sai_dinh_dang";
        }

        public LabelFormatException(string message)
            : base(message)
        {
            Code = "nhan_sai_dinh_dang";
        }

        public LabelFormatException(string message, Exception innerException)
            : base(message, innerException)
        {
            Code = "nhan_sai_dinh_dang";
        }

        public LabelFormatException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        /// <summary>Ma loi may doc duoc, vd "nhan_sai_dinh_dang", "loai_nhan_chua_ho_tro".</summary>
        public string Code { get; }
    }
}
