using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Crowd.Labeling;

namespace Crowd.Seeding.Tests
{
    /// <summary>
    /// Kich ban seed phai DUNG LUAT NGHIEP VU — neu khong, seed se nem loi luc
    /// service khoi dong (domain tu chan), kho tim nguyen nhan hon nhieu. Test
    /// nay bat loi ngay khi ai do sua kich ban.
    /// </summary>
    public sealed class KichBanSeedTests
    {
        [Fact]
        public void SeedIds_cung_ten_cung_id_khac_ten_khac_id()
        {
            Assert.Equal(SeedIds.Tu("project:p1"), SeedIds.Tu("project:p1"));
            Assert.NotEqual(SeedIds.Tu("project:p1"), SeedIds.Tu("project:p2"));
        }

        [Fact]
        public void Moi_id_trong_kich_ban_la_duy_nhat()
        {
            List<Guid> ids = new List<Guid>();
            ids.AddRange(KichBanSeed.Users.Select(u => u.Id));
            ids.AddRange(KichBanSeed.Deposits.Select(d => d.IntentId));

            foreach (SeedProject p in KichBanSeed.Projects)
            {
                ids.Add(p.Id);
                ids.Add(p.DatasetId);
                ids.AddRange(p.Samples.Select(s => s.Id));
                ids.AddRange(p.Samples.Select(s => KichBanSeed.TaskIdCua(s.Id)));
                ids.AddRange(p.Gold.Select(g => g.Id));
                ids.AddRange(p.Attempts.Select(b => b.AttemptId));
                ids.AddRange(p.Attempts.SelectMany(b => b.Answers).Where(a => p.LaCauVang(a.SampleId) == false).Select(a => a.AnnotationId));
            }

            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void Ky_quy_bang_so_suat_nhan_thuong_cong_phi()
        {
            foreach (SeedProject p in KichBanSeed.Projects)
            {
                // Cung cong thuc LabelingProject.ChiPhiUocTinhVnd (khong co cong link).
                Assert.Equal(p.Slots * (p.RewardVnd + p.PlatformFeePerPassVnd), p.EscrowVnd);
                Assert.InRange(p.PassThresholdPercent, 60, 95);
            }
        }

        [Fact]
        public void Tien_nap_du_cho_moi_khoan_ky_quy()
        {
            foreach (SeedUser u in KichBanSeed.Users)
            {
                long nap = KichBanSeed.Deposits.Where(d => d.BusinessId == u.Id && d.DaThanhToan).Sum(d => d.AmountVnd);
                long kyQuy = KichBanSeed.Projects.Where(p => p.OwnerId == u.Id && p.DaKyQuy).Sum(p => p.EscrowVnd);
                Assert.True(nap >= kyQuy, u.Key + ": nap " + nap + " < ky quy " + kyQuy);
            }
        }

        [Fact]
        public void Du_an_can_test_co_du_cau_vang_cho_test()
        {
            foreach (SeedProject p in KichBanSeed.Projects.Where(x => x.RequireEntranceTest))
            {
                Assert.True(p.Gold.Count(g => g.ForEntranceTest) >= p.EntranceQuestionCount, p.Key);
            }
        }

        [Fact]
        public void Moc_thoi_gian_vong_doi_dung_thu_tu()
        {
            DateTimeOffset bayGio = DateTimeOffset.UtcNow;

            foreach (SeedProject p in KichBanSeed.Projects)
            {
                DateTimeOffset lucTao = bayGio - p.CreatedAgo;

                Assert.Equal(p.DaKyQuy, p.EscrowedAgo.HasValue);
                Assert.Equal(p.DaChay, p.PublishedAgo.HasValue);

                if (p.DaKyQuy)
                {
                    Assert.True(p.LucKyQuy(bayGio) >= lucTao, p.Key + ": ky quy truoc khi tao");
                }

                if (p.DaChay)
                {
                    Assert.True(p.LucDuyet(bayGio) >= p.LucKyQuy(bayGio), p.Key + ": duyet truoc khi ky quy");
                }
                else
                {
                    Assert.Empty(p.Attempts);
                }

                if (p.Stage == SeedStage.Completed)
                {
                    Assert.NotNull(p.GradedAgo);
                    Assert.NotNull(p.CompletedAgo);
                    Assert.True(p.GradedAgo >= p.CompletedAgo, p.Key + ": hoan thanh truoc khi cham");
                    Assert.True(p.PublishedAgo >= p.GradedAgo, p.Key + ": cham truoc khi chay");
                }
            }
        }

        [Fact]
        public void Bai_lam_hop_le_voi_cau_hoi_va_thanh_vien()
        {
            DateTimeOffset bayGio = DateTimeOffset.UtcNow;

            foreach (SeedProject p in KichBanSeed.Projects)
            {
                HashSet<Guid> labeler = new HashSet<Guid>(p.Members.Where(m => m.Role == SeedMemberRole.Labeler).Select(m => m.UserId));
                int soVangKiemTra = p.Gold.Count(g => !g.ForEntranceTest);

                // Moi labeler toi da MOT bai mot du an; bai da nop khong vuot so suat.
                Assert.Equal(p.Attempts.Count, p.Attempts.Select(b => b.LabelerId).Distinct().Count());
                Assert.True(p.Attempts.Count(b => b.State == SeedAttemptState.Submitted) <= p.Slots, p.Key + ": vuot so suat");

                foreach (SeedAttempt b in p.Attempts)
                {
                    Assert.Equal(p.Id, b.ProjectId);
                    Assert.Contains(b.LabelerId, labeler);
                    Assert.True(bayGio - b.StartedAgo > p.LucDuyet(bayGio), p.Key + ": lam bai truoc khi du an chay");
                    Assert.InRange(b.GoldCorrect, 0, b.GoldTotal);
                    Assert.Equal(soVangKiemTra, b.GoldTotal);

                    foreach (SeedAnswer a in b.Answers)
                    {
                        Assert.Contains(p.Samples, s => s.Id == a.SampleId);
                        // Nhan kiem nhu API that: tap nhan + metadata mau.
                        LabelPayload nhan = SeedEvents.NhanCua(p, a);
                        Assert.Equal(p.Modality, nhan.TaskType);
                    }

                    if (b.State == SeedAttemptState.Submitted)
                    {
                        Assert.NotNull(b.SubmittedAgo);
                        Assert.True(b.SubmittedAgo <= b.StartedAgo, "nop truoc khi bat dau");
                    }
                    else
                    {
                        Assert.Null(b.SubmittedAgo);
                    }

                    // Ket qua cham chi co o du an Completed, va dung luat: dat = du nguong VA dung het cau vang.
                    Assert.Equal(p.Stage == SeedStage.Completed, b.Result != null);
                    if (b.Result != null)
                    {
                        bool dat = b.Result.AgreementPercent >= p.PassThresholdPercent && b.GoldCorrect == b.GoldTotal;
                        Assert.Equal(dat, b.Result.Passed);
                    }
                }
            }
        }

        [Fact]
        public void Anh_png_tat_dinh_va_khong_trung_trong_mot_du_an()
        {
            byte[] lan1 = AnhMauPng.Tao(10, 20, 30, 64);
            byte[] lan2 = AnhMauPng.Tao(10, 20, 30, 64);

            Assert.Equal(lan1, lan2);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, lan1.Take(8).ToArray());

            // project-svc UNIQUE (project_id, sha256): hai mau trung dau van tay → loi luc
            // seed. Cung cong thuc voi ProjectSeeder.
            foreach (SeedProject p in KichBanSeed.Projects)
            {
                List<string> sha = p.Samples.Select(DauVanTay).ToList();
                Assert.Equal(sha.Count, sha.Distinct().Count());
            }
        }

