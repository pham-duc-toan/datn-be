using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crowd.Settings
{
    /// <summary>
    /// BAN SAO mot setting trong DB cua tung service (bang settings_replica). Nguon su
    /// that la bang settings cua admin-svc; ban sao duoc cap nhat qua setting.changed /
    /// settings.snapshot, nen service van doc duoc khi admin-svc dang chet.
    /// </summary>
    public sealed class SettingReplica
    {
        private SettingReplica()
        {
            Key = string.Empty;
            ValueJson = "null";
        }

        public string Key { get; private set; }

        /// <summary>Gia tri JSON vo huong: 30, true, 0.5, "chuoi".</summary>
        public string ValueJson { get; private set; }

        public long Version { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public static SettingReplica Tao(string key, string valueJson, long version, DateTimeOffset luc)
        {
            SettingReplica r = new SettingReplica();
            r.Key = key;
            r.ValueJson = valueJson;
            r.Version = version;
            r.UpdatedAt = luc;
            return r;
        }

        /// <summary>Ghi de neu version moi hon. false = ban den tre / trung, bo qua.</summary>
        public bool ApDung(string valueJson, long version, DateTimeOffset luc)
        {
            if (version <= Version)
            {
                return false;
            }

            ValueJson = valueJson;
            Version = version;
            UpdatedAt = luc;
            return true;
        }
    }

    /// <summary>DbContext cua moi service goi modelBuilder.ApplyConfiguration(new SettingReplicaConfiguration()).</summary>
    public sealed class SettingReplicaConfiguration : IEntityTypeConfiguration<SettingReplica>
    {
        public void Configure(EntityTypeBuilder<SettingReplica> builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.ToTable("settings_replica");
            builder.HasKey(x => x.Key);
            builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(100);
            builder.Property(x => x.ValueJson).HasColumnName("value").HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.Version).HasColumnName("version").IsRequired();
            builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        }
    }
}
