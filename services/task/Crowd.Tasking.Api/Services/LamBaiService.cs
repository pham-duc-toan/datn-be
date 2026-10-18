using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Tasking;
using Crowd.Labeling;
using Crowd.Settings;
using Crowd.Tasking.Api.Dtos;
using Crowd.Tasking.Api.Exceptions;
using Crowd.Tasking.Domain.Attempts;
using Crowd.Tasking.Domain.Common;
using Crowd.Tasking.Domain.Eligibility;
using Crowd.Tasking.Domain.Labelers;
using Crowd.Tasking.Domain.Members;
using Crowd.Tasking.Domain.Projects;
using Crowd.Tasking.Domain.Tasks;
using Crowd.Tasking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AttemptAnswer = Crowd.Tasking.Domain.Attempts.AttemptAnswer;
using TraLoiNop = Crowd.Contracts.Tasking.AttemptAnswer;

namespace Crowd.Tasking.Api.Services
{
    /// <summary>
    /// LAM BAI ca du an (docs 3.18) — thay cho cap task tung cau.
    ///
    ///   POST /tasks/projects/{id}/attempt          bat dau (giu mot suat) / mo lai bai dang lam
    ///   PUT  /tasks/attempts/{id}/answers/{sample} luu nhap MOT cau (quay lai, bo qua tuy y)
    ///   POST /tasks/attempts/{id}/submit           nop CA BAI: kiem dinh dang moi cau, cham cau vang
    ///
    /// Moi thao tac ghi la MOT transaction Postgres co KHOA DONG; khong goi HTTP sang service nao (dieu
    /// kien tham gia doc tu ban sao trong task_db, docs muc 4). Thu tu khoa: du an → bai → cau hoi.
    /// </summary>
    public sealed class LamBaiService
    {
        private readonly TaskDbContext _db;
        private readonly TaskEventPublisher _events;
        private readonly ThuBaiService _thuBai;
        private readonly IMediaLinks _media;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public LamBaiService(
            TaskDbContext db,
            TaskEventPublisher events,
            ThuBaiService thuBai,
            IMediaLinks media,
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

            if (thuBai == null)
            {
                throw new ArgumentNullException(nameof(thuBai));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
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
            _thuBai = thuBai;
            _media = media;
            _settings = settings;
            _clock = clock;
        }

        // =====================================================================
        // BAT DAU / MO LAI
        // =====================================================================

        /// <summary>
        /// Bat dau lam bai: giu mot suat, nhan CA DE (cau that + cau vang kiem tra, thu tu xao rieng). Dang co
        /// bai do du an nay thi tra LAI bai do (bam hai lan khong ton hai suat). Da nop → 409 da_nop_bai.
        /// </summary>
        public async Task<AttemptResponse> BatDauAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            Attempt? cu = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(
                a => a.ProjectId == projectId && a.LabelerId == labelerId
                     && (a.State == AttemptState.InProgress || a.State == AttemptState.Submitted), ct);

            if (cu != null && cu.State == AttemptState.Submitted)
            {
                throw new RuleViolationException("da_nop_bai", "Ban da nop bai cho du an nay — cho du an ket thuc de xem ket qua.");
            }

            if (cu != null && cu.ExpiresAt > bayGio)
            {
                return await TaoResponseAsync(cu, ct);
            }

            // 1. DIEU KIEN — doc ban sao cua chinh task_db, fail-safe ve tu choi.
            ProjectSnapshot? duAnDoc = await _db.ProjectSnapshots.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
            MemberCache? thanhVien = await _db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == labelerId, ct);
            LabelerProfile? labeler = await _db.Labelers.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == labelerId, ct);

