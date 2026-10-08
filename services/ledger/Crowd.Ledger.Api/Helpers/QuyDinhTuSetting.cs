using System;
using Crowd.BuildingBlocks.Settings;
using Crowd.Ledger.Domain.Withdrawals;

namespace Crowd.Ledger.Api.Helpers
{
    /// <summary>Doc setting hien tai thanh quy dinh truyen vao domain. Goi MOI lan can.</summary>
    public static class QuyDinhTuSetting
    {
        public static QuyDinhRut Rut(ISettings s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return new QuyDinhRut
            {
                ToiThieuVnd = s.SoLon(SettingKeys.LedgerWithdrawMinVnd),
                NguongThueVnd = s.SoLon(SettingKeys.LedgerWithdrawTaxThresholdVnd),
                ThueSuatPhanTram = s.SoNguyen(SettingKeys.LedgerWithdrawTaxPercent),
            };
        }

        /// <summary>Lenh rut nay co duoc he thong duyet luon khong (ledger.withdraw_auto_approve*).</summary>
        public static bool DuocTuDuyetRut(ISettings s, long soTienVnd)
        {
            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return s.DungSai(SettingKeys.LedgerWithdrawAutoApprove)
                   && soTienVnd <= s.SoLon(SettingKeys.LedgerWithdrawAutoApproveMaxVnd);
        }
    }
}
