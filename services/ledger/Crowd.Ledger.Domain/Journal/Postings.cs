using System;
using System.Collections.Generic;
using Crowd.Ledger.Domain.Accounts;
using Crowd.Ledger.Domain.Common;

namespace Crowd.Ledger.Domain.Journal
{
    /// <summary>
    /// MOI dong tien cua he thong, viet thanh but toan — docs 3.3 dich sang code.
    /// Ham thuan, khong database: test duoc tung dong tien bang tay.
    ///
    /// Quy uoc: dong AM = tien ROI tai khoan, dong DUONG = tien VAO tai khoan.
    /// </summary>
    public static class Postings
    {
        /// <summary>Nap tien (FB-03): cong → doanh nghiep kha dung.</summary>
        public static JournalEntry NapTien(Guid intentId, Guid businessId, long amount, string provider, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            return JournalEntry.Tao(
                JournalEntryType.Deposit,
                "deposit:" + intentId,
                "Nap tien qua " + provider,
                luc,
                new List<JournalLine>
                {
                    new JournalLine(AccountCodes.Gateway(provider), -amount),
                    new JournalLine(AccountCodes.BusinessAvailable(businessId), amount),
                });
        }

        /// <summary>Publish (saga buoc 2): doanh nghiep kha dung → ky quy du an.</summary>
        public static JournalEntry DatKyQuy(Guid projectId, Guid businessId, long amount, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            return JournalEntry.Tao(
                JournalEntryType.EscrowReserve,
                "escrow:" + projectId,
                "Ky quy du an",
                luc,
                new List<JournalLine>
                {
                    new JournalLine(AccountCodes.BusinessAvailable(businessId), -amount),
                    new JournalLine(AccountCodes.ProjectEscrow(projectId), amount),
                });
        }

        /// <summary>
        /// Nhan duoc duyet (VD-M-15): ky quy −(don gia + phi) → labeler treo +don gia,
        /// nen tang +phi. Phi = 0 thi bo dong phi (dong 0 dong khong hop le).
        /// </summary>
        public static JournalEntry ChiTraNhan(Guid annotationId, Guid projectId, Guid labelerId, long amount, long fee, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            if (fee < 0)
            {
                throw new InvalidValueException("phi_am", "Phi khong duoc am.");
            }

            List<JournalLine> dong = new List<JournalLine>
            {
                new JournalLine(AccountCodes.ProjectEscrow(projectId), -checked(amount + fee)),
                new JournalLine(AccountCodes.LabelerPending(labelerId), amount),
            };

            if (fee > 0)
            {
                dong.Add(new JournalLine(AccountCodes.PlatformFee, fee));
            }

            return JournalEntry.Tao(JournalEntryType.AnnotationPayout, "annotation:" + annotationId, "Chi tra nhan duoc duyet", luc, dong);
        }

        /// <summary>
        /// Luot vuot link hop le (dac ta 2.4): ky quy −(sharer + nen tang) → sharer TREO +sharer,
        /// nen tang +phan con lai. Sharer dung chung vi voi labeler (mot tai khoan, mot vi).
        /// </summary>
        public static JournalEntry ChiTraCongLink(Guid clickId, Guid projectId, Guid sharerId, long sharerAmount, long platformAmount, DateTimeOffset luc)
        {
            BatBuocDuong(sharerAmount);
            if (platformAmount < 0)
            {
                throw new InvalidValueException("phi_am", "Phan nen tang khong duoc am.");
            }

            List<JournalLine> dong = new List<JournalLine>
            {
                new JournalLine(AccountCodes.ProjectEscrow(projectId), -checked(sharerAmount + platformAmount)),
                new JournalLine(AccountCodes.LabelerPending(sharerId), sharerAmount),
            };

            if (platformAmount > 0)
            {
                dong.Add(new JournalLine(AccountCodes.PlatformFee, platformAmount));
            }

            return JournalEntry.Tao(JournalEntryType.GateClickPayout, "click:" + clickId, "Chi tra luot vuot link", luc, dong);
        }

        /// <summary>Hoa hong gioi thieu (FS-08): lay tu phi nen tang, khong tru vao thu nhap nguoi duoc moi.</summary>
        public static JournalEntry HoaHongGioiThieu(Guid clickId, Guid referrerId, long amount, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            return JournalEntry.Tao(
                JournalEntryType.ReferralCommission,
                "referral:" + clickId,
                "Hoa hong gioi thieu",
                luc,
                new List<JournalLine>
                {
                    new JournalLine(AccountCodes.PlatformFee, -amount),
                    new JournalLine(AccountCodes.LabelerPending(referrerId), amount),
                });
        }

