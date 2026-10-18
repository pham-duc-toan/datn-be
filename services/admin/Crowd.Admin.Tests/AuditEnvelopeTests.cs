using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Crowd.Admin.Api.Workers;

namespace Crowd.Admin.Tests
{
    /// <summary>
    /// AuditConsumer.DocEnvelope: doc truong chung cua MOI loai event (khong biet kieu payload) va cat
    /// payload qua lon. Sai o day thi message cua mot service bat ky roi vao DLQ cua audit.
    /// </summary>
    public sealed class AuditEnvelopeTests
    {
        private static readonly Guid EventId = Guid.Parse("01a13100-0000-7000-8000-000000000001");
        private static readonly Guid CorrelationId = Guid.Parse("01a13100-0000-7000-8000-000000000002");
        private static readonly Guid UserId = Guid.Parse("01a13100-0000-7000-8000-000000000003");

        private static byte[] Envelope(string actorJson, string causationJson, string payloadJson)
        {
            string json = "{\"eventId\":\"" + EventId + "\",\"eventType\":\"project.published\",\"version\":1,"
                + "\"occurredAt\":\"2026-10-12T08:00:00+00:00\",\"producer\":\"project-svc\","
                + "\"correlationId\":\"" + CorrelationId + "\",\"causationId\":" + causationJson + ","
                + "\"actor\":" + actorJson + ",\"payload\":" + payloadJson + "}";
            return Encoding.UTF8.GetBytes(json);
        }

        [Fact]
        public void Doc_du_truong_khi_co_nguoi_gay_ra()
        {
            DongAudit d = AuditConsumer.DocEnvelope(Envelope(
                "{\"userId\":\"" + UserId + "\",\"role\":\"business\"}", "null", "{\"projectId\":\"x\"}"));

            Assert.Equal(EventId, d.EventId);
            Assert.Equal("project.published", d.EventType);
            Assert.Equal("project-svc", d.Producer);
            Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), d.OccurredAt);
            Assert.Equal(CorrelationId, d.CorrelationId);
            Assert.Null(d.CausationId);
            Assert.Equal(UserId, d.ActorId);
            Assert.Equal("business", d.ActorRole);
            Assert.Equal("{\"projectId\":\"x\"}", d.PayloadJson);
        }

        [Fact]
        public void Actor_null_la_he_thong_tu_dong()
        {
            Guid nguyenNhan = Guid.NewGuid();
            DongAudit d = AuditConsumer.DocEnvelope(Envelope("null", "\"" + nguyenNhan + "\"", "{}"));

            Assert.Null(d.ActorId);
            Assert.Null(d.ActorRole);
            Assert.Equal(nguyenNhan, d.CausationId);
        }

        [Fact]
        public void Payload_qua_lon_bi_cat_con_kich_thuoc()
        {
            string lon = "{\"x\":\"" + new string('a', AuditConsumer.PayloadToiDa) + "\"}";
            DongAudit d = AuditConsumer.DocEnvelope(Envelope("null", "null", lon));

            using (JsonDocument doc = JsonDocument.Parse(d.PayloadJson))
            {
                Assert.True(doc.RootElement.GetProperty("_truncated").GetBoolean());
                Assert.Equal(Encoding.UTF8.GetByteCount(lon), doc.RootElement.GetProperty("bytes").GetInt32());
            }
        }

        [Fact]
        public void Payload_dung_nguong_thi_giu_nguyen()
        {
            // {"x":"..."} = 8 byte vo + noi dung → vua dung PayloadToiDa byte.
            string vua = "{\"x\":\"" + new string('a', AuditConsumer.PayloadToiDa - 8) + "\"}";
            DongAudit d = AuditConsumer.DocEnvelope(Envelope("null", "null", vua));

            Assert.Equal(vua, d.PayloadJson);
        }

        [Theory]
        [InlineData("{\"amountVnd\":50000,\"platformFeeVnd\":15000}", 50000, 15000)]
        [InlineData("{\"netAmountVnd\":45000}", 45000, 0)]
        [InlineData("{\"amountVnd\":70000,\"netAmountVnd\":63000}", 70000, 0)]
        [InlineData("{\"projectId\":\"x\"}", 0, 0)]
        [InlineData("{\"amountVnd\":\"khong phai so\"}", 0, 0)]
        public void Doc_so_tien_cho_bao_cao(string payload, long tien, long phi)
        {
            DongAudit d = AuditConsumer.DocEnvelope(Envelope("null", "null", payload));

            Assert.Equal(tien, d.AmountVnd);
            Assert.Equal(phi, d.FeeVnd);
        }

        [Fact]
        public void So_tien_doc_truoc_khi_cat_payload_qua_lon()
        {
            string lon = "{\"amountVnd\":1234,\"x\":\"" + new string('a', AuditConsumer.PayloadToiDa) + "\"}";
            DongAudit d = AuditConsumer.DocEnvelope(Envelope("null", "null", lon));

            Assert.Equal(1234, d.AmountVnd);
            Assert.Contains("_truncated", d.PayloadJson, StringComparison.Ordinal);
        }

        [Fact]
        public void Ngay_bao_cao_tinh_theo_gio_Viet_Nam()
        {
            // 17:30 UTC ngay 12 = 00:30 ngay 13 gio Viet Nam.
            Assert.Equal(new DateOnly(2026, 10, 13), AuditConsumer.NgayVietNam(new DateTimeOffset(2026, 10, 12, 17, 30, 0, TimeSpan.Zero)));
            Assert.Equal(new DateOnly(2026, 10, 12), AuditConsumer.NgayVietNam(new DateTimeOffset(2026, 10, 12, 16, 59, 0, TimeSpan.Zero)));
        }

        [Fact]
        public void Mac_dinh_khong_luu_chi_tiet_event_tan_suat_cao_nhung_giu_thao_tac_quan_tri()
        {
            string[] loaiTru = Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh.Split(',');

            // Bai nop mang ca de (lon, tan suat cao) — chi dem.
            Assert.Contains("attempt.submitted", loaiTru);
            Assert.Contains("funds.held", loaiTru);
            Assert.DoesNotContain("project.graded", loaiTru);
            Assert.DoesNotContain("attempt.appeal_resolved", loaiTru);
            Assert.DoesNotContain("user.blocked", loaiTru);
            Assert.DoesNotContain("setting.changed", loaiTru);
            Assert.DoesNotContain("deposit.confirmed", loaiTru);
            Assert.True(Crowd.BuildingBlocks.Settings.SettingCatalog.AuditLoaiTruMacDinh.Length <= 1000);
        }

        [Fact]
        public void Message_khong_phai_envelope_thi_nem_loi_de_vao_DLQ()
        {
            Assert.ThrowsAny<JsonException>(() => AuditConsumer.DocEnvelope(Encoding.UTF8.GetBytes("khong phai json")));
            Assert.Throws<KeyNotFoundException>(() => AuditConsumer.DocEnvelope(Encoding.UTF8.GetBytes("{\"eventType\":\"a.b\"}")));
        }
    }
}
