using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crowd.Annotation.Infrastructure.Persistence
{
    /// <summary>
    /// CHI phuc vu cong cu `dotnet ef`, khong bao gio chay luc ung dung chay.
    ///
    /// Khi sinh migration, EF can dung duoc mot DbContext de doc model. Binh
    /// thuong no lay tu Program.cs cua project khoi dong, nhung annotation-svc
    /// chua dang ky DbContext o do. Lop nay lap khoang trong day.
    ///
    /// Chuoi ket noi o day chi de EF DOC MODEL va SINH file migration — no
    /// khong mo ket noi that. Chuoi dung khi chay nam trong appsettings.
    /// </summary>
    public sealed class AnnotationDbContextFactory
        : IDesignTimeDbContextFactory<AnnotationDbContext>
    {
        private const string ChuoiKetNoiMacDinh =
            "Host=localhost;Port=5404;Database=annotation_db;" +
            "Username=annotation_user;Password=dev_annotation_pw";

        public AnnotationDbContext CreateDbContext(string[] args)
        {
            // Cho phep tro sang database khac khi can, vd chay migration len VPS:
            //     ANNOTATION_DB=... dotnet ef database update
            string? tuBienMoiTruong = Environment.GetEnvironmentVariable("ANNOTATION_DB");

            string chuoiKetNoi;
            if (string.IsNullOrWhiteSpace(tuBienMoiTruong))
            {
                chuoiKetNoi = ChuoiKetNoiMacDinh;
            }
            else
            {
                chuoiKetNoi = tuBienMoiTruong;
            }

            DbContextOptionsBuilder<AnnotationDbContext> builder =
                new DbContextOptionsBuilder<AnnotationDbContext>();

            builder.UseNpgsql(chuoiKetNoi);

            return new AnnotationDbContext(builder.Options);
        }
    }
}
