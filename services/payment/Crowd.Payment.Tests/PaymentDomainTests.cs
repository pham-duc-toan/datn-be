using System;
using Crowd.Payment.Domain.Common;
using Crowd.Payment.Domain.Deposits;
using Crowd.Payment.Domain.Payouts;
using Crowd.Payment.Infrastructure.Providers;
using Microsoft.Extensions.Options;

namespace Crowd.Payment.Tests
{
    public sealed class DepositTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        /// <summary>Gia tri mac dinh cua setting payment.deposit_min_vnd / deposit_max_vnd.</summary>
        private static readonly QuyDinhNap QuyDinh = new QuyDinhNap
        {
            ToiThieuVnd = 10000,
            ToiDaVnd = 500000000,
        };

        private static PaymentIntent LenhNap()
        {
            return PaymentIntent.Tao(Guid.NewGuid(), 1000000, "sandbox", "k1", QuyDinh, Luc);
        }

        [Fact]
        public void Webhook_lap_lai_cung_ma_giao_dich_khong_ghi_lan_hai()
        {
            PaymentIntent p = LenhNap();

            Assert.True(p.XacNhanThanhCong("TXN1", 1000000, Luc));
            Assert.False(p.XacNhanThanhCong("TXN1", 1000000, Luc));
            Assert.Equal(PaymentIntentStatus.Succeeded, p.Status);
        }

        [Fact]
        public void Cong_bao_sai_so_tien_thi_tu_choi()
        {
            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => LenhNap().XacNhanThanhCong("TXN1", 10000, Luc));
            Assert.Equal("sai_so_tien", ex.Code);
        }

        [Fact]
        public void So_tien_nap_ngoai_khoang_bi_tu_choi()
        {
            Assert.Throws<InvalidValueException>(() => PaymentIntent.Tao(Guid.NewGuid(), -1, "sandbox", "k", QuyDinh, Luc));
            Assert.Throws<InvalidValueException>(() => PaymentIntent.Tao(Guid.NewGuid(), 5000, "sandbox", "k", QuyDinh, Luc));
        }

        [Fact]
        public void Chuyen_khoan_thu_cong_bao_da_chuyen_roi_admin_duyet()
        {
            PaymentIntent p = PaymentIntent.TaoChuyenKhoan(Guid.NewGuid(), 2000000, "k", QuyDinh, Luc);
            Assert.StartsWith("CROWD", p.TransferCode);
            Assert.Equal(PaymentIntent.ChuyenKhoanThuCong, p.Provider);

            p.BaoDaChuyen(Luc.AddMinutes(5));
            p.BaoDaChuyen(Luc.AddMinutes(6));   // bam lai: khong loi
            Assert.Equal(PaymentIntentStatus.AwaitingApproval, p.Status);

            Guid admin = Guid.NewGuid();
            p.DuyetChuyenKhoan(admin, "FT2610010001", Luc.AddHours(1));
            Assert.Equal(PaymentIntentStatus.Succeeded, p.Status);
            Assert.Equal("FT2610010001", p.ProviderTxnId);
            Assert.Equal(admin, p.ReviewedBy);

            Assert.Throws<RuleViolationException>(() => p.TuChoiChuyenKhoan(admin, "x", Luc.AddHours(2)));
        }

        [Fact]
        public void Chuyen_khoan_thu_cong_tu_choi_phai_co_ly_do()
        {
            PaymentIntent p = PaymentIntent.TaoChuyenKhoan(Guid.NewGuid(), 2000000, "k", QuyDinh, Luc);
            p.BaoDaChuyen(Luc);

            Assert.Throws<InvalidValueException>(() => p.TuChoiChuyenKhoan(Guid.NewGuid(), " ", Luc));
            p.TuChoiChuyenKhoan(Guid.NewGuid(), "Khong thay tien ve", Luc);
            Assert.Equal(PaymentIntentStatus.Rejected, p.Status);
            Assert.Throws<RuleViolationException>(() => p.DuyetChuyenKhoan(null, null, Luc));
        }

        [Fact]
        public void Lenh_qua_cong_khong_duyet_tay_duoc()
        {
            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => LenhNap().DuyetChuyenKhoan(Guid.NewGuid(), null, Luc));
            Assert.Equal("khong_phai_chuyen_khoan", ex.Code);
        }
    }

    public sealed class PayoutTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Ba_pha_Requested_Sending_Succeeded()
        {
            Payout p = Payout.Tao(Guid.NewGuid(), Guid.NewGuid(), 100000, "VCB", Luc);

            Assert.Throws<RuleViolationException>(() => p.ThanhCong("x", Luc));

            p.BatDauGui(Luc);
            Assert.Equal(PayoutStatus.Sending, p.Status);
            Assert.Equal(1, p.Attempts);

            p.ThanhCong("TXN", Luc);
            Assert.Equal(PayoutStatus.Succeeded, p.Status);
        }

        [Fact]
        public void Ket_o_Sending_qua_lau_thi_can_tra_cuu_VD_M_09()
        {
            Payout p = Payout.Tao(Guid.NewGuid(), Guid.NewGuid(), 100000, "VCB", Luc);
            p.BatDauGui(Luc);

            Assert.False(p.CanTraCuu(TimeSpan.FromMinutes(15), Luc.AddMinutes(14)));
            Assert.True(p.CanTraCuu(TimeSpan.FromMinutes(15), Luc.AddMinutes(15)));
        }
    }

    public sealed class SandboxProviderTests
    {
        private static SandboxPaymentProvider Cong()
        {
            SandboxOptions o = new SandboxOptions();
            o.Secret = "bi_mat";
            return new SandboxPaymentProvider(Options.Create(o));
        }

        [Fact]
        public void Chu_ky_dung_thi_qua_sai_mot_ky_tu_thi_truot()
        {
            SandboxPaymentProvider c = Cong();
            string body = "{\"intentId\":\"x\",\"amountVnd\":1000}";
            string ky = c.Ky(body);

            Assert.True(c.KiemChuKy(body, ky));
            Assert.False(c.KiemChuKy(body, ky.Substring(0, 63) + (ky[63] == 'a' ? 'b' : 'a')));
            Assert.False(c.KiemChuKy(body.Replace("1000", "9000", StringComparison.Ordinal), ky));
            Assert.False(c.KiemChuKy(body, null));
        }

        [Fact]
        public async System.Threading.Tasks.Task Timeout_lan_dau_khong_ro_tra_cuu_sau_thi_thanh_cong()
        {
            SandboxPaymentProvider c = Cong();
            Guid id = Guid.NewGuid();

            ProviderPayoutResult lanDau = await c.ChuyenTienAsync(id, 1000, "VCB-TIMEOUT", default);
            ProviderPayoutResult traCuu = await c.TraCuuAsync(id, "VCB-TIMEOUT", default);
            ProviderPayoutResult tuChoi = await c.ChuyenTienAsync(id, 1000, "VCB-FAIL", default);

            Assert.Equal(ProviderOutcome.Unknown, lanDau.Outcome);
            Assert.Equal(ProviderOutcome.Succeeded, traCuu.Outcome);
            Assert.Equal(ProviderOutcome.Failed, tuChoi.Outcome);
        }
    }
}
