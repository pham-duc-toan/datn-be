using System;
using Crowd.Notification.Api.Entities;
using Crowd.Notification.Api.Services;

namespace Crowd.Notification.Tests
{
    public sealed class LoaiThongBaoTests
    {
        [Fact]
        public void Loai_bat_buoc_luon_gui_email_du_nguoi_dung_tat()
        {
            LoaiThongBao rut = LoaiThongBao.Tim(LoaiThongBao.RutTien)!;

            Assert.True(rut.BatBuoc);
            Assert.True(rut.GuiEmail(false));
            Assert.True(rut.GuiEmail(null));
        }

        [Fact]
        public void Loai_thuong_theo_tuy_chon_nguoi_dung_khong_co_thi_theo_mac_dinh()
        {
            LoaiThongBao duAn = LoaiThongBao.Tim(LoaiThongBao.DuAn)!;
            LoaiThongBao duyet = LoaiThongBao.Tim(LoaiThongBao.NhanDuyet)!;

            Assert.True(duAn.GuiEmail(null));
            Assert.False(duAn.GuiEmail(false));
            // Loai ban nhieu: mac dinh tat email, bat duoc.
            Assert.False(duyet.GuiEmail(null));
            Assert.True(duyet.GuiEmail(true));
        }

        [Fact]
        public void Ma_loai_la_null()
        {
            Assert.Null(LoaiThongBao.Tim("khong_co"));
        }

        [Fact]
        public void Moi_ma_loai_duy_nhat()
        {
            System.Collections.Generic.HashSet<string> ma = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (LoaiThongBao l in LoaiThongBao.TatCa)
            {
                Assert.True(ma.Add(l.Ma), "Trung ma " + l.Ma);
                Assert.True(l.Ma.Length <= 40, "Ma qua dai cho cot type: " + l.Ma);
            }
        }
    }

    public sealed class ThongBaoEmailTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

        private static ThongBao Moi(bool guiEmail)
        {
            return ThongBao.Tao(Guid.NewGuid(), LoaiThongBao.DuAn, NoiDungThongBao.DuAnDangChay(Guid.NewGuid()), guiEmail, T);
        }

        [Fact]
        public void Khong_gui_email_thi_skipped_khong_hen_gio()
        {
            ThongBao t = Moi(false);

            Assert.Equal(EmailState.Skipped, t.EmailState);
            Assert.Null(t.EmailNextAt);
        }

        [Fact]
        public void Gui_loi_thi_hen_lai_tang_dan_qua_so_lan_thi_bo()
        {
            ThongBao t = Moi(true);
            Assert.Equal(EmailState.Pending, t.EmailState);

            t.GuiEmailLoi("SMTP timeout", 3, T);
            Assert.Equal(EmailState.Pending, t.EmailState);
            Assert.Equal(T + TimeSpan.FromSeconds(10), t.EmailNextAt);

            t.GuiEmailLoi("SMTP timeout", 3, T);
            Assert.Equal(T + TimeSpan.FromSeconds(20), t.EmailNextAt);

            t.GuiEmailLoi("SMTP timeout", 3, T);
            Assert.Equal(EmailState.Failed, t.EmailState);
            Assert.Null(t.EmailNextAt);
            Assert.Equal(3, t.EmailAttempts);
        }

        [Fact]
        public void Backoff_toi_da_30_phut()
        {
            Assert.Equal(TimeSpan.FromMinutes(30), ThongBao.ThoiGianChoGuiLai(50));
        }

        [Fact]
        public void Da_gui_thi_sent()
        {
            ThongBao t = Moi(true);
            t.DaGuiEmail(T);

            Assert.Equal(EmailState.Sent, t.EmailState);
            Assert.Equal(T, t.EmailSentAt);
        }

        [Fact]
        public void Gop_cong_don_tien_va_so_lan_dua_len_dau()
        {
            ThongBao t = ThongBao.TaoGop(Guid.NewGuid(), LoaiThongBao.TienKhaDung, NoiDungThongBao.TienKhaDung(500, 1), 500, false, T);
            t.GopThem(700, NoiDungThongBao.TienKhaDung(1200, 2), T.AddMinutes(5));

            Assert.Equal(2, t.MergedCount);
            Assert.Equal(1200, t.AmountVnd);
            Assert.Equal(T.AddMinutes(5), t.CreatedAt);
            Assert.Equal("1.200 đ (2 khoản) đã chuyển sang số dư khả dụng — có thể rút.", t.Body);
        }

