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
                ids.AddRange(p.Submissions.Select(s => s.AssignmentId));
                ids.AddRange(p.Submissions.Select(s => s.AnnotationId));
            }

            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void Ngan_sach_moi_du_an_du_ky_quy_toi_thieu()
        {
            foreach (SeedProject p in KichBanSeed.Projects)
            {
                // Cung cong thuc LabelingProject.ChiPhiUocTinhVnd.
                long toiThieu = p.Samples.Count * (long)p.TranRedundancy * (p.UnitPriceVnd + p.PlatformFeePerLabelVnd);
                Assert.True(p.BudgetVnd >= toiThieu, p.Key + ": ngan sach " + p.BudgetVnd + " < " + toiThieu);
            }
        }

        [Fact]
        public void Tien_nap_du_cho_moi_khoan_ky_quy()
        {
            foreach (SeedUser u in KichBanSeed.Users)
            {
                long nap = KichBanSeed.Deposits.Where(d => d.BusinessId == u.Id && d.DaThanhToan).Sum(d => d.AmountVnd);
                long kyQuy = KichBanSeed.Projects.Where(p => p.OwnerId == u.Id && p.DaKyQuy).Sum(p => p.BudgetVnd);
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
                Assert.Equal(p.Stage == SeedStage.Running, p.PublishedAgo.HasValue);

                if (p.DaKyQuy)
                {
                    Assert.True(p.LucKyQuy(bayGio) >= lucTao, p.Key + ": ky quy truoc khi tao");
                }

                if (p.Stage == SeedStage.Running)
                {
                    Assert.True(p.LucDuyet(bayGio) >= p.LucKyQuy(bayGio), p.Key + ": duyet truoc khi ky quy");
                }
                else
                {
                    Assert.Empty(p.Submissions);
                }
            }
        }

        [Fact]
        public void Luot_nop_hop_le_voi_task_va_thanh_vien()
        {
            DateTimeOffset bayGio = DateTimeOffset.UtcNow;

            foreach (SeedProject p in KichBanSeed.Projects)
            {
                HashSet<Guid> labeler = new HashSet<Guid>(p.Members.Where(m => m.Role == SeedMemberRole.Labeler).Select(m => m.UserId));
                HashSet<Guid> duocDuyet = new HashSet<Guid>(p.Members.Where(m => m.Role == SeedMemberRole.Reviewer).Select(m => m.UserId));
                duocDuyet.Add(p.OwnerId);
                HashSet<Guid> mauVang = new HashSet<Guid>(p.Gold.Select(g => g.SampleId));

                foreach (SeedSubmission s in p.Submissions)
                {
                    SeedSample? mau = p.Samples.FirstOrDefault(x => x.Id == s.SampleId);
                    Assert.NotNull(mau);
                    Assert.Equal(mau.StorageKey, s.StorageKey);
                    Assert.Equal(KichBanSeed.TaskIdCua(s.SampleId), s.TaskId);
                    Assert.DoesNotContain(s.SampleId, mauVang);

                    Assert.Contains(s.LabelerId, labeler);
                    // Nhan kiem nhu API that: tap nhan + metadata mau.
                    LabelPayload nhan = SeedEvents.NhanCua(p, s);
                    Assert.Equal(p.Modality, nhan.TaskType);
                    Assert.True(bayGio - s.SubmittedAgo > p.LucDuyet(bayGio), "nop truoc khi du an chay");

                    if (s.Review != SeedReview.PendingReview)
                    {
                        Assert.NotNull(s.ReviewerId);
                        Assert.Contains(s.ReviewerId.Value, duocDuyet);
                        Assert.NotEqual(s.LabelerId, s.ReviewerId.Value);
                        Assert.True(s.ReviewedAgo <= s.SubmittedAgo, "duyet truoc khi nop");
                    }
                }

                // Moi task toi da redundancy luot nop, moi labeler mot luot.
                foreach (IGrouping<Guid, SeedSubmission> nhom in p.Submissions.GroupBy(s => s.TaskId))
                {
                    Assert.True(nhom.Count() <= p.Redundancy, p.Key + ": task vuot redundancy");
                    Assert.Equal(nhom.Count(), nhom.Select(s => s.LabelerId).Distinct().Count());
                }
            }
        }

        [Fact]
        public void Khieu_nai_con_trong_han_7_ngay()
        {
            foreach (SeedSubmission s in KichBanSeed.Projects.SelectMany(p => p.Submissions).Where(x => x.Review == SeedReview.Appealed))
            {
                Assert.NotNull(s.AppealMessage);
                Assert.NotNull(s.AppealedAgo);
                Assert.True(s.AppealedAgo <= s.ReviewedAgo);
                Assert.True(s.ReviewedAgo - s.AppealedAgo <= TimeSpan.FromDays(7));
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
