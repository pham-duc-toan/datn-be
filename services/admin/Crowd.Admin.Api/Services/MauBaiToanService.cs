using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Entities;
using Crowd.Admin.Api.Persistence;
using Crowd.BuildingBlocks.Persistence;
using Crowd.Labeling;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Admin.Api.Services
{
    /// <summary>Danh muc loai bai toan / template workspace (FM-03) — xem MauBaiToan.</summary>
    public sealed partial class MauBaiToanService
    {
        private readonly AdminDbContext _db;
        private readonly TimeProvider _clock;

        public MauBaiToanService(AdminDbContext db, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _clock = clock;
        }

        [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,59}$")]
        private static partial Regex MaHopLe();

        /// <summary>Danh sach cho moi nguoi dang nhap: chi mau dang bat. Admin (tatCa) thay ca mau da tat.</summary>
        public async Task<IReadOnlyList<TemplateResponse>> DanhSachAsync(string? modality, bool tatCa, CancellationToken ct)
        {
            IQueryable<MauBaiToan> q = _db.MauBaiToans.AsNoTracking();
            if (!tatCa)
            {
                q = q.Where(m => m.Active);
            }

            if (!string.IsNullOrWhiteSpace(modality))
            {
                string mo = modality.Trim().ToLowerInvariant();
                q = q.Where(m => m.Modality == mo);
            }

            List<MauBaiToan> ds = await q.OrderBy(m => m.Modality).ThenBy(m => m.Name).ToListAsync(ct);
            return ds.Select(TaoResponse).ToList();
        }

        public async Task<TemplateResponse> LayAsync(Guid id, bool tatCa, CancellationToken ct)
        {
            MauBaiToan? m = await _db.MauBaiToans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m == null || (!m.Active && !tatCa))
            {
                throw new AdminException(404, "khong_tim_thay", "Không tìm thấy mẫu bài toán.");
            }

            return TaoResponse(m);
        }

        public async Task<TemplateResponse> TaoAsync(TemplateRequest body, Guid adminId, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string ma = (body.Code ?? string.Empty).Trim().ToLowerInvariant();
            if (!MaHopLe().IsMatch(ma))
            {
                throw new AdminException(400, "ma_khong_hop_le", "code: 2–60 ký tự a-z, 0-9, '-' (bắt đầu bằng chữ / số).");
            }

            DuLieuMau d = KiemTra(body);
            MauBaiToan m = MauBaiToan.Tao(ma, d.Ten, d.MoTa, d.Modality, d.TapNhan, d.HuongDan, body.SuggestedUnitPriceVnd,
                body.SuggestedRedundancy, adminId, _clock.GetUtcNow());
            _db.MauBaiToans.Add(m);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_workspace_templates_code"))
            {
                throw new AdminException(409, "ma_da_ton_tai", "Đã có mẫu với code này.");
            }

            return TaoResponse(m);
        }

        public async Task<TemplateResponse> SuaAsync(Guid id, TemplateRequest body, Guid adminId, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            MauBaiToan m = await LayDeSuaAsync(id, ct);
            DuLieuMau d = KiemTra(body);
            m.Sua(d.Ten, d.MoTa, d.Modality, d.TapNhan, d.HuongDan, body.SuggestedUnitPriceVnd, body.SuggestedRedundancy, adminId, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoResponse(m);
        }

        public async Task<TemplateResponse> DatHoatDongAsync(Guid id, bool bat, Guid adminId, CancellationToken ct)
        {
            MauBaiToan m = await LayDeSuaAsync(id, ct);
            m.DatHoatDong(bat, adminId, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoResponse(m);
        }

        /// <summary>
        /// Mau co san cho moi truong dev (seed): them mau con thieu theo code, khong ghi de mau admin da sua.
        /// Mau seed ban cu (ten khong dau, chua ai sua: Version 1, nguoi sua la admin seed) duoc nang len ban co dau.
        /// </summary>
        public async Task<int> SeedAsync(Guid adminId, CancellationToken ct)
        {
            // { ma, ten cu (ban khong dau), ten, mo ta, tap nhan, huong dan }
            string[][] ds = new string[][]
            {
                new string[] { "phan-loai-anh", "Phan loai anh", "Phân loại ảnh", "Mỗi ảnh chọn một lớp.",
                    "{\"modality\":\"image\",\"tools\":[{\"name\":\"lop\",\"kind\":\"classification\",\"classes\":[\"Lớp A\",\"Lớp B\"]}]}",
                    "# Hướng dẫn\n\nChọn đúng MỘT lớp cho mỗi ảnh." },
                new string[] { "ner-tieng-viet", "Gan the thuc the tieng Viet", "Gán thẻ thực thể tiếng Việt", "Đánh dấu tên người, địa điểm, tổ chức trong văn bản.",
                    "{\"modality\":\"text\",\"tools\":[{\"name\":\"thuc_the\",\"kind\":\"span\",\"classes\":[\"Tên người\",\"Địa điểm\",\"Tổ chức\"]}]}",
                    "# Hướng dẫn\n\nBôi đen trọn cụm từ của thực thể, không lấy dấu câu." },
                new string[] { "chep-loi-am-thanh", "Chep loi am thanh", "Chép lời âm thanh", "Nghe và chép lại lời nói, đánh dấu đoạn có tiếng nói.",
                    "{\"modality\":\"audio\",\"tools\":[{\"name\":\"loi_noi\",\"kind\":\"transcription\"},{\"name\":\"doan\",\"kind\":\"temporalSegment\",\"classes\":[\"Giọng nói\",\"Im lặng\"]}]}",
                    "# Hướng dẫn\n\nChép đúng từng từ, không sửa ngữ pháp." },
                new string[] { "phat-hien-vat-the-xoay", "Phat hien vat the (khung xoay)", "Phát hiện vật thể (khung xoay)", "Vẽ khung xoay theo hướng vật thể — xuất YOLO-OBB.",
                    "{\"modality\":\"image\",\"tools\":[{\"name\":\"khung\",\"kind\":\"bbox\",\"classes\":[\"Vật thể\"],\"rotatable\":true}]}",
                    "# Hướng dẫn\n\nKhung ôm sát vật thể, xoay theo cạnh dài." },
            };

            int moi = 0;
            foreach (string[] m in ds)
            {
                string ma = m[0];
                LabelSchema tapNhan = LabelSchema.Doc(m[4]);
                MauBaiToan? co = await _db.MauBaiToans.FirstOrDefaultAsync(x => x.Code == ma, ct);
                if (co == null)
                {
                    _db.MauBaiToans.Add(MauBaiToan.Tao(ma, m[2], m[3], tapNhan.Modality, tapNhan.ToRawJson().Json, m[5], null, null, adminId, _clock.GetUtcNow()));
                    moi++;
                }
                else if (co.Version == 1 && co.UpdatedBy == adminId && co.Name == m[1])
                {
                    co.Sua(m[2], m[3], tapNhan.Modality, tapNhan.ToRawJson().Json, m[5], co.SuggestedUnitPriceVnd, co.SuggestedRedundancy, adminId, _clock.GetUtcNow());
                }
            }

            await _db.SaveChangesAsync(ct);
            return moi;
        }

        private async Task<MauBaiToan> LayDeSuaAsync(Guid id, CancellationToken ct)
        {
            MauBaiToan? m = await _db.MauBaiToans.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m == null)
            {
                throw new AdminException(404, "khong_tim_thay", "Không tìm thấy mẫu bài toán.");
            }

            return m;
        }

        private sealed class DuLieuMau
        {
            public required string Ten { get; init; }

            public required string MoTa { get; init; }

            public required string Modality { get; init; }

            public required string TapNhan { get; init; }

            public required string HuongDan { get; init; }
        }

        private static DuLieuMau KiemTra(TemplateRequest body)
        {
            string ten = (body.Name ?? string.Empty).Trim();
            if (ten.Length == 0 || ten.Length > MauBaiToan.DoDaiTenToiDa)
            {
                throw new AdminException(400, "ten_khong_hop_le", "name: 1-" + MauBaiToan.DoDaiTenToiDa + " ký tự.");
            }

            string moTa = (body.Description ?? string.Empty).Trim();
            if (moTa.Length > MauBaiToan.DoDaiMoTaToiDa)
            {
                throw new AdminException(400, "mo_ta_qua_dai", "description tối đa " + MauBaiToan.DoDaiMoTaToiDa + " ký tự.");
            }

            string huongDan = body.GuidelineMarkdown ?? string.Empty;
            if (huongDan.Length > MauBaiToan.DoDaiHuongDanToiDa)
            {
                throw new AdminException(400, "huong_dan_qua_dai", "guidelineMarkdown tối đa " + MauBaiToan.DoDaiHuongDanToiDa + " ký tự.");
            }

            if (body.SuggestedUnitPriceVnd.HasValue && body.SuggestedUnitPriceVnd.Value < 0)
            {
                throw new AdminException(400, "gia_khong_hop_le", "suggestedUnitPriceVnd >= 0.");
            }

            if (body.SuggestedRedundancy.HasValue && (body.SuggestedRedundancy.Value < 1 || body.SuggestedRedundancy.Value > 20))
            {
                throw new AdminException(400, "redundancy_khong_hop_le", "suggestedRedundancy 1-20.");
            }

            if (body.LabelSchema.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new AdminException(400, "thieu_tap_nhan", "labelSchema phải là object (có modality, tools).");
            }

            // Cung bo doc voi project-svc: mau qua duoc day thi ap vao du an cung qua.
            LabelSchema tapNhan;
            try
            {
                tapNhan = LabelSchema.Doc(body.LabelSchema.GetRawText());
            }
            catch (LabelFormatException ex)
            {
                throw new AdminException(400, "tap_nhan_khong_hop_le", ex.Message);
            }

            return new DuLieuMau
            {
                Ten = ten,
                MoTa = moTa,
                Modality = tapNhan.Modality,
                TapNhan = tapNhan.ToRawJson().Json,
                HuongDan = huongDan,
            };
        }

        private static TemplateResponse TaoResponse(MauBaiToan m)
        {
            return new TemplateResponse
            {
                Id = m.Id,
                Code = m.Code,
                Name = m.Name,
                Description = m.Description,
                Modality = m.Modality,
                LabelSchema = JsonNode.Parse(m.LabelSchemaJson),
                GuidelineMarkdown = m.GuidelineMarkdown,
                SuggestedUnitPriceVnd = m.SuggestedUnitPriceVnd,
                SuggestedRedundancy = m.SuggestedRedundancy,
                Active = m.Active,
                Version = m.Version,
                UpdatedAt = m.UpdatedAt,
            };
        }
    }
}