        [Fact]
        public void Khong_gop_vao_thong_bao_da_doc_hoac_da_gui_email()
        {
            ThongBao daDoc = ThongBao.TaoGop(Guid.NewGuid(), LoaiThongBao.TienKhaDung, NoiDungThongBao.TienKhaDung(500, 1), 500, false, T);
            daDoc.DanhDauDaDoc(T);
            Assert.Throws<InvalidOperationException>(() => daDoc.GopThem(1, NoiDungThongBao.TienKhaDung(501, 2), T));

            ThongBao daGui = ThongBao.TaoGop(Guid.NewGuid(), LoaiThongBao.TienKhaDung, NoiDungThongBao.TienKhaDung(500, 1), 500, true, T);
            daGui.DaGuiEmail(T);
            Assert.Throws<InvalidOperationException>(() => daGui.GopThem(1, NoiDungThongBao.TienKhaDung(501, 2), T));
        }

        [Fact]
        public void Chi_loai_ban_nhieu_duoc_gop()
        {
            foreach (LoaiThongBao l in LoaiThongBao.TatCa)
            {
                bool nenGop = l.Ma == LoaiThongBao.NhanDuyet || l.Ma == LoaiThongBao.TienKhaDung;
                Assert.Equal(nenGop, l.Gop);
                // Loai bat buoc (tien, tai khoan) luon la tung thong bao rieng.
                Assert.False(l.Gop && l.BatBuoc);
            }
        }

