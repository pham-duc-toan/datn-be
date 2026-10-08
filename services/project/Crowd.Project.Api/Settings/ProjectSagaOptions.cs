using System;

namespace Crowd.Project.Api.Settings
{
    /// <summary>Tham so saga publish (docs 3.4). Doc tu muc "Saga".</summary>
    public sealed class ProjectSagaOptions
    {
        public const string SectionName = "Saga";

        /// <summary>
        /// CHI DUNG LUC DEV, khi ledger-svc (P1) chua ton tai.
        ///
        /// true = bam publish thi coi nhu ledger da giu tien ngay, du an nhay
        /// thang sang Cho duyet, KHONG phat project.publish_requested. Van di qua
        /// DUNG cac phuong thuc domain (YeuCauPublish → XacNhanDaKyQuy), nen luat
        /// chuyen trang thai khong bi bo qua.
        ///
        /// Khi co ledger: dat false, va consumer escrow.* tu lam nua con lai.
        /// </summary>
        public bool BoQuaKyQuy { get; set; }
    }
}