            string? lyDo = EligibilityPolicy.LyDoTuChoi(duAnDoc, thanhVien, labeler, bayGio);
            if (lyDo != null)
            {
                throw new ForbiddenException(lyDo, "Ban chua du dieu kien lam bai cua du an nay.");
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // 2. KHOA du an: dem suat va dong thu bai deu xep hang qua dong nay.
                ProjectSnapshot? duAn = await TaskQueries.KhoaDuAnAsync(_db, projectId, ct);
                if (duAn == null || !duAn.ConNhanBai(bayGio))
                {
                    throw new RuleViolationException("du_an_khong_nhan_bai", "Du an khong con nhan bai lam.");
                }

                // Bai cu qua han ma worker chua kip quet: cho het han ngay de lam bai moi.
                if (cu != null)
                {
                    Attempt? khoaCu = await TaskQueries.KhoaBaiAsync(_db, cu.Id, ct);
                    if (khoaCu != null)
                    {
                        khoaCu.HetHan(bayGio);
                    }
                }

                int dangGiu = await _db.Attempts.CountAsync(
                    a => a.ProjectId == projectId && a.Id != (cu == null ? Guid.Empty : cu.Id)
                         && (a.State == AttemptState.Submitted || (a.State == AttemptState.InProgress && a.ExpiresAt > bayGio)),
                    ct);
                if (dangGiu >= duAn.Slots)
                {
                    throw new RuleViolationException("het_suat", "Du an da du " + duAn.Slots + " nguoi lam bai.");
                }

                // 3. DE: moi cau that (Open) + cau vang kiem tra — thu tu xao rieng cho bai nay.
                List<Guid> cauThat = await _db.Tasks.AsNoTracking()
                    .Where(t => t.ProjectId == projectId && t.State == TaskState.Open)
                    .Select(t => t.SampleId)
                    .ToListAsync(ct);
                List<Guid> cauVang = await _db.GoldSamples.AsNoTracking()
                    .Where(g => g.ProjectId == projectId && g.Purpose == "qualityCheck")
                    .Select(g => g.SampleId)
                    .ToListAsync(ct);

                List<Guid> de = cauThat.Concat(cauVang).Distinct().OrderBy(x => Random.Shared.Next()).ToList();

                DateTimeOffset hetHan = bayGio + _settings.ThoiGian(SettingKeys.TaskAttemptDuration);
                if (duAn.Deadline.HasValue && duAn.Deadline.Value < hetHan)
                {
                    hetHan = duAn.Deadline.Value;
                }

                Attempt moi = Attempt.BatDau(projectId, labelerId, de, bayGio, hetHan);
                _db.Attempts.Add(moi);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return await TaoResponseAsync(moi, ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_attempts_project_labeler_active"))
            {
                // Cung labeler bam "bat dau" hai lan CUNG LUC: UNIQUE (project, labeler) WHERE dang lam / da nop
                // chon ra mot. Ben thua tra lai chinh bai ben thang vua tao.
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();

                Attempt? benThang = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(
                    a => a.ProjectId == projectId && a.LabelerId == labelerId && a.State == AttemptState.InProgress, ct);
                if (benThang == null)
                {
                    throw;
                }

                return await TaoResponseAsync(benThang, ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>GET /tasks/attempts/{id} — mo lai bai (link xem file moi). Bai cua nguoi khac → 404 (BOLA).</summary>
        public async Task<AttemptResponse> LayAsync(Guid attemptId, Caller caller, CancellationToken ct)
        {
            Attempt a = await LayCuaMinhAsync(attemptId, caller, ct);
            return await TaoResponseAsync(a, ct);
        }

        // =====================================================================
        // LUU NHAP / NOP / BO
        // =====================================================================

        /// <summary>
        /// Luu nhap MOT cau (JSON object toi da 256 KB, chua kiem luat — nhap la do dang; kiem day du khi
        /// nop). data null = xoa cau tra loi (bo trong cau nay).
        /// </summary>
        public async Task<SaveAnswerResponse> LuuCauTraLoiAsync(
            Guid attemptId, Guid sampleId, JsonElement? data, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                Attempt? a = await TaskQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Khong tim thay bai lam.");
                }

                a.KiemConLamDuoc(bayGio);

                if (!a.CoCauHoi(sampleId))
                {
                    throw new NotFoundException("Cau hoi khong thuoc bai lam nay.");
                }

                AttemptAnswer? tl = await _db.AttemptAnswers.FirstOrDefaultAsync(x => x.AttemptId == attemptId && x.SampleId == sampleId, ct);
                bool coTraLoi = data.HasValue && data.Value.ValueKind != JsonValueKind.Null && data.Value.ValueKind != JsonValueKind.Undefined;

                if (!coTraLoi)
                {
                    if (tl != null)
                    {
                        _db.AttemptAnswers.Remove(tl);
                    }
                }
                else
                {
                    if (data!.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidValueException("cau_tra_loi_sai_dinh_dang", "Cau tra loi phai la mot object JSON.");
                    }

                    string json = data.Value.GetRawText();
                    if (tl == null)
                    {
                        _db.AttemptAnswers.Add(AttemptAnswer.Tao(attemptId, sampleId, json, bayGio));
                    }
                    else
                    {
                        tl.Ghi(json, bayGio);
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                int daLam = await _db.AttemptAnswers.CountAsync(x => x.AttemptId == attemptId, ct);
                return new SaveAnswerResponse
                {
                    AttemptId = attemptId,
                    SampleId = sampleId,
                    Answered = coTraLoi,
                    AnsweredCount = daLam,
                    SavedAt = bayGio,
                };
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>
        /// Nop CA BAI. Moi cau da tra loi phai dung dinh dang (Crowd.Labeling: theo tap nhan va metadata mau)
        /// — sai thi 400 bai_chua_hop_le kem danh sach cau, bai van giu de sua. Cau bo trong duoc phep (tinh
        /// la khong giong da so; cau vang bo trong tinh la sai). Cau vang cham ngay bang CUNG luat voi bai test
        /// dau vao; chi so dung / tong di theo bai — khong ai biet cau nao la vang cho toi khi cham.
        /// Bai nop nay lam du an du suat → dong thu bai trong cung transaction.
        /// </summary>
        public async Task<SubmitAttemptResponse> NopAsync(Guid attemptId, string? clientIp, string? deviceId, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            Attempt? xem = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (xem == null || xem.LabelerId != labelerId)
            {
                throw new NotFoundException("Khong tim thay bai lam.");
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                ProjectSnapshot? duAn = await TaskQueries.KhoaDuAnAsync(_db, xem.ProjectId, ct);
                if (duAn == null || duAn.LabelSchema == null)
                {
                    throw new RuleViolationException("du_an_chua_san_sang", "Khong tim thay cau hinh du an.");
                }

                Attempt? a = await TaskQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null)
                {
                    throw new NotFoundException("Khong tim thay bai lam.");
                }

                a.KiemConLamDuoc(bayGio);

                if (duAn.Status == SnapshotStatus.Paused)
                {
                    throw new RuleViolationException("du_an_tam_dung", "Du an dang tam dung — bai van duoc giu, nop lai khi du an chay tiep.");
                }

                if (!duAn.ConNhanBai(bayGio))
                {
                    throw new RuleViolationException("du_an_khong_nhan_bai", "Du an khong con nhan bai nop.");
                }

                Dictionary<Guid, LabelingTask> cauHoi = await _db.Tasks.AsNoTracking()
                    .Where(t => t.ProjectId == a.ProjectId && a.QuestionOrder.Contains(t.SampleId))
                    .ToDictionaryAsync(t => t.SampleId, ct);
                Dictionary<Guid, GoldSample> vang = await _db.GoldSamples.AsNoTracking()
                    .Where(g => g.ProjectId == a.ProjectId && g.Purpose == "qualityCheck")
                    .ToDictionaryAsync(g => g.SampleId, ct);
                Dictionary<Guid, AttemptAnswer> traLoi = await _db.AttemptAnswers.AsNoTracking()
                    .Where(x => x.AttemptId == a.Id)
                    .ToDictionaryAsync(x => x.SampleId, ct);

                long tongByte = 0;
                foreach (AttemptAnswer x in traLoi.Values)
                {
                    tongByte = tongByte + Encoding.UTF8.GetByteCount(x.DataJson);
                }

                if (tongByte > _settings.SoLon(SettingKeys.TaskAttemptMaxBytes))
                {
                    throw new InvalidValueException("bai_qua_lon", "Toan bo cau tra loi vuot gioi han kich thuoc mot bai.");
                }

                NguongKhop nguong = NguongKhopTuSetting.Doc(_settings);
                List<InvalidAnswer> cauSai = new List<InvalidAnswer>();
                List<AttemptAnswer> cauTraLoiThat = new List<AttemptAnswer>();
                List<TraLoiNop> traLoiPhat = new List<TraLoiNop>();
                int soVang = 0;
                int soVangDung = 0;

                for (int i = 0; i < a.QuestionOrder.Count; i++)
                {
                    Guid sampleId = a.QuestionOrder[i];
                    LabelingTask? t;
                    if (!cauHoi.TryGetValue(sampleId, out t))
                    {
                        continue;
                    }

                    SampleMetadata thongTinMau = SampleMetadata.Tu(t.Metadata);
                    LabelPayload? nhan = null;
                    AttemptAnswer? x;
                    if (traLoi.TryGetValue(sampleId, out x))
                    {
                        try
                        {
                            nhan = LabelPayload.Tao(duAn.LabelSchema, x.DataJson, thongTinMau);
                        }
                        catch (LabelFormatException ex)
                        {
                            cauSai.Add(new InvalidAnswer { Index = i + 1, SampleId = sampleId, Code = ex.Code, Message = ex.Message });
                            continue;
                        }
                    }

                    GoldSample? g;
                    if (vang.TryGetValue(sampleId, out g))
                    {
                        soVang++;
                        if (nhan != null && nhan.KhopDapAn(duAn.LabelSchema, g.ExpectedPayload, nguong, thongTinMau))
                        {
                            soVangDung++;
                        }

                        continue;
                    }

                    traLoiPhat.Add(new TraLoiNop
                    {
                        SampleId = sampleId,
                        StorageKey = t.StorageKey,
                        SampleContent = t.Content,
                        SampleMetadata = t.Metadata,
                        LabelPayload = nhan,
                    });
                }

                if (cauSai.Count > 0)
                {
                    throw new BaiChuaHopLeException(cauSai);
                }

                a.Nop(soVang, soVangDung, clientIp, deviceId, bayGio);

                _events.Phat(caller, new AttemptSubmitted
                {
                    AttemptId = a.Id,
                    ProjectId = a.ProjectId,
                    LabelerId = a.LabelerId,
                    Answers = traLoiPhat,
                    GoldTotal = soVang,
                    GoldCorrect = soVangDung,
                    StartedAt = a.StartedAt,
                    SubmittedAt = bayGio,
                    ClientIp = clientIp,
                    DeviceId = deviceId,
                });

                await _db.SaveChangesAsync(ct);

                int daNop = await _db.Attempts.CountAsync(x => x.ProjectId == a.ProjectId && x.State == AttemptState.Submitted, ct);
                bool vuaDong = false;
                if (daNop >= duAn.Slots)
                {
                    vuaDong = await _thuBai.DongThuBaiAsync(duAn, "du_suat", bayGio, caller, ct);
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                return new SubmitAttemptResponse
                {
                    AttemptId = a.Id,
                    SubmittedAt = bayGio,
                    QuestionCount = a.QuestionOrder.Count,
                    AnsweredCount = traLoi.Count,
                    CollectionFinished = vuaDong,
                };
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>Labeler tu bo bai: mat suat, nguoi khac vao lam. Lam lai duoc neu du an con suat.</summary>
        public async Task BoBaiAsync(Guid attemptId, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                Attempt? a = await TaskQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Khong tim thay bai lam.");
                }

                if (a.State == AttemptState.Submitted)
                {
                    throw new RuleViolationException("bai_da_nop", "Bai da nop, khong bo duoc.");
                }

                a.Huy(_clock.GetUtcNow());
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        // =====================================================================
        // KHIEU NAI CA BAI
        // =====================================================================

        public async Task<AttemptSummary> KhieuNaiAsync(Guid attemptId, AppealRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            Guid labelerId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                Attempt? a = await TaskQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Khong tim thay bai lam.");
                }

                ProjectSnapshot duAn = await _db.ProjectSnapshots.AsNoTracking().FirstAsync(p => p.ProjectId == a.ProjectId, ct);
                if (duAn.SettledAt.HasValue || !duAn.AppealDeadline.HasValue)
                {
                    throw new RuleViolationException("het_han_khieu_nai", "Du an da chot ket qua.");
                }

                a.KhieuNai(body.Message, duAn.AppealDeadline.Value, bayGio);

                _events.Phat(caller, new AttemptAppealOpened
                {
                    AttemptId = a.Id,
                    ProjectId = a.ProjectId,
                    LabelerId = a.LabelerId,
                    Message = a.AppealMessage!,
                    AgreementPercent = a.AgreementPercent ?? 0,
                    PassThresholdPercent = duAn.PassThresholdPercent,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                int daLam = await _db.AttemptAnswers.CountAsync(x => x.AttemptId == a.Id, ct);
                return TaoTomTat(a, duAn, daLam, bayGio);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>Admin phan xu khieu nai. Chap nhan → ledger tra thuong. Het han va het khieu nai → chot du an.</summary>
        public async Task<AttemptSummary> PhanXuAsync(Guid attemptId, ResolveAppealRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Attempt? xem = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (xem == null)
            {
                throw new NotFoundException("Khong tim thay bai lam.");
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                ProjectSnapshot duAn = (await TaskQueries.KhoaDuAnAsync(_db, xem.ProjectId, ct))!;
                Attempt a = (await TaskQueries.KhoaBaiAsync(_db, attemptId, ct))!;

                a.PhanXu(body.Accept, body.Note, caller.LayUserId(), bayGio);

                int daLam = await _db.AttemptAnswers.CountAsync(x => x.AttemptId == a.Id, ct);
                _events.Phat(caller, new AttemptAppealResolved
                {
                    AttemptId = a.Id,
                    ProjectId = a.ProjectId,
                    LabelerId = a.LabelerId,
                    Accepted = body.Accept,
                    Note = a.AppealNote,
                    AnsweredCount = daLam,
                });

                await _db.SaveChangesAsync(ct);
                if (await _thuBai.ThuChotAsync(duAn, bayGio, caller, ct))
                {
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                return TaoTomTat(a, duAn, daLam, bayGio);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>GET /tasks/admin/appeals?state=open — hang doi khieu nai ca bai.</summary>
        public async Task<IReadOnlyList<AttemptSummary>> DanhSachKhieuNaiAsync(string? state, CancellationToken ct)
        {
            AppealState loc = AppealState.Open;
            if (!string.IsNullOrWhiteSpace(state) && (!Enum.TryParse(state, true, out loc) || !Enum.IsDefined(loc) || loc == AppealState.None))
            {
                throw new InvalidValueException("trang_thai_khong_hop_le", "state: open | accepted | rejected.");
            }

            List<Attempt> ds = await _db.Attempts.AsNoTracking()
                .Where(a => a.AppealState == loc)
                .OrderBy(a => a.AppealedAt)
                .Take(200)
                .ToListAsync(ct);
            return await TomTatNhieuAsync(ds, ct);
        }

        // =====================================================================
        // DOC
        // =====================================================================

        /// <summary>GET /tasks/me/attempts — moi bai cua toi, moi nhat truoc.</summary>
        public async Task<IReadOnlyList<AttemptSummary>> BaiCuaToiAsync(Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            List<Attempt> ds = await _db.Attempts.AsNoTracking()
                .Where(a => a.LabelerId == uid)
                .OrderByDescending(a => a.StartedAt)
                .Take(200)
                .ToListAsync(ct);
            return await TomTatNhieuAsync(ds, ct);
        }

        /// <summary>GET /tasks/projects/{id}/slots — ai xem du an deu xem duoc (cho du an "con x suat").</summary>
        public async Task<SlotsResponse> SuatAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            ProjectSnapshot? duAn = await _db.ProjectSnapshots.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
            if (duAn == null || !duAn.IsConfigured)
            {
                throw new NotFoundException("Du an chua chay.");
            }

            int dangLam = await _db.Attempts.CountAsync(a => a.ProjectId == projectId && a.State == AttemptState.InProgress && a.ExpiresAt > bayGio, ct);
            int daNop = await _db.Attempts.CountAsync(a => a.ProjectId == projectId && a.State == AttemptState.Submitted, ct);

            AttemptSummary? cuaToi = null;
            if (caller.UserId != null)
            {
                Guid uid = caller.UserId.Value;
                Attempt? a = await _db.Attempts.AsNoTracking()
                    .Where(x => x.ProjectId == projectId && x.LabelerId == uid)
                    .OrderByDescending(x => x.StartedAt)
                    .FirstOrDefaultAsync(ct);
                if (a != null)
                {
                    int daLam = await _db.AttemptAnswers.CountAsync(x => x.AttemptId == a.Id, ct);
                    cuaToi = TaoTomTat(a, duAn, daLam, bayGio);
                }
            }

            return new SlotsResponse
            {
                ProjectId = projectId,
                Slots = duAn.Slots,
                InProgress = dangLam,
                Submitted = daNop,
                Available = Math.Max(0, duAn.Slots - dangLam - daNop),
                QuestionCount = duAn.QuestionCount,
                RewardVnd = duAn.RewardVnd,
                PassThresholdPercent = duAn.PassThresholdPercent,
                Deadline = duAn.Deadline,
                Phase = GiaiDoan(duAn),
                CollectionFinishedAt = duAn.CollectionFinishedAt,
                GradedAt = duAn.GradedAt,
                AppealDeadline = duAn.AppealDeadline,
                Mine = cuaToi,
            };
        }

        /// <summary>GET /tasks/projects/{id}/attempts — chu du an / admin xem moi bai (ket qua, khieu nai).</summary>
        public async Task<IReadOnlyList<AttemptSummary>> BaiCuaDuAnAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await KiemChuDuAnAsync(projectId, caller, ct);
            List<Attempt> ds = await _db.Attempts.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.State != AttemptState.Cancelled)
                .OrderBy(a => a.StartedAt)
                .Take(1000)
                .ToListAsync(ct);
            return await TomTatNhieuAsync(ds, ct);
        }

        /// <summary>Tien do du an (FB-20) — chi chu du an hoac admin.</summary>
        public async Task<ProgressResponse> TienDoAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await KiemChuDuAnAsync(projectId, caller, ct);

            ProjectSnapshot duAn = await _db.ProjectSnapshots.AsNoTracking().FirstAsync(p => p.ProjectId == projectId, ct);

            // Du an da dong va da don du lieu van hanh: tra ban chup tien do cuoi.
            if (duAn.FinalProgressJson != null)
            {
                ProgressResponse? cuoi = JsonSerializer.Deserialize<ProgressResponse>(duAn.FinalProgressJson, JsonSerializerOptions.Web);
                if (cuoi != null)
                {
                    return cuoi;
                }
            }

            return await TinhTienDoAsync(duAn, ct);
        }

        /// <summary>Dem tien do tu bang attempts — KHONG kiem quyen (noi goi da kiem).</summary>
        public async Task<ProgressResponse> TinhTienDoAsync(ProjectSnapshot duAn, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Guid id = duAn.ProjectId;
            var dem = await _db.Attempts.AsNoTracking()
                .Where(a => a.ProjectId == id)
                .GroupBy(a => a.State)
                .Select(g => new { State = g.Key, So = g.Count() })
                .ToListAsync(ct);

            int So(AttemptState s)
            {
                foreach (var d in dem)
                {
                    if (d.State == s)
                    {
                        return d.So;
                    }
                }

                return 0;
            }

            int daNop = So(AttemptState.Submitted);
            DateTimeOffset tu = bayGio - TimeSpan.FromHours(24);
            int nop24h = await _db.Attempts.AsNoTracking().CountAsync(
                a => a.ProjectId == id && a.State == AttemptState.Submitted && a.SubmittedAt >= tu, ct);
            int khieuNai = await _db.Attempts.AsNoTracking().CountAsync(a => a.ProjectId == id && a.AppealState == AppealState.Open, ct);

            return new ProgressResponse
            {
                ProjectId = id,
                Slots = duAn.Slots,
                QuestionCount = duAn.QuestionCount,
                InProgress = So(AttemptState.InProgress),
                Submitted = daNop,
                Expired = So(AttemptState.Expired),
                Cancelled = So(AttemptState.Cancelled),
                Phase = GiaiDoan(duAn),
                FinishReason = duAn.FinishReason,
                Deadline = duAn.Deadline,
                CollectionFinishedAt = duAn.CollectionFinishedAt,
                GradedAt = duAn.GradedAt,
                AppealDeadline = duAn.AppealDeadline,
                SettledAt = duAn.SettledAt,
                PassedCount = duAn.PassedCount,
                OpenAppeals = khieuNai,
                EstimatedCompletionAt = duAn.CollectionFinishedAt.HasValue ? duAn.CollectionFinishedAt : UocTinhXong(duAn.Slots - daNop, nop24h, bayGio),
            };
        }

        /// <summary>Con <paramref name="conLai"/> bai, toc do <paramref name="nop24h"/> bai / 24 gio → luc du suat.</summary>
        public static DateTimeOffset? UocTinhXong(int conLai, int nop24h, DateTimeOffset bayGio)
        {
            if (conLai <= 0)
            {
                return bayGio;
            }

            if (nop24h <= 0)
            {
                return null;
            }

            return bayGio + TimeSpan.FromHours(24.0 * conLai / nop24h);
        }

        /// <summary>
        /// Cho project-svc truoc khi HUY du an: da tam dung chua, bao nhieu bai dang lam / da nop. Chi doc.
        /// </summary>
        public async Task<CloseCheckResponse> KiemDongDuAnAsync(Guid projectId, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            ProjectSnapshot? duAn = await _db.ProjectSnapshots.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);

            return new CloseCheckResponse
            {
                ProjectId = projectId,
                Paused = duAn != null && duAn.Status == SnapshotStatus.Paused,
                InProgressCount = await _db.Attempts.CountAsync(
                    a => a.ProjectId == projectId && a.State == AttemptState.InProgress && a.ExpiresAt > bayGio, ct),
                SubmittedCount = await _db.Attempts.CountAsync(a => a.ProjectId == projectId && a.State == AttemptState.Submitted, ct),
            };
        }

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        public static string GiaiDoan(ProjectSnapshot duAn)
        {
            if (duAn.SettledAt.HasValue)
            {
                return "settled";
            }

            if (duAn.GradedAt.HasValue)
            {
                return "graded";
            }

            if (duAn.CollectionFinishedAt.HasValue)
            {
                return "finished";
            }

            return "open";
        }

        private async Task<Attempt> LayCuaMinhAsync(Guid attemptId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            Attempt? a = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attemptId, ct);
            if (a == null || a.LabelerId != uid)
            {
                throw new NotFoundException("Khong tim thay bai lam.");
            }

            return a;
        }

        private async Task KiemChuDuAnAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            if (caller.IsAdmin)
            {
                if (!await _db.ProjectSnapshots.AnyAsync(p => p.ProjectId == projectId, ct))
                {
                    throw new NotFoundException("Khong tim thay du an.");
                }

                return;
            }

            Guid uid = caller.LayUserId();
            bool laChu = await _db.Members.AnyAsync(
                m => m.ProjectId == projectId && m.UserId == uid
                     && m.Role == CachedMemberRole.Owner && m.State == CachedMemberState.Active,
                ct);
            if (!laChu || !await _db.ProjectSnapshots.AnyAsync(p => p.ProjectId == projectId, ct))
            {
                throw new NotFoundException("Khong tim thay du an.");
            }
        }

        private async Task<IReadOnlyList<AttemptSummary>> TomTatNhieuAsync(List<Attempt> ds, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            List<Guid> duAnIds = ds.Select(a => a.ProjectId).Distinct().ToList();
            Dictionary<Guid, ProjectSnapshot> duAn = await _db.ProjectSnapshots.AsNoTracking()
                .Where(p => duAnIds.Contains(p.ProjectId))
                .ToDictionaryAsync(p => p.ProjectId, ct);
            List<Guid> baiIds = ds.Select(a => a.Id).ToList();
            Dictionary<Guid, int> daLam = await _db.AttemptAnswers.AsNoTracking()
                .Where(x => baiIds.Contains(x.AttemptId))
                .GroupBy(x => x.AttemptId)
                .Select(g => new { Id = g.Key, So = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.So, ct);

            List<AttemptSummary> kq = new List<AttemptSummary>();
            foreach (Attempt a in ds)
            {
                ProjectSnapshot? p;
                if (!duAn.TryGetValue(a.ProjectId, out p))
                {
                    continue;
                }

                int n;
                kq.Add(TaoTomTat(a, p, daLam.TryGetValue(a.Id, out n) ? n : 0, bayGio));
            }

            return kq;
        }

        private static string Ten<T>(T e) where T : struct, Enum
        {
            string s = e.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        private static AttemptSummary TaoTomTat(Attempt a, ProjectSnapshot p, int daLam, DateTimeOffset bayGio)
        {
            bool daCham = a.GradedAt.HasValue;
            bool coTheKhieuNai = daCham && a.Passed == false && a.AppealState == AppealState.None
                                 && p.AppealDeadline.HasValue && bayGio <= p.AppealDeadline.Value && !p.SettledAt.HasValue;

            return new AttemptSummary
            {
                AttemptId = a.Id,
                ProjectId = a.ProjectId,
                LabelerId = a.LabelerId,
                State = Ten(a.State),
                StartedAt = a.StartedAt,
                ExpiresAt = a.ExpiresAt,
                SubmittedAt = a.SubmittedAt,
                EndedAt = a.EndedAt,
                QuestionCount = a.QuestionOrder.Count,
                AnsweredCount = daLam,
                RewardVnd = p.RewardVnd,
                PassThresholdPercent = p.PassThresholdPercent,
                ComparableCount = a.ComparableCount,
                MatchedCount = a.MatchedCount,
                AgreementPercent = a.AgreementPercent,
                GoldTotal = daCham ? a.GoldTotal : null,
                GoldCorrect = daCham ? a.GoldCorrect : null,
                Passed = a.Passed,
                FinalPassed = daCham ? a.DatCuoiCung : null,
                GradedAt = a.GradedAt,
                AppealState = Ten(a.AppealState),
                AppealMessage = a.AppealMessage,
                AppealedAt = a.AppealedAt,
                AppealNote = a.AppealNote,
                AppealResolvedAt = a.AppealResolvedAt,
                AppealDeadline = p.AppealDeadline,
                CanAppeal = coTheKhieuNai,
            };
        }

        private async Task<AttemptResponse> TaoResponseAsync(Attempt a, CancellationToken ct)
        {
            ProjectSnapshot p = await _db.ProjectSnapshots.AsNoTracking().FirstAsync(x => x.ProjectId == a.ProjectId, ct);
            Dictionary<Guid, LabelingTask> cauHoi = await _db.Tasks.AsNoTracking()
                .Where(t => t.ProjectId == a.ProjectId && a.QuestionOrder.Contains(t.SampleId))
                .ToDictionaryAsync(t => t.SampleId, ct);
            Dictionary<Guid, AttemptAnswer> traLoi = await _db.AttemptAnswers.AsNoTracking()
                .Where(x => x.AttemptId == a.Id)
                .ToDictionaryAsync(x => x.SampleId, ct);

            List<AttemptQuestion> ds = new List<AttemptQuestion>();
            for (int i = 0; i < a.QuestionOrder.Count; i++)
            {
                LabelingTask? t;
                if (!cauHoi.TryGetValue(a.QuestionOrder[i], out t))
                {
                    continue;
                }

                // FP-02/03: file qua media-svc — anh mang watermark chim = ma cua labeler; client khong biet
                // khoa MinIO, khong tai duoc ban goc.
                string? fileUrl = null;
                if (t.StorageKey != null)
                {
                    MauCanXem mau = new MauCanXem
                    {
                        SampleId = t.SampleId,
                        ProjectId = t.ProjectId,
                        StorageKey = t.StorageKey,
                        Modality = t.Modality,
                    };
                    fileUrl = _media.TaoLink(mau, NguoiXem.Labeler(a.LabelerId), CheDoXem.Watermark);
                }

                AttemptAnswer? x;
                bool co = traLoi.TryGetValue(t.SampleId, out x);
                ds.Add(new AttemptQuestion
                {
                    Index = i + 1,
                    SampleId = t.SampleId,
                    Modality = t.Modality,
                    FileUrl = fileUrl,
                    Content = t.Content,
                    Metadata = t.Metadata,
                    Answer = co ? RawJson.Tu(x!.DataJson) : null,
                    AnsweredAt = co ? x!.UpdatedAt : null,
                });
            }

            return new AttemptResponse
            {
                AttemptId = a.Id,
                ProjectId = a.ProjectId,
                State = Ten(a.State),
                StartedAt = a.StartedAt,
                ExpiresAt = a.ExpiresAt,
                SubmittedAt = a.SubmittedAt,
                LabelSchema = p.LabelSchema!.ToRawJson(),
                SchemaVersion = LabelPayload.PhienBanHienTai,
                RewardVnd = p.RewardVnd,
                PassThresholdPercent = p.PassThresholdPercent,
                SensitiveContent = p.SensitiveContent,
                QuestionCount = ds.Count,
                AnsweredCount = traLoi.Count,
                Questions = ds,
            };
        }
    }
}
