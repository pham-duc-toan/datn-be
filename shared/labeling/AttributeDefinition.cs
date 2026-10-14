using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.Labeling
{
    /// <summary>
    /// Mot THUOC TINH cua tung doi tuong trong cong cu dang danh sach, vd:
    ///     {"name":"che_khuat","type":"boolean"}
    ///     {"name":"mau","type":"choice","options":["do","xanh"],"required":true}
    ///     {"name":"chu","type":"text","maxLength":200}        ← OCR: khung + chu trong khung
    ///     {"name":"so_nguoi","type":"number"}
    ///
    /// Trong nhan, doi tuong mang them "attributes": {"che_khuat":true,"mau":"do"}.
    /// </summary>
    public sealed class AttributeDefinition
    {
        public const string Choice = "choice";
        public const string Boolean = "boolean";
        public const string Text = "text";
        public const string Number = "number";

        public const int MaxLengthMacDinh = 500;

        private AttributeDefinition()
        {
            Name = string.Empty;
            Type = string.Empty;
            Options = Array.Empty<string>();
        }

        public string Name { get; private set; }

        /// <summary>choice | boolean | text | number</summary>
        public string Type { get; private set; }

        /// <summary>choice: cac lua chon.</summary>
        public IReadOnlyList<string> Options { get; private set; }

        /// <summary>Doi tuong nao cung phai co thuoc tinh nay.</summary>
        public bool Required { get; private set; }

        /// <summary>text: so ky tu toi da.</summary>
        public int MaxLength { get; private set; }

        public bool CoLuaChon(string giaTri)
        {
            foreach (string o in Options)
            {
                if (string.Equals(o, giaTri, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static AttributeDefinition Doc(JsonObject o)
        {
            AttributeDefinition a = new AttributeDefinition();
            a.Name = o["name"]!.GetValue<string>();
            a.Type = o["type"]!.GetValue<string>();

            List<string> luaChon = new List<string>();
            JsonArray? mang = o["options"] as JsonArray;
            if (mang != null)
            {
                foreach (JsonNode? n in mang)
                {
                    luaChon.Add(n!.GetValue<string>().Trim());
                }
            }

            a.Options = luaChon;

            JsonNode? batBuoc = o["required"];
            a.Required = batBuoc != null && batBuoc.GetValue<bool>();

            JsonNode? doDai = o["maxLength"];
            a.MaxLength = doDai == null ? MaxLengthMacDinh : Crowd.Labeling.Tools.HinhHoc.SoNguyen(doDai, "tap_nhan_sai_dinh_dang");
            return a;
        }

        /// <summary>Dang CHUAN (dien gia tri mac dinh tuong minh).</summary>
        internal JsonObject ToJson()
        {
            JsonObject o = new JsonObject();
            o["name"] = Name;
            o["type"] = Type;
            o["required"] = Required;

            if (Type == Choice)
            {
                JsonArray mang = new JsonArray();
                foreach (string l in Options)
                {
                    mang.Add(l);
                }

                o["options"] = mang;
            }

            if (Type == Text)
            {
                o["maxLength"] = MaxLength;
            }

            return o;
        }
    }
}