        /// <summary>Link vi pham: khoan dang treo cua sharer → platform:withheld (cho xu ly).</summary>
        public static JournalEntry GiuDoanhThu(Guid holdId, Guid userId, long amount, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            return JournalEntry.Tao(
                JournalEntryType.RevenueWithheld,
                "withhold:" + holdId,
                "Giu doanh thu cua link vi pham",
                luc,
                new List<JournalLine>
                {
                    new JournalLine(AccountCodes.LabelerPending(userId), -amount),
                    new JournalLine(AccountCodes.PlatformWithheld, amount),
                });
        }

        /// <summary>Het thoi gian treo: labeler treo → labeler kha dung.</summary>
        public static JournalEntry GiaiPhongTreo(Guid holdId, Guid labelerId, long amount, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            return JournalEntry.Tao(
                JournalEntryType.HoldRelease,
                "hold:" + holdId,
                "Het thoi gian treo",
                luc,
                new List<JournalLine>
                {
                    new JournalLine(AccountCodes.LabelerPending(labelerId), -amount),
                    new JournalLine(AccountCodes.LabelerAvailable(labelerId), amount),
                });
        }

        /// <summary>Huy hoac hoan thanh: toan bo so du ky quy con lai → doanh nghiep.</summary>
        public static JournalEntry TraKyQuy(Guid projectId, Guid businessId, long amount, bool laHuy, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            return JournalEntry.Tao(
                laHuy ? JournalEntryType.Refund : JournalEntryType.EscrowRelease,
                (laHuy ? "refund:" : "release:") + projectId,
                laHuy ? "Hoan ky quy du an bi huy" : "Tra ky quy con du khi hoan thanh",
                luc,
                new List<JournalLine>
                {
                    new JournalLine(AccountCodes.ProjectEscrow(projectId), -amount),
                    new JournalLine(AccountCodes.BusinessAvailable(businessId), amount),
                });
        }

        /// <summary>
        /// Xin rut (FL-11): labeler kha dung −so tien → dang chuyen +thuc nhan,
        /// thue giu lai +thue (VD-M-11). Tien CHUA roi he thong — cho cong xac nhan.
        /// </summary>
        public static JournalEntry YeuCauRut(Guid withdrawalId, Guid labelerId, long amount, long tax, DateTimeOffset luc)
        {
            BatBuocDuong(amount);
            long net = checked(amount - tax);

            List<JournalLine> dong = new List<JournalLine>
            {
                new JournalLine(AccountCodes.LabelerAvailable(labelerId), -amount),
                new JournalLine(AccountCodes.PayoutInFlight, net),
            };

            if (tax > 0)
            {
                dong.Add(new JournalLine(AccountCodes.PlatformTaxWithheld, tax));
            }

            return JournalEntry.Tao(JournalEntryType.WithdrawalRequest, "withdrawal:" + withdrawalId, "Yeu cau rut tien", luc, dong);
        }

        /// <summary>Cong xac nhan chuyen xong: dang chuyen → cong. Tien da ROI he thong.</summary>
        public static JournalEntry HoanTatRut(Guid withdrawalId, long net, string provider, DateTimeOffset luc)
        {
            BatBuocDuong(net);
            return JournalEntry.Tao(
                JournalEntryType.WithdrawalComplete,
                "withdrawal:" + withdrawalId,
                "Cong da chuyen khoan",
                luc,
                new List<JournalLine>
                {
                    new JournalLine(AccountCodes.PayoutInFlight, -net),
                    new JournalLine(AccountCodes.Gateway(provider), net),
                });
        }

        /// <summary>
        /// Cong that bai: BUT TOAN DAO cua YeuCauRut — tra labeler TOAN BO, ke ca
        /// thue da giu (khong chuyen thi khong phat sinh thu nhap chiu thue).
        /// </summary>
        public static JournalEntry DaoRut(Guid withdrawalId, Guid labelerId, long net, long tax, DateTimeOffset luc, string moTa)
        {
            BatBuocDuong(net);

            List<JournalLine> dong = new List<JournalLine>
            {
                new JournalLine(AccountCodes.PayoutInFlight, -net),
                new JournalLine(AccountCodes.LabelerAvailable(labelerId), checked(net + tax)),
            };

            if (tax > 0)
            {
                dong.Add(new JournalLine(AccountCodes.PlatformTaxWithheld, -tax));
            }

            return JournalEntry.Tao(JournalEntryType.WithdrawalReversal, "withdrawal:" + withdrawalId, moTa, luc, dong);
        }

        /// <summary>
        /// VD-M-05, tang BIEN: so tien NGUOI DUNG xin (nap, rut) luon duong. Khong
        /// kiem o day thi "rut −100.000d" bien ghi no thanh ghi co.
        /// </summary>
        private static void BatBuocDuong(long amount)
        {
            if (amount <= 0)
            {
                throw new InvalidValueException("so_tien_khong_hop_le", "So tien phai lon hon 0.");
            }
        }
    }
}
