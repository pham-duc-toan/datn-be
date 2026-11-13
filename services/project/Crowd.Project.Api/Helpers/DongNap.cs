using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Crowd.BuildingBlocks.Settings;
using Crowd.Labeling;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;

namespace Crowd.Project.Api.Helpers
{
    /// <summary>
    /// Mot DONG du lieu client gui len (text / pair / list / file) → doc truong, tao mau cho cac loai du lieu
    /// KHONG phai file (noi dung nam ngay trong database nen tao xong la ready).
    ///
    ///   text  {"text":...}
    ///   pair  {"a":...,"b":...}
    ///   list  {"items":["...","..."]}   2–50 muc
    ///   file  {"file":"&lt;key&gt;", "name"?, "durationSec"?, "width"?, "height"?, "fps"?}
    ///
    /// Moi loai deu nhan them "question"?: cau hoi RIENG cua mau (samples.question) — bo trong thi labeler thay
    /// cau hoi chung cua du an. Cau hoi khong nam trong content (truoc day pair / list co "prompt").
    ///
    /// Dong sai nem InvalidValueException — nguoi goi bo qua dong do va ghi ly do, mot dong hong khong lam hong ca lo.
    /// </summary>
    public static class DongNap
    {
        /// <summary>Mau danh sach (xep hang): so muc toi thieu / toi da — trung gioi han cua ranking.result.schema.json.</summary>
        private const int SoMucToiThieu = 2;

        private const int SoMucToiDa = 50;

        /// <summary>"question" cua dong (cau hoi rieng cua mau). null = khong gui / chuoi rong.</summary>
        public static string? CauHoi(JsonObject o)
        {
            if (o == null)
            {
                throw new ArgumentNullException(nameof(o));
            }

            return ChuoiTuyChon(o, "question");
        }

        /// <summary>
        /// Khoa chong trung cua mau: noi dung (Sha256) + cau hoi rieng — cung mot anh hoi hai cau khac nhau la hai
        /// mau. Khop voi chi muc ux_samples_project_sha256_question.
        /// </summary>
        public static string KhoaTrung(string sha256, string? question)
        {
            return sha256 + "|" + (question ?? string.Empty);
        }

