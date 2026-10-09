using System;
using System.Collections.Generic;
using Crowd.Labeling;

namespace Crowd.Gate.Domain
{
    /// <summary>Ket cuc cua mot lan nop bai tren trang vuot link.</summary>
    public enum KetQuaLuot
    {
        /// <summary>Dat cau vang va duoc tinh tien cho nguoi chia se.</summary>
        TinhTien,

        /// <summary>Truot cau vang: cap bo cau moi, khong token, khong tien.</summary>
        TruotCauVang,

        /// <summary>Dat nhung IP nay da tinh mot luot cho link trong cua so (24 gio).</summary>
        TrungIp,

        /// <summary>Dat nhung chinh chu link tu vuot (cung tai khoan / cung IP luc tao link) — VD-L-04.</summary>
        TuVuot,

        /// <summary>Dat nhung du an het ngan sach cong link — VD-L-03.</summary>
        HetNganSach,

        /// <summary>Khong co du an nao phuc vu duoc (hoac cong link dang tat): chi dem nguoc roi mo link.</summary>
        KhongCoCauHoi,
    }

    /// <summary>
    /// Luat thuan cua cong link — khong database, khong Redis: test duoc bang tay.
    /// </summary>
    public static class LuatCongLink
    {
        /// <summary>
        /// Du an chay duoc tren trang vuot link khong. Khach vang lai co ~10 giay va khong
        /// duoc huan luyen (VD-L-08), nen chi nhan nhan RE va NHANH:
        ///   - du lieu anh / van ban / cap (khong audio, video — nghe xem mat thoi gian);
        ///   - moi cong cu la phan loai CHON MOT lop, hoac so sanh cap.
        /// Tra ve ly do khong ho tro, null neu ho tro.
        /// </summary>
        public static string? LyDoKhongHoTro(string modality, LabelSchema tapNhan)
        {
            if (tapNhan == null)
            {
                throw new ArgumentNullException(nameof(tapNhan));
            }

            if (modality != Modalities.Image && modality != Modalities.Text && modality != Modalities.Pair)
            {
                return "Loai du lieu " + modality + " khong phu hop trang vuot link (chi anh, van ban, cap).";
            }

            foreach (ToolDefinition t in tapNhan.Tools)
            {
                bool phanLoaiMot = t.Kind == ToolKinds.Classification && !t.AllowMultiple;
                if (!phanLoaiMot && t.Kind != ToolKinds.Pairwise)
                {
                    return "Cong cu '" + t.Name + "' (" + t.Kind + ") qua kho cho khach vang lai.";
                }
            }

            return null;
        }

        /// <summary>
        /// Dat khi MOI cau vang deu khop dap an (cham bang dung luat cua Crowd.Labeling,
        /// nguong theo setting labeling.threshold.*). Thieu cau tra loi = sai.
        /// </summary>
        public static bool DatCauVang(
            IReadOnlyList<Guid> cauVang,
            IReadOnlyDictionary<Guid, LabelPayload> traLoi,
            IReadOnlyDictionary<Guid, LabelPayload> dapAn,
            LabelSchema tapNhan,
            NguongKhop nguong)
        {
            if (cauVang == null)
            {
                throw new ArgumentNullException(nameof(cauVang));
            }

            if (traLoi == null)
            {
                throw new ArgumentNullException(nameof(traLoi));
            }

            if (dapAn == null)
            {
                throw new ArgumentNullException(nameof(dapAn));
            }

            foreach (Guid id in cauVang)
            {
                LabelPayload? cua;
                LabelPayload? dung;
                if (!traLoi.TryGetValue(id, out cua) || !dapAn.TryGetValue(id, out dung))
                {
                    return false;
                }

                if (!cua.KhopDapAn(tapNhan, dung, nguong))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Phan loai mot lan nop DA DAT cau vang. Thu tu kiem quan trong: tu vuot truoc
        /// trung IP (tu vuot khong duoc "an" luot IP cua khach that), het ngan sach cuoi
        /// cung (luot van hop le, chi la khong con tien tra).
        /// </summary>
        public static KetQuaLuot PhanLoai(bool coCauHoi, bool laChuLink, bool cungIpNguoiTao, bool ipDaTinhTrongCuaSo, bool conNganSach)
        {
            if (!coCauHoi)
            {
                return KetQuaLuot.KhongCoCauHoi;
            }

            if (laChuLink || cungIpNguoiTao)
            {
                return KetQuaLuot.TuVuot;
            }

            if (ipDaTinhTrongCuaSo)
            {
                return KetQuaLuot.TrungIp;
            }

            if (!conNganSach)
            {
                return KetQuaLuot.HetNganSach;
            }

            return KetQuaLuot.TinhTien;
        }

        /// <summary>
        /// Tien mot luot (dac ta 2.4): ky quy tru (don gia + phi) moi nhan nhu kenh chuyen
        /// nghiep; nguoi chia se nhan don gia x (1 − ti le phi) = don gia − phi; nen tang giu
        /// phan con lai. Vi du don gia 200, phi 60 (30%): sharer 140, nen tang 120, ky quy 260.
        /// </summary>
        public static TienLuot TinhTien(int soNhanThat, long donGiaVnd, long phiMoiNhanVnd)
        {
            if (soNhanThat < 0 || donGiaVnd < 0 || phiMoiNhanVnd < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(soNhanThat), "Khong duoc am.");
            }

            long moiNhanSharer = Math.Max(0, donGiaVnd - phiMoiNhanVnd);
            long kyQuyMoiNhan = donGiaVnd + phiMoiNhanVnd;
            long sharer = checked(soNhanThat * moiNhanSharer);
            long kyQuy = checked(soNhanThat * kyQuyMoiNhan);
            return new TienLuot(sharer, kyQuy - sharer);
        }

        /// <summary>Ky quy mot luot tieu ton — gate chi phuc vu du an con ngan sach >= so nay.</summary>
        public static long ChiPhiMotLuot(int soCauThat, long donGiaVnd, long phiMoiNhanVnd)
        {
            return checked(soCauThat * (donGiaVnd + phiMoiNhanVnd));
        }
    }

    public sealed class TienLuot
    {
        public TienLuot(long sharerVnd, long nenTangVnd)
        {
            SharerVnd = sharerVnd;
            NenTangVnd = nenTangVnd;
        }

        public long SharerVnd { get; }

        public long NenTangVnd { get; }

        public long TongVnd
        {
            get { return SharerVnd + NenTangVnd; }
        }
    }
}
