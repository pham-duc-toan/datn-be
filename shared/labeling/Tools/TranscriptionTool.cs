using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// Chep lai NOI DUNG CO SAN thanh van ban — loi noi trong audio / video, chu trong anh:
    /// {"text":"xin chao"}. Co mot dap an dung, nen cham va gop duoc (khac freeText).
    ///
    /// Cham dap an bang ti le loi ky tu (CER) sau khi chuan hoa (chu thuong, gop
    /// khoang trang): CER &lt;= nguong (mac dinh 0.1) la khop.
    ///
    /// Gop nhieu nguoi: lay ban "O GIUA" — ban co tong do lech toi cac ban khac nho nhat (hoa thi
    /// ban nop truoc). Mot nguoi dong y khi CER cua ho so voi ban do &lt;= nguong; khong qua mot nua
    /// dong y → tranh chap. Khong tron chu cua nhieu ban voi nhau (ROVER): ket qua luon la mot ban
    /// do nguoi that go, khong sinh ra cau khong ai viet.
    /// </summary>
    internal sealed class TranscriptionTool : IToolKind
    {
        public const double NguongMacDinh = 0.1;

        public string Kind
        {
            get { return ToolKinds.Transcription; }
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            return KiemVanBan(ketQua, tool);
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            double nguong = tool.MatchThreshold ?? nguongMacDinh ?? NguongMacDinh;
            return HinhHoc.Cer(DocText(nop), DocText(dapAn)) <= nguong;
        }

        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool, ThamSoGop thamSo)
        {
            List<string> chu = new List<string>();
            foreach (JsonNode k in ketQua)
            {
                chu.Add(DocText(k));
            }

            string giua = chu[ThuocTinh.BanOGiua(chu)];
            double nguong = tool.MatchThreshold ?? thamSo.NguongMacDinh ?? NguongMacDinh;

            JsonArray dongY = new JsonArray();
            int soDongY = 0;
            foreach (string c in chu)
            {
                bool khop = HinhHoc.Cer(c, giua) <= nguong + HinhHoc.SaiSoNguong;
                dongY.Add(khop);
                if (khop)
                {
                    soDongY++;
                }
            }

            bool tranhChap = soDongY * 2 <= chu.Count;
            return new JsonObject
            {
                ["method"] = "medoid",
                ["final"] = tranhChap ? null : new JsonObject { ["text"] = giua },
                ["agrees"] = dongY,
                ["disputed"] = tranhChap,
            };
        }

        /// <summary>Luat chung cua cong cu nhap van ban (transcription, freeText).</summary>
        internal static JsonNode KiemVanBan(JsonNode ketQua, ToolDefinition tool)
        {
            string text = DocText(ketQua);

            if (tool.Required && text.Trim().Length == 0)
            {
                throw new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "': van ban khong duoc rong.");
            }

            if (text.Length > tool.MaxLength)
            {
                throw new LabelFormatException("nhan_khong_hop_le", "Cong cu '" + tool.Name + "': toi da " + tool.MaxLength + " ky tu.");
            }

            return new JsonObject { ["text"] = text };
        }

        private static string DocText(JsonNode ketQua)
        {
            return ketQua["text"]!.GetValue<string>();
        }
    }

    /// <summary>
    /// Van ban TU DO cho moi loai du lieu — mo ta anh, dich, tom tat, tra loi cau hoi, viet lai cau
    /// tra loi cua LLM: {"text":"mot con meo nam tren ghe"}.
    ///
    /// Cau tra loi MO, khong co mot dap an dung duy nhat, nen:
    ///   - KHONG cham: dap an cau vang co cong cu nay thi bo qua no (cac cong cu khac van cham);
    ///   - KHONG gop tu dong ("none"): ket qua cuoi la cac ban da duoc nguoi duyet chap nhan.
    /// Can cham / gop (chep lai noi dung co san) thi dung transcription.
    /// </summary>
    internal sealed class FreeTextTool : IToolKind
    {
        public string Kind
        {
            get { return ToolKinds.FreeText; }
        }

        public JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau)
        {
            return TranscriptionTool.KiemVanBan(ketQua, tool);
        }

        public bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau)
        {
            return true;
        }

        public JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool, ThamSoGop thamSo)
        {
            return new JsonObject { ["method"] = "none" };
        }
    }
}
