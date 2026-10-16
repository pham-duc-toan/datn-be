using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Crowd.Ledger.Domain.Invoices;

namespace Crowd.Ledger.Api.Helpers
{
    /// <summary>
    /// Ban the hien cua hoa don:
    ///   - HTML: trang in duoc (trinh duyet "In → Luu PDF"), bo cuc theo mau hoa don GTGT;
    ///   - XML: cau truc theo dinh dang hoa don dien tu Thong tu 78/2021 (HDon / DLHDon / TTChung /
    ///     NDHDon). Day la ban DU LIEU de gui nha cung cap hoa don dien tu (VNPT, Viettel...) ky so va
    ///     cap ma co quan thue — buoc do can hop dong voi nha cung cap, chua tich hop.
    /// Moi gia tri chen vao HTML / XML deu duoc ma hoa (HtmlEncode / XmlWriter) — ten doanh nghiep do
    /// nguoi dung nhap, khong duoc chen tho.
    /// </summary>
    public static class HoaDonRenderer
    {
        private static readonly CultureInfo Vn = CultureInfo.GetCultureInfo("vi-VN");

        public static string Tien(long so)
        {
            return so.ToString("N0", Vn);
        }

        public static string Html(Invoice h)
        {
            if (h == null)
            {
                throw new ArgumentNullException(nameof(h));
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\">");
            sb.Append("<title>Hoa don ").Append(E(h.Serial)).Append('-').Append(E(h.SoHienThi)).Append("</title>");
            sb.Append("<style>body{font-family:Arial,sans-serif;max-width:860px;margin:24px auto;color:#111}")
              .Append("h1{text-align:center;font-size:20px;margin:0}.c{text-align:center}.r{text-align:right}")
              .Append("table{width:100%;border-collapse:collapse;margin-top:12px}td,th{border:1px solid #999;padding:6px;font-size:13px}")
              .Append(".k td{border:none;padding:2px 0}.nho{font-size:12px;color:#444}</style></head><body>");

            sb.Append("<h1>HÓA ĐƠN GIÁ TRỊ GIA TĂNG</h1>");
            sb.Append("<p class=\"c nho\">(Bản thể hiện của hóa đơn điện tử)</p>");
            sb.Append("<p class=\"c\">Ký hiệu: <b>").Append(E(h.Serial)).Append("</b> &nbsp; Số: <b>").Append(E(h.SoHienThi)).Append("</b> &nbsp; ")
              .Append("Ngày ").Append(h.IssuedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd 'tháng' MM 'năm' yyyy", CultureInfo.InvariantCulture)).Append("</p>");
            sb.Append("<p class=\"c nho\">Kỳ: tháng ").Append(E(h.Period)).Append("</p>");

            sb.Append("<table class=\"k\">");
            Dong(sb, "Đơn vị bán hàng", h.SellerName);
            Dong(sb, "Mã số thuế", h.SellerTaxCode);
            Dong(sb, "Địa chỉ", h.SellerAddress);
            sb.Append("<tr><td colspan=\"2\">&nbsp;</td></tr>");
            Dong(sb, "Đơn vị mua hàng", h.BuyerName);
            Dong(sb, "Mã số thuế", h.BuyerTaxCode);
            Dong(sb, "Địa chỉ", h.BuyerAddress);
            Dong(sb, "Hình thức thanh toán", "Trừ ví trả trước (chuyển khoản / cổng thanh toán)");
            sb.Append("</table>");

            sb.Append("<table><tr><th>STT</th><th>Tên hàng hóa, dịch vụ</th><th>ĐVT</th><th>Số lượng</th><th>Thành tiền chưa thuế</th>")
              .Append("<th>Thuế suất</th><th>Tiền thuế</th><th>Cộng</th></tr>");
            int stt = 1;
            foreach (InvoiceItem i in h.Items)
            {
                sb.Append("<tr><td class=\"c\">").Append(stt++).Append("</td><td>").Append(E(i.Description)).Append("</td><td class=\"c\">Lượt</td>")
                  .Append("<td class=\"r\">").Append(Tien(i.Quantity)).Append("</td><td class=\"r\">").Append(Tien(i.NetVnd)).Append("</td>")
                  .Append("<td class=\"c\">").Append(h.VatPercent).Append("%</td><td class=\"r\">").Append(Tien(i.VatVnd)).Append("</td>")
                  .Append("<td class=\"r\">").Append(Tien(i.TotalVnd)).Append("</td></tr>");
            }

            sb.Append("<tr><td colspan=\"4\" class=\"r\"><b>Cộng</b></td><td class=\"r\"><b>").Append(Tien(h.TotalNetVnd)).Append("</b></td><td></td>")
              .Append("<td class=\"r\"><b>").Append(Tien(h.TotalVatVnd)).Append("</b></td><td class=\"r\"><b>").Append(Tien(h.TotalVnd)).Append("</b></td></tr>");
            sb.Append("</table>");
            sb.Append("<p><b>Tổng tiền thanh toán:</b> ").Append(Tien(h.TotalVnd)).Append(" đồng</p>");
            sb.Append("<p><b>Số tiền viết bằng chữ:</b> ").Append(E(h.AmountInWords)).Append("</p>");

            if (h.PassThrough.Count > 0)
            {
                sb.Append("<h3>Bảng kê tiền chi hộ (không thuộc doanh thu, không chịu thuế GTGT)</h3><table><tr><th>STT</th><th>Nội dung</th><th>Số tiền</th></tr>");
                int k = 1;
                foreach (PassThroughItem p in h.PassThrough)
                {
                    sb.Append("<tr><td class=\"c\">").Append(k++).Append("</td><td>").Append(E(p.Description)).Append("</td><td class=\"r\">")
                      .Append(Tien(p.AmountVnd)).Append("</td></tr>");
                }

                sb.Append("<tr><td colspan=\"2\" class=\"r\"><b>Cộng chi hộ</b></td><td class=\"r\"><b>").Append(Tien(h.PassThroughVnd)).Append("</b></td></tr></table>");
            }

            sb.Append("<p class=\"nho\">Mã hóa đơn nội bộ: ").Append(h.Id).Append(". Chưa ký số / chưa cấp mã cơ quan thuế.</p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        public static string Xml(Invoice h)
        {
            if (h == null)
            {
                throw new ArgumentNullException(nameof(h));
            }

            XmlWriterSettings cfg = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlWriter w = XmlWriter.Create(ms, cfg))
                {
                    w.WriteStartDocument();
                    w.WriteStartElement("HDon");
                    w.WriteStartElement("DLHDon");
                    w.WriteAttributeString("Id", "HD" + h.Id.ToString("N"));

                    w.WriteStartElement("TTChung");
                    w.WriteElementString("PBan", "2.0.0");
                    w.WriteElementString("THDon", "Hóa đơn giá trị gia tăng");
                    w.WriteElementString("KHMSHDon", h.Serial.Substring(0, 1));
                    w.WriteElementString("KHHDon", h.Serial.Length > 1 ? h.Serial.Substring(1) : h.Serial);
                    w.WriteElementString("SHDon", h.Number.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("NLap", h.IssuedAt.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    w.WriteElementString("DVTTe", "VND");
                    w.WriteElementString("TGia", "1");
                    w.WriteElementString("HTTToan", "Chuyển khoản");
                    w.WriteEndElement();

                    w.WriteStartElement("NDHDon");
                    w.WriteStartElement("NBan");
                    w.WriteElementString("Ten", h.SellerName);
                    w.WriteElementString("MST", h.SellerTaxCode);
                    w.WriteElementString("DChi", h.SellerAddress);
                    w.WriteEndElement();
                    w.WriteStartElement("NMua");
                    w.WriteElementString("Ten", h.BuyerName);
                    w.WriteElementString("MST", h.BuyerTaxCode);
                    w.WriteElementString("DChi", h.BuyerAddress);
                    w.WriteElementString("DCTDTu", h.BuyerEmail);
                    w.WriteEndElement();

                    w.WriteStartElement("DSHHDVu");
                    int stt = 1;
                    foreach (InvoiceItem i in h.Items)
                    {
                        w.WriteStartElement("HHDVu");
                        w.WriteElementString("TChat", "1");
                        w.WriteElementString("STT", (stt++).ToString(CultureInfo.InvariantCulture));
                        w.WriteElementString("THHDVu", i.Description);
                        w.WriteElementString("DVTinh", "Lượt");
                        w.WriteElementString("SLuong", i.Quantity.ToString(CultureInfo.InvariantCulture));
                        w.WriteElementString("ThTien", i.NetVnd.ToString(CultureInfo.InvariantCulture));
                        w.WriteElementString("TSuat", h.VatPercent.ToString(CultureInfo.InvariantCulture) + "%");
                        w.WriteElementString("TThue", i.VatVnd.ToString(CultureInfo.InvariantCulture));
                        w.WriteEndElement();
                    }

                    w.WriteEndElement();

                    w.WriteStartElement("TToan");
                    w.WriteStartElement("THTTLTSuat");
                    w.WriteStartElement("LTSuat");
                    w.WriteElementString("TSuat", h.VatPercent.ToString(CultureInfo.InvariantCulture) + "%");
                    w.WriteElementString("ThTien", h.TotalNetVnd.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("TThue", h.TotalVatVnd.ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteElementString("TgTCThue", h.TotalNetVnd.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("TgTThue", h.TotalVatVnd.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("TgTTTBSo", h.TotalVnd.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("TgTTTBChu", h.AmountInWords);
                    w.WriteEndElement();

                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndDocument();
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static void Dong(StringBuilder sb, string nhan, string giaTri)
        {
            sb.Append("<tr><td style=\"width:190px\">").Append(E(nhan)).Append(":</td><td><b>").Append(E(giaTri)).Append("</b></td></tr>");
        }

        /// <summary>
        /// Thoat 5 ky tu dac biet cua HTML (&amp; &lt; &gt; " '). Khong dung WebUtility.HtmlEncode: no doi ca chu
        /// co dau Latin-1 (à, ã, ì) thanh &amp;#224; — trinh duyet van hien dung nhung ban luu / tim kiem kho doc.
        /// </summary>
        private static string E(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? string.Empty)
            {
                if (c == '&')
                {
                    sb.Append("&amp;");
                }
                else if (c == '<')
                {
                    sb.Append("&lt;");
                }
                else if (c == '>')
                {
                    sb.Append("&gt;");
                }
                else if (c == '"')
                {
                    sb.Append("&quot;");
                }
                else if (c == '\'')
                {
                    sb.Append("&#39;");
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
