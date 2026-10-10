using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Quality;
using Crowd.Contracts.Tasking;
using Crowd.Labeling;
using Crowd.Settings;
using Crowd.Tasking.Api.Dtos;
using Crowd.Tasking.Api.Exceptions;
using Crowd.Tasking.Domain.Assignments;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Eligibility;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// Cap task theo lease (FL-06), nop, bo qua — DUONG NONG cua he thong.
    ///
    /// Moi thao tac la MOT transaction Postgres co KHOA DONG. Khong goi HTTP sang
    /// service nao: dieu kien tham gia doc tu ban sao trong task_db (docs muc 4).
    /// </summary>
    public sealed class LeaseService
    {
        private readonly TaskDbContext _db;
        private readonly TaskEventPublisher _events;
        private readonly LeaseRevoker _revoker;
        private readonly IObjectStorage _storage;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public LeaseService(
            TaskDbContext db,
            TaskEventPublisher events,
            LeaseRevoker revoker,
            IObjectStorage storage,
            ISettings settings,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (revoker == null)
            {
                throw new ArgumentNullException(nameof(revoker));
            }

            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _events = events;
            _revoker = revoker;
            _storage = storage;
            _settings = settings;
            _clock = clock;
        }

        // =====================================================================
        // NHAN TASK
        // =====================================================================

        /// <summary>
        /// Cap mot task cua du an cho labeler. Da dang giu task cua du an nay thi
        /// tra LAI task do (bam "nhan task" hai lan khong ton hai luot).
        /// null = het task de cap.
        /// </summary>
        public async Task<LeaseResponse?> NhanTaskAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            // 1. DIEU KIEN — doc ban sao cua chinh task_db, fail-safe ve tu choi.
            ProjectSnapshot? duAn = await _db.ProjectSnapshots.FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
            MemberCache? thanhVien = await _db.Members.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == labelerId, ct);
            LabelerProfile? labeler = await _db.Labelers.FirstOrDefaultAsync(l => l.UserId == labelerId, ct);

            string? lyDo = EligibilityPolicy.LyDoTuChoi(duAn, thanhVien, labeler, bayGio);
            if (lyDo != null)
            {
                throw new ForbiddenException(lyDo, "Ban chua du dieu kien nhan task cua du an nay.");
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                // 2. Dang giu task cua du an nay? Con han thi tra lai; qua han ma
                //    reaper chua kip quet thi cho het han ngay de nhan task moi.
                Assignment? dangGiu = await _db.Assignments.FirstOrDefaultAsync(
                    a => a.ProjectId == projectId && a.LabelerId == labelerId && a.State == AssignmentState.Leased, ct);

                if (dangGiu != null)
                {
                    if (dangGiu.DangGiu(bayGio))
                    {
                        await tx.CommitAsync(ct);
                        LabelingTask taskCu = await _db.Tasks.FirstAsync(t => t.Id == dangGiu.TaskId, ct);
                        return await TaoResponseAsync(dangGiu, taskCu, duAn!);
                    }

                    Assignment? khoa = await TaskQueries.KhoaAssignmentAsync(_db, dangGiu.Id, ct);
                    if (khoa != null && khoa.State == AssignmentState.Leased)
                    {
                        await _revoker.HetHanAsync(new List<Assignment> { khoa }, bayGio, ct);
                        await _db.SaveChangesAsync(ct);
                    }
                }

                // 3. Tron CAU VANG KIEM TRA (FQ-04): voi xac suat GoldCheckPercent, cap
                //    mot cau vang labeler chua lam thay vi task that. Response y het task
                //    that — labeler khong phan biet duoc (VD-Q-03).
                Assignment? cauVang = await ThuCapCauVangAsync(duAn!, labelerId, bayGio, ct);
                if (cauVang != null)
                {
                    _db.Assignments.Add(cauVang);
                    _events.Phat(caller, new TaskLeased
                    {
                        AssignmentId = cauVang.Id,
                        TaskId = cauVang.TaskId,
                        ProjectId = projectId,
                        LabelerId = labelerId,
                        ExpiresAt = cauVang.ExpiresAt,
                    });

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    LabelingTask taskVang = await _db.Tasks.AsNoTracking().FirstAsync(t => t.Id == cauVang.TaskId, ct);
                    return await TaoResponseAsync(cauVang, taskVang, duAn!);
                }

                // 4. KHOA mot task con cho, ngau nhien trong cua so dau hang doi (SKIP LOCKED, VD-T-06).
                LabelingTask? task = await KhoaMotTaskAsync(projectId, labelerId, ct);
                if (task == null)
                {
                    await tx.CommitAsync(ct);
                    return null;
                }

                // 4. Tao luot lease + tang bo dem — domain kiem lai "con cho khong".
                Assignment moi = Assignment.Tao(task, labelerId, bayGio, _settings.ThoiGian(SettingKeys.TaskLeaseDuration));
                _db.Assignments.Add(moi);

                _events.Phat(caller, new TaskLeased
                {
                    AssignmentId = moi.Id,
                    TaskId = task.Id,
                    ProjectId = projectId,
                    LabelerId = labelerId,
                    ExpiresAt = moi.ExpiresAt,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return await TaoResponseAsync(moi, task, duAn!);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_assignments_one_lease_per_project"))
            {
                // Cung labeler bam "nhan task" hai lan CUNG LUC: ca hai lot qua buoc
                // 2, UNIQUE (project, labeler) WHERE Leased chon ra mot. Ben thua tra
                // lai chinh luot ben thang vua tao.
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();

                Assignment? benThang = await _db.Assignments.AsNoTracking().FirstOrDefaultAsync(
                    a => a.ProjectId == projectId && a.LabelerId == labelerId && a.State == AssignmentState.Leased, ct);

                if (benThang == null)
                {
                    throw;
                }

                LabelingTask t = await _db.Tasks.AsNoTracking().FirstAsync(x => x.Id == benThang.TaskId, ct);
                return await TaoResponseAsync(benThang, t, duAn!);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        // =====================================================================
        // NOP / BO QUA
        // =====================================================================

        /// <summary>
        /// Nop nhan (VD-T-01). Khoa DONG assignment truoc: reaper dang quet cung
        /// dong phai cho — nen "het han" va "nop" khong the cung thanh cong.
        /// </summary>
        public async Task<SubmitResponse> NopAsync(
            Guid assignmentId, JsonElement? duLieuNhan, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();

            var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                Assignment a = await KhoaCuaMinhAsync(assignmentId, labelerId, ct);

                ProjectSnapshot? duAn = await _db.ProjectSnapshots.FirstOrDefaultAsync(p => p.ProjectId == a.ProjectId, ct);
                if (duAn == null)
                {
                    throw new RuleViolationException("du_an_chua_san_sang", "Khong tim thay cau hinh du an.");
                }

                if (!duLieuNhan.HasValue)
                {
                    throw new InvalidValueException("thieu_nhan", "Can truong \"payload\" chua nhan.");
                }

                if (duAn.LabelSchema == null)
                {
                    throw new RuleViolationException("du_an_chua_san_sang", "Du an chua co tap nhan.");
                }

                List<LabelingTask> khoa = await TaskQueries.KhoaTaskAsync(_db, new[] { a.TaskId }, ct);
                LabelingTask task = khoa[0];

                // Kiem theo TAP NHAN cua du an (ban sao tu project.published) va
                // metadata MAU (khung trong anh, doan thoi gian trong doan audio).
                // Sai → LabelFormatException → 400, luot lease van giu de sua va nop lai.
                LabelPayload nhan = LabelPayload.Tao(duAn.LabelSchema, duLieuNhan.Value, SampleMetadata.Tu(task.Metadata));

                DateTimeOffset bayGio = _clock.GetUtcNow();
                bool vuaDu = a.Nop(task, nhan, bayGio);

                if (a.IsGold)
                {
                    // Cau vang: cham ngay bang CUNG luat voi bai test dau vao, bao cho
                    // quality-svc. Khong tao nhan, khong tra tien. Response giong het
                    // task that de labeler khong nhan ra.
                    GoldSample? vang = await _db.GoldSamples.AsNoTracking()
                        .FirstOrDefaultAsync(g => g.ProjectId == a.ProjectId && g.SampleId == a.SampleId, ct);

                    if (vang != null)
                    {
                        _events.Phat(caller, new GoldAnswered
                        {
                            AssignmentId = a.Id,
                            ProjectId = a.ProjectId,
                            SampleId = a.SampleId,
                            LabelerId = a.LabelerId,
                            Correct = nhan.KhopDapAn(duAn.LabelSchema, vang.ExpectedPayload, NguongKhopTuSetting.Doc(_settings)),
                            AnsweredAt = bayGio,
                        });
                    }

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return new SubmitResponse { AssignmentId = a.Id, TaskCompleted = false };
                }

                _events.Phat(caller, new AssignmentSubmitted
                {
                    AssignmentId = a.Id,
                    TaskId = a.TaskId,
                    ProjectId = a.ProjectId,
                    SampleId = a.SampleId,
                    StorageKey = task.StorageKey,
                    SampleContent = task.Content,
                    SampleMetadata = task.Metadata,
                    LabelerId = a.LabelerId,
                    LabelPayload = nhan,
                    LeasedAt = a.LeasedAt,
                    SubmittedAt = bayGio,
                });

                if (vuaDu)
                {
                    _events.Phat(caller, new TaskRedundancyReached
                    {
                        TaskId = task.Id,
                        ProjectId = task.ProjectId,
                        SampleId = task.SampleId,
                        Redundancy = task.RedundancyTarget,
                    });
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return new SubmitResponse { AssignmentId = a.Id, TaskCompleted = vuaDu };
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>Labeler bo qua task (FL-05): tra cho ve pool ngay, khong doi 15 phut.</summary>
        public async Task BoQuaAsync(Guid assignmentId, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();
            var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                Assignment a = await KhoaCuaMinhAsync(assignmentId, labelerId, ct);
                List<LabelingTask> khoa = await TaskQueries.KhoaTaskAsync(_db, new[] { a.TaskId }, ct);

                a.BoQua(khoa[0], _clock.GetUtcNow());

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        // =====================================================================
        // DOC
        // =====================================================================

        public async Task<IReadOnlyList<LeaseResponse>> DangGiuAsync(Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            List<Assignment> ds = await _db.Assignments
                .AsNoTracking()
                .Where(a => a.LabelerId == labelerId && a.State == AssignmentState.Leased && a.ExpiresAt > bayGio)
                .OrderBy(a => a.ExpiresAt)
                .ToListAsync(ct);

            List<LeaseResponse> ketQua = new List<LeaseResponse>();
            foreach (Assignment a in ds)
            {
                LabelingTask t = await _db.Tasks.AsNoTracking().FirstAsync(x => x.Id == a.TaskId, ct);
                ProjectSnapshot p = await _db.ProjectSnapshots.AsNoTracking().FirstAsync(x => x.ProjectId == a.ProjectId, ct);
                ketQua.Add(await TaoResponseAsync(a, t, p));
            }

            return ketQua;
        }

        /// <summary>
        /// Cho project-svc truoc khi DONG du an (hoan thanh / huy): da tam dung chua, con
        /// luot dang giu khong, bao nhieu luot nop that. Chi doc.
        /// </summary>
        public async Task<CloseCheckResponse> KiemDongDuAnAsync(Guid projectId, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            ProjectSnapshot? duAn = await _db.ProjectSnapshots.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);

            int dangGiu = await _db.Assignments.CountAsync(
                a => a.ProjectId == projectId && a.State == AssignmentState.Leased && a.ExpiresAt > bayGio, ct);
            int daNop = await _db.Assignments.CountAsync(
                a => a.ProjectId == projectId && a.State == AssignmentState.Submitted && !a.IsGold, ct);

            return new CloseCheckResponse
            {
                ProjectId = projectId,
                Paused = duAn != null && duAn.Status == SnapshotStatus.Paused,
                ActiveLeases = dangGiu,
                SubmittedCount = daNop,
            };
        }

        /// <summary>Tien do du an (FB-20) — chi chu du an (theo ban sao thanh vien) hoac admin.</summary>
        public async Task<ProgressResponse> TienDoAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            if (!caller.IsAdmin)
            {
                Guid uid = caller.LayUserId();
                bool laChu = await _db.Members.AnyAsync(
                    m => m.ProjectId == projectId && m.UserId == uid
                         && m.Role == CachedMemberRole.Owner && m.State == CachedMemberState.Active,
                    ct);

                if (!laChu)
                {
                    throw new NotFoundException("Khong tim thay du an.");
                }
            }

            List<LabelingTask> tasks = await _db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId).ToListAsync(ct);

            int canNop = 0;
            foreach (LabelingTask t in tasks)
            {
                if (t.State != TaskState.Excluded)
                {
                    canNop = canNop + t.RedundancyTarget;
                }
            }

            return new ProgressResponse
            {
                ProjectId = projectId,
                TotalTasks = tasks.Count,
                OpenTasks = tasks.Count(t => t.State == TaskState.Open),
                CompletedTasks = tasks.Count(t => t.State == TaskState.Completed),
                ExcludedTasks = tasks.Count(t => t.State == TaskState.Excluded),
                ActiveLeases = tasks.Sum(t => t.ActiveLeaseCount),
                Submissions = tasks.Sum(t => t.SubmittedCount),
                RequiredSubmissions = canNop,
            };
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        /// <summary>
        /// Khoa mot task con cho. Khong khoa duoc ma van con ung vien (chung dang bi
        /// request khac giu trong tich tac) thi thu lai vai lan — chi tra null khi
        /// THAT SU het task.
        /// </summary>
        private async Task<LabelingTask?> KhoaMotTaskAsync(Guid projectId, Guid labelerId, CancellationToken ct)
        {
            int soLanThuLai = _settings.SoNguyen(SettingKeys.TaskLeaseRetryCount);
            int cuaSo = _settings.SoNguyen(SettingKeys.TaskLeaseCandidateWindow);
            for (int lan = 0; lan <= soLanThuLai; lan++)
            {
                LabelingTask? task = await TaskQueries.KhoaMotUngVienAsync(_db, projectId, labelerId, cuaSo, ct);
                if (task != null)
                {
                    return task;
                }

                bool con = await TaskQueries.ConUngVienAsync(_db, projectId, labelerId, ct);
                if (!con)
                {
                    return null;
                }

                await Task.Delay(_settings.ThoiGian(SettingKeys.TaskLeaseRetryDelay), ct);
            }

            return null;
        }

        /// <summary>
        /// Quyet dinh co cap cau vang khong va chon cau nao. null = cap task that.
        ///
        /// Chi cap khi CON task that de lam: het task that ma van phat cau vang thi
        /// labeler lam khong cong mai, va de nhan ra "task nay la cau kiem tra".
        /// Moi cau vang moi labeler lam toi da mot lan.
        /// </summary>
        private async Task<Assignment?> ThuCapCauVangAsync(
            ProjectSnapshot duAn, Guid labelerId, DateTimeOffset bayGio, CancellationToken ct)
        {
            if (duAn.GoldCheckPercent <= 0 || RandomNumberGenerator.GetInt32(100) >= duAn.GoldCheckPercent)
            {
                return null;
            }

            Guid projectId = duAn.ProjectId;
            if (!await TaskQueries.ConUngVienAsync(_db, projectId, labelerId, ct))
            {
                return null;
            }

            IQueryable<Guid> daLam = _db.Assignments
                .Where(a => a.ProjectId == projectId && a.LabelerId == labelerId && a.IsGold)
                .Select(a => a.TaskId);

            List<Guid> ungVien = await _db.Tasks
                .Where(t => t.ProjectId == projectId
                            && t.State == TaskState.Excluded
                            && _db.GoldSamples.Any(g => g.ProjectId == projectId && g.SampleId == t.SampleId && g.Purpose == "qualityCheck")
                            && !daLam.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);

            if (ungVien.Count == 0)
            {
                return null;
            }

            Guid chon = ungVien[RandomNumberGenerator.GetInt32(ungVien.Count)];
            LabelingTask task = await _db.Tasks.FirstAsync(t => t.Id == chon, ct);
            return Assignment.TaoCauVang(task, labelerId, bayGio, _settings.ThoiGian(SettingKeys.TaskLeaseDuration));
        }

        /// <summary>Khoa luot lease CUA CHINH nguoi goi. Cua nguoi khac → 404 (BOLA).</summary>
        private async Task<Assignment> KhoaCuaMinhAsync(Guid assignmentId, Guid labelerId, CancellationToken ct)
        {
            Assignment? a = await TaskQueries.KhoaAssignmentAsync(_db, assignmentId, ct);

            if (a == null || a.LabelerId != labelerId)
            {
                throw new NotFoundException("Khong tim thay luot nhan task.");
            }

            return a;
        }

        private async Task<LeaseResponse> TaoResponseAsync(Assignment a, LabelingTask t, ProjectSnapshot p)
        {
            return new LeaseResponse
            {
                AssignmentId = a.Id,
                ProjectId = a.ProjectId,
                TaskId = a.TaskId,
                SampleId = a.SampleId,
                Modality = t.Modality,
                FileUrl = t.StorageKey == null ? null : await _storage.TaoLinkXemAsync(t.StorageKey),
                Content = t.Content,
                Metadata = t.Metadata,
                LabelSchema = p.LabelSchema!.ToRawJson(),
                SchemaVersion = LabelPayload.PhienBanHienTai,
                ExpiresAt = a.ExpiresAt,
                UnitPriceVnd = p.UnitPriceVnd,
            };
        }
    }
}
