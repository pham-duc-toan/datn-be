using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crowd.BuildingBlocks.Messaging;
using Crowd.Contracts.Project;
using Crowd.Contracts.Tasking;

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
        /// Chi cac truong quality-svc luu: ban sao du an (bang projects), bai DA NOP (attempts + labels) va —
        /// du an Completed — ket qua cham (project_collections, attempt_grades). Thoi diem ghi bang "so giay
        /// truoc luc seed" de file khong doi theo ngay chay.
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

                ProjectPublished e = SeedEvents.DaPublish(p, moc);
                JsonArray bai = new JsonArray();
                foreach (SeedAttempt b in p.Attempts)
                {
                    if (b.State != SeedAttemptState.Submitted)
                    {
                        continue;
                    }

                    bai.Add(TaoBai(p, b, moc));
                }

                JsonObject o = new JsonObject
                {
                    ["projectId"] = e.ProjectId.ToString(),
                    ["ownerId"] = e.OwnerId.ToString(),
                    ["modality"] = e.Modality,
                    ["labelSchema"] = e.LabelSchema.Node().DeepClone(),
                    ["passThresholdPercent"] = e.PassThresholdPercent,
                    ["slots"] = e.Slots,
                    ["questionCount"] = e.QuestionCount,
                    ["createdAgoSeconds"] = (long)p.CreatedAgo.TotalSeconds,
                    ["attempts"] = bai,
                };

                if (p.Stage == SeedStage.Completed)
                {
                    o["gradedAgoSeconds"] = (long)p.GradedAgo!.Value.TotalSeconds;
                    o["completedAgoSeconds"] = (long)p.CompletedAgo!.Value.TotalSeconds;
                    o["majorityUsed"] = SeedEvents.DaCham(p, moc).MajorityUsed;
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

        private static JsonObject TaoBai(SeedProject p, SeedAttempt b, DateTimeOffset moc)
        {
            AttemptSubmitted nop = SeedEvents.BaiNop(p, b, moc);
            JsonArray cauHoi = new JsonArray();
            JsonArray nhan = new JsonArray();
            foreach (AttemptAnswer a in nop.Answers)
            {
                cauHoi.Add(a.SampleId.ToString());
                if (a.LabelPayload != null)
                {
                    nhan.Add(new JsonObject
                    {
                        ["sampleId"] = a.SampleId.ToString(),
                        ["data"] = JsonNode.Parse(a.LabelPayload.DataJson),
                        ["sampleMetadata"] = a.SampleMetadata.Node().DeepClone(),
                    });
                }
            }

            JsonObject o = new JsonObject
            {
                ["attemptId"] = b.AttemptId.ToString(),
                ["labelerId"] = b.LabelerId.ToString(),
                ["sampleIds"] = cauHoi,
                ["labels"] = nhan,
                ["goldTotal"] = b.GoldTotal,
                ["goldCorrect"] = b.GoldCorrect,
                ["startedAgoSeconds"] = (long)b.StartedAgo.TotalSeconds,
                ["submittedAgoSeconds"] = (long)b.SubmittedAgo!.Value.TotalSeconds,
            };

            if (b.Result != null)
            {
                o["result"] = new JsonObject
                {
                    ["comparableCount"] = b.Result.ComparableCount,
                    ["matchedCount"] = b.Result.MatchedCount,
                    ["agreementPercent"] = b.Result.AgreementPercent,
                    ["passed"] = b.Result.Passed,
                };
            }

            return o;
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
