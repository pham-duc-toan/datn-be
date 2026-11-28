using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Doanh nghiep SUA ket qua cuoi cua tung mau truoc khi xuat file (bang sample_overrides — xem SampleOverride).
    ///
    ///   PUT    /annotations/projects/{id}/results/{sampleId}/override  {data, note} → thay toan bo phan sua cua mau
    ///   DELETE /annotations/projects/{id}/results/{sampleId}/override                → tro ve ket qua so dong
    ///
    /// Luat:
    ///   - chi CHU DU AN (admin khong sua du lieu cua doanh nghiep);
    ///   - chi khi du an da cham xong (project_collections.graded_at) — truoc do chua co ket qua de xem lai;
    ///   - chi mau dang co trong ket qua (co nhan da chot) — mau khong ai dat chua lam o buoc nay;
    ///   - gia tri sua qua CUNG bo kiem nhan voi luc labeler nop (LabelPayload.ChuanHoaMotPhan), nen file xuat khong
    ///     bao gio hong dinh dang;
    ///   - chi doi ket qua xuat, KHONG doi cham bai / tien thuong / uy tin.
    /// File xuat tu nhan ra ket qua da doi: dau van tay file tinh tren ket qua cuoi (XuatKetQua.DauVanTay).
    /// </summary>
    public sealed class SuaKetQuaService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly AnnotationService _ketQua;
        private readonly TimeProvider _clock;
        private readonly ILogger<SuaKetQuaService> _logger;

        public SuaKetQuaService(
            ProjectDbContext db,
            ProjectAccessService access,
            AnnotationService ketQua,
            TimeProvider clock,
            ILogger<SuaKetQuaService> logger)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (ketQua == null)
            {
                throw new ArgumentNullException(nameof(ketQua));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _db = db;
            _access = access;
            _ketQua = ketQua;
            _clock = clock;
            _logger = logger;
        }

        public async Task<SampleOverrideInfo> SuaAsync(Guid projectId, Guid sampleId, SuaKetQuaRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await KiemDuocSuaAsync(projectId, caller, ct);
            LabelSchema tapNhan = duAn.LabelSchema!;

            if (!body.Data.HasValue)
            {
                throw new InvalidValueException("chua_sua_gi", "Chưa có công cụ nào được sửa.");
            }

            Sample? mau = await _db.Samples.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Id == sampleId, ct);
            if (mau == null)
            {
                throw new NotFoundException("Không tìm thấy mẫu.");
            }

            SampleResult? soDong = await _ketQua.KetQuaSoDongMotMauAsync(duAn, mau, ct);
            if (soDong == null)
            {
                throw new RuleViolationException("mau_chua_co_ket_qua", "Mẫu này chưa có câu trả lời nào được chấp nhận nên chưa sửa được.");
            }

            // Ket qua so dong cua cac cong cu KHONG sua — de kiem quan he tro toi doan span doanh nghiep khong sua.
            JsonObject ngoaiCanh = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> t in soDong.Tools)
            {
                JsonObject? o = t.Value as JsonObject;
                JsonNode? final = o == null ? null : o["final"];
                if (final != null)
                {
                    ngoaiCanh[t.Key] = final.DeepClone();
                }
            }

            // Sai dinh dang → LabelFormatException → 400 (cung thong bao voi luc labeler nop).
            string giaTri = LabelPayload.ChuanHoaMotPhan(tapNhan, body.Data.Value, SampleMetadata.Tu(mau.Metadata), ngoaiCanh);
            List<string> congCu = ((JsonObject)JsonNode.Parse(giaTri)!).Select(k => k.Key).ToList();
            string bam = SuaKetQua.BamSoDong(soDong.Tools, congCu);

            Guid nguoiSua = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();
            SampleOverride? cu = await _db.SampleOverrides.FirstOrDefaultAsync(s => s.ProjectId == projectId && s.SampleId == sampleId, ct);
            SampleOverride s;
            if (cu == null)
            {
                s = SampleOverride.Tao(projectId, sampleId, giaTri, bam, body.Note, nguoiSua, bayGio);
                _db.SampleOverrides.Add(s);
            }
            else
            {
                cu.Sua(giaTri, bam, body.Note, nguoiSua, bayGio);
                s = cu;
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (cu == null)
            {
                // Hai tab cung luu lan dau: ban kia vua tao — ghi de bang ban nay.
                _db.ChangeTracker.Clear();
                SampleOverride daCo = await _db.SampleOverrides.FirstAsync(x => x.ProjectId == projectId && x.SampleId == sampleId, ct);
                daCo.Sua(giaTri, bam, body.Note, nguoiSua, bayGio);
                await _db.SaveChangesAsync(ct);
                s = daCo;
            }

            _logger.LogInformation("Doanh nghiep sua ket qua mau {SampleId} du an {ProjectId}: {CongCu}", sampleId, projectId, string.Join(",", congCu));
            return new SampleOverrideInfo
            {
                Tools = s.CongCu(),
                Note = s.Note,
                EditedBy = s.EditedBy,
                EditedAt = s.EditedAt,
                ConsensusChanged = false,
            };
        }

        /// <summary>Bo phan sua — mau tro ve ket qua so dong. Chua sua thi khong lam gi (goi lai vo hai).</summary>
        public async Task BoSuaAsync(Guid projectId, Guid sampleId, Caller caller, CancellationToken ct)
        {
            await KiemDuocSuaAsync(projectId, caller, ct);
            int soDong = await _db.SampleOverrides.Where(s => s.ProjectId == projectId && s.SampleId == sampleId).ExecuteDeleteAsync(ct);
            if (soDong > 0)
            {
                _logger.LogInformation("Doanh nghiep bo sua ket qua mau {SampleId} du an {ProjectId}", sampleId, projectId);
            }
        }

        private async Task<LabelingProject> KiemDuocSuaAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayChoChuAsync(projectId, caller, ct);
            if (caller.UserId == null || duAn.OwnerId != caller.UserId.Value)
            {
                throw new ForbiddenException("Chỉ chủ dự án được sửa kết quả.");
            }

            if (duAn.LabelSchema == null)
            {
                throw new RuleViolationException("chua_co_tap_nhan", "Dự án chưa có tập nhãn.");
            }

            bool daCham = await _db.CollectionRounds.AnyAsync(c => c.ProjectId == projectId && c.GradedAt != null, ct);
            if (!daCham)
            {
                throw new RuleViolationException("chua_cham_xong", "Dự án chấm xong mới sửa được kết quả.");
            }

            return duAn;
        }
    }
}