        [Fact]
        public void Tap_nhan_mau_va_cau_vang_hop_le_theo_loai_du_lieu()
        {
            foreach (SeedProject p in KichBanSeed.Projects)
            {
                LabelSchema tapNhan = p.TapNhan();
                Assert.Equal(p.Modality, tapNhan.Modality);

                foreach (SeedSample s in p.Samples)
                {
                    Assert.Equal(p.Modality, s.Modality);

                    // File (anh, am thanh) co khoa thuoc du an; text / pair co noi dung.
                    if (Modalities.LaFile(s.Modality))
                    {
                        Assert.NotNull(s.StorageKey);
                        Assert.StartsWith(p.Id + "/", s.StorageKey, StringComparison.Ordinal);
                        Assert.Null(s.ContentJson);
                    }
                    else
                    {
                        Assert.Null(s.StorageKey);
                        Assert.NotNull(s.ContentJson);
                    }
                }

                // Dap an vang kiem nhu POST /gold-items that.
                foreach (SeedGold g in p.Gold)
                {
                    LabelPayload dapAn = LabelPayload.Tao(tapNhan, g.PayloadJson, p.Mau(g.SampleId).Metadata);
                    Assert.Equal(p.Modality, dapAn.TaskType);
                }
            }
        }

        [Fact]
        public void Am_thanh_cat_doan_dung_segmentSeconds()
        {
            SeedProject p6 = KichBanSeed.Project("p6");
            int? seg = p6.TapNhan().SegmentSeconds;
            Assert.NotNull(seg);

            foreach (SeedSample s in p6.Samples.Where(x => x.Metadata.SegmentStart.HasValue))
            {
                Assert.True(s.Metadata.SegmentEnd - s.Metadata.SegmentStart <= seg.Value);
                Assert.Equal(s.Metadata.SegmentEnd - s.Metadata.SegmentStart, s.Metadata.DurationSec);
                Assert.True(s.Metadata.SegmentEnd <= s.FileSeconds);
            }

            byte[] wav = AmThanhMau.TaoWav(440, 1);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal(44 + 16000 * 2, wav.Length);
        }

        private static string DauVanTay(SeedSample s)
        {
            if (s.Modality == Modalities.Image)
            {
                return Bam(AnhMauPng.Tao(s.R, s.G, s.B, 32));
            }

            if (s.Modality == Modalities.Audio)
            {
                string sha = Bam(AmThanhMau.TaoWav(s.ToneHz, s.FileSeconds));
                return s.Metadata.SegmentStart.HasValue
                    ? Bam(Encoding.UTF8.GetBytes(sha + "#" + s.Metadata.SegmentStart.Value.ToString("0.###", CultureInfo.InvariantCulture)))
                    : sha;
            }

            return Bam(Encoding.UTF8.GetBytes(s.ContentJson!));
        }

        private static string Bam(byte[] b)
        {
            return Convert.ToHexStringLower(SHA256.HashData(b));
        }
    }
}