        [Fact]
        public void Danh_dau_da_doc_idempotent()
        {
            ThongBao t = Moi(false);

            Assert.True(t.DanhDauDaDoc(T));
            Assert.False(t.DanhDauDaDoc(T.AddMinutes(1)));
            Assert.Equal(T, t.ReadAt);
        }
    }

    public sealed class NoiDungThongBaoTests
    {
        [Fact]
        public void Tien_dinh_dang_viet_nam()
        {
            Assert.Equal("13.000 đ", NoiDungThongBao.Tien(13000));
            Assert.Equal("1.500.000 đ", NoiDungThongBao.Tien(1500000));
        }

        [Fact]
        public void Nhan_duoc_duyet_mot_va_nhieu_nhan()
        {
            Guid p = Guid.NewGuid();

            Assert.Equal("+5.000 đ (đang treo, sẽ chuyển sang khả dụng sau thời gian treo).", NoiDungThongBao.NhanDuocDuyet(p, 5000, 1).NoiDungChinh);
            Assert.StartsWith("3 nhãn được duyệt, tổng +15.000 đ", NoiDungThongBao.NhanDuocDuyet(p, 15000, 3).NoiDungChinh, StringComparison.Ordinal);
        }

        [Fact]
        public void Tu_choi_lan_dau_va_khieu_nai_bi_bac_khac_nhau()
        {
            Guid p = Guid.NewGuid();
            NoiDung lanDau = NoiDungThongBao.NhanBiTuChoi(p, "Sai lop", false);
            NoiDung cuoi = NoiDungThongBao.NhanBiTuChoi(p, "Giu nguyen", true);

            Assert.Equal("Nhãn bị từ chối", lanDau.TieuDe);
            Assert.Contains("khiếu nại", lanDau.NoiDungChinh, StringComparison.Ordinal);
            Assert.Equal("Khiếu nại bị bác", cuoi.TieuDe);
            Assert.Equal("/projects/" + p, cuoi.DuongDan);
        }
    }

    public sealed class EmailGiaoDichTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Gui_xong_thi_xoa_noi_dung_chua_link()
        {
            EmailGiaoDich e = EmailGiaoDich.Tao(Guid.NewGuid(), "a@b.com", "A", "Xac thuc", "link bi mat", T);
            e.DaGui(T);

            Assert.Equal(EmailState.Sent, e.State);
            Assert.Equal(string.Empty, e.Body);
        }

        [Fact]
        public void Loi_qua_so_lan_thi_bo_va_xoa_noi_dung()
        {
            EmailGiaoDich e = EmailGiaoDich.Tao(Guid.NewGuid(), "a@b.com", "A", "Xac thuc", "link bi mat", T);
            e.GuiLoi("SMTP", 2, T);
            Assert.Equal(EmailState.Pending, e.State);
            Assert.Equal("link bi mat", e.Body);

            e.GuiLoi("SMTP", 2, T);
            Assert.Equal(EmailState.Failed, e.State);
            Assert.Equal(string.Empty, e.Body);
        }

        [Fact]
        public void Email_xac_thuc_co_link_va_gio_viet_nam()
        {
            string noiDung = NoiDungThongBao.EmailXacThuc("An", "http://fe/xac-thuc-email?token=abc", T);

            Assert.Contains("http://fe/xac-thuc-email?token=abc", noiDung, StringComparison.Ordinal);
            Assert.Contains("15:00 12/10/2026 (giờ Việt Nam)", noiDung, StringComparison.Ordinal);
        }

        [Fact]
        public void Ket_qua_tranh_chap_khac_nhau_cho_hai_ben()
        {
            Guid p = Guid.NewGuid();
            NoiDung choLabeler = NoiDungThongBao.TranhChapKetQua(p, 20000, true, true, "Nhan sai");
            NoiDung choDoanhNghiep = NoiDungThongBao.TranhChapKetQua(p, 20000, false, true, "Nhan sai");

            Assert.Contains("không được trả", choLabeler.NoiDungChinh, StringComparison.Ordinal);
            Assert.Contains("được hoàn lại", choDoanhNghiep.NoiDungChinh, StringComparison.Ordinal);
        }

        [Fact]
        public void Loai_tranh_chap_bat_buoc_email_cap_do_mac_dinh_tat()
        {
            Assert.True(LoaiThongBao.Tim(LoaiThongBao.TranhChap)!.BatBuoc);
            Assert.False(LoaiThongBao.Tim(LoaiThongBao.CapDo)!.GuiEmail(null));
            Assert.Equal(16, LoaiThongBao.TatCa.Count);
        }

        [Fact]
        public void Thanh_vien_du_an_co_ten_va_ly_do()
        {
            Guid p = Guid.NewGuid();

            Assert.Contains("“Gán nhãn chó mèo”", NoiDungThongBao.DuocThemVaoDuAn(p, "Gán nhãn chó mèo").NoiDungChinh, StringComparison.Ordinal);
            Assert.Contains("dự án.", NoiDungThongBao.DuocThemVaoDuAn(p, null).NoiDungChinh, StringComparison.Ordinal);
            Assert.Contains("đạt 90%", NoiDungThongBao.DonThamGiaMoi(p, "X", 90).NoiDungChinh, StringComparison.Ordinal);
            Assert.EndsWith("Lý do: thiếu kinh nghiệm.", NoiDungThongBao.KetQuaDonThamGia(p, "X", false, "thiếu kinh nghiệm").NoiDungChinh, StringComparison.Ordinal);
            Assert.Equal("Đơn xin tham gia được duyệt", NoiDungThongBao.KetQuaDonThamGia(p, "X", true, null).TieuDe);

            // Chu du an co the nhan rat nhieu don: mac dinh khong gui email.
            Assert.False(LoaiThongBao.Tim(LoaiThongBao.DonThamGia)!.GuiEmail(null));
            Assert.True(LoaiThongBao.Tim(LoaiThongBao.ThanhVien)!.GuiEmail(null));
        }
    }
    public sealed class NgonNguVaRealtimeTests
    {
        [Fact]
        public void Moi_mau_deu_co_ban_tieng_Anh()
        {
            Guid p = Guid.NewGuid();
            NoiDung[] ds = new NoiDung[]
            {
                NoiDungThongBao.DoanhNghiepDaXacMinh(), NoiDungThongBao.DoanhNghiepBiTuChoi("x"), NoiDungThongBao.TaiKhoanBiKhoa(null),
                NoiDungThongBao.TaiKhoanMoKhoa(), NoiDungThongBao.NapTienThanhCong(1000), NoiDungThongBao.DuAnDangChay(p),
                NoiDungThongBao.DuAnThieuKyQuy(p, "x"), NoiDungThongBao.DuAnBiHuy(p, "x"), NoiDungThongBao.HoanKyQuy(p, 1),
                NoiDungThongBao.NhanDuocDuyet(p, 1, 2), NoiDungThongBao.NhanBiTuChoi(p, "x", true), NoiDungThongBao.TienKhaDung(1, 1),
                NoiDungThongBao.RutThanhCong(1), NoiDungThongBao.RutThatBai("x"), NoiDungThongBao.RutBiTuChoi(1, "x"),
                NoiDungThongBao.TranhChapMo(p, 1, "x"), NoiDungThongBao.TranhChapKetQua(p, 1, true, true, "x"), NoiDungThongBao.CapDoMoi(2),
                NoiDungThongBao.HuyHieuMoi("x"), NoiDungThongBao.DuocThemVaoDuAn(p, "x"), NoiDungThongBao.DonThamGiaMoi(p, "x", null),
                NoiDungThongBao.KetQuaDonThamGia(p, "x", false, "het_han"), NoiDungThongBao.LoiMoiDuAn(p, "x", true, DateTimeOffset.UnixEpoch),
                NoiDungThongBao.EmailLoiMoiNguoiMoi("x", "http://l", DateTimeOffset.UnixEpoch), NoiDungThongBao.HaiLopThayDoi(false, "disabled"),
                NoiDungThongBao.XacMinhDanhTinhDaNop(), NoiDungThongBao.XacMinhDanhTinhDuyet(), NoiDungThongBao.XacMinhDanhTinhTuChoi("x"),
                NoiDungThongBao.ThuXacThuc("A", "http://l", DateTimeOffset.UnixEpoch), NoiDungThongBao.ThuDatLaiMatKhau("A", "http://l", DateTimeOffset.UnixEpoch),
            };
            foreach (NoiDung n in ds)
            {
                NoiDung en = n.TheoNgonNgu(NgonNgu.TiengAnh);
                Assert.NotEqual(n.TieuDe, en.TieuDe);
                Assert.Equal(n.DuongDan, en.DuongDan);
                Assert.Same(n, n.TheoNgonNgu(NgonNgu.TiengViet));
                Assert.Same(n, n.TheoNgonNgu(null));
            }

            Assert.Contains("VND", NoiDungThongBao.NapTienThanhCong(1500000).TheoNgonNgu("en").NoiDungChinh, StringComparison.Ordinal);
            Assert.True(NgonNgu.HopLe("en") && NgonNgu.HopLe("vi") && !NgonNgu.HopLe("fr") && !NgonNgu.HopLe(null));
        }

        [Fact]
        public void Don_het_han_va_bao_mat_bat_buoc_email()
        {
            NoiDung h = NoiDungThongBao.KetQuaDonThamGia(Guid.NewGuid(), "X", false, "het_han");
            Assert.Equal("Đơn xin tham gia đã hết hạn", h.TieuDe);
            Assert.True(LoaiThongBao.Tim(LoaiThongBao.BaoMat)!.GuiEmail(false));
            Assert.Contains("đặt lại", NoiDungThongBao.HaiLopThayDoi(false, "admin_reset").NoiDungChinh, StringComparison.Ordinal);
            Assert.Contains("TẮT", NoiDungThongBao.HaiLopThayDoi(false, "disabled").NoiDungChinh, StringComparison.Ordinal);
            Assert.Equal(NoiDungThongBao.ThuXacThuc("A", "http://l", DateTimeOffset.UnixEpoch).NoiDungChinh,
                NoiDungThongBao.EmailXacThuc("A", "http://l", DateTimeOffset.UnixEpoch));
        }

        [Fact]
        public void Tach_noi_dung_listen_notify()
        {
            Guid u = Guid.NewGuid();
            Guid t = Guid.NewGuid();
            Guid a;
            Guid b;
            Assert.True(RealtimeListener.TachNoiDung(u.ToString("N") + ":" + t.ToString("N"), out a, out b));
            Assert.Equal(u, a);
            Assert.Equal(t, b);
            Assert.False(RealtimeListener.TachNoiDung("rac", out a, out b));
            Assert.False(RealtimeListener.TachNoiDung(string.Empty, out a, out b));
        }

        [Fact]
        public void Ve_realtime_chi_luu_bam()
        {
            string token = VeRealtime.TaoToken();
            Assert.True(token.Length >= 40);
            Assert.DoesNotContain("+", token, StringComparison.Ordinal);
            Assert.Equal(64, VeRealtime.Bam(token).Length);
            Assert.NotEqual(VeRealtime.Bam(token), VeRealtime.Bam(VeRealtime.TaoToken()));
        }
    }
}
