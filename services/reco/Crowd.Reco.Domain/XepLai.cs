using System;
using System.Collections.Generic;
using System.Linq;

namespace Crowd.Reco.Domain
{
    /// <summary>
    /// Xep lai sau khi cham diem (muc 5.1 + 5.6 ke hoach):
    ///   - trong moi khung 10 the, toi da N du an cung doanh nghiep (setting reco.max_per_business_top);
    ///   - khong de 3 the lien tiep cung loai du lieu (xen loai);
    ///   - o thu K, 2K, ... (setting reco.explore_every) la the KHAM PHA: du an con trong be du an moi, hoac loai viec
    ///     labeler chua tung lam — chon ngau nhien co trong so theo diem.
    /// Ket qua toi da KichThuocDanhSach the; the khong xep duoc vi luat (het cho cho doanh nghiep) bi day xuong cuoi.
    /// </summary>
    public static class XepLai
    {
        private const int Khung = 10;
        private const int NhinTruoc = 20;

        public static List<DuAnDaCham> Xep(
            IReadOnlyList<DuAnDaCham> daCham,
            LabelerGoiY nguoi,
            ThongSoGoiY thongSo,
            Random ngauNhien)
        {
            if (daCham == null)
            {
                throw new ArgumentNullException(nameof(daCham));
            }

            if (nguoi == null)
            {
                throw new ArgumentNullException(nameof(nguoi));
            }

            if (thongSo == null)
            {
                throw new ArgumentNullException(nameof(thongSo));
            }

            if (ngauNhien == null)
            {
                throw new ArgumentNullException(nameof(ngauNhien));
            }

            List<DuAnDaCham> conLai = daCham.OrderByDescending(x => x.Diem).ThenBy(x => x.DuAn.Id).ToList();
            List<DuAnDaCham> kq = new List<DuAnDaCham>();
            int toiDa = Math.Max(1, thongSo.KichThuocDanhSach);

            while (conLai.Count > 0 && kq.Count < toiDa)
            {
                int viTri = kq.Count;
                bool laOKhamPha = thongSo.KhamPhaMoi > 0 && (viTri + 1) % thongSo.KhamPhaMoi == 0;
                DuAnDaCham? chon = null;

                if (laOKhamPha)
                {
                    chon = ChonKhamPha(conLai, kq, nguoi, thongSo, ngauNhien);
                }

                if (chon == null)
                {
                    chon = ChonTheoLuat(conLai, kq, thongSo);
                }

                Guid daChon = chon.DuAn.Id;
                conLai.RemoveAll(x => x.DuAn.Id == daChon);
                kq.Add(chon);
            }

            return kq;
        }

        /// <summary>The dau tien (theo diem) trong NhinTruoc the khong pham luat doanh nghiep / xen loai; khong co thi the dau.</summary>
        private static DuAnDaCham ChonTheoLuat(List<DuAnDaCham> conLai, List<DuAnDaCham> kq, ThongSoGoiY thongSo)
        {
            int gioiHan = Math.Min(conLai.Count, NhinTruoc);
            for (int i = 0; i < gioiHan; i++)
            {
                if (HopLuat(conLai[i], kq, thongSo))
                {
                    return conLai[i];
                }
            }

            // Khong the nao hop luat (vd chi con du an cua mot doanh nghiep): uu tien luat doanh nghiep, bo luat xen loai.
            for (int i = 0; i < gioiHan; i++)
            {
                if (DuChoDoanhNghiep(conLai[i], kq, thongSo))
                {
                    return conLai[i];
                }
            }

            return conLai[0];
        }

        private static DuAnDaCham? ChonKhamPha(
            List<DuAnDaCham> conLai, List<DuAnDaCham> kq, LabelerGoiY nguoi, ThongSoGoiY thongSo, Random ngauNhien)
        {
            List<DuAnDaCham> ungVien = conLai
                .Where(x => (x.DuAn.SeenCount < thongSo.NguongDuAnMoi || !nguoi.SoThich.DaTungLam(x.DuAn)) && DuChoDoanhNghiep(x, kq, thongSo))
                .ToList();
            if (ungVien.Count == 0)
            {
                return null;
            }

            double tong = 0;
            foreach (DuAnDaCham x in ungVien)
            {
                tong += Math.Max(x.Diem, 1e-6);
            }

            double r = ngauNhien.NextDouble() * tong;
            DuAnDaCham chon = ungVien[ungVien.Count - 1];
            foreach (DuAnDaCham x in ungVien)
            {
                r -= Math.Max(x.Diem, 1e-6);
                if (r <= 0)
                {
                    chon = x;
                    break;
                }
            }

            List<string> lyDo = new List<string>(chon.LyDo);
            if (!lyDo.Contains(ChamDiem.LyDoKhamPha))
            {
                lyDo.Add(ChamDiem.LyDoKhamPha);
            }

            DuAnDaCham daGan = new DuAnDaCham
            {
                DuAn = chon.DuAn,
                Diem = chon.Diem,
                PBatDau = chon.PBatDau,
                PDat = chon.PDat,
                TienMoiPhut = chon.TienMoiPhut,
                DacTrung = chon.DacTrung,
                LyDo = lyDo,
                KhamPha = true,
            };
            int i = conLai.IndexOf(chon);
            conLai[i] = daGan;
            return daGan;
        }

        private static bool HopLuat(DuAnDaCham x, List<DuAnDaCham> kq, ThongSoGoiY thongSo)
        {
            if (!DuChoDoanhNghiep(x, kq, thongSo))
            {
                return false;
            }

            int n = kq.Count;
            if (n >= 2 && kq[n - 1].DuAn.Modality == x.DuAn.Modality && kq[n - 2].DuAn.Modality == x.DuAn.Modality)
            {
                return false;
            }

            return true;
        }

        /// <summary>Trong khung 10 the hien tai, doanh nghiep nay con cho khong.</summary>
        private static bool DuChoDoanhNghiep(DuAnDaCham x, List<DuAnDaCham> kq, ThongSoGoiY thongSo)
        {
            int dauKhung = kq.Count / Khung * Khung;
            int dem = 0;
            for (int i = dauKhung; i < kq.Count; i++)
            {
                if (kq[i].DuAn.OwnerId == x.DuAn.OwnerId)
                {
                    dem++;
                }
            }

            return dem < Math.Max(1, thongSo.ToiDaMotDoanhNghiep);
        }
    }
}
