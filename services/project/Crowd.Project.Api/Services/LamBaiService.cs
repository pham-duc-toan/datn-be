using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Contracts.Tasking;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Exceptions;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Annotations;
using Crowd.Project.Domain.Attempts;
using Crowd.Project.Domain.Collections;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Eligibility;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// LAM BAI ca du an (docs 3.18):
    ///
    ///   POST /tasks/projects/{id}/attempt          bat dau (giu mot suat) / mo lai bai dang lam
    ///   POST /tasks/attempts/{id}/submit           nop CA BAI (moi cau tra loi trong body): kiem dinh dang, cham cau vang
    ///
    /// Nhap (cau dang lam do) KHONG luu o backend: frontend giu trong localStorage, nop thi gui ca bai mot lan.
    ///
    /// Truoc day day la task-svc, doc BAN SAO du an / mau / cau vang / thanh vien dung tu event. Nay cung
    /// database voi du an nen doc thang bang GOC (projects, project_collections, samples, gold_items,
    /// project_members) — khong con ban sao tre, khong con event den lech thu tu. Nop bai ghi nhan vao
    /// annotations TRONG CUNG transaction (truoc day annotation-svc nhan qua attempt.submitted).
    ///
    /// Moi thao tac ghi la MOT transaction co KHOA DONG. Thu tu khoa: dot thu bai → bai → mau.
    /// </summary>
    public sealed class LamBaiService
    {
        private readonly ProjectDbContext _db;
        private readonly ProjectEventPublisher _events;
        private readonly ThuBaiService _thuBai;
        private readonly ProjectAccessService _access;
        private readonly IMediaLinks _media;
        private readonly IdentityClient _identity;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public LamBaiService(
            ProjectDbContext db,
            ProjectEventPublisher events,
            ThuBaiService thuBai,
            ProjectAccessService access,
            IMediaLinks media,
            IdentityClient identity,
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

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
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
            _access = access;
            _media = media;
            _identity = identity;
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
                throw new RuleViolationException("da_nop_bai", "Bạn đã nộp bài cho dự án này — chờ dự án kết thúc để xem kết quả.");
            }

            if (cu != null && cu.ExpiresAt > bayGio)
            {
                return await TaoResponseAsync(cu, ct);
            }

            // 1. DIEU KIEN — du an / dot / thanh vien doc thang bang goc; ho so (khoa, cap, uy tin) hoi identity qua
            //    gRPC (3.24). identity khong tra loi → 503: khong kiem duoc thi khong cho mo bai.
            LabelingProject? duAnDoc = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (duAnDoc == null)
            {
                throw new NotFoundException("Không tìm thấy dự án.");
            }

            CollectionRound? dotDoc = await _db.CollectionRounds.AsNoTracking().FirstOrDefaultAsync(d => d.ProjectId == projectId, ct);
            ProjectMember? thanhVien = await _db.ProjectMembers.AsNoTracking().FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == labelerId, ct);
            NguoiDungTraCuu? nguoi = await _identity.LayAsync(labelerId, ct);
            if (nguoi == null)
            {
                throw new ForbiddenException("tai_khoan_khong_ton_tai", "Không tìm thấy tài khoản của bạn.");
            }

            HoSoNguoiDung hoSo = nguoi.HoSo();
            string? lyDo = EligibilityPolicy.LyDoTuChoi(duAnDoc, dotDoc, thanhVien, hoSo, bayGio);
            if (lyDo != null)
            {
                throw new ForbiddenException(lyDo, EligibilityPolicy.GiaiThich(lyDo, duAnDoc, hoSo));
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // 2. KHOA dot thu bai: dem suat va dong thu bai deu xep hang qua dong nay. Doc LAI du an SAU khoa:
                // tam dung / ket thuc som khoa dong nay truoc khi doi du an, nen ben sau luon thay trang thai moi.
                CollectionRound? dot = await LamBaiQueries.KhoaDotAsync(_db, projectId, ct);
                LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
                if (!CollectionRound.ConNhanBai(duAn, dot, bayGio))
                {
                    throw new RuleViolationException("du_an_khong_nhan_bai", "Dự án không còn nhận bài làm.");
                }

                // Bai cu qua han ma worker chua kip quet: cho het han ngay de lam bai moi.
                if (cu != null)
                {
                    Attempt? khoaCu = await LamBaiQueries.KhoaBaiAsync(_db, cu.Id, ct);
                    if (khoaCu != null)
                    {
                        khoaCu.HetHan(bayGio);
                    }
                }

                // KHONG dem bai cua chinh nguoi nay: hai request "bat dau" cung luc cua MOT labeler (FE goi trung)
                // — request sau thay bai request truoc vua tao, dem thanh du suat → bao "het suat" sai. Bo qua bai
                // cua minh thi request sau di tiep toi UNIQUE (project, labeler) ben duoi va tra lai chinh bai do.
                int dangGiu = await _db.Attempts.CountAsync(
                    a => a.ProjectId == projectId && a.LabelerId != labelerId
                         && (a.State == AttemptState.Submitted || (a.State == AttemptState.InProgress && a.ExpiresAt > bayGio)),
                    ct);

                // Tran NGUOI LAM (so suat thuong x he so, chup luc duyet) — khong phai so suat thuong: cho nhieu
                // nguoi lam hon de da so chac hon; n suat thuong trao cho n bai dat nop som nhat khi cham.
                if (dangGiu >= dot!.MaxParticipants)
                {
                    throw new RuleViolationException("het_suat", "Dự án đã đủ " + dot.MaxParticipants + " người làm bài.");
                }

                // 3. DE: moi mau THAT (khong phai cau vang, chua bi loai) + cau vang kiem tra — xao rieng cho bai nay.
                List<Guid> cauThat = await _db.Samples.AsNoTracking()
                    .Where(s => s.ProjectId == projectId && s.ExcludedAt == null
                                && !_db.GoldItems.Any(g => g.ProjectId == projectId && g.SampleId == s.Id))
                    .Select(s => s.Id)
                    .ToListAsync(ct);
                List<Guid> cauVang = await _db.GoldItems.AsNoTracking()
                    .Where(g => g.ProjectId == projectId && g.Purpose == GoldPurpose.QualityCheck)
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
                // Cung labeler bam "bat dau" hai lan CUNG LUC: UNIQUE (project, labeler) WHERE dang lam / da nop chon
                // ra mot. Ben thua tra lai chinh bai ben thang vua tao.
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
        // NOP / BO
        // =====================================================================

        /// <summary>
        /// Nop CA BAI. Moi cau da tra loi phai dung dinh dang (Crowd.Labeling: theo tap nhan va metadata mau) —
        /// sai thi 400 bai_chua_hop_le kem danh sach cau, bai van giu de sua. Cau bo trong duoc phep (tinh la
        /// khong giong da so; cau vang bo trong tinh la sai). Cau vang cham ngay bang CUNG luat voi bai test dau
        /// vao; chi so dung / tong di theo bai — khong ai biet cau nao la vang cho toi khi cham.
        ///
        /// Moi cau tra loi di trong body (frontend giu nhap trong localStorage). TRONG MOT transaction: bai thanh
        /// Submitted, cau that vao annotations, cau vang vao attempts.gold_answers, outbox ghi attempt.submitted
        /// (quality-svc). Bai nop nay lam du an du nguoi → dong thu bai ngay trong cung transaction.
        /// </summary>
        public async Task<SubmitAttemptResponse> NopAsync(
            Guid attemptId, SubmitAttemptRequest? body, string? clientIp, string? deviceId, Caller caller, CancellationToken ct)
        {
            Guid labelerId = caller.LayUserId();
            DateTimeOffset bayGio = _clock.GetUtcNow();

            // Kiem body TRUOC khi khoa gi: chi giu cau co tra loi; trung cau / khong phai object → 400.
            Dictionary<Guid, string> traLoi = DocCauTraLoi(body);

            Attempt? xem = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (xem == null || xem.LabelerId != labelerId)
            {
                throw new NotFoundException("Không tìm thấy bài làm.");
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                CollectionRound? dot = await LamBaiQueries.KhoaDotAsync(_db, xem.ProjectId, ct);
                LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == xem.ProjectId, ct);
                if (dot == null || duAn.LabelSchema == null)
                {
                    throw new RuleViolationException("du_an_chua_san_sang", "Không tìm thấy cấu hình dự án.");
                }

                Attempt? a = await LamBaiQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null)
                {
                    throw new NotFoundException("Không tìm thấy bài làm.");
                }

                a.KiemConLamDuoc(bayGio);

                if (duAn.Status == ProjectStatus.Paused)
                {
                    throw new RuleViolationException("du_an_tam_dung", "Dự án đang tạm dừng — bài vẫn được giữ, nộp lại khi dự án chạy tiếp.");
                }

                if (!CollectionRound.ConNhanBai(duAn, dot, bayGio))
                {
                    throw new RuleViolationException("du_an_khong_nhan_bai", "Dự án không còn nhận bài nộp.");
                }

                LabelSchema tapNhan = duAn.LabelSchema;
                List<Guid> deBai = a.QuestionOrder.ToList();
                Dictionary<Guid, Sample> cauHoi = await _db.Samples.AsNoTracking()
                    .Where(s => s.ProjectId == a.ProjectId && deBai.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, ct);
                Dictionary<Guid, GoldItem> vang = await _db.GoldItems.AsNoTracking()
                    .Where(g => g.ProjectId == a.ProjectId && g.Purpose == GoldPurpose.QualityCheck)
                    .ToDictionaryAsync(g => g.SampleId, ct);
                foreach (Guid sampleId in traLoi.Keys)
                {
                    if (!a.CoCauHoi(sampleId))
                    {
                        throw new InvalidValueException("cau_khong_thuoc_bai", "Câu " + sampleId + " không thuộc bài làm này.");
                    }
                }

                long tongByte = 0;
                foreach (string json in traLoi.Values)
                {
                    tongByte = tongByte + Encoding.UTF8.GetByteCount(json);
                }

                if (tongByte > _settings.SoLon(SettingKeys.TaskAttemptMaxBytes))
                {
                    throw new InvalidValueException("bai_qua_lon", "Toàn bộ câu trả lời vượt giới hạn kích thước một bài.");
                }

                NguongKhop nguong = NguongKhopTuSetting.Doc(_settings);
                List<InvalidAnswer> cauSai = new List<InvalidAnswer>();
                List<LabelAnnotation> nhanMoi = new List<LabelAnnotation>();
                JsonObject traLoiVang = new JsonObject();
                int soVang = 0;
                int soVangDung = 0;

                for (int i = 0; i < a.QuestionOrder.Count; i++)
                {
                    Guid sampleId = a.QuestionOrder[i];
                    Sample? mau;
                    if (!cauHoi.TryGetValue(sampleId, out mau))
                    {
                        continue;
                    }

                    SampleMetadata thongTinMau = SampleMetadata.Tu(mau.Metadata);
                    LabelPayload? nhan = null;
                    string? x;
                    if (traLoi.TryGetValue(sampleId, out x))
                    {
                        try
                        {
                            nhan = LabelPayload.Tao(tapNhan, x, thongTinMau);
                        }
                        catch (LabelFormatException ex)
                        {
                            cauSai.Add(new InvalidAnswer { Index = i + 1, SampleId = sampleId, Code = ex.Code, Message = ex.Message });
                            continue;
                        }
                    }

                    GoldItem? g;
                    if (vang.TryGetValue(sampleId, out g))
                    {
                        soVang++;
                        if (nhan != null)
                        {
                            traLoiVang[sampleId.ToString()] = JsonNode.Parse(nhan.DataJson);
                            if (nhan.KhopDapAn(tapNhan, g.ExpectedPayload, nguong, thongTinMau))
                            {
                                soVangDung++;
                            }
                        }

                        continue;
                    }

                    if (nhan != null)
                    {
                        nhanMoi.Add(LabelAnnotation.TaoTuBaiNop(a.Id, a.ProjectId, sampleId, a.LabelerId, nhan));
                    }
                }

                if (cauSai.Count > 0)
                {
                    throw new BaiChuaHopLeException(cauSai);
                }

                a.Nop(soVang, soVangDung, traLoi.Count, traLoiVang.Count == 0 ? null : traLoiVang.ToJsonString(), clientIp, deviceId, bayGio);

                // Cau that vao kho nhan (cung transaction).
                _db.Annotations.AddRange(nhanMoi);

                _events.Phat(caller, new AttemptSubmitted
                {
                    AttemptId = a.Id,
                    ProjectId = a.ProjectId,
                    LabelerId = a.LabelerId,
                    AnsweredCount = nhanMoi.Count,
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
                if (daNop >= dot.MaxParticipants)
                {
                    vuaDong = await _thuBai.DongThuBaiAsync(dot, "du_suat", bayGio, caller, ct);
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
                Attempt? a = await LamBaiQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Không tìm thấy bài làm.");
                }

                if (a.State == AttemptState.Submitted)
                {
                    throw new RuleViolationException("bai_da_nop", "Bài đã nộp, không bỏ được.");
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
                Attempt? a = await LamBaiQueries.KhoaBaiAsync(_db, attemptId, ct);
                if (a == null || a.LabelerId != labelerId)
                {
                    throw new NotFoundException("Không tìm thấy bài làm.");
                }

                CollectionRound dot = await _db.CollectionRounds.AsNoTracking().FirstAsync(d => d.ProjectId == a.ProjectId, ct);
                LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == a.ProjectId, ct);
                if (dot.SettledAt.HasValue || !dot.AppealDeadline.HasValue)
                {
                    throw new RuleViolationException("het_han_khieu_nai", "Dự án đã chốt kết quả.");
                }

                a.KhieuNai(body.Message, dot.AppealDeadline.Value, bayGio);

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

                return TaoTomTat(a, duAn, dot, a.AnsweredCount ?? 0, bayGio);
            }
            finally
            {
                await tx.DisposeAsync();
            }
        }

        /// <summary>
        /// Admin phan xu khieu nai. Chap nhan → nhan cua bai vao ket qua, ledger tra thuong (neu con suat). Het
        /// han va het khieu nai → chot du an. Tat ca trong MOT transaction.
        /// </summary>
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
                throw new NotFoundException("Không tìm thấy bài làm.");
            }

            var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                CollectionRound dot = (await LamBaiQueries.KhoaDotAsync(_db, xem.ProjectId, ct))!;
                Attempt a = (await LamBaiQueries.KhoaBaiAsync(_db, attemptId, ct))!;
                LabelingProject duAn = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == a.ProjectId, ct);

                // Chap nhan khieu nai: duoc thuong neu CON suat (khong lay lai tien cua bai da tra). Dem duoi khoa dot.
                int daThuong = await _db.Attempts.CountAsync(x => x.ProjectId == a.ProjectId && x.Rewarded == true, ct);
                Guid admin = caller.LayUserId();
                // Chap nhan → appeal_state = Accepted: moi nhan cua bai tu dong tinh la Accepted (trang thai nhan suy
                // ra tu bai — LabelAnnotation.TrangThai), khong UPDATE nhan nao.
                a.PhanXu(body.Accept, body.Note, admin, daThuong < duAn.Slots, bayGio);

                int daLam = a.AnsweredCount ?? 0;
                _events.Phat(caller, new AttemptAppealResolved
                {
                    AttemptId = a.Id,
                    ProjectId = a.ProjectId,
                    LabelerId = a.LabelerId,
                    Accepted = body.Accept,
                    Rewarded = body.Accept && a.DuocThuong,
                    Note = a.AppealNote,
                    AnsweredCount = daLam,
                });

                await _db.SaveChangesAsync(ct);
                if (await _thuBai.ThuChotAsync(dot, bayGio, caller, ct))
                {
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                return TaoTomTat(a, duAn, dot, daLam, bayGio);
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
                .OrderByDescending(a => a.AppealedAt)
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
            LabelingProject? duAn = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
            CollectionRound? dot = await _db.CollectionRounds.AsNoTracking().FirstOrDefaultAsync(d => d.ProjectId == projectId, ct);
            if (duAn == null || dot == null)
            {
                throw new NotFoundException("Dự án chưa chạy.");
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
                    cuaToi = TaoTomTat(a, duAn, dot, SoCauDaLam(a), bayGio);
                }
            }

            return new SlotsResponse
            {
                ProjectId = projectId,
                Slots = duAn.Slots,
                MaxParticipants = dot.MaxParticipants,
                InProgress = dangLam,
                Submitted = daNop,
                Available = Math.Max(0, dot.MaxParticipants - dangLam - daNop),
                QuestionCount = duAn.QuestionCount,
                RewardVnd = duAn.RewardVnd,
                PassThresholdPercent = duAn.PassThresholdPercent,
                Deadline = duAn.Deadline,
                Phase = dot.GiaiDoan(),
                CollectionFinishedAt = duAn.GradingStartedAt,
                GradedAt = dot.GradedAt,
                AppealDeadline = dot.AppealDeadline,
                Mine = cuaToi,
            };
        }

        /// <summary>GET /tasks/projects/{id}/attempts — chu du an / admin xem moi bai (ket qua, khieu nai).</summary>
        public async Task<IReadOnlyList<AttemptSummary>> BaiCuaDuAnAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            await _access.LayChoChuAsync(projectId, caller, ct);

            List<Attempt> ds = await _db.Attempts.AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.State != AttemptState.Cancelled)
                .OrderByDescending(a => a.StartedAt)
                .Take(1000)
                .ToListAsync(ct);
            return await TomTatNhieuAsync(ds, ct);
        }

        /// <summary>Tien do du an (FB-20) — chi chu du an hoac admin.</summary>
        public async Task<ProgressResponse> TienDoAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayChoChuAsync(projectId, caller, ct);
            CollectionRound? dot = await _db.CollectionRounds.AsNoTracking().FirstOrDefaultAsync(d => d.ProjectId == projectId, ct);
            if (dot == null)
            {
                throw new NotFoundException("Dự án chưa chạy.");
            }

            // Du an da dong va da don du lieu van hanh: tra ban chup tien do cuoi.
            if (dot.FinalProgressJson != null)
            {
                ProgressResponse? cuoi = JsonSerializer.Deserialize<ProgressResponse>(dot.FinalProgressJson, JsonSerializerOptions.Web);
                if (cuoi != null)
                {
                    return cuoi;
                }
            }

            return await TinhTienDoAsync(duAn, dot, ct);
        }

        /// <summary>Dem tien do tu bang attempts — KHONG kiem quyen (noi goi da kiem).</summary>
        public async Task<ProgressResponse> TinhTienDoAsync(LabelingProject duAn, CollectionRound dot, CancellationToken ct)
        {
            if (duAn == null)
            {
                throw new ArgumentNullException(nameof(duAn));
            }

            if (dot == null)
            {
                throw new ArgumentNullException(nameof(dot));
            }

            DateTimeOffset bayGio = _clock.GetUtcNow();
            Guid id = duAn.Id;
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
                MaxParticipants = dot.MaxParticipants,
                QuestionCount = duAn.QuestionCount,
                InProgress = So(AttemptState.InProgress),
                Submitted = daNop,
                Expired = So(AttemptState.Expired),
                Cancelled = So(AttemptState.Cancelled),
                Phase = dot.GiaiDoan(),
                FinishReason = dot.FinishReason,
                Deadline = duAn.Deadline,
                CollectionFinishedAt = duAn.GradingStartedAt,
                GradedAt = dot.GradedAt,
                AppealDeadline = dot.AppealDeadline,
                SettledAt = dot.SettledAt,
                PassedCount = dot.PassedCount,
                OpenAppeals = khieuNai,
                EstimatedCompletionAt = dot.DaDongThuBai ? duAn.GradingStartedAt : UocTinhXong(dot.MaxParticipants - daNop, nop24h, bayGio),
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

        // =====================================================================
        // Ham phu tro
        // =====================================================================

        private async Task<Attempt> LayCuaMinhAsync(Guid attemptId, Caller caller, CancellationToken ct)
        {
            Guid uid = caller.LayUserId();
            Attempt? a = await _db.Attempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attemptId, ct);
            if (a == null || a.LabelerId != uid)
            {
                throw new NotFoundException("Không tìm thấy bài làm.");
            }

            return a;
        }

        /// <summary>
        /// Body nop bai → {sampleId: json cau tra loi}. Cau data null / thieu = bo trong (khong tinh). Hai dong cung
        /// mot cau hoac data khong phai object → 400.
        /// </summary>
        private static Dictionary<Guid, string> DocCauTraLoi(SubmitAttemptRequest? body)
        {
            Dictionary<Guid, string> kq = new Dictionary<Guid, string>();
            if (body == null || body.Answers == null)
            {
                return kq;
            }

            foreach (SubmittedAnswer x in body.Answers)
            {
                if (x == null)
                {
                    throw new InvalidValueException("cau_tra_loi_sai_dinh_dang", "Mỗi câu trả lời phải có sampleId.");
                }

                if (kq.ContainsKey(x.SampleId))
                {
                    throw new InvalidValueException("cau_tra_loi_trung", "Câu " + x.SampleId + " được gửi hai lần.");
                }

                bool coTraLoi = x.Data.HasValue && x.Data.Value.ValueKind != JsonValueKind.Null && x.Data.Value.ValueKind != JsonValueKind.Undefined;
                if (!coTraLoi)
                {
                    continue;
                }

                if (x.Data!.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidValueException("cau_tra_loi_sai_dinh_dang", "Câu trả lời phải là một object JSON.");
                }

                kq[x.SampleId] = x.Data.Value.GetRawText();
            }

            return kq;
        }

        /// <summary>Bai da nop: so chup luc nop. Bai dang lam: backend khong biet (nhap o frontend) → 0.</summary>
        private static int SoCauDaLam(Attempt a)
        {
            return a.AnsweredCount.HasValue ? a.AnsweredCount.Value : 0;
        }

        private async Task<IReadOnlyList<AttemptSummary>> TomTatNhieuAsync(List<Attempt> ds, CancellationToken ct)
        {
            DateTimeOffset bayGio = _clock.GetUtcNow();
            List<Guid> duAnIds = ds.Select(a => a.ProjectId).Distinct().ToList();
            Dictionary<Guid, LabelingProject> duAn = await _db.Projects.AsNoTracking()
                .Where(p => duAnIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);
            Dictionary<Guid, CollectionRound> dot = await _db.CollectionRounds.AsNoTracking()
                .Where(d => duAnIds.Contains(d.ProjectId))
                .ToDictionaryAsync(d => d.ProjectId, ct);

            List<AttemptSummary> kq = new List<AttemptSummary>();
            foreach (Attempt a in ds)
            {
                LabelingProject? p;
                CollectionRound? d;
                if (!duAn.TryGetValue(a.ProjectId, out p) || !dot.TryGetValue(a.ProjectId, out d))
                {
                    continue;
                }

                kq.Add(TaoTomTat(a, p, d, SoCauDaLam(a), bayGio));
            }

            return kq;
        }

        private static string Ten<T>(T e)
            where T : struct, Enum
        {
            string s = e.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        private static AttemptSummary TaoTomTat(Attempt a, LabelingProject p, CollectionRound dot, int daLam, DateTimeOffset bayGio)
        {
            bool daCham = a.GradedAt.HasValue;
            bool coTheKhieuNai = daCham && a.Passed == false && !a.DuocThuong && a.AppealState == AppealState.None
                                 && dot.AppealDeadline.HasValue && bayGio <= dot.AppealDeadline.Value && !dot.SettledAt.HasValue;

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
                Rewarded = daCham ? a.DuocThuong : null,
                GradedAt = a.GradedAt,
                AppealState = Ten(a.AppealState),
                AppealMessage = a.AppealMessage,
                AppealedAt = a.AppealedAt,
                AppealNote = a.AppealNote,
                AppealResolvedAt = a.AppealResolvedAt,
                AppealDeadline = dot.AppealDeadline,
                CanAppeal = coTheKhieuNai,
            };
        }

        /// <summary>
        /// Bai day du cho workspace. Bai dang lam: khong kem cau tra loi (nhap o localStorage cua frontend). Bai da
        /// nop: cau that doc tu annotations, cau vang tu attempts.gold_answers.
        /// </summary>
        private async Task<AttemptResponse> TaoResponseAsync(Attempt a, CancellationToken ct)
        {
            LabelingProject p = await _db.Projects.AsNoTracking().FirstAsync(x => x.Id == a.ProjectId, ct);
            List<Guid> deBai = a.QuestionOrder.ToList();
            Dictionary<Guid, Sample> cauHoi = await _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == a.ProjectId && deBai.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

            Dictionary<Guid, KeyValuePair<string, DateTimeOffset>> traLoi = new Dictionary<Guid, KeyValuePair<string, DateTimeOffset>>();
            if (a.State == AttemptState.Submitted)
            {
                List<LabelAnnotation> nhan = await _db.Annotations.AsNoTracking()
                    .Where(x => x.ProjectId == a.ProjectId && x.AttemptId == a.Id)
                    .ToListAsync(ct);
                foreach (LabelAnnotation x in nhan)
                {
                    traLoi[x.SampleId] = new KeyValuePair<string, DateTimeOffset>(x.DataJson, a.SubmittedAt!.Value);
                }

                if (a.GoldAnswersJson != null)
                {
                    JsonObject? vang = JsonNode.Parse(a.GoldAnswersJson) as JsonObject;
                    if (vang != null)
                    {
                        foreach (KeyValuePair<string, JsonNode?> cap in vang)
                        {
                            Guid sampleId;
                            if (cap.Value != null && Guid.TryParse(cap.Key, out sampleId))
                            {
                                traLoi[sampleId] = new KeyValuePair<string, DateTimeOffset>(cap.Value.ToJsonString(), a.SubmittedAt!.Value);
                            }
                        }
                    }
                }
            }

            List<AttemptQuestion> ds = new List<AttemptQuestion>();
            for (int i = 0; i < a.QuestionOrder.Count; i++)
            {
                Sample? s;
                if (!cauHoi.TryGetValue(a.QuestionOrder[i], out s))
                {
                    continue;
                }

                // FP-02/03: file qua media-svc — anh mang watermark chim = ma cua labeler; client khong biet khoa
                // MinIO, khong tai duoc ban goc.
                string? fileUrl = null;
                if (s.StorageKey != null)
                {
                    MauCanXem mau = new MauCanXem
                    {
                        SampleId = s.Id,
                        ProjectId = s.ProjectId,
                        StorageKey = s.StorageKey,
                        Modality = s.Modality,
                    };
                    fileUrl = _media.TaoLink(mau, NguoiXem.Labeler(a.LabelerId), CheDoXem.Watermark);
                }

                KeyValuePair<string, DateTimeOffset> x;
                bool co = traLoi.TryGetValue(s.Id, out x);
                ds.Add(new AttemptQuestion
                {
                    Index = i + 1,
                    SampleId = s.Id,
                    Modality = s.Modality,
                    FileUrl = fileUrl,
                    Content = s.Content,
                    Question = s.Question,
                    Metadata = s.Metadata,
                    Answer = co ? RawJson.Tu(x.Key) : null,
                    AnsweredAt = co ? x.Value : null,
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
                AnsweredCount = SoCauDaLam(a),
                Questions = ds,
            };
        }
    }
}
