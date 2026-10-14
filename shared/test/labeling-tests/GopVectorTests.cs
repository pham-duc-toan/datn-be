using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tests
{
    /// <summary>
    /// Bo vi du GOP NHAN dung chung cho C# va Python: contracts/labeling/testdata/gop.vectors.json.
    ///
    /// Thuat toan gop nam o HAI noi — Crowd.Labeling (annotation-svc chot ket qua tu nhan da duyet) va
    /// quality-svc app/gop.py (dong thuan tren nhan vua nop). Hai ben chay cung bo vi du nay va phai ra
    /// cung "expected"; lech la test cua ben do do.
    ///
    /// Sua thuat toan / them vi du: UPDATE_GOP_VECTORS=1 dotnet test shared/test/labeling-tests, xem lai
    /// phan "expected" vua sinh (git diff) roi commit, sau do sua app/gop.py cho test Python xanh.
    /// </summary>
    public sealed class GopVectorTests
    {
        private const string BienMoiTruongCapNhat = "UPDATE_GOP_VECTORS";

        private static readonly string DuongDan = Path.Combine(TimGocRepo(), "contracts", "labeling", "testdata", "gop.vectors.json");

        [Fact]
        public void Ket_qua_gop_khop_bo_vi_du_chung()
        {
            JsonArray viDu = (JsonArray)JsonNode.Parse(File.ReadAllText(DuongDan))!;
            bool capNhat = Environment.GetEnvironmentVariable(BienMoiTruongCapNhat) == "1";
            List<string> lech = new List<string>();

            foreach (JsonNode? n in viDu)
            {
                JsonObject v = (JsonObject)n!;
                JsonObject thuc = Chay(v);

                if (capNhat)
                {
                    v["expected"] = thuc;
                    continue;
                }

                JsonNode? mong = v["expected"];
                if (mong == null || !GiongNhau(mong, thuc))
                {
                    lech.Add(v["name"]!.GetValue<string>() + "\n   mong: " + (mong == null ? "(chua co)" : mong.ToJsonString()) + "\n   thuc: " + thuc.ToJsonString());
                }
            }

            if (capNhat)
            {
                JsonSerializerOptions tuyChon = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                File.WriteAllText(DuongDan, viDu.ToJsonString(tuyChon).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
                return;
            }

            Assert.True(lech.Count == 0, "Ket qua gop lech bo vi du (" + BienMoiTruongCapNhat + "=1 de cap nhat):\n" + string.Join("\n", lech));
        }

        private static JsonObject Chay(JsonObject v)
        {
            LabelSchema tapNhan = LabelSchema.Doc(v["schema"]!.ToJsonString());
            SampleMetadata? mau = v["metadata"] == null ? null : SampleMetadata.Tu(RawJson.Tu(v["metadata"]!.DeepClone()));

            List<LabelPayload> nhan = new List<LabelPayload>();
            foreach (JsonNode? l in (JsonArray)v["labels"]!)
            {
                // Tao (khong phai TuLuuTru): vi du phai la nhan HOP LE, va di qua buoc chuan hoa nhu nhan that.
                nhan.Add(LabelPayload.Tao(tapNhan, l!.ToJsonString(), mau));
            }

            return LabelAggregator.Gop(tapNhan, nhan, NguongKhop.CuaThuVien, mau, v["minF1"]!.GetValue<double>());
        }

        /// <summary>So sanh sau; so thuc lech duoi 1e-6 coi nhu bang (4 va 4.0 la mot).</summary>
        private static bool GiongNhau(JsonNode? a, JsonNode? b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }

            JsonObject? oa = a as JsonObject;
            JsonObject? ob = b as JsonObject;
            if (oa != null || ob != null)
            {
                if (oa == null || ob == null || oa.Count != ob.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<string, JsonNode?> kv in oa)
                {
                    JsonNode? kia;
                    if (!ob.TryGetPropertyValue(kv.Key, out kia) || !GiongNhau(kv.Value, kia))
                    {
                        return false;
                    }
                }

                return true;
            }

            JsonArray? ma = a as JsonArray;
            JsonArray? mb = b as JsonArray;
            if (ma != null || mb != null)
            {
                if (ma == null || mb == null || ma.Count != mb.Count)
                {
                    return false;
                }

                for (int i = 0; i < ma.Count; i++)
                {
                    if (!GiongNhau(ma[i], mb[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (a.GetValueKind() == JsonValueKind.Number && b.GetValueKind() == JsonValueKind.Number)
            {
                return Math.Abs(double.Parse(a.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)
                                - double.Parse(b.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)) < 1e-6;
            }

            return a.ToJsonString() == b.ToJsonString();
        }

        private static string TimGocRepo()
        {
            DirectoryInfo? thuMuc = new DirectoryInfo(AppContext.BaseDirectory);
            while (thuMuc != null && !Directory.Exists(Path.Combine(thuMuc.FullName, "contracts", "labeling")))
            {
                thuMuc = thuMuc.Parent;
            }

            if (thuMuc == null)
            {
                throw new InvalidOperationException("Khong tim thay goc repo (thu muc contracts/labeling).");
            }

            return thuMuc.FullName;
        }
    }
}
