using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Crowd.Reco.Domain
{
    /// <summary>
    /// Mo hinh du doan cho MOT cap (labeler, du an), dau vao la vector DacTrung:
    ///   PBatDau — P(bat dau lam | thay the);
    ///   PDat    — P(bai dat | da bat dau).
    /// "Kha nang kiem tien" KHONG phai dau ra mo hinh — ghep o ChamDiem cung thuong, P(lot suat), so phut.
    /// Mo hinh chi DUNG (nhan trong so voi dac trung) trong request; HUAN LUYEN o ml-svc moi dem.
    /// </summary>
    public interface IMoHinhGoiY
    {
        /// <summary>"g1" (cong thuc tay) hoac ma phien ban ml-svc dat.</summary>
        string PhienBan { get; }

        string Loai { get; }

        double PBatDau(double[] x);

        double PDat(double[] x);
    }

    /// <summary>
    /// G1 — chua co hoc may (it du lieu): P(bat dau) ≈ muc hop the, P(dat) ≈ bo dem dat / truot theo cong cu. Cong thuc
    /// va duong xu ly request giu nguyen khi thay bang mo hinh hoc may, chi doi cho lay hai xac suat.
    /// </summary>
    public sealed class MoHinhG1 : IMoHinhGoiY
    {
        public static readonly MoHinhG1 Instance = new MoHinhG1();

        public string PhienBan
        {
            get { return "g1"; }
        }

        public string Loai
        {
            get { return "formula"; }
        }

        public double PBatDau(double[] x)
        {
            if (x == null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            return x[DacTrung.Match];
        }

        public double PDat(double[] x)
        {
            if (x == null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            return x[DacTrung.PPassPrior];
        }
    }

    /// <summary>Mot bo phan loai nhi phan tren vector da sap theo DacTrung.Ten (logistic hoac rung cay).</summary>
    public interface IPhanLoai
    {
        double XacSuat(double[] x);
    }

    /// <summary>Hoi quy logistic: sigmoid(b + Σ w_i × (x_i − mean_i) / scale_i). Vai chuc trong so, &lt; 1 µs / du an.</summary>
    public sealed class Logistic : IPhanLoai
    {
        private readonly double _chan;
        private readonly double[] _w;
        private readonly double[] _tb;
        private readonly double[] _thang;

        public Logistic(double intercept, double[] coef, double[] mean, double[] scale)
        {
            if (coef == null || mean == null || scale == null)
            {
                throw new ArgumentNullException(nameof(coef));
            }

            if (coef.Length != mean.Length || coef.Length != scale.Length)
            {
                throw new FormatException("Logistic: coef / mean / scale phai cung do dai.");
            }

            _chan = intercept;
            _w = coef;
            _tb = mean;
            _thang = scale;
        }

        public double XacSuat(double[] x)
        {
            if (x == null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            double z = _chan;
            for (int i = 0; i < _w.Length && i < x.Length; i++)
            {
                double s = _thang[i] == 0 ? 1 : _thang[i];
                double v = double.IsNaN(x[i]) ? _tb[i] : x[i];
                z += _w[i] * (v - _tb[i]) / s;
            }

            return MoHinhHocMay.Sigmoid(z);
        }
    }

    /// <summary>Mot nut cay LightGBM (dump_model): la co LeafValue, nut trong co SplitFeature / Threshold.</summary>
    public sealed class NutCay
    {
        public int SplitFeature { get; init; } = -1;

        public double Threshold { get; init; }

        public bool DefaultLeft { get; init; }

        /// <summary>None | Zero | NaN (missing_type cua LightGBM).</summary>
        public string MissingType { get; init; } = "None";

        public NutCay? Left { get; init; }

        public NutCay? Right { get; init; }

        public double LeafValue { get; init; }

        public bool LaLa
        {
            get { return Left == null || Right == null; }
        }
    }

    /// <summary>
    /// Rung cay LightGBM (muc tieu binary): xac suat = sigmoid(Σ gia tri la cua moi cay). Doc tu JSON cua
    /// booster.dump_model() — C# tu duyet cay, KHONG can thu vien native (ONNX) trong container.
    /// </summary>
    public sealed class RungCay : IPhanLoai
    {
        private readonly IReadOnlyList<NutCay> _cay;

        public RungCay(IReadOnlyList<NutCay> cay)
        {
            if (cay == null)
            {
                throw new ArgumentNullException(nameof(cay));
            }

            _cay = cay;
        }

        public int SoCay
        {
            get { return _cay.Count; }
        }

        public double XacSuat(double[] x)
        {
            return MoHinhHocMay.Sigmoid(DiemTho(x));
        }

        public double DiemTho(double[] x)
        {
            if (x == null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            double tong = 0;
            foreach (NutCay goc in _cay)
            {
                NutCay n = goc;
                while (!n.LaLa)
                {
                    double v = n.SplitFeature >= 0 && n.SplitFeature < x.Length ? x[n.SplitFeature] : double.NaN;
                    bool thieu = double.IsNaN(v) || (n.MissingType == "Zero" && v == 0);
                    bool sangTrai;
                    if (thieu && n.MissingType != "None")
                    {
                        sangTrai = n.DefaultLeft;
                    }
                    else
                    {
                        if (double.IsNaN(v))
                        {
                            v = 0;
                        }

                        sangTrai = v <= n.Threshold;
                    }

                    n = sangTrai ? n.Left! : n.Right!;
                }

                tong += n.LeafValue;
            }

            return tong;
        }
    }

    /// <summary>
    /// Mo hinh do ml-svc hoc (logistic hoac LightGBM). File JSON:
    /// { "version", "kind": "logistic"|"lightgbm", "features": [ten...], "start": {...}, "pass": {...} }.
    /// Dac trung map THEO TEN vao DacTrung.Ten — mo hinh cu thieu dac trung moi van chay (dac trung la → NaN).
    /// </summary>
    public sealed class MoHinhHocMay : IMoHinhGoiY
    {
        private readonly IPhanLoai _batDau;
        private readonly IPhanLoai _dat;
        private readonly int[] _viTri;

        public MoHinhHocMay(string phienBan, string loai, IReadOnlyList<string> dacTrung, IPhanLoai batDau, IPhanLoai dat)
        {
            if (string.IsNullOrWhiteSpace(phienBan))
            {
                throw new ArgumentException("Thieu phien ban mo hinh.", nameof(phienBan));
            }

            if (dacTrung == null)
            {
                throw new ArgumentNullException(nameof(dacTrung));
            }

            if (batDau == null || dat == null)
            {
                throw new ArgumentNullException(nameof(batDau));
            }

            PhienBan = phienBan;
            Loai = loai;
            _batDau = batDau;
            _dat = dat;
            _viTri = new int[dacTrung.Count];
            for (int i = 0; i < dacTrung.Count; i++)
            {
                _viTri[i] = IndexOf(DacTrung.Ten, dacTrung[i]);
            }
        }

        public string PhienBan { get; }

        public string Loai { get; }

        public double PBatDau(double[] x)
        {
            return _batDau.XacSuat(SapXep(x));
        }

        public double PDat(double[] x)
        {
            return _dat.XacSuat(SapXep(x));
        }

        private double[] SapXep(double[] x)
        {
            if (x == null)
            {
                throw new ArgumentNullException(nameof(x));
            }

            double[] y = new double[_viTri.Length];
            for (int i = 0; i < _viTri.Length; i++)
            {
                int j = _viTri[i];
                y[i] = j >= 0 && j < x.Length ? x[j] : double.NaN;
            }

            return y;
        }

        public static double Sigmoid(double z)
        {
            if (z >= 0)
            {
                return 1.0 / (1.0 + Math.Exp(-z));
            }

            double e = Math.Exp(z);
            return e / (1.0 + e);
        }

        private static int IndexOf(IReadOnlyList<string> ds, string ten)
        {
            for (int i = 0; i < ds.Count; i++)
            {
                if (string.Equals(ds[i], ten, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Doc file mo hinh JSON cua ml-svc. Sai dinh dang → FormatException (giu mo hinh dang chay).</summary>
        public static MoHinhHocMay Doc(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new FormatException("File mo hinh rong.");
            }

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement goc = doc.RootElement;
                string phienBan = ChuoiBatBuoc(goc, "version");
                string loai = ChuoiBatBuoc(goc, "kind");
                List<string> dacTrung = new List<string>();
                foreach (JsonElement e in MangBatBuoc(goc, "features").EnumerateArray())
                {
                    dacTrung.Add(e.GetString() ?? string.Empty);
                }

                IPhanLoai batDau = DocPhanLoai(loai, Con(goc, "start"), dacTrung.Count);
                IPhanLoai dat = DocPhanLoai(loai, Con(goc, "pass"), dacTrung.Count);
                return new MoHinhHocMay(phienBan, loai, dacTrung, batDau, dat);
            }
        }

        private static IPhanLoai DocPhanLoai(string loai, JsonElement e, int soDacTrung)
        {
            if (loai == "logistic")
            {
                double chan = e.GetProperty("intercept").GetDouble();
                double[] w = SoThuc(MangBatBuoc(e, "coef"));
                JsonElement m;
                JsonElement s;
                double[] tb = e.TryGetProperty("mean", out m) ? SoThuc(m) : new double[w.Length];
                double[] th = e.TryGetProperty("scale", out s) ? SoThuc(s) : MotHet(w.Length);
                if (w.Length != soDacTrung)
                {
                    throw new FormatException("Logistic: so trong so khac so dac trung.");
                }

                return new Logistic(chan, w, tb, th);
            }

            if (loai == "lightgbm")
            {
                List<NutCay> cay = new List<NutCay>();
                foreach (JsonElement t in MangBatBuoc(e, "trees").EnumerateArray())
                {
                    cay.Add(DocNut(t, 0));
                }

                return new RungCay(cay);
            }

            throw new FormatException("Loai mo hinh khong ho tro: " + loai);
        }

        private static NutCay DocNut(JsonElement e, int doSau)
        {
            if (doSau > 64)
            {
                throw new FormatException("Cay qua sau.");
            }

            JsonElement la;
            if (e.TryGetProperty("leaf_value", out la))
            {
                return new NutCay { LeafValue = la.GetDouble() };
            }

            JsonElement dt;
            JsonElement dl;
            JsonElement mt;
            string kieu = e.TryGetProperty("decision_type", out dt) ? dt.GetString() ?? "<=" : "<=";
            if (kieu != "<=")
            {
                throw new FormatException("Chi ho tro split so (decision_type <=).");
            }

            double nguong = e.GetProperty("threshold").ValueKind == JsonValueKind.String
                ? double.Parse(e.GetProperty("threshold").GetString()!, CultureInfo.InvariantCulture)
                : e.GetProperty("threshold").GetDouble();

            return new NutCay
            {
                SplitFeature = e.GetProperty("split_feature").GetInt32(),
                Threshold = nguong,
                DefaultLeft = e.TryGetProperty("default_left", out dl) && dl.GetBoolean(),
                MissingType = e.TryGetProperty("missing_type", out mt) ? mt.GetString() ?? "None" : "None",
                Left = DocNut(e.GetProperty("left_child"), doSau + 1),
                Right = DocNut(e.GetProperty("right_child"), doSau + 1),
            };
        }

        private static string ChuoiBatBuoc(JsonElement e, string ten)
        {
            JsonElement v;
            if (!e.TryGetProperty(ten, out v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
            {
                throw new FormatException("File mo hinh thieu '" + ten + "'.");
            }

            return v.GetString()!;
        }

        private static JsonElement MangBatBuoc(JsonElement e, string ten)
        {
            JsonElement v;
            if (!e.TryGetProperty(ten, out v) || v.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("File mo hinh thieu mang '" + ten + "'.");
            }

            return v;
        }

        private static JsonElement Con(JsonElement e, string ten)
        {
            JsonElement v;
            if (!e.TryGetProperty(ten, out v) || v.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("File mo hinh thieu '" + ten + "'.");
            }

            return v;
        }

        private static double[] SoThuc(JsonElement mang)
        {
            double[] kq = new double[mang.GetArrayLength()];
            int i = 0;
            foreach (JsonElement e in mang.EnumerateArray())
            {
                kq[i] = e.GetDouble();
                i++;
            }

            return kq;
        }

        private static double[] MotHet(int n)
        {
            double[] kq = new double[n];
            for (int i = 0; i < n; i++)
            {
                kq[i] = 1;
            }

            return kq;
        }
    }
}
