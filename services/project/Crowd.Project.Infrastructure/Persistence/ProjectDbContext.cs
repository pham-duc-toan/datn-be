using System;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Project.Domain.Datasets;
using Crowd.Project.Domain.EntranceTests;
using Crowd.Project.Domain.Gold;
using Crowd.Project.Domain.Members;
using Crowd.Project.Domain.Projects;
using Crowd.Project.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Crowd.Project.Infrastructure.Persistence
{
    /// <summary>DbContext cua project-svc, noi toi project_db (cong 5402).</summary>
    public sealed class ProjectDbContext : DbContext
    {
        public ProjectDbContext(DbContextOptions<ProjectDbContext> options)
            : base(options)
        {
        }

        public DbSet<LabelingProject> Projects => Set<LabelingProject>();

        public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

        public DbSet<Dataset> Datasets => Set<Dataset>();

        public DbSet<Sample> Samples => Set<Sample>();

        public DbSet<GoldItem> GoldItems => Set<GoldItem>();

        public DbSet<EntranceAttempt> EntranceAttempts => Set<EntranceAttempt>();

        public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

        public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new LabelingProjectConfiguration());
            modelBuilder.ApplyConfiguration(new ProjectMemberConfiguration());
            modelBuilder.ApplyConfiguration(new DatasetConfiguration());
            modelBuilder.ApplyConfiguration(new SampleConfiguration());
            modelBuilder.ApplyConfiguration(new GoldItemConfiguration());
            modelBuilder.ApplyConfiguration(new EntranceAttemptConfiguration());

            // Ha tang dung chung: outbox (gui event) + processed_events (nhan event).
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        }
    }
}
