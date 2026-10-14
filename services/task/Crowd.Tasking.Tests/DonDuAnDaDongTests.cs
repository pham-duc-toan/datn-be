using System;
using System.Text.Json;
using Crowd.Tasking.Api.Dtos;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Projects;

namespace Crowd.Tasking.Tests
{
    /// <summary>
    /// Don du lieu van hanh cua du an da dong: chi du an DA DONG moi duoc chup tien do cuoi, ban chup khong
    /// bi ghi de, va chi danh dau "da don" sau khi da chup — de API tien do khong bao gio tra so 0 gia.
    /// </summary>
    public sealed class DonDuAnDaDongTests
    {
        private static readonly DateTimeOffset Luc = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

        private static ProjectSnapshot DuAnDangChay()
        {
            ProjectSnapshot s = ProjectSnapshot.TaoChuaPublish(Guid.NewGuid(), Luc);
            s.ApDungPublished(Guid.NewGuid(), OutOfOrderEventTests.TapNhanAnh("a", "b"), 1000, 2, 2, 0, Luc.AddDays(10), true, false, null, null, Luc);
            return s;
        }

        [Fact]
        public void Du_an_dang_chay_khong_duoc_chup_tien_do_cuoi()
        {
            ProjectSnapshot s = DuAnDangChay();

            RuleViolationException ex = Assert.Throws<RuleViolationException>(() => s.ChupTienDoCuoi("{}"));

            Assert.Equal("du_an_chua_dong", ex.Code);
            Assert.Null(s.FinalProgressJson);
        }

        [Fact]
        public void Du_an_da_dong_chup_mot_lan_lan_sau_giu_ban_dau()
        {
            ProjectSnapshot s = DuAnDangChay();
            s.Dong(Luc.AddHours(1));

            s.ChupTienDoCuoi("{\"totalTasks\":3}");
            s.ChupTienDoCuoi("{\"totalTasks\":0}");

            // Lan hai chay khi bang tasks da bi xoa mot phan — khong duoc ghi de so dung bang so thieu.
            Assert.Equal("{\"totalTasks\":3}", s.FinalProgressJson);
        }

        [Fact]
        public void Chua_chup_thi_khong_danh_dau_da_don()
        {
            ProjectSnapshot s = DuAnDangChay();
            s.Dong(Luc.AddHours(1));

            Assert.Throws<InvalidOperationException>(() => s.DanhDauDaDon(Luc.AddDays(90)));
            Assert.Null(s.DataPurgedAt);
        }

        [Fact]
        public void Danh_dau_da_don_giu_moc_dau_tien()
        {
            ProjectSnapshot s = DuAnDangChay();
            s.Dong(Luc.AddHours(1));
            s.ChupTienDoCuoi("{}");

            s.DanhDauDaDon(Luc.AddDays(90));
            s.DanhDauDaDon(Luc.AddDays(91));

            Assert.Equal(Luc.AddDays(90), s.DataPurgedAt);
        }

        [Fact]
        public void Ban_chup_tien_do_doc_lai_duoc_nguyen_ven()
        {
            ProgressResponse goc = new ProgressResponse
            {
                ProjectId = Guid.NewGuid(),
                TotalTasks = 5,
                OpenTasks = 0,
                CompletedTasks = 4,
                ExcludedTasks = 1,
                ActiveLeases = 0,
                Submissions = 8,
                RequiredSubmissions = 8,
                CancelledTasks = 0,
                SubmissionsLast24h = 0,
                EstimatedCompletionAt = null,
                FrozenAt = Luc,
            };

            string json = JsonSerializer.Serialize(goc, JsonSerializerOptions.Web);
            ProgressResponse? doc = JsonSerializer.Deserialize<ProgressResponse>(json, JsonSerializerOptions.Web);

            Assert.NotNull(doc);
            Assert.Equal(goc.ProjectId, doc.ProjectId);
            Assert.Equal(5, doc.TotalTasks);
            Assert.Equal(4, doc.CompletedTasks);
            Assert.Equal(8, doc.Submissions);
            Assert.Equal(Luc, doc.FrozenAt);
            Assert.Null(doc.EstimatedCompletionAt);
        }
    }
}
