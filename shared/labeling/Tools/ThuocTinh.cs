using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crowd.Labeling.Tools
{
    /// <summary>
    /// THUOC TINH cua tung doi tuong trong cong cu dang danh sach:
    ///     {"labelId":"xe","x":1,"y":2,"w":3,"h":4,"attributes":{"che_khuat":true,"bien_so":"30A-123.45"}}
    /// Khai trong tap nhan ("attributes" cua cong cu). Hai viec: kiem luc nop va so voi ban CHUAN (dap
    /// an cau vang). Gop thuoc tinh cua nhieu nguoi nam o quality-svc (app/gop.py, kien truc 3.24).
    /// </summary>
    internal static class ThuocTinh
    {
        public const string Khoa = "attributes";

        /// <summary>Kiem thuoc tinh cua mot doi tuong theo khai bao cua cong cu; bo object rong.</summary>
        public static void KiemVaChuanHoa(JsonObject phanTu, ToolDefinition tool)
        {
            JsonObject? tt = phanTu[Khoa] as JsonObject;

            if (tt != null)
            {
                foreach (KeyValuePair<string, JsonNode?> kv in tt)
                {
                    AttributeDefinition? khai = tool.ThuocTinh(kv.Key);
                    if (khai == null)
                    {
                        throw Sai(tool, "thuộc tính “" + kv.Key + "” không có trong tập nhãn.");
                    }

                    KiemGiaTri(khai, kv.Value, tool);
                }
            }

            foreach (AttributeDefinition khai in tool.Attributes)
            {
                if (!khai.Required)
                {
                    continue;
                }

                JsonNode? v = tt == null ? null : tt[khai.Name];
                if (v == null)
                {
                    throw Sai(tool, "đối tượng thiếu thuộc tính bắt buộc “" + khai.Name + "”.");
                }

                if (khai.Type == AttributeDefinition.Text && v.GetValue<string>().Trim().Length == 0)
                {
                    throw Sai(tool, "thuộc tính bắt buộc “" + khai.Name + "” không được để trống.");
                }
            }

            if (tt != null && tt.Count == 0)
            {
                phanTu.Remove(Khoa);
            }
        }

        /// <summary>
        /// Thuoc tinh cua doi tuong dang xet co khop ban CHUAN khong. Chi so cac thuoc tinh ban
        /// chuan CO gia tri: chuan khong ghi thi khong cham (dap an bo trong / nhom khong thong nhat).
        /// </summary>
        public static bool Khop(JsonObject chuan, JsonObject xet, ToolDefinition tool)
        {
            JsonObject? c = chuan[Khoa] as JsonObject;
            if (c == null)
            {
                return true;
            }

            JsonObject? x = xet[Khoa] as JsonObject;
            foreach (AttributeDefinition khai in tool.Attributes)
            {
                JsonNode? giaTriChuan = c[khai.Name];
                if (giaTriChuan == null)
                {
                    continue;
                }

                JsonNode? giaTriXet = x == null ? null : x[khai.Name];
                if (giaTriXet == null || !GiaTriKhop(khai, giaTriChuan, giaTriXet))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool GiaTriKhop(AttributeDefinition khai, JsonNode chuan, JsonNode xet)
        {
            if (khai.Type == AttributeDefinition.Number)
            {
                return Math.Abs(HinhHoc.So(chuan) - HinhHoc.So(xet)) <= HinhHoc.SaiSoNguong;
            }

            if (khai.Type == AttributeDefinition.Text)
            {
                return HinhHoc.Cer(xet.GetValue<string>(), chuan.GetValue<string>()) <= TranscriptionTool.NguongMacDinh + HinhHoc.SaiSoNguong;
            }

            return chuan.ToJsonString() == xet.ToJsonString();
        }

        private static void KiemGiaTri(AttributeDefinition khai, JsonNode? v, ToolDefinition tool)
        {
            JsonValueKind loai = v == null ? JsonValueKind.Null : v.GetValueKind();

            if (khai.Type == AttributeDefinition.Boolean)
            {
                if (loai != JsonValueKind.True && loai != JsonValueKind.False)
                {
                    throw Sai(tool, "thuộc tính “" + khai.Name + "” phải là có / không.");
                }
            }
            else if (khai.Type == AttributeDefinition.Number)
            {
                if (loai != JsonValueKind.Number)
                {
                    throw Sai(tool, "thuộc tính “" + khai.Name + "” phải là số.");
                }
            }
            else
            {
                if (loai != JsonValueKind.String)
                {
                    throw Sai(tool, "thuộc tính “" + khai.Name + "” phải là chữ.");
                }

                string chu = v!.GetValue<string>();
                if (khai.Type == AttributeDefinition.Choice && !khai.CoLuaChon(chu))
                {
                    throw Sai(tool, "thuộc tính “" + khai.Name + "”: “" + chu + "” không nằm trong các lựa chọn.");
                }

                if (khai.Type == AttributeDefinition.Text && chu.Length > khai.MaxLength)
                {
                    throw Sai(tool, "thuộc tính “" + khai.Name + "” tối đa " + khai.MaxLength + " ký tự.");
                }
            }
        }

        private static LabelFormatException Sai(ToolDefinition tool, string loi)
        {
            return new LabelFormatException("nhan_khong_hop_le", TenHienThi.TienToNhan(tool) + loi);
        }
    }
}
