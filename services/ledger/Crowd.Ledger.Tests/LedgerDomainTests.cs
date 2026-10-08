using System;
using System.Collections.Generic;
using System.Linq;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Escrows;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Domain.Withdrawals;

namespace Crowd.Ledger.Tests
{
    public sealed class DoubleEntryTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid DoanhNghiep = Guid.NewGuid();
        private static readonly Guid Labeler = Guid.NewGuid();
        private static readonly Guid DuAn = Guid.NewGuid();

        private static long Tong(JournalEntry e)
        {
            return e.Lines.Sum(l => l.Amount);
        }

        private static long Dong(JournalEntry e, string code)
        {
            return e.Lines.Where(l => l.AccountCode == code).Sum(l => l.Amount);
        }

        [Fact]
        public void But_toan_lech_khong_bao_gio_duoc_tao()
        {
            List<JournalLine> lech = new List<JournalLine> { new JournalLine("a", -100), new JournalLine("b", 99) };

            InvalidValueException ex = Assert.Throws<InvalidValueException>(() =>
                JournalEntry.Tao(JournalEntryType.Deposit, "x", "x", Luc, lech));
            Assert.Equal("but_toan_lech", ex.Code);
        }

        [Fact]
        public void Dong_bang_0_bi_tu_choi_VD_M_05()
        {
            Assert.Throws<InvalidValueException>(() => new JournalLine("a", 0));
        }

        [Fact]
        public void Moi_dong_tien_deu_can_bang_0()
        {
            List<JournalEntry> tatCa = new List<JournalEntry>
            {
                Postings.NapTien(Guid.NewGuid(), DoanhNghiep, 1000000, "sandbox", Luc),
                Postings.DatKyQuy(DuAn, DoanhNghiep, 520000, Luc),
                Postings.ChiTraNhan(Guid.NewGuid(), DuAn, Labeler, 200000, 60000, Luc),
                Postings.GiaiPhongTreo(Guid.NewGuid(), Labeler, 200000, Luc),
                Postings.TraKyQuy(DuAn, DoanhNghiep, 260000, true, Luc),
                Postings.YeuCauRut(Guid.NewGuid(), Labeler, 2000000, 200000, Luc),
                Postings.HoanTatRut(Guid.NewGuid(), 1800000, "sandbox", Luc),
                Postings.DaoRut(Guid.NewGuid(), Labeler, 1800000, 200000, Luc, "Dao"),
            };

            foreach (JournalEntry e in tatCa)
            {
                Assert.Equal(0, Tong(e));
            }
        }

        [Fact]
        public void Chi_tra_nhan_theo_VD_M_15_escrow_tru_260k_labeler_200k_platform_60k()
        {
            JournalEntry e = Postings.ChiTraNhan(Guid.NewGuid(), DuAn, Labeler, 200000, 60000, Luc);

            Assert.Equal(-260000, Dong(e, AccountCodes.ProjectEscrow(DuAn)));
            Assert.Equal(200000, Dong(e, AccountCodes.LabelerPending(Labeler)));
            Assert.Equal(60000, Dong(e, AccountCodes.PlatformFee));
        }

        [Fact]
        public void So_tien_nguoi_dung_xin_phai_duong_VD_M_05()
        {
            Assert.Throws<InvalidValueException>(() => Postings.NapTien(Guid.NewGuid(), DoanhNghiep, -500, "sandbox", Luc));
            Assert.Throws<InvalidValueException>(() => Postings.YeuCauRut(Guid.NewGuid(), Labeler, 0, 0, Luc));
        }

        [Fact]
        public void Dao_rut_tra_labeler_ca_thue_da_giu()
        {
            JournalEntry e = Postings.DaoRut(Guid.NewGuid(), Labeler, 1800000, 200000, Luc, "Dao");

            Assert.Equal(2000000, Dong(e, AccountCodes.LabelerAvailable(Labeler)));
            Assert.Equal(-200000, Dong(e, AccountCodes.PlatformTaxWithheld));
        }

