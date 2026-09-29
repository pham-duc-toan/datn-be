using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Journal;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Ledger.Infrastructure.Persistence
{
    /// <summary>
    /// CON DUONG DUY NHAT de ghi vao so cai. Khong code nao khac duoc them
    /// JournalEntry hay sua accounts.balance.
    ///
    /// Moi lan ghi, TRONG transaction dang mo cua noi goi:
    ///   1. Khoa so cai (advisory lock) — cac lan ghi xep hang, chuoi bam thang
    ///      hang va doc-roi-ghi so du khong the dua nhau.
    ///   2. (Type, Reference) da co → khong ghi lai (VD-M-02), tra false.
    ///   3. Cap nhat so du BANG CAU UPDATE CO DIEU KIEN:
    ///          SET balance = balance + d WHERE code = c AND balance + d >= 0
    ///      0 dong = khong du tien → nem loi, transaction quay lui (VD-M-01).
    ///      Du service da kiem so du truoc, cau nay van la chot chan cuoi.
    ///   4. Noi chuoi bam voi but toan cuoi (VD-M-04) roi luu.
    ///
    /// Luu NGAY (SaveChanges) trong transaction: but toan sau trong cung
    /// transaction can doc hash cua but toan truoc. Commit van do noi goi quyet.
    ///
    /// Khoa toan cuc lam moi lan ghi so cai xep hang — o quy mo do an (vai chuc
    /// giao dich/giay) la du. Nghen that su o cong link (1.000 luot/giay) da co
    /// huong gop lo o VD-M-08.
    /// </summary>
    public sealed class LedgerWriter
    {
        /// <summary>Khoa advisory cua so cai — mot so co dinh, chi ledger dung.</summary>
        private const long KhoaSoCai = 20260928;

        private readonly LedgerDbContext _db;

        public LedgerWriter(LedgerDbContext db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            _db = db;
        }

        /// <summary>
        /// Khoa so cai toi het transaction. Goi TRUOC khi doc so du de quyet dinh
        /// (vd du tien ky quy khong) — nho vay "doc" va "ghi" khong bi chen ngang.
        /// Goi nhieu lan trong mot transaction van an toan.
        /// </summary>
        public async Task KhoaAsync(CancellationToken ct)
        {
            if (_db.Database.CurrentTransaction == null)
            {
                throw new InvalidOperationException("Ghi so cai phai nam trong mot transaction.");
            }

            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", new object[] { KhoaSoCai }, ct);
        }

        public async Task<long> SoDuAsync(string code, CancellationToken ct)
        {
            Account? a = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
            return a == null ? 0 : a.Balance;
        }

        /// <summary>Ghi mot but toan. false = da ghi tu truoc (cung Type + Reference).</summary>
        public async Task<bool> GhiAsync(JournalEntry entry, CancellationToken ct)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            await KhoaAsync(ct);

            bool daCo = await _db.JournalEntries.AnyAsync(e => e.Type == entry.Type && e.Reference == entry.Reference, ct);
            if (daCo)
            {
                return false;
            }

            foreach (JournalLine dong in entry.Lines)
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO accounts (code, balance, created_at) VALUES ({0}, 0, {1}) ON CONFLICT (code) DO NOTHING",
                    new object[] { dong.AccountCode, entry.CreatedAt },
                    ct);

                bool choAm = AccountCodes.ChoPhepAm(dong.AccountCode);

                int soDong = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE accounts SET balance = balance + {0} " +
                    " WHERE code = {1} AND ({2} OR balance + {0} >= 0)",
                    new object[] { dong.Amount, dong.AccountCode, choAm },
                    ct);

                if (soDong == 0)
                {
                    throw new RuleViolationException(
                        "khong_du_so_du",
                        "Tai khoan " + dong.AccountCode + " khong du so du cho giao dich nay.");
                }
            }

            string? hashTruoc = await _db.JournalEntries
                .OrderByDescending(e => e.Seq)
                .Select(e => e.Hash)
                .FirstOrDefaultAsync(ct);

            entry.NoiChuoi(hashTruoc ?? JournalEntry.HashGoc);
            _db.JournalEntries.Add(entry);

            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
