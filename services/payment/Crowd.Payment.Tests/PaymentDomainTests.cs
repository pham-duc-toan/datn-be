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

        private static PaymentIntent LenhNap()
        {
            return PaymentIntent.Tao(Guid.NewGuid(), 1000000, "sandbox", "k1", Luc);
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
            Assert.Throws<InvalidValueException>(() => PaymentIntent.Tao(Guid.NewGuid(), -1, "sandbox", "k", Luc));
            Assert.Throws<InvalidValueException>(() => PaymentIntent.Tao(Guid.NewGuid(), 5000, "sandbox", "k", Luc));
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
