using System;
using System.Collections.Generic;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;

namespace Crowd.Project.Domain.Projects
{
    /// <summary>
    /// Tap nhan cua du an (FB-12). Voi bai toan phan loai: danh sach lop, va co
    /// cho chon nhieu lop cho mot mau hay khong.
    ///
    /// BAT BIEN (immutable): tao ra roi khong sua duoc. Muon doi thi tao ban
    /// moi va gan de len — nho vay khong ai sua len mot schema dang duoc
    /// cau hoi vang hay nhan da nop tham chieu toi.
    ///
    /// Luu trong database thanh mot cot JSONB: moi loai bai toan mot hinh dang
    /// khac nhau ma khong phai them bang.
    /// </summary>
    public sealed class LabelSchema
    {
        public const int SoLopToiThieu = 2;
        public const int SoLopToiDa = 100;
        public const int DoDaiTenLopToiDa = 50;

        private readonly List<string> _classes;

        private LabelSchema(List<string> classes, bool allowMultiple)
        {
            _classes = classes;
            AllowMultiple = allowMultiple;
        }

        /// <summary>Ten cac lop, giu nguyen thu tu doanh nghiep nhap.</summary>
        public IReadOnlyList<string> Classes
        {
            get { return _classes; }
        }

        /// <summary>true = mot mau duoc gan nhieu lop (multi-label).</summary>
        public bool AllowMultiple { get; }

        /// <summary>
        /// Tao schema phan loai. Ten lop duoc cat khoang trang; trung ten (khong
        /// phan biet hoa thuong) la loi — "Meo" va "meo" la mot lop.
        /// </summary>
        public static LabelSchema TaoPhanLoai(IEnumerable<string> classes, bool allowMultiple)
        {
            if (classes == null)
            {
                throw new InvalidValueException("label_schema_rong", "Phai co danh sach lop nhan.");
            }

            List<string> daCat = new List<string>();
            HashSet<string> daGap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string lop in classes)
            {
                string ten = lop == null ? string.Empty : lop.Trim();

                if (ten.Length == 0 || ten.Length > DoDaiTenLopToiDa)
                {
                    throw new InvalidValueException(
                        "ten_lop_khong_hop_le",
                        "Ten lop phai tu 1 den " + DoDaiTenLopToiDa + " ky tu.");
                }

                if (!daGap.Add(ten))
                {
                    throw new InvalidValueException("ten_lop_trung", "Lop '" + ten + "' bi trung.");
                }

                daCat.Add(ten);
            }

            if (daCat.Count < SoLopToiThieu || daCat.Count > SoLopToiDa)
            {
                throw new InvalidValueException(
                    "so_lop_khong_hop_le",
                    "Bai toan phan loai can tu " + SoLopToiThieu + " den " + SoLopToiDa + " lop.");
            }

            return new LabelSchema(daCat, allowMultiple);
        }

        /// <summary>
        /// Mot nhan (o dinh dang chung) co hop le voi tap nhan nay khong. Dinh dang
        /// da duoc LabelPayload kiem; o day chi kiem cac LOP no dung.
        /// </summary>
        public bool LaNhanHopLe(LabelPayload nhan)
        {
            if (nhan == null)
            {
                return false;
            }

            return LaBoNhanHopLe(nhan.CacLop());
        }

        /// <summary>
        /// Mot bo nhan co hop le voi schema nay khong: khong rong, moi nhan deu
        /// la lop co that, khong lap lai, va chi mot nhan neu khong cho multi-label.
        ///
        /// Dung chung cho cau hoi vang, bai test dau vao — va sau nay annotation-svc
        /// kiem nhan nop len cung theo dung luat nay.
        /// </summary>
        public bool LaBoNhanHopLe(IReadOnlyCollection<string> nhan)
        {
            if (nhan == null || nhan.Count == 0)
            {
                return false;
            }

            if (!AllowMultiple && nhan.Count != 1)
            {
                return false;
            }

            HashSet<string> daGap = new HashSet<string>(StringComparer.Ordinal);

            foreach (string n in nhan)
            {
                if (n == null || !_classes.Contains(n) || !daGap.Add(n))
                {
                    return false;
                }
            }

            return true;
        }

        public bool CoLop(string ten)
        {
            return ten != null && _classes.Contains(ten);
        }
    }
}
