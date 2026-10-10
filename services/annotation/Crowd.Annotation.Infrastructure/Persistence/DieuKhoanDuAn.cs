using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Infrastructure.Persistence
{
    /// <summary>
    /// Khoa dong dieu khoan du an de "dong so" va cac thao tac lam doi tien khong chen nhau.
    /// PHAI goi trong mot transaction dang mo — khoa ton tai toi luc transaction ket thuc.
    ///
    ///   dong so (API noi bo), consumer project.*   → FOR UPDATE
    ///   duyet / tu choi / khieu nai / phan xu      → FOR SHARE (nhieu thao tac chay song song duoc)
    ///
    /// Mot khieu nai chen vao luc dang dong so: hoac commit TRUOC (dong so thay no → tu choi
    /// dong), hoac cho dong so commit roi doc thay ClosedAt → 409. Khong co khe giua "kiem"
    /// va "dong".
    /// </summary>
    public static class DieuKhoanDuAn
    {
        public static Task<ProjectTerms?> KhoaAsync(AnnotationDbContext db, Guid projectId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.ProjectTerms
                .FromSqlRaw("SELECT * FROM project_terms WHERE project_id = {0} FOR UPDATE", projectId)
                .FirstOrDefaultAsync(ct);
        }

        public static Task<ProjectTerms?> KhoaChiaSeAsync(AnnotationDbContext db, Guid projectId, CancellationToken ct)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            return db.ProjectTerms
                .FromSqlRaw("SELECT * FROM project_terms WHERE project_id = {0} FOR SHARE", projectId)
                .FirstOrDefaultAsync(ct);
        }
    }
}
