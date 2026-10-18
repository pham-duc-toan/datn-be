using System;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Annotation.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Annotation.Infrastructure.Persistence
{
    /// <summary>
    /// Khoa dong dieu khoan du an khi consumer project.completed / project.cancelled danh dau ket thuc.
    /// PHAI goi trong mot transaction dang mo — khoa ton tai toi luc transaction ket thuc.
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
    }
}
