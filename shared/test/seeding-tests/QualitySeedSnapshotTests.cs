using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.Contracts.Quality;

namespace Crowd.Seeding.Tests
{
    /// <summary>
    /// quality-svc viet bang Python nen khong doc duoc KichBanSeed (C#). Test nay xuat
    /// phan quality can — ket qua cham cua cac du an seed — ra shared/seeding/quality-seed.json,
    /// va canh file luon khop kich ban (giong anh chup hop dong event).
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
        /// Chi KET QUA cua chinh quality-svc (kien truc 3.22): de bai, bai nop va nhan quality doc qua gRPC tu
        /// project-svc (da seed o do), nen file chi con: du an nao co bai nop (danh dau can tinh DS) va — du an
        /// Completed — moc cham (project_gradings) + moc dong. Bang chung uy tin (so cau so duoc / giong / cau vang)
        /// nam tren project.attempts da seed, quality doc qua gRPC. Thoi diem ghi bang "so giay truoc luc seed" de
        /// file khong doi theo ngay chay.
        /// </summary>
        private static string TaoNoiDung()
        {
            JsonArray duAn = new JsonArray();
            DateTimeOffset moc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            foreach (SeedProject p in KichBanSeed.Projects)
            {
                if (!p.DaChay)
                {
                    continue;
                }

                int daNop = 0;
                foreach (SeedAttempt b in p.Attempts)
                {
                    if (b.State == SeedAttemptState.Submitted)
                    {
                        daNop++;
                    }
                }

                JsonObject o = new JsonObject
                {
                    ["projectId"] = p.Id.ToString(),
                    ["submittedAttempts"] = daNop,
                };

                if (p.Stage == SeedStage.Completed)
                {
                    ProjectGraded cham = SeedEvents.DaCham(p, moc);
                    o["gradedAgoSeconds"] = (long)p.GradedAgo!.Value.TotalSeconds;
                    o["completedAgoSeconds"] = (long)p.CompletedAgo!.Value.TotalSeconds;
                    o["majorityUsed"] = cham.MajorityUsed;
                }

                duAn.Add(o);
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
