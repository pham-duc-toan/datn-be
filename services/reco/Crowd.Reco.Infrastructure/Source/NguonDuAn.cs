using System;
using System.Collections.Generic;
using System.Text.Json;
using Crowd.Reco.Domain;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Crowd.Reco.Infrastructure.Source
{
    /// <summary>Mot dong v_reco_projects (chua gan SeenCount / OpenedAt — bo nho cua reco-svc tu gan).</summary>
    public sealed class DongDuAn
    {
        public required Guid Id { get; init; }

        public required Guid OwnerId { get; init; }

        public required string Name { get; init; }

        public required string Description { get; init; }

        public required string Modality { get; init; }

        public required string[] Domains { get; init; }

        public required string[] Languages { get; init; }

        public required string[] ToolKinds { get; init; }

        public required long RewardVnd { get; init; }

        public required int Slots { get; init; }

        public required int QuestionCount { get; init; }

        public DateTimeOffset? Deadline { get; init; }

        public required DateTimeOffset PublishedAt { get; init; }

        public required bool IsPrivate { get; init; }

        public int? MinLevel { get; init; }

        public int? MinReputation { get; init; }

        public required bool RequireEntranceTest { get; init; }

        public required bool RequireVerifiedIdentity { get; init; }

        public required bool SensitiveContent { get; init; }

        public required bool NdaRequired { get; init; }

        public required bool Accepting { get; init; }

        public required int SubmittedCount { get; init; }

        public required int InProgressCount { get; init; }

        public required int StartedCount { get; init; }

        public double? MedianSecondsPerQuestion { get; init; }

        public required IReadOnlyList<CongCuGoiY> Tools { get; init; }
    }

    /// <summary>Mau xem truoc (v_reco_preview_samples) — MOT mau moi du an, he thong tu lay.</summary>
    public sealed class DongMauXemTruoc
    {
        public required Guid ProjectId { get; init; }

        public required Guid SampleId { get; init; }

        public required string Modality { get; init; }

        public string? StorageKey { get; init; }

        public string? ContentJson { get; init; }

        public required string MetadataJson { get; init; }

        public string? Question { get; init; }
    }

    /// <summary>Tap loai tru / duoc thay cua MOT labeler (v_reco_exclusions + v_reco_owner_blocks).</summary>
    public sealed class LoaiTruCuaNguoi
    {
        public required HashSet<Guid> DaNop { get; init; }

        public required HashSet<Guid> BiChan { get; init; }

        public required HashSet<Guid> ThanhVien { get; init; }

        public required HashSet<Guid> DoanhNghiepChan { get; init; }
    }

    /// <summary>
    /// Doc THANG view cua project-svc bang role reco_reader (kien truc 3.32 / 3.39) — khong ban sao, khong gRPC. Pool
    /// nho (connection string Maximum Pool Size=3): tai len project_db KHONG tang theo so nguoi luot — bo nho nap moi
    /// 30 giay; tap loai tru cua mot nguoi chi doc khi Redis thieu (sau do cap nhat bang event).
    /// </summary>
    public sealed class NguonDuAn : IDisposable
    {
        private const string CotDuAn = "project_id, owner_id, name, description, modality, domains, languages, tool_kinds, reward_vnd, slots, "
            + "question_count, deadline, published_at, visibility, min_level, min_reputation, require_entrance_test, require_verified_identity, "
            + "sensitive_content, nda_required, accepting, submitted_count, in_progress_count, started_count, median_seconds_per_question, tools::text";

        private readonly NpgsqlDataSource _project;

        public NguonDuAn(string projectRead)
        {
            if (string.IsNullOrWhiteSpace(projectRead))
            {
                throw new ArgumentException("Thieu chuoi ket noi doc view cua project-svc.", nameof(projectRead));
            }

            _project = NpgsqlDataSource.Create(projectRead);
        }

        public void Dispose()
        {
            _project.Dispose();
        }

        /// <summary>Moi du an DANG NHAN BAI (bo nho goi y).</summary>
        public async Task<List<DongDuAn>> DocDangNhanBaiAsync(CancellationToken ct)
        {
            List<DongDuAn> ds = new List<DongDuAn>();
            await using (NpgsqlCommand cmd = _project.CreateCommand("SELECT " + CotDuAn + " FROM v_reco_projects WHERE accepting"))
            await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    ds.Add(Doc(r));
                }
            }

            return ds;
        }

        /// <summary>Mot du an theo id (ke ca da dong) — consumer cham bai can the cua du an da dong. null = khong co.</summary>
        public async Task<DongDuAn?> DocMotAsync(Guid projectId, CancellationToken ct)
        {
            await using (NpgsqlCommand cmd = _project.CreateCommand("SELECT " + CotDuAn + " FROM v_reco_projects WHERE project_id = $1"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = projectId });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    if (await r.ReadAsync(ct))
                    {
                        return Doc(r);
                    }
                }
            }

            return null;
        }

        public async Task<LoaiTruCuaNguoi> DocLoaiTruAsync(Guid userId, CancellationToken ct)
        {
            LoaiTruCuaNguoi kq = new LoaiTruCuaNguoi
            {
                DaNop = new HashSet<Guid>(),
                BiChan = new HashSet<Guid>(),
                ThanhVien = new HashSet<Guid>(),
                DoanhNghiepChan = new HashSet<Guid>(),
            };

            await using (NpgsqlCommand cmd = _project.CreateCommand("SELECT project_id, kind FROM v_reco_exclusions WHERE user_id = $1"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        Guid p = r.GetGuid(0);
                        string loai = r.GetString(1);
                        if (loai == "submitted")
                        {
                            kq.DaNop.Add(p);
                        }
                        else if (loai == "blocked")
                        {
                            kq.BiChan.Add(p);
                        }
                        else if (loai == "member")
                        {
                            kq.ThanhVien.Add(p);
                        }
                    }
                }
            }

            await using (NpgsqlCommand cmd = _project.CreateCommand("SELECT owner_id FROM v_reco_owner_blocks WHERE user_id = $1"))
            {
                cmd.Parameters.Add(new NpgsqlParameter { Value = userId });
                await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await r.ReadAsync(ct))
                    {
                        kq.DoanhNghiepChan.Add(r.GetGuid(0));
                    }
                }
            }

            return kq;
        }

        /// <summary>Mau xem truoc cua cac du an dang nhan bai (view tu loc NDA / nhay cam / rieng tu / cau vang).</summary>
        public async Task<List<DongMauXemTruoc>> DocMauXemTruocAsync(CancellationToken ct)
        {
            List<DongMauXemTruoc> ds = new List<DongMauXemTruoc>();
            await using (NpgsqlCommand cmd = _project.CreateCommand(
                "SELECT project_id, sample_id, modality, storage_key, content::text, metadata::text, question FROM v_reco_preview_samples"))
            await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                {
                    ds.Add(new DongMauXemTruoc
                    {
                        ProjectId = r.GetGuid(0),
                        SampleId = r.GetGuid(1),
                        Modality = r.GetString(2),
                        StorageKey = r.IsDBNull(3) ? null : r.GetString(3),
                        ContentJson = r.IsDBNull(4) ? null : r.GetString(4),
                        MetadataJson = r.IsDBNull(5) ? "{}" : r.GetString(5),
                        Question = r.IsDBNull(6) ? null : r.GetString(6),
                    });
                }
            }

            return ds;
        }

        private static DongDuAn Doc(NpgsqlDataReader r)
        {
            return new DongDuAn
            {
                Id = r.GetGuid(0),
                OwnerId = r.GetGuid(1),
                Name = r.GetString(2),
                Description = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                Modality = r.GetString(4),
                Domains = r.IsDBNull(5) ? Array.Empty<string>() : r.GetFieldValue<string[]>(5),
                Languages = r.IsDBNull(6) ? Array.Empty<string>() : r.GetFieldValue<string[]>(6),
                ToolKinds = r.IsDBNull(7) ? Array.Empty<string>() : r.GetFieldValue<string[]>(7),
                RewardVnd = r.GetInt64(8),
                Slots = r.GetInt32(9),
                QuestionCount = r.GetInt32(10),
                Deadline = r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11),
                PublishedAt = r.GetFieldValue<DateTimeOffset>(12),
                IsPrivate = string.Equals(r.GetString(13), "Private", StringComparison.Ordinal),
                MinLevel = r.IsDBNull(14) ? null : r.GetInt32(14),
                MinReputation = r.IsDBNull(15) ? null : r.GetInt32(15),
                RequireEntranceTest = r.GetBoolean(16),
                RequireVerifiedIdentity = r.GetBoolean(17),
                SensitiveContent = r.GetBoolean(18),
                NdaRequired = r.GetBoolean(19),
                Accepting = !r.IsDBNull(20) && r.GetBoolean(20),
                SubmittedCount = (int)r.GetInt64(21),
                InProgressCount = (int)r.GetInt64(22),
                StartedCount = (int)r.GetInt64(23),
                MedianSecondsPerQuestion = r.IsDBNull(24) ? null : r.GetDouble(24),
                Tools = r.IsDBNull(25) ? Array.Empty<CongCuGoiY>() : DocCongCu(r.GetString(25)),
            };
        }

        /// <summary>Doc mang tools cua label_schema (camelCase). Hong / sai kieu → bo qua cong cu do.</summary>
        public static IReadOnlyList<CongCuGoiY> DocCongCu(string json)
        {
            List<CongCuGoiY> ds = new List<CongCuGoiY>();
            try
            {
                using (JsonDocument d = JsonDocument.Parse(json))
                {
                    if (d.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        return ds;
                    }

                    foreach (JsonElement t in d.RootElement.EnumerateArray())
                    {
                        if (t.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        string? ten = Chuoi(t, "name");
                        string? loai = Chuoi(t, "kind");
                        if (ten == null || loai == null)
                        {
                            continue;
                        }

                        List<string> lop = new List<string>();
                        JsonElement c;
                        if (t.TryGetProperty("classes", out c) && c.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement x in c.EnumerateArray())
                            {
                                if (x.ValueKind == JsonValueKind.String)
                                {
                                    lop.Add(x.GetString() ?? string.Empty);
                                }
                            }
                        }

                        string? cauHoi = Chuoi(t, "question");
                        ds.Add(new CongCuGoiY
                        {
                            Name = ten,
                            Kind = loai,
                            Question = string.IsNullOrWhiteSpace(cauHoi) ? null : cauHoi.Trim(),
                            Classes = lop,
                        });
                    }
                }
            }
            catch (JsonException)
            {
                return ds;
            }

            return ds;
        }

        private static string? Chuoi(JsonElement o, string ten)
        {
            JsonElement v;
            if (o.TryGetProperty(ten, out v) && v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }

            return null;
        }
    }
}
