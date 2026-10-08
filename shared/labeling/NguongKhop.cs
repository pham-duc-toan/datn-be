using System;
using System.Collections.Generic;

namespace Crowd.Labeling
{
    /// <summary>
    /// Nguong cham MAC DINH theo loai cong cu (kind -&gt; nguong), dung khi cong cu
    /// khong khai matchThreshold. Service dung tu setting labeling.threshold.*;
    /// loai nao khong co trong bang thi dung mac dinh cua thu vien.
    /// </summary>
    public sealed class NguongKhop
    {
        /// <summary>Khong ghi de gi — moi cong cu dung mac dinh cua thu vien.</summary>
        public static readonly NguongKhop CuaThuVien = new NguongKhop(new Dictionary<string, double>());

        private readonly Dictionary<string, double> _theoLoai;

        public NguongKhop(IReadOnlyDictionary<string, double> theoLoai)
        {
            if (theoLoai == null)
            {
                throw new ArgumentNullException(nameof(theoLoai));
            }

            _theoLoai = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> cap in theoLoai)
            {
                _theoLoai[cap.Key] = cap.Value;
            }
        }

        public double? Lay(string kind)
        {
            double giaTri;
            if (_theoLoai.TryGetValue(kind, out giaTri))
            {
                return giaTri;
            }

            return null;
        }
    }
}