        public static Sample TaoMauNoiDung(JsonObject o, string modality, string ten, Guid projectId, DateTimeOffset bayGio, ISettings settings)
        {
            if (o == null)
            {
                throw new ArgumentNullException(nameof(o));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            QuyDinhDuLieu quyDinh = QuyDinhTuSetting.DuLieu(settings);
            int doDaiToiDa = settings.SoNguyen(SettingKeys.DatasetTextMaxChars);

            switch (modality)
            {
                case Modalities.Text:
                {
                    string text = ChuoiBatBuoc(o, "text");
                    if (text.Length > doDaiToiDa)
                    {
                        throw new InvalidValueException("text_qua_dai", "văn bản tối đa " + doDaiToiDa + " ký tự.");
                    }

                    RawJson noiDung = RawJson.Tu(new JsonObject { ["text"] = text });
                    SampleMetadata md = new SampleMetadata { Length = text.Length };
                    return Sample.TaoTuNoiDung(projectId, Modalities.Text, noiDung, ten, Bam(noiDung.Json), md, bayGio, quyDinh);
                }

                case Modalities.Pair:
                {
                    JsonObject nd = new JsonObject
                    {
                        ["a"] = ChuoiBatBuoc(o, "a"),
                        ["b"] = ChuoiBatBuoc(o, "b"),
                    };

                    RawJson raw = RawJson.Tu(nd);
                    if (raw.Json.Length > doDaiToiDa)
                    {
                        throw new InvalidValueException("text_qua_dai", "nội dung cặp tối đa " + doDaiToiDa + " ký tự.");
                    }

                    return Sample.TaoTuNoiDung(projectId, Modalities.Pair, raw, ten, Bam(raw.Json), SampleMetadata.Rong, bayGio, quyDinh);
                }

                case Modalities.List:
                    return TaoDanhSach(o, ten, projectId, bayGio, quyDinh, doDaiToiDa);

                default:
                    throw new InvalidValueException("loai_du_lieu_khong_hop_le", "Loại dữ liệu " + modality + " không nạp bằng nội dung.");
            }
        }

        /// <summary>
        /// Mau DANH SACH (cong cu ranking): {"items":["...","..."]} — 2 den 50 muc, moi muc
        /// la mot chuoi khong rong. So muc ghi vao metadata (itemCount) de kiem nhan xep hang.
        /// </summary>
        private static Sample TaoDanhSach(JsonObject o, string ten, Guid projectId, DateTimeOffset bayGio, QuyDinhDuLieu quyDinh, int doDaiToiDa)
        {
            JsonArray? mang = o["items"] as JsonArray;
            if (mang == null || mang.Count < SoMucToiThieu || mang.Count > SoMucToiDa)
            {
                throw new InvalidValueException(
                    "items_khong_hop_le", "\"items\" phải là mảng từ " + SoMucToiThieu + " đến " + SoMucToiDa + " mục.");
            }

            JsonArray muc = new JsonArray();
            foreach (JsonNode? n in mang)
            {
                string? chu = null;
                JsonValue? v = n as JsonValue;
                if (v != null)
                {
                    v.TryGetValue(out chu);
                }

                if (string.IsNullOrWhiteSpace(chu))
                {
                    throw new InvalidValueException("items_khong_hop_le", "mỗi mục trong \"items\" phải là chuỗi không rỗng.");
                }

                muc.Add(chu);
            }

            JsonObject noiDung = new JsonObject { ["items"] = muc };
            RawJson nd = RawJson.Tu(noiDung);
            if (nd.Json.Length > doDaiToiDa)
            {
                throw new InvalidValueException("text_qua_dai", "nội dung danh sách tối đa " + doDaiToiDa + " ký tự.");
            }

            SampleMetadata md = new SampleMetadata { ItemCount = muc.Count };
            return Sample.TaoTuNoiDung(projectId, Modalities.List, nd, ten, Bam(nd.Json), md, bayGio, quyDinh);
        }

        /// <summary>Goi y thong tin file client khai trong dong (durationSec, width, height, fps) — chi dung khi may khong do duoc.</summary>
        public static SampleMetadata GoiYFile(JsonObject o)
        {
            if (o == null)
            {
                throw new ArgumentNullException(nameof(o));
            }

            double? rong = SoTuyChon(o, "width");
            double? cao = SoTuyChon(o, "height");
            return new SampleMetadata
            {
                DurationSec = SoTuyChon(o, "durationSec"),
                Width = rong.HasValue ? (int?)rong.Value : null,
                Height = cao.HasValue ? (int?)cao.Value : null,
                Fps = SoTuyChon(o, "fps"),
            };
        }

        public static string TenDong(JsonObject o, int viTri)
        {
            return ChuoiTuyChon(o, "name") ?? ("dong-" + (viTri + 1).ToString(CultureInfo.InvariantCulture));
        }

        public static string ChuoiBatBuoc(JsonObject o, string ten)
        {
            string? s = ChuoiTuyChon(o, ten);
            if (s == null || s.Trim().Length == 0)
            {
                throw new InvalidValueException("thieu_truong", "thiếu trường \"" + ten + "\".");
            }

            return s;
        }

        public static string? ChuoiTuyChon(JsonObject o, string ten)
        {
            JsonValue? v = o[ten] as JsonValue;
            string? s;
            return v != null && v.TryGetValue(out s) ? s : null;
        }

        public static double? SoTuyChon(JsonObject o, string ten)
        {
            JsonValue? v = o[ten] as JsonValue;
            double d;
            return v != null && v.TryGetValue(out d) && d > 0 ? d : null;
        }

        public static string Bam(string s)
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
        }

        /// <summary>Ghi them ly do bo qua, toi da <paramref name="toiDa" /> dong (con lai chi dem).</summary>
        public static void GhiLoi(List<string> loi, int viTri, string thongBao, int toiDa)
        {
            if (loi.Count < toiDa)
            {
                loi.Add("dòng " + (viTri + 1).ToString(CultureInfo.InvariantCulture) + ": " + thongBao);
            }
        }
    }
}
