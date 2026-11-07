using System;
using System.Linq;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Crowd.Project.Tests
{
    /// <summary>
    /// Bang PHAN VUNG (samples, annotations — tao bang SQL tay trong migration GopLamBaiVaNhan) khong cho
    /// "INSERT ... RETURNING xmin": Postgres bao "cannot retrieve a system column in this context". EF sinh dung
    /// cau do cho moi entity co RowVersion map vao xmin → service chet ngay luc seed. Test nay chan tai phat
    /// ma khong can database (chi dung model).
    /// </summary>
    public sealed class PhanVungModelTests
    {
        private static IModel Model()
        {
            DbContextOptions<ProjectDbContext> options = new DbContextOptionsBuilder<ProjectDbContext>()
                .UseNpgsql("Host=khong-ket-noi;Database=x")
                .Options;
            using (ProjectDbContext db = new ProjectDbContext(options))
            {
                return db.GetService<IDesignTimeModel>().Model;
            }
        }

        [Theory]
        [InlineData(typeof(LabelAnnotation))]
        [InlineData(typeof(Sample))]
        public void BangPhanVung_KhongCoCotXmin(Type kieu)
        {
            IEntityType? e = Model().FindEntityType(kieu);
            Assert.NotNull(e);
            Assert.DoesNotContain(e!.GetProperties(), p => string.Equals(p.GetColumnName(), "xmin", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(typeof(LabelAnnotation))]
        [InlineData(typeof(Sample))]
        public void BangPhanVung_KhoaChinhChuaCotPhanVung(Type kieu)
        {
            IEntityType e = Model().FindEntityType(kieu)!;
            Assert.Contains("project_id", e.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName()));
        }
    }
}
