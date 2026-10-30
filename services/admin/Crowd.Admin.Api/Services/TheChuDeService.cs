using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crowd.Admin.Api.Dtos;
using Crowd.Admin.Api.Entities;
using Crowd.Admin.Api.Persistence;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.BuildingBlocks.Tags;
using Crowd.Contracts.Admin;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Admin.Api.Services
{
    /// <summary>Danh muc the chu de (linh vuc / ngon ngu) — xem TheChuDe.</summary>
    public sealed class TheChuDeService
    {
        private readonly AdminDbContext _db;
        private readonly IOutboxWriter _outbox;
        private readonly TimeProvider _clock;

        public TheChuDeService(AdminDbContext db, IOutboxWriter outbox, TimeProvider clock)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _db = db;
            _outbox = outbox;
            _clock = clock;
        }

        /// <summary>
        /// Ca the an / da gop (frontend can ten de hien the da gan tu truoc); o chon the chi hien the con dung.
        /// Sap theo loai roi ten.
        /// </summary>
        public async Task<IReadOnlyList<TagResponse>> DanhSachAsync(string? kind, CancellationToken ct)
        {
            IQueryable<TheChuDe> q = _db.TheChuDes.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(kind))
            {
                string loai = kind.Trim().ToLowerInvariant();
                q = q.Where(t => t.Kind == loai);
            }

            List<TheChuDe> ds = await q.OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync(ct);
            return ds.Select(TaoResponse).ToList();
        }

        public async Task<TagResponse> TaoAsync(CreateTagRequest body, Guid adminId, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            string loai = (body.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (!MaThe.LaLoaiHopLe(loai))
            {
                throw new AdminException(400, "loai_the_khong_hop_le", "Loại thẻ chỉ gồm lĩnh vực (domain) hoặc ngôn ngữ (language).");
            }

            string ten = KiemTen(body.Name);
            string ma = MaThe.TuChu(string.IsNullOrWhiteSpace(body.Code) ? ten : body.Code);
            if (ma.Length == 0)
            {
                throw new AdminException(400, "ma_the_khong_hop_le", "Mã thẻ cần có chữ cái hoặc chữ số.");
            }

            TheChuDe? trung = await _db.TheChuDes.AsNoTracking().FirstOrDefaultAsync(t => t.Kind == loai && t.Code == ma, ct);
            if (trung != null)
            {
                throw new AdminException(409, "the_da_ton_tai", "Đã có thẻ mã “" + ma + "” (" + trung.Name + ").");
            }

            TheChuDe moi = TheChuDe.Tao(loai, ma, ten, adminId, _clock.GetUtcNow());
            _db.TheChuDes.Add(moi);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, "ux_tags_kind_code"))
            {
                throw new AdminException(409, "the_da_ton_tai", "Đã có thẻ mã “" + ma + "”.");
            }

            return TaoResponse(moi);
        }

        public async Task<TagResponse> DoiTenAsync(Guid id, RenameTagRequest body, Guid adminId, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            TheChuDe t = await LayDeSuaAsync(id, ct);
            t.DoiTen(KiemTen(body.Name), adminId, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoResponse(t);
        }

        public async Task<TagResponse> DatAnAsync(Guid id, bool an, Guid adminId, CancellationToken ct)
        {
            TheChuDe t = await LayDeSuaAsync(id, ct);
            if (t.DaGop)
            {
                throw new AdminException(409, "the_da_gop", "Thẻ đã được gộp vào thẻ khác, không bật lại được.");
            }

            t.DatAn(an, adminId, _clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return TaoResponse(t);
        }

        /// <summary>
        /// Gop the id vao the dich: the nay an han, cac the da gop vao no truoc do chuyen sang the dich (khong de chuoi
        /// gop), phat tag.merged de identity / project doi ma da gan.
        /// </summary>
        public async Task<TagResponse> GopAsync(Guid id, MergeTagRequest body, Guid adminId, Guid correlationId, CancellationToken ct)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            TheChuDe nguon = await LayDeSuaAsync(id, ct);
            if (nguon.DaGop)
            {
                throw new AdminException(409, "the_da_gop", "Thẻ này đã được gộp trước đó.");
            }

            TheChuDe? dich = await _db.TheChuDes.FirstOrDefaultAsync(t => t.Id == body.TargetId, ct);
            if (dich == null || dich.Id == nguon.Id || dich.Kind != nguon.Kind)
            {
                throw new AdminException(400, "the_dich_khong_hop_le", "Chọn một thẻ khác, cùng loại, để gộp vào.");
            }

            if (dich.DaGop)
            {
                throw new AdminException(400, "the_dich_da_gop", "Thẻ đích đã được gộp vào thẻ khác — chọn thẻ còn dùng.");
            }

            DateTimeOffset luc = _clock.GetUtcNow();
            List<TheChuDe> daGopVaoNguon = await _db.TheChuDes.Where(t => t.Kind == nguon.Kind && t.MergedInto == nguon.Code).ToListAsync(ct);
            foreach (TheChuDe cu in daGopVaoNguon)
            {
                cu.GopVao(dich.Code, adminId, luc);
            }

            nguon.GopVao(dich.Code, adminId, luc);
            _outbox.Enqueue(EventEnvelope.Create(
                SettingService.Producer,
                correlationId,
                new TagMerged { Kind = nguon.Kind, FromCode = nguon.Code, ToCode = dich.Code },
                null,
                new EventActor(adminId, ActorRole.Admin)));
            await _db.SaveChangesAsync(ct);
            return TaoResponse(nguon);
        }

        /// <summary>
        /// Danh muc mac dinh: them the con thieu theo ma (khong dong vao the admin da doi ten / an / gop). Chay moi lan
        /// khoi dong, moi moi truong — day la du lieu tham chieu, khong phai du lieu demo.
        /// </summary>
        public async Task<int> SeedAsync(CancellationToken ct)
        {
            string[][] ds = new string[][]
            {
                new string[] { MaThe.LinhVuc, "Y tế" },
                new string[] { MaThe.LinhVuc, "Giao thông" },
                new string[] { MaThe.LinhVuc, "Thương mại điện tử" },
                new string[] { MaThe.LinhVuc, "Tài chính – ngân hàng" },
                new string[] { MaThe.LinhVuc, "Giáo dục" },
                new string[] { MaThe.LinhVuc, "Pháp luật" },
                new string[] { MaThe.LinhVuc, "Nông nghiệp" },
                new string[] { MaThe.LinhVuc, "Thực phẩm – ẩm thực" },
                new string[] { MaThe.LinhVuc, "Thời trang" },
                new string[] { MaThe.LinhVuc, "Bất động sản" },
                new string[] { MaThe.LinhVuc, "Thể thao" },
                new string[] { MaThe.LinhVuc, "Truyền thông – giải trí" },
                new string[] { MaThe.LinhVuc, "Công nghệ thông tin" },
                new string[] { MaThe.LinhVuc, "Sản xuất – công nghiệp" },
                new string[] { MaThe.LinhVuc, "Môi trường" },
                new string[] { MaThe.LinhVuc, "An ninh – giám sát" },
                new string[] { MaThe.LinhVuc, "Chăm sóc khách hàng" },
                new string[] { MaThe.NgonNgu, "Tiếng Việt", "vi" },
                new string[] { MaThe.NgonNgu, "Tiếng Anh", "en" },
                new string[] { MaThe.NgonNgu, "Tiếng Trung", "zh" },
                new string[] { MaThe.NgonNgu, "Tiếng Nhật", "ja" },
                new string[] { MaThe.NgonNgu, "Tiếng Hàn", "ko" },
                new string[] { MaThe.NgonNgu, "Tiếng Pháp", "fr" },
                new string[] { MaThe.NgonNgu, "Tiếng Đức", "de" },
                new string[] { MaThe.NgonNgu, "Tiếng Thái", "th" },
                new string[] { MaThe.NgonNgu, "Tiếng Khmer", "km" },
                new string[] { MaThe.NgonNgu, "Tiếng Lào", "lo" },
            };

            HashSet<string> daCo = (await _db.TheChuDes.AsNoTracking().Select(t => t.Kind + ":" + t.Code).ToListAsync(ct)).ToHashSet();
            DateTimeOffset luc = _clock.GetUtcNow();
            int moi = 0;
            foreach (string[] d in ds)
            {
                string ma = d.Length > 2 ? d[2] : MaThe.TuChu(d[1]);
                if (daCo.Add(d[0] + ":" + ma))
                {
                    _db.TheChuDes.Add(TheChuDe.Tao(d[0], ma, d[1], null, luc));
                    moi++;
                }
            }

            await _db.SaveChangesAsync(ct);
            return moi;
        }

        private static string KiemTen(string? name)
        {
            string ten = (name ?? string.Empty).Trim();
            if (ten.Length == 0 || ten.Length > TheChuDe.DoDaiTenToiDa)
            {
                throw new AdminException(400, "ten_the_khong_hop_le", "Tên thẻ từ 1 đến " + TheChuDe.DoDaiTenToiDa + " ký tự.");
            }

            return ten;
        }

        private async Task<TheChuDe> LayDeSuaAsync(Guid id, CancellationToken ct)
        {
            TheChuDe? t = await _db.TheChuDes.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t == null)
            {
                throw new AdminException(404, "khong_tim_thay", "Không tìm thấy thẻ.");
            }

            return t;
        }

        private static TagResponse TaoResponse(TheChuDe t)
        {
            return new TagResponse
            {
                Id = t.Id,
                Kind = t.Kind,
                Code = t.Code,
                Name = t.Name,
                Hidden = t.Hidden,
                MergedInto = t.MergedInto,
                UpdatedAt = t.UpdatedAt,
            };
        }
    }
}
