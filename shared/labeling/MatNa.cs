using System;
using System.Text.Json.Nodes;
using Crowd.Labeling.Tools;

namespace Crowd.Labeling
{
    /// <summary>
    /// Doc mot mat na phan vung (ket qua cong cu mask) cho noi can so lieu cua no — vd xuat COCO
    /// can dien tich va hinh bao. Dinh dang RLE: xem Tools/Rle.cs.
    /// </summary>
    public static class MatNa
    {
        /// <summary>So pixel duoc bat cua mot doi tuong mask ({"labelId","rle":{"size","counts"}}).</summary>
        public static long DienTich(JsonObject doiTuong)
        {
            if (doiTuong == null)
            {
                throw new ArgumentNullException(nameof(doiTuong));
            }

            return Rle.DienTich(Rle.Doc(doiTuong).Doan);
        }

        /// <summary>Hinh bao [x, y, w, h] (pixel) cua vung duoc bat; null neu mat na rong.</summary>
        public static double[]? HopBao(JsonObject doiTuong)
        {
            if (doiTuong == null)
            {
                throw new ArgumentNullException(nameof(doiTuong));
            }

            return Rle.HopBao(Rle.Doc(doiTuong));
        }
    }
}
