using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Journal
{
    public enum JournalEntryType
    {
        /// <summary>Nap tien: cong → doanh nghiep.</summary>
        Deposit,

        /// <summary>Publish: doanh nghiep → ky quy du an.</summary>
        EscrowReserve,

        /// <summary>Nhan duoc duyet: ky quy → labeler (treo) + phi nen tang.</summary>
        AnnotationPayout,

        /// <summary>Het treo: labeler treo → labeler kha dung.</summary>
        HoldRelease,

        /// <summary>Huy du an: ky quy con lai → doanh nghiep.</summary>
        Refund,

        /// <summary>Hoan thanh du an: ky quy con lai → doanh nghiep.</summary>
        EscrowRelease,

        /// <summary>Xin rut: labeler kha dung → dang chuyen + thue giu lai.</summary>
        WithdrawalRequest,

        /// <summary>Cong bao chuyen xong: dang chuyen → cong (tien roi he thong).</summary>
        WithdrawalComplete,

        /// <summary>Cong bao that bai: BUT TOAN DAO — tra lai labeler toan bo.</summary>
        WithdrawalReversal,
    }

    /// <summary>Mot dong cua but toan: tai khoan va so tien CO DAU (am = ra, duong = vao).</summary>
    public sealed class JournalLine
    {
        private JournalLine()
        {
            AccountCode = string.Empty;
        }

        public JournalLine(string accountCode, long amount)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
            {
                throw new InvalidValueException("tai_khoan_rong", "Dong but toan thieu tai khoan.");
            }

            // VD-M-05: amount <> 0. Dau am/duong la HUONG chuyen tien, khong phai
            // loi — dat CHECK (amount > 0) o day se pha chinh phep ghi kep.
            if (amount == 0)
            {
                throw new InvalidValueException("so_tien_bang_0", "Dong but toan khong duoc bang 0.");
            }

            AccountCode = accountCode;
            Amount = amount;
        }

        public long Id { get; private set; }

        public string AccountCode { get; private set; }

        public long Amount { get; private set; }
    }

    /// <summary>
    /// BUT TOAN GHI KEP (docs 3.3) — don vi ghi chep duy nhat cua so cai.
    ///
    /// BAT BIEN: tong cac dong = 0. Tien khong tu sinh ra, khong tu mat di: dong
    /// nao lay ra bao nhieu thi dong khac nhan vao dung bay nhieu. Kiem ngay luc
    /// tao — but toan lech KHONG BAO GIO duoc tao ra.
    ///
    /// CHI THEM, KHONG SUA, KHONG XOA (VD-M-04): sai thi ghi but toan DAO. Database
    /// co trigger tu choi UPDATE/DELETE; them CHUOI BAM: moi but toan mang hash
    /// cua but toan truoc, sua lan mot dong cu la gay chuoi o moi dong sau do.
    ///
    /// (Type, Reference) la UNIQUE: cung mot su viec (vd "annotation:{id}") chi
    /// ghi so MOT lan — lop chan thu hai sau processed_events (VD-M-02).
    /// </summary>
    public sealed class JournalEntry
    {
        public const string HashGoc = "GENESIS";

        private List<JournalLine> _lines;

        private JournalEntry()
        {
            Reference = string.Empty;
            Description = string.Empty;
            PrevHash = string.Empty;
            Hash = string.Empty;
            _lines = new List<JournalLine>();
        }

        public Guid Id { get; private set; }

        /// <summary>So thu tu toan cuc, database tu tang — thu tu cua chuoi bam.</summary>
        public long Seq { get; private set; }

        public JournalEntryType Type { get; private set; }

        /// <summary>Su viec goc, vd "annotation:0199...". UNIQUE cung Type.</summary>
        public string Reference { get; private set; }

        public string Description { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public string PrevHash { get; private set; }

        public string Hash { get; private set; }

        public IReadOnlyList<JournalLine> Lines
        {
            get { return _lines; }
        }

        public static JournalEntry Tao(
            JournalEntryType type,
            string reference,
            string description,
            DateTimeOffset luc,
            IReadOnlyList<JournalLine> lines)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new InvalidValueException("thieu_tham_chieu", "But toan phai co tham chieu su viec goc.");
            }

            if (lines == null || lines.Count < 2)
            {
                throw new InvalidValueException("it_nhat_hai_dong", "But toan ghi kep can it nhat hai dong.");
            }

            long tong = 0;
            foreach (JournalLine l in lines)
            {
                tong = checked(tong + l.Amount);
            }

            if (tong != 0)
            {
                throw new InvalidValueException(
                    "but_toan_lech",
                    "Tong but toan phai bang 0, dang la " + tong + ".");
            }

            JournalEntry e = new JournalEntry();
            e.Id = Guid.CreateVersion7();
            e.Type = type;
            e.Reference = reference;
            e.Description = description ?? string.Empty;

            // Cat ve MICRO giay: Postgres chi luu toi micro giay. Khong cat thi doc
            // lai tu DB ra thoi diem khac mot chut, hash tinh lai lech → bao nham
            // "so cai bi sua".
            long ticksUtc = luc.UtcTicks - (luc.UtcTicks % 10);
            e.CreatedAt = new DateTimeOffset(ticksUtc, TimeSpan.Zero);
            e._lines = new List<JournalLine>(lines);
            return e;
        }

        /// <summary>
        /// Noi vao chuoi: gan hash cua but toan truoc va tinh hash cua minh. Goi
        /// DUNG MOT LAN, ngay truoc khi luu, khi da khoa chuoi.
        /// </summary>
        public void NoiChuoi(string prevHash)
        {
            if (!string.IsNullOrEmpty(Hash))
            {
                throw new InvalidOperationException("But toan da noi chuoi.");
            }

            PrevHash = string.IsNullOrEmpty(prevHash) ? HashGoc : prevHash;
            Hash = TinhHash(PrevHash);
        }

        /// <summary>Kiem hash dang luu co khop noi dung khong — phat hien sua tay.</summary>
        public bool HashKhop()
        {
            return string.Equals(Hash, TinhHash(PrevHash), StringComparison.Ordinal);
        }

        /// <summary>
        /// Hash = SHA-256 cua (hash truoc + noi dung chuan hoa). Dong sap theo tai
        /// khoan de thu tu luu trong DB khong lam doi hash. Thoi diem tinh den
        /// micro giay vi Postgres chi luu toi micro giay.
        /// </summary>
        private string TinhHash(string prevHash)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(prevHash).Append('|')
              .Append(Id.ToString()).Append('|')
              .Append(Type.ToString()).Append('|')
              .Append(Reference).Append('|')
              .Append((CreatedAt.UtcTicks / 10).ToString(CultureInfo.InvariantCulture));

            foreach (JournalLine l in _lines.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ThenBy(x => x.Amount))
            {
                sb.Append('|').Append(l.AccountCode).Append('=').Append(l.Amount.ToString(CultureInfo.InvariantCulture));
            }

            byte[] bam = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexStringLower(bam);
        }
    }
}