        [Fact]
        public void Chi_tai_khoan_cong_duoc_am()
        {
            Assert.True(AccountCodes.ChoPhepAm(AccountCodes.Gateway("vnpay")));
            Assert.False(AccountCodes.ChoPhepAm(AccountCodes.BusinessAvailable(DoanhNghiep)));
            Assert.False(AccountCodes.ChoPhepAm(AccountCodes.PlatformFee));
        }
    }

    public sealed class HashChainTests
    {
        private static JournalEntry MotButToan(string reference)
        {
            return JournalEntry.Tao(
                JournalEntryType.Deposit,
                reference,
                "x",
                new DateTimeOffset(2026, 10, 1, 8, 0, 0, 123, TimeSpan.Zero).AddTicks(4567),
                new List<JournalLine> { new JournalLine("gateway:sandbox", -100), new JournalLine("b", 100) });
        }

        [Fact]
        public void Noi_chuoi_va_kiem_lai_duoc()
        {
            JournalEntry dau = MotButToan("r1");
            dau.NoiChuoi(JournalEntry.HashGoc);
            JournalEntry sau = MotButToan("r2");
            sau.NoiChuoi(dau.Hash);

            Assert.True(dau.HashKhop());
            Assert.True(sau.HashKhop());
            Assert.Equal(dau.Hash, sau.PrevHash);
            Assert.Equal(64, sau.Hash.Length);
        }

        [Fact]
        public void Thoi_diem_cat_ve_micro_giay_de_khop_Postgres()
        {
            JournalEntry e = MotButToan("r");
            Assert.Equal(0, e.CreatedAt.UtcTicks % 10);
        }

        [Fact]
        public void Khong_noi_chuoi_hai_lan()
        {
            JournalEntry e = MotButToan("r");
            e.NoiChuoi(JournalEntry.HashGoc);
            Assert.Throws<InvalidOperationException>(() => e.NoiChuoi("khac"));
        }
    }

    public sealed class WithdrawalTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        /// <summary>Gia tri mac dinh cua setting ledger.withdraw_*.</summary>
        private static readonly QuyDinhRut QuyDinh = new QuyDinhRut
        {
            ToiThieuVnd = 50000,
            NguongThueVnd = 2000000,
            ThueSuatPhanTram = 10,
        };

        [Fact]
        public void Thue_TNCN_10_phan_tram_tu_2_trieu_theo_tung_lan_VD_M_11()
        {
            Assert.Equal(0, Withdrawal.TinhThue(1999999, QuyDinh.NguongThueVnd, QuyDinh.ThueSuatPhanTram));
            Assert.Equal(200000, Withdrawal.TinhThue(2000000, QuyDinh.NguongThueVnd, QuyDinh.ThueSuatPhanTram));
            Assert.Equal(250000, Withdrawal.TinhThue(2500009 - 9, QuyDinh.NguongThueVnd, QuyDinh.ThueSuatPhanTram));
            Assert.Equal(200000, Withdrawal.TinhThue(2000009, QuyDinh.NguongThueVnd, QuyDinh.ThueSuatPhanTram)); // lam tron XUONG den dong
        }

        [Fact]
        public void Lenh_rut_tinh_so_thuc_nhan()
        {
            Withdrawal w = Withdrawal.Tao(Guid.NewGuid(), 3000000, "VCB 0123", "k1", QuyDinh, Luc);

            Assert.Equal(300000, w.TaxVnd);
            Assert.Equal(2700000, w.NetVnd);
        }

        [Fact]
        public void Tu_choi_duoi_toi_thieu_va_thieu_khoa_idempotency()
        {
            Assert.Throws<InvalidValueException>(() => Withdrawal.Tao(Guid.NewGuid(), 10000, "VCB", "k", QuyDinh, Luc));
            Assert.Throws<InvalidValueException>(() => Withdrawal.Tao(Guid.NewGuid(), 100000, "VCB", "", QuyDinh, Luc));
        }

        [Fact]
        public void Lenh_da_chot_khong_chot_lai()
        {
            Withdrawal w = Withdrawal.Tao(Guid.NewGuid(), 100000, "VCB", "k", QuyDinh, Luc);
            w.Duyet(null, Luc);
            w.HoanTat(Luc);

            Assert.Throws<RuleViolationException>(() => w.ThatBai("x", Luc));
        }

        [Fact]
        public void Lenh_moi_cho_duyet_chua_chot_duoc_qua_cong()
        {
            Withdrawal w = Withdrawal.Tao(Guid.NewGuid(), 100000, "VCB", "k", QuyDinh, Luc);

            Assert.Equal(WithdrawalState.PendingApproval, w.State);
            Assert.Throws<RuleViolationException>(() => w.HoanTat(Luc));
            Assert.Throws<RuleViolationException>(() => w.ThatBai("x", Luc));
        }

        [Fact]
        public void Admin_duyet_hoac_tu_choi_mot_lan()
        {
            Guid admin = Guid.NewGuid();

            Withdrawal duyet = Withdrawal.Tao(Guid.NewGuid(), 100000, "VCB", "k", QuyDinh, Luc);
            duyet.Duyet(admin, Luc.AddHours(1));
            Assert.Equal(WithdrawalState.Requested, duyet.State);
            Assert.Equal(admin, duyet.ReviewedBy);
            Assert.Throws<RuleViolationException>(() => duyet.TuChoi(admin, "muon", Luc.AddHours(2)));

            Withdrawal tuChoi = Withdrawal.Tao(Guid.NewGuid(), 100000, "VCB", "k2", QuyDinh, Luc);
            Assert.Throws<InvalidValueException>(() => tuChoi.TuChoi(admin, "  ", Luc));
            tuChoi.TuChoi(admin, "Sai so tai khoan", Luc.AddHours(1));
            Assert.Equal(WithdrawalState.Rejected, tuChoi.State);
            Assert.Equal("Sai so tai khoan", tuChoi.FailureReason);
            Assert.Throws<RuleViolationException>(() => tuChoi.Duyet(admin, Luc.AddHours(2)));
        }

        [Fact]
        public void Thue_theo_quy_dinh_dang_hieu_luc()
        {
            // Admin doi nguong 1 trieu, thue suat 5%.
            Assert.Equal(50000, Withdrawal.TinhThue(1000000, 1000000, 5));
            Assert.Equal(0, Withdrawal.TinhThue(999999, 1000000, 5));
        }
    }

    public sealed class HoldAndEscrowTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Treo_3_ngay_moi_giai_phong()
        {
            FundsHold h = FundsHold.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 200000, Luc, TimeSpan.FromDays(3));

            Assert.False(h.DenHan(Luc.AddDays(2)));
            Assert.Throws<RuleViolationException>(() => h.GiaiPhong(Luc.AddDays(2)));

            h.GiaiPhong(Luc.AddDays(3));
            Assert.Equal(HoldState.Released, h.State);
            Assert.Throws<RuleViolationException>(() => h.GiaiPhong(Luc.AddDays(4)));
        }

        [Fact]
        public void Chan_chi_vuot_redundancy_VD_M_03()
        {
            ProjectEscrow e = ProjectEscrow.Tao(Guid.NewGuid(), Guid.NewGuid(), 1000000, Luc);
            Assert.False(e.VuotRedundancy(99));

            e.DatRedundancy(3);
            Assert.False(e.VuotRedundancy(2));
            Assert.True(e.VuotRedundancy(3));
        }
    }
}
