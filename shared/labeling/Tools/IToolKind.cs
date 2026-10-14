using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>Tham so khi gop ket qua cua nhieu nguoi cho mot mau.</summary>
    internal sealed class ThamSoGop
    {
        public ThamSoGop(double? nguongMacDinh, SampleMetadata mau, double f1ToiThieu)
        {
            NguongMacDinh = nguongMacDinh;
            Mau = mau;
            F1ToiThieu = f1ToiThieu;
        }

        /// <summary>Nguong mac dinh cua loai cong cu (setting labeling.threshold.*); null = mac dinh cua thu vien.</summary>
        public double? NguongMacDinh { get; }

        /// <summary>Thong tin mau (kich thuoc anh de chuan hoa do lech diem / duong).</summary>
        public SampleMetadata Mau { get; }

        /// <summary>
        /// Cong cu dang danh sach: mot nguoi "dong y" voi ket qua gop khi F1 giua cac doi tuong cua
        /// ho va cac doi tuong da gop dat muc nay (setting quality.consensus_min_f1).
        /// </summary>
        public double F1ToiThieu { get; }
    }

    /// <summary>
    /// Hanh vi cua MOT loai cong cu. Hinh dang JSON da duoc JSON Schema kiem
    /// truoc khi toi day — o day chi con NGU NGHIA.
    /// </summary>
    internal interface IToolKind
    {
        string Kind { get; }

        /// <summary>
        /// Kiem ket qua theo cau hinh cong cu va thong tin mau (khung trong anh,
        /// doan van trong van ban...). Tra ve ban CHUAN HOA. Sai thi nem
        /// LabelFormatException ma "nhan_khong_hop_le".
        /// </summary>
        JsonNode KiemVaChuanHoa(JsonNode ketQua, ToolDefinition tool, SampleMetadata mau);

        /// <summary>
        /// Ket qua nop co khop dap an khong — cham cau vang, bai test dau vao.
        /// Nguong: matchThreshold cua cong cu, khong co thi nguongMacDinh (setting
        /// labeling.threshold.*), khong co nua thi mac dinh cua thu vien.
        /// </summary>
        bool Khop(JsonNode nop, JsonNode dapAn, ToolDefinition tool, double? nguongMacDinh, SampleMetadata mau);

        /// <summary>
        /// Gop ket qua cua nhieu nguoi cho mot mau. Tra ve object co:
        ///   method     cach gop: majority | cluster | medoid | median | borda | none
        ///   final      ket qua gop; null khi TRANH CHAP (khong co ket qua duoc da so ung ho)
        ///   disputed   tranh chap hay khong
        ///   agrees     tung nguoi (theo thu tu dua vao) co khop ket qua gop khong
        /// "none" = cong cu khong gop tu dong (freeText) — ket qua cuoi la cac nhan da duyet.
        ///
        /// quality-svc (Python, app/gop.py) cai lai DUNG thuat toan nay; hai ben doi chieu bang
        /// contracts/labeling/testdata/gop.vectors.json.
        /// </summary>
        JsonObject Gop(IReadOnlyList<JsonNode> ketQua, ToolDefinition tool, ThamSoGop thamSo);
    }
}
