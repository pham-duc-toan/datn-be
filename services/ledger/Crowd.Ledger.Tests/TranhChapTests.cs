using System;
using System.Linq;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;
using Crowd.Ledger.Domain.Disputes;
using Crowd.Ledger.Domain.Holds;
using Crowd.Ledger.Domain.Journal;
using Crowd.Ledger.Infrastructure.Persistence;

namespace Crowd.Ledger.Tests
{
    public sealed class TranhChapTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

        private static FundsHold Treo()
        {
            return FundsHold.Tao(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20000, T, TimeSpan.FromDays(3));
        }

        [Fact]
        public void Dong_bang_thi_worker_khong_giai_phong_duoc()
        {
            FundsHold h = Treo();
            h.DongBang();

            Assert.Equal(HoldState.Disputed, h.State);
            Assert.False(h.DenHan(T.AddDays(10)));
        }

        [Fact]
        public void Tien_da_giai_phong_thi_khong_tranh_chap_duoc()
        {
            FundsHold h = Treo();
            h.GiaiPhong(T.AddDays(3));

            Assert.Equal("khong_con_treo", Assert.Throws<RuleViolationException>(() => h.DongBang()).Code);
        }

        [Fact]
        public void Labeler_thang_thi_tro_lai_treo_qua_han_thi_giai_phong_ngay()
        {
            FundsHold h = Treo();
            h.DongBang();
            h.MoBang();

            Assert.Equal(HoldState.Held, h.State);
            Assert.True(h.DenHan(T.AddDays(4)));
        }

        [Fact]
        public void Doanh_nghiep_thang_thi_tien_khong_toi_labeler()
        {
            FundsHold h = Treo();
            h.DongBang();
            h.HoanChoDoanhNghiep(T.AddDays(1));

            Assert.Equal(HoldState.Refunded, h.State);
            Assert.False(h.DenHan(T.AddDays(10)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void But_toan_hoan_tranh_chap_can_bang_va_dao_dung_dong(bool veKyQuy)
        {
            Guid duAn = Guid.NewGuid();
            Guid dn = Guid.NewGuid();
            Guid lab = Guid.NewGuid();

            JournalEntry e = Postings.HoanTranhChap(Guid.NewGuid(), duAn, dn, lab, 20000, 6000, veKyQuy, T);

            Assert.Equal(0, e.Lines.Sum(l => l.Amount));
            Assert.Equal(-20000, e.Lines.Single(l => l.AccountCode == AccountCodes.LabelerPending(lab)).Amount);
            Assert.Equal(-6000, e.Lines.Single(l => l.AccountCode == AccountCodes.PlatformFee).Amount);
            string dich = veKyQuy ? AccountCodes.ProjectEscrow(duAn) : AccountCodes.BusinessAvailable(dn);
            Assert.Equal(26000, e.Lines.Single(l => l.AccountCode == dich).Amount);
            Assert.Equal(JournalEntryType.DisputeRefund, e.Type);
        }

        [Fact]
        public void Tranh_chap_bat_buoc_ly_do_va_chi_phan_xu_mot_lan()
        {
            Assert.Equal("thieu_ly_do", Assert.Throws<InvalidValueException>(
                () => Dispute.Mo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, " ", T)).Code);

            Dispute d = Dispute.Mo(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Nhan sai", T);
            Assert.Equal("thieu_ghi_chu", Assert.Throws<InvalidValueException>(() => d.PhanXu(true, "", Guid.NewGuid(), T)).Code);

            d.PhanXu(true, "Dung la sai", Guid.NewGuid(), T);
            Assert.Equal(DisputeState.RefundedBusiness, d.State);
            Assert.Equal("tranh_chap_da_xu", Assert.Throws<RuleViolationException>(() => d.PhanXu(false, "x", Guid.NewGuid(), T)).Code);
        }

        [Fact]
        public void Khoa_mo_khoa_theo_moc_su_kien_den_lech_thu_tu_van_dung()
        {
            BlockedUser b = BlockedUser.Tao(Guid.NewGuid());

            // mo khoa (moc T+5) toi TRUOC khoa (moc T): khoa cu hon bi bo qua.
            Assert.True(b.MoKhoa(T.AddMinutes(5)));
            Assert.False(b.Khoa(T));
            Assert.False(b.Blocked);

            Assert.True(b.Khoa(T.AddMinutes(10)));
            Assert.True(b.Blocked);
        }
    }
}
