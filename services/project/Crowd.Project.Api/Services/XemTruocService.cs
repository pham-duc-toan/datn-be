using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crowd.BuildingBlocks.Auth.Http;
using Crowd.BuildingBlocks.Settings;
using Crowd.BuildingBlocks.Storage;
using Crowd.Labeling;
using Crowd.Project.Api.Dtos;
using Crowd.Project.Api.Helpers;
using Crowd.Project.Domain.Common;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence;
using Crowd.Settings;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Api.Services
{
    /// <summary>
    /// Hai thu chu du an dung de thay du an "qua mat labeler" (kien truc 3.39):
    ///   - MAU XEM TRUOC (P1): he thong tu lay MOT mau that hien tren the cho du an; doanh nghiep chi bat / tat (mac dinh
    ///     bat). Luat an toan (khong NDA / nhay cam / rieng tu / cau vang) nam ca o domain lan view v_reco_preview_samples.
    ///   - BAI XEM THU (Q2): dung y bai that tu du lieu that, KHONG ghi DB; cham ngay cau vang bang CUNG luat luc nop bai.
    /// </summary>
    public sealed class XemTruocService
    {
        /// <summary>So cau that toi da cua bai xem thu (them toi da 2 cau vang kiem tra).</summary>
        private const int CauThatXemThu = 5;
        private const int CauVangXemThu = 2;

        private readonly ProjectDbContext _db;
        private readonly ProjectAccessService _access;
        private readonly IMediaLinks _media;
        private readonly TenDoanhNghiep _tenDoanhNghiep;
        private readonly ISettings _settings;
        private readonly TimeProvider _clock;

        public XemTruocService(
            ProjectDbContext db,
            ProjectAccessService access,
            IMediaLinks media,
            TenDoanhNghiep tenDoanhNghiep,
            ISettings settings,
            TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (access == null)
            {
                throw new ArgumentNullException(nameof(access));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            if (tenDoanhNghiep == null)
            {
                throw new ArgumentNullException(nameof(tenDoanhNghiep));
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
            _access = access;
            _media = media;
            _tenDoanhNghiep = tenDoanhNghiep;
            _settings = settings;
            _clock = clock;
        }

        // =====================================================================
        // MAU XEM TRUOC (P1) — he thong tu lay MOT mau, doanh nghiep chi bat / tat
        // =====================================================================

        /// <summary>PUT /projects/{id}/preview { enabled } — bat / tat mau xem truoc tren the cho du an.</summary>
        public async Task<PreviewSettingResponse> DatXemTruocAsync(Guid projectId, SetPreviewRequest body, Caller caller, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            LabelingProject theoDoi = await _db.Projects.FirstAsync(p => p.Id == duAn.Id, ct);
            theoDoi.DatXemTruoc(body.Enabled, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return new PreviewSettingResponse
            {
                Enabled = theoDoi.PreviewEnabled,
                BlockedReason = theoDoi.LyDoKhongChoXemTruoc(),
                Showing = theoDoi.HienMauXemTruoc(),
            };
        }

        /// <summary>Mau that da nap xong, chua bi loai, khong phai cau vang (cung luat voi view v_reco_preview_samples).</summary>
        private IQueryable<Sample> MauHopLe(Guid projectId)
        {
            return _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId
                            && s.IngestState == TrangThaiNap.SanSang
                            && s.ExcludedAt == null
                            && !_db.GoldItems.Any(g => g.ProjectId == projectId && g.SampleId == s.Id));
        }

        // =====================================================================
        // BAI XEM THU (Q2)
        // =====================================================================

        /// <summary>
        /// Dung bai xem thu: toi da 5 cau that + 2 cau vang kiem tra, xao thu tu — giong cach bai that tron cau vang vao
        /// cau that. Du an o moi trang thai (ke ca Nhap) mien la da co tap nhan va mau. File qua media-svc co watermark
        /// mang ma chu du an (dung y nhung gi labeler thay).
        /// </summary>
        public async Task<TrialAttemptResponse> TaoBaiXemThuAsync(Guid projectId, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            if (duAn.LabelSchema == null || duAn.LabelSchema.Tools.Count == 0)
            {
                throw new RuleViolationException("chua_co_tap_nhan", "Thêm ít nhất một công cụ gán nhãn rồi mới xem thử được.");
            }

            List<Guid> cauVang = await _db.GoldItems.AsNoTracking()
                .Where(g => g.ProjectId == projectId && g.Purpose == GoldPurpose.QualityCheck)
                .Select(g => g.SampleId)
                .ToListAsync(ct);
            // Cung thu tu voi view v_reco_preview_samples (created_at, id) → mau dau = mau xem truoc tren the cho.
            List<Sample> that = await MauHopLe(projectId).OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).Take(CauThatXemThu).ToListAsync(ct);
            List<Sample> vang = await _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId && cauVang.Contains(s.Id) && s.IngestState == TrangThaiNap.SanSang)
                .OrderBy(s => s.CreatedAt)
                .Take(CauVangXemThu)
                .ToListAsync(ct);
            if (that.Count + vang.Count == 0)
            {
                throw new RuleViolationException("chua_co_mau", "Nạp mẫu ở tab Dữ liệu rồi mới xem thử được.");
            }

            int soCauThat = await MauHopLe(projectId).CountAsync(ct);
            int soCauVangDe = await _db.GoldItems.AsNoTracking().CountAsync(g => g.ProjectId == projectId && g.Purpose == GoldPurpose.QualityCheck, ct);

            List<Sample> de = that.Concat(vang).OrderBy(s => Random.Shared.Next()).ToList();
            string? tenDoanhNghiep = null;
            if (de.Any(s => s.StorageKey != null))
            {
                tenDoanhNghiep = await _tenDoanhNghiep.LayAsync(duAn.OwnerId, ct);
            }

            NguoiXem nguoi = NguoiXem.Labeler(caller.LayUserId());
            List<AttemptQuestion> cau = new List<AttemptQuestion>();
            for (int i = 0; i < de.Count; i++)
            {
                Sample s = de[i];
                string? fileUrl = null;
                string? chuNoi = null;
                if (s.StorageKey != null)
                {
                    MauCanXem mau = MauService.MauXem(s, tenDoanhNghiep);
                    fileUrl = _media.TaoLink(mau, nguoi, CheDoXem.Watermark);
                    chuNoi = _media.ChuDeLenTrinhPhat(mau, nguoi, CheDoXem.Watermark);
                }

                cau.Add(new AttemptQuestion
                {
                    Index = i + 1,
                    SampleId = s.Id,
                    Modality = s.Modality,
                    FileUrl = fileUrl,
                    WatermarkText = chuNoi,
                    Content = s.Content,
                    Question = s.Question,
                    Metadata = s.Metadata,
                    Answer = null,
                    AnsweredAt = null,
                });
            }

            return new TrialAttemptResponse
            {
                ProjectId = duAn.Id,
                LabelSchema = duAn.LabelSchema.ToRawJson(),
                SchemaVersion = LabelPayload.PhienBanHienTai,
                RewardVnd = duAn.RewardVnd,
                PassThresholdPercent = duAn.PassThresholdPercent,
                SensitiveContent = duAn.SensitiveContent,
                RealQuestionCount = soCauThat + soCauVangDe,
                PreviewSampleId = that.Count > 0 ? that[0].Id : null,
                QuestionCount = cau.Count,
                Questions = cau,
            };
        }

        /// <summary>
        /// Cham bai xem thu: kiem dinh dang tung cau (nhu nop bai that) va so cau vang voi dap an (cung nguong khop).
        /// Khong ghi gi. Cau khong thuoc du an → 400.
        /// </summary>
        public async Task<TrialCheckResponse> ChamBaiXemThuAsync(Guid projectId, SubmitAttemptRequest? body, Caller caller, CancellationToken ct)
        {
            LabelingProject duAn = await _access.LayDeQuanLyAsync(projectId, caller, ct);
            if (duAn.LabelSchema == null)
            {
                throw new RuleViolationException("chua_co_tap_nhan", "Dự án chưa có tập nhãn.");
            }

            Dictionary<Guid, string> traLoi = LamBaiService.DocCauTraLoi(body);
            List<Guid> ids = body == null || body.Answers == null
                ? new List<Guid>()
                : body.Answers.Where(x => x != null).Select(x => x.SampleId).Distinct().ToList();

            Dictionary<Guid, Sample> mau = await _db.Samples.AsNoTracking()
                .Where(s => s.ProjectId == projectId && ids.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);
            foreach (Guid id in ids)
            {
                if (!mau.ContainsKey(id))
                {
                    throw new InvalidValueException("cau_khong_thuoc_bai", "Câu " + id + " không thuộc dự án này.");
                }
            }

            Dictionary<Guid, GoldItem> vang = await _db.GoldItems.AsNoTracking()
                .Where(g => g.ProjectId == projectId && g.Purpose == GoldPurpose.QualityCheck && ids.Contains(g.SampleId))
                .ToDictionaryAsync(g => g.SampleId, ct);

            NguongKhop nguong = NguongKhopTuSetting.Doc(_settings);
            LabelSchema tapNhan = duAn.LabelSchema;
            List<InvalidAnswer> cauSai = new List<InvalidAnswer>();
            List<TrialGoldResult> ketQua = new List<TrialGoldResult>();
            int dung = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                Guid id = ids[i];
                SampleMetadata thongTin = SampleMetadata.Tu(mau[id].Metadata);
                LabelPayload? nhan = null;
                string? json;
                if (traLoi.TryGetValue(id, out json))
                {
                    try
                    {
                        nhan = LabelPayload.Tao(tapNhan, json, thongTin);
                    }
                    catch (LabelFormatException ex)
                    {
                        cauSai.Add(new InvalidAnswer { Index = i + 1, SampleId = id, Code = ex.Code, Message = ex.Message });
                        continue;
                    }
                }

                GoldItem? g;
                if (!vang.TryGetValue(id, out g))
                {
                    continue;
                }

                bool khop = nhan != null && nhan.KhopDapAn(tapNhan, g.ExpectedPayload, nguong, thongTin);
                if (khop)
                {
                    dung++;
                }

                ketQua.Add(new TrialGoldResult
                {
                    SampleId = id,
                    Answered = nhan != null,
                    Correct = khop,
                    Expected = RawJson.Tu(g.ExpectedPayload.DataJson),
                });
            }

            return new TrialCheckResponse
            {
                GoldTotal = ketQua.Count,
                GoldCorrect = dung,
                AnsweredCount = traLoi.Count,
                InvalidAnswers = cauSai,
                Gold = ketQua,
                WouldFailGold = dung < ketQua.Count,
            };
        }
    }
}
