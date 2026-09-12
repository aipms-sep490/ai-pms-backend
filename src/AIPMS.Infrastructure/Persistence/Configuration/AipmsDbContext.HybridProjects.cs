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
    }
}
