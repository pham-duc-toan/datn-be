using System;

namespace Crowd.Ledger.Domain.Accounts
{
    /// <summary>
    /// Ten tai khoan trong so cai (docs 3.3). Moi ten la mot CHUOI co cau truc de
    /// doc bang mat trong psql ma khong phai tra bang khac.
    ///
    /// Khac docs mot cho: ky quy tach THEO DU AN ("escrow:project:{id}") chu
    /// khong gop "business:{id}:escrow". Nho vay:
    ///   - so du cua tai khoan ky quy CHINH LA phan chua dung cua du an do →
    ///     hoan tien khi huy lay dung con so nay (VD-M-10), khong phai tinh lai.
    ///   - chi tra vuot ky quy cua MOT du an bi chan ngay, khong an sang tien
    ///     ky quy cua du an khac cung doanh nghiep (VD-M-03).
    /// </summary>
    public static class AccountCodes
    {
        public const string PlatformFee = "platform:fee";

        /// <summary>Doanh thu cong link bi giu lai vi link vi pham (VD-L-01), cho xu ly.</summary>
        public const string PlatformWithheld = "platform:withheld";

        /// <summary>Thue TNCN da khau tru, cho nop ngan sach (VD-M-11).</summary>
        public const string PlatformTaxWithheld = "platform:tax_withheld";

        /// <summary>Tien rut DANG TREN DUONG toi ngan hang: da tru labeler, chua chac chuyen xong.</summary>
        public const string PayoutInFlight = "payout:in_flight";

        public static string BusinessAvailable(Guid businessId)
        {
            return "business:" + businessId + ":available";
        }

        public static string ProjectEscrow(Guid projectId)
        {
            return "escrow:project:" + projectId;
        }

        /// <summary>Thu lao da duyet nhung con TREO 3-7 ngay (dac ta 2.11).</summary>
        public static string LabelerPending(Guid labelerId)
        {
            return "labeler:" + labelerId + ":pending";
        }

        public static string LabelerAvailable(Guid labelerId)
        {
            return "labeler:" + labelerId + ":available";
        }

        /// <summary>
        /// Tai khoan doi ung voi the gioi ben ngoai (cong thanh toan). Tien VAO he
        /// thong lam no AM, tien RA lam no duong tro lai — so du am cua no chinh
        /// la tong tien dang nam trong he thong ma cong da thu ho.
        /// </summary>
        public static string Gateway(string provider)
        {
            return "gateway:" + provider;
        }

        /// <summary>CHI tai khoan cong duoc am. Moi tai khoan khac am la loi tien.</summary>
        public static bool ChoPhepAm(string code)
        {
            return code != null && code.StartsWith("gateway:", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Mot tai khoan. Balance la BO NHO DEM cua tong cac dong journal_lines —
    /// nguon su that la bang but toan. Giu cot nay de:
    ///   - kiem "du tien khong" bang MOT cau UPDATE co dieu kien (VD-M-01);
    ///   - doc so du khong phai SUM ca lich su.
    /// Doi soat (ReconciliationService) kiem Balance == SUM(lines) moi ngay.
    /// </summary>
    public sealed class Account
    {
        private Account()
        {
            Code = string.Empty;
        }

        public string Code { get; private set; }

        public long Balance { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public static Account Tao(string code, DateTimeOffset luc)
        {
            Account a = new Account();
            a.Code = code;
            a.Balance = 0;
            a.CreatedAt = luc;
            return a;
        }
    }
}
