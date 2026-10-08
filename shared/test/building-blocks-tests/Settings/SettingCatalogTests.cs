using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.BuildingBlocks.Settings;

namespace Crowd.BuildingBlocks.Tests.Settings
{
    /// <summary>
    /// Catalog setting la NGUON DUY NHAT cua kieu / mac dinh / gioi han. quality-svc
    /// (Python) khong doc duoc C#, nen test nay xuat catalog ra
    /// shared/settings/catalog.json va canh file luon khop code.
    ///
    /// Cap nhat sau khi sua catalog:
    ///     UPDATE_SETTINGS_CATALOG=1 dotnet test shared/test/building-blocks-tests
    /// </summary>
    public sealed class SettingCatalogTests
    {
        private const string BienMoiTruongCapNhat = "UPDATE_SETTINGS_CATALOG";

        private static readonly JsonSerializerOptions JsonDep = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        [Fact]
        public void Khoa_khong_trung_va_moi_hang_so_deu_co_dinh_nghia()
        {
            HashSet<string> khoa = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettingDefinition d in SettingCatalog.TatCa)
            {
                Assert.True(khoa.Add(d.Key), "Trung khoa " + d.Key);
            }

            foreach (System.Reflection.FieldInfo f in typeof(SettingKeys).GetFields())
            {
                string? giaTri = f.GetValue(null) as string;
                Assert.NotNull(giaTri);
                Assert.True(khoa.Contains(giaTri!), "SettingKeys." + f.Name + " chua co trong catalog");
            }

            Assert.Equal(khoa.Count, typeof(SettingKeys).GetFields().Length);
        }

        [Fact]
        public void Kiem_gia_tri_theo_kieu_va_gioi_han()
        {
            SettingDefinition phi = SettingCatalog.Lay(SettingKeys.FeePlatformPercent);
            Assert.Null(phi.KiemGiaTri(JsonValue.Create(25)));
            Assert.NotNull(phi.KiemGiaTri(JsonValue.Create(25.5)));
            Assert.NotNull(phi.KiemGiaTri(JsonValue.Create(-1)));
            Assert.NotNull(phi.KiemGiaTri(JsonValue.Create("30")));
            Assert.NotNull(phi.KiemGiaTri(null));

            SettingDefinition tuDuyet = SettingCatalog.Lay(SettingKeys.ProjectAutoApprove);
            Assert.Null(tuDuyet.KiemGiaTri(JsonValue.Create(true)));
            Assert.NotNull(tuDuyet.KiemGiaTri(JsonValue.Create(1)));

            // Gia tri doc tu JSON (DB / event) cung qua dung cua kiem.
            Assert.Null(phi.KiemGiaTri(JsonNode.Parse("40")));
        }

        [Fact]
        public void File_catalog_json_khop_code()
        {
            string hienTai = TaoNoiDung();
            string duongDan = Path.Combine(TimGocRepo(), "shared", "settings", "catalog.json");

            if (Environment.GetEnvironmentVariable(BienMoiTruongCapNhat) == "1")
            {
                File.WriteAllText(duongDan, hienTai);
            }

            Assert.True(File.Exists(duongDan), "Chua co " + duongDan + ". Chay voi " + BienMoiTruongCapNhat + "=1.");

            string daCommit = File.ReadAllText(duongDan).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.True(
                daCommit == hienTai,
                "catalog.json lech SettingCatalog. Chay: " + BienMoiTruongCapNhat + "=1 dotnet test shared/test/building-blocks-tests");
        }

        private static string TaoNoiDung()
        {
            JsonArray ds = new JsonArray();
            foreach (SettingDefinition d in SettingCatalog.TatCa)
            {
                JsonObject o = new JsonObject
                {
                    ["key"] = d.Key,
                    ["group"] = d.Group,
                    ["type"] = d.TypeName,
                    ["default"] = d.DefaultValue.DeepClone(),
                    ["min"] = d.Min.HasValue ? JsonValue.Create(d.Min.Value) : null,
                    ["max"] = d.Max.HasValue ? JsonValue.Create(d.Max.Value) : null,
                    ["unit"] = d.Unit,
                    ["effect"] = d.EffectName,
                    ["description"] = d.Description,
                };
                ds.Add(o);
            }

            return ds.ToJsonString(JsonDep).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        }

        private static string TimGocRepo()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "datn.slnx")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                throw new InvalidOperationException("Khong tim thay goc repo (datn.slnx).");
            }

            return dir.FullName;
        }
    }
}
