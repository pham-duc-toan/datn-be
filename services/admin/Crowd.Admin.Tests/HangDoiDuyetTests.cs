using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Admin.Api.Services;
using Crowd.Admin.Api.Workers;
using Xunit;

namespace Crowd.Admin.Tests
{
    public sealed class HangDoiDuyetTests
    {
        private static readonly Guid A = Guid.Parse("01900000-0000-7000-8000-000000000001");
        private static readonly Guid B = Guid.Parse("01900000-0000-7000-8000-000000000002");

        private static ThayDoiDon? Chieu(string loai, string json, string? vaiTro = null)
        {
            using (JsonDocument d = JsonDocument.Parse(json))
            {
                return HangDoiDuyet.PhanLoai(loai, d.RootElement, vaiTro);
            }
        }

        [Fact]
        public void Kyb_mo_roi_dong()
        {
            ThayDoiDon? mo = Chieu("business.kyb_submitted", "{\"businessId\":\"" + A + "\",\"companyName\":\"Cong ty X\"}");
            Assert.NotNull(mo);
            Assert.Equal("kyb", mo!.Kind);
            Assert.Equal(KieuThayDoi.Mo, mo.Kieu);
            Assert.Equal("Cong ty X", mo.Title);
            Assert.Equal(A, mo.RequesterId);

            ThayDoiDon? tuChoi = Chieu("business.kyb_rejected", "{\"businessId\":\"" + A + "\",\"reason\":\"thieu giay phep\"}");
            Assert.Equal(KieuThayDoi.Dong, tuChoi!.Kieu);
            Assert.Equal("rejected", tuChoi.Status);
            Assert.Equal("thieu giay phep", tuChoi.Note);
            Assert.True(tuChoi.TaoNeuChuaCo);
        }

        [Fact]
        public void Du_an_cho_ky_quy_roi_cho_duyet_roi_admin_tu_choi()
        {
            ThayDoiDon? xin = Chieu("project.publish_requested",
                "{\"projectId\":\"" + A + "\",\"ownerId\":\"" + B + "\",\"escrowAmountVnd\":500000,\"projectName\":\"Gan nhan xe\"}");
            Assert.Equal(KieuThayDoi.ChoKyQuy, xin!.Kieu);
            Assert.Equal(500000L, xin.AmountVnd);
            Assert.Equal("Gan nhan xe", xin.Title);

            Assert.Equal(KieuThayDoi.MoTuCho, Chieu("escrow.reserved", "{\"projectId\":\"" + A + "\",\"amountVnd\":500000}")!.Kieu);

            string huy = "{\"projectId\":\"" + A + "\",\"ownerId\":\"" + B + "\",\"wasEscrowed\":true,\"reason\":\"vi pham\",\"approvedAnnotationCount\":0}";
            Assert.Equal("rejected", Chieu("project.cancelled", huy, "admin")!.Status);
            Assert.Equal("cancelled", Chieu("project.cancelled", huy, "business")!.Status);
            // Du an huy / publish xay ra ca khi khong co don → khong tao dong moi.
            Assert.False(Chieu("project.cancelled", huy, "admin")!.TaoNeuChuaCo);
        }

        [Fact]
        public void Rut_tien_khieu_nai_tranh_chap_link()
        {
            Assert.Equal(2000000L, Chieu("withdrawal.review_requested",
                "{\"withdrawalId\":\"" + A + "\",\"labelerId\":\"" + B + "\",\"amountVnd\":2000000}")!.AmountVnd);
            ThayDoiDon? chi = Chieu("payout.requested",
                "{\"withdrawalId\":\"" + A + "\",\"labelerId\":\"" + B + "\",\"netAmountVnd\":1800000,\"bankAccount\":\"x\"}");
            Assert.Equal("approved", chi!.Status);
            Assert.False(chi.TaoNeuChuaCo);   // rut tu duyet cung phat payout.requested

            Assert.Equal("approved", Chieu("appeal.resolved",
                "{\"annotationId\":\"" + A + "\",\"projectId\":\"" + B + "\",\"labelerId\":\"" + B + "\",\"accepted\":true}")!.Status);
            Assert.Equal("rejected", Chieu("appeal.resolved",
                "{\"annotationId\":\"" + A + "\",\"projectId\":\"" + B + "\",\"labelerId\":\"" + B + "\",\"accepted\":false,\"note\":\"sai\"}")!.Status);

            ThayDoiDon? xu = Chieu("dispute.resolved", "{\"disputeId\":\"" + A + "\",\"annotationId\":\"" + B + "\",\"businessId\":\"" + B
                + "\",\"labelerId\":\"" + B + "\",\"projectId\":\"" + B + "\",\"amountVnd\":1000,\"outcome\":\"refundBusiness\",\"note\":\"nhan sai\"}");
            Assert.Equal("resolved", xu!.Status);
            Assert.Equal("refundBusiness: nhan sai", xu.Note);

            Assert.Equal("3 bao cao", Chieu("link.review_requested",
                "{\"linkId\":\"" + A + "\",\"ownerId\":\"" + B + "\",\"code\":\"abc\",\"reportCount\":3}")!.Note);
            string tat = "{\"linkId\":\"" + A + "\",\"ownerId\":\"" + B + "\",\"reason\":\"lua dao\",\"withholdRevenue\":true}";
            Assert.Equal("approved", Chieu("link.disabled", tat, "admin")!.Status);
            Assert.Equal("cancelled", Chieu("link.disabled", tat, "sharer")!.Status);
        }

        [Fact]
        public void Event_khong_lien_quan_bo_qua()
        {
            Assert.Null(Chieu("annotation.approved", "{}"));
            Assert.Null(Chieu("user.registered", "{}"));
            foreach (string loai in HangDoiDuyet.LoaiEvent)
            {
                Assert.Contains('.', loai);
            }
        }

        [Fact]
        public void Doc_dlq_tu_management_api()
        {
            string json = "[{\"name\":\"ledger-svc.annotation-approved\",\"messages\":0},"
                + "{\"name\":\"ledger-svc.annotation-approved.dlq\",\"messages\":3},"
                + "{\"name\":\"admin-svc.audit.dlq\",\"messages\":0},"
                + "{\"name\":\"quality-svc.x.dlq\"}]";
            List<KeyValuePair<string, long>> ds = DlqMonitorWorker.DocDlq(json);
            Assert.Equal(3, ds.Count);
            Assert.Equal("ledger-svc.annotation-approved.dlq", ds[0].Key);
            Assert.Equal(3L, ds[0].Value);
            Assert.Equal(0L, ds[2].Value);
        }
    }
}
