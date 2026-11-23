using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureHybridProjects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeamAcademicConfiguration>(e =>
        {
            e.ToTable("team_academic_configurations"); e.HasKey(x => x.TeamId);
            e.Property(x => x.TeamId).HasColumnName("team_id").ValueGeneratedNever();
            e.Property(x => x.ProjectMode).HasColumnName("project_mode").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.PrimaryMajorId).HasColumnName("primary_major_id");
            e.Property(x => x.LeadDepartmentId).HasColumnName("lead_department_id");
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.ResponsibilityVersion).HasColumnName("responsibility_version");
            e.Property(x => x.TrackCode).HasColumnName("track_code").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.HasIntegrationDeliverable).HasColumnName("has_integration_deliverable").HasDefaultValue(false);
            e.Property(x => x.MktAttributionLimitsNote).HasColumnName("mkt_attribution_limits_note").HasMaxLength(2000);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Major>().WithMany().HasForeignKey(x => x.PrimaryMajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.LeadDepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Requirements).WithOne().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<TeamMajorRequirement>(e =>
        {
            e.ToTable("team_major_requirements"); e.HasKey(x => new { x.TeamId, x.MajorId });
            e.Property(x => x.TeamId).HasColumnName("team_id"); e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.MinMembers).HasColumnName("min_members"); e.Property(x => x.MaxMembers).HasColumnName("max_members");
            e.Property(x => x.Responsibility).HasColumnName("responsibility").HasMaxLength(1000);
            e.HasOne<Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ProjectRegistrationSnapshot>(e =>
        {
            e.ToTable("project_registration_snapshots"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ProjectPeriodId).HasColumnName("project_period_id");
            e.Property(x => x.LeadDepartmentId).HasColumnName("lead_department_id");
            e.Property(x => x.SubmittedBy).HasColumnName("submitted_by");
            e.Property(x => x.SubmittedAt).HasColumnName("submitted_at").HasPrecision(0);
            e.Property(x => x.SnapshotJson).HasColumnName("snapshot_json");
            e.HasIndex(x => new { x.ProjectId, x.Id });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ProjectPeriod>().WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.LeadDepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Decisions).WithOne().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ProjectDepartmentDecision>(e =>
        {
            e.ToTable("project_department_decisions"); e.HasKey(x => new { x.SnapshotId, x.DepartmentId });
            e.Property(x => x.SnapshotId).HasColumnName("snapshot_id"); e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.DecidedBy).HasColumnName("decided_by");
            e.Property(x => x.DecidedAt).HasColumnName("decided_at").HasPrecision(0);
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Project>(e =>
        {
            e.Property(x => x.TrackCode).HasColumnName("track_code").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.IntegrationDeliverableSummary).HasColumnName("integration_deliverable_summary").HasMaxLength(2000);
            e.Property(x => x.MktAttributionLimits).HasColumnName("mkt_attribution_limits").HasMaxLength(2000);
        });
        modelBuilder.Entity<ProjectGateRoundScopeDecision>(e =>
        {
            e.ToTable("project_gate_round_scope_decisions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.GateInstanceId).HasColumnName("gate_instance_id");
            e.Property(x => x.RoundNumber).HasColumnName("round_number");
            e.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            e.Property(x => x.DecidedBy).HasColumnName("decided_by");
            e.Property(x => x.DecidedAt).HasColumnName("decided_at").HasPrecision(0);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.HasIndex(x => new { x.GateInstanceId, x.RoundNumber, x.Scope }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ProjectGateInstance>().WithMany().HasForeignKey(x => x.GateInstanceId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.DecidedBy).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<WorkflowSlaNotification>(e =>
        {
            e.ToTable("workflow_sla_notifications"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(40).IsUnicode(false);
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.RoundNumber).HasColumnName("round_number");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.SlaTier).HasColumnName("sla_tier").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.SentAt).HasColumnName("sent_at").HasPrecision(0);
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.RoundNumber, x.DepartmentId, x.SlaTier }).IsUnique();
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
