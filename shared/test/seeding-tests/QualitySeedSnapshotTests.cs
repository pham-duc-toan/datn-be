using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Project;

namespace Crowd.Seeding.Tests
{
    /// <summary>
    /// quality-svc viet bang Python nen khong doc duoc KichBanSeed (C#). Test nay xuat
    /// phan quality can — ban sao cac du an seed DA PUBLISH, dung hinh dang
    /// project.published — ra shared/seeding/quality-seed.json, va canh file luon khop
    /// kich ban (giong anh chup hop dong event).
    ///
    /// Cap nhat sau khi sua kich ban:
    ///     UPDATE_SEED_SNAPSHOT=1 dotnet test shared/test/seeding-tests
    /// </summary>
    public sealed class QualitySeedSnapshotTests
    {
        private const string BienMoiTruongCapNhat = "UPDATE_SEED_SNAPSHOT";

        private static readonly string DuongDan =
            Path.Combine(TimGocRepo(), "shared", "seeding", "quality-seed.json");

        [Fact]
        public void File_seed_cua_quality_khop_kich_ban()
        {
            string hienTai = TaoNoiDung();

            if (Environment.GetEnvironmentVariable(BienMoiTruongCapNhat) == "1")
            {
                File.WriteAllText(DuongDan, hienTai);
            }

            Assert.True(File.Exists(DuongDan), "Chua co " + DuongDan + ". Chay voi " + BienMoiTruongCapNhat + "=1.");

            string daCommit = File.ReadAllText(DuongDan).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.True(
                daCommit == hienTai,
                "quality-seed.json lech kich ban seed. Chay: " + BienMoiTruongCapNhat + "=1 dotnet test shared/test/seeding-tests");
        }

        /// <summary>
        /// Chi cac truong quality-svc luu (bang projects). Bo deadline / thoi diem de
        /// file khong doi theo ngay chay.
        /// </summary>
        private static string TaoNoiDung()
        {
            JsonArray duAn = new JsonArray();
            DateTimeOffset moc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            foreach (SeedProject p in KichBanSeed.Projects)
            {
                if (p.Stage != SeedStage.Running)
                {
                    continue;
                }

                ProjectPublished e = SeedEvents.DaPublish(p, moc);
                duAn.Add(new JsonObject
                {
                    ["projectId"] = e.ProjectId.ToString(),
                    ["ownerId"] = e.OwnerId.ToString(),
                    ["modality"] = e.Modality,
                    ["labelSchema"] = e.LabelSchema.Node().DeepClone(),
                    ["redundancy"] = e.Redundancy,
                    ["maxRedundancy"] = e.MaxRedundancy,
                });
            }

            JsonObject goc = new JsonObject
            {
                ["_comment"] = "SINH TU DONG boi shared/test/seeding-tests/QualitySeedSnapshotTests.cs — khong sua tay.",
                ["projects"] = duAn,
            };

            return goc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
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
