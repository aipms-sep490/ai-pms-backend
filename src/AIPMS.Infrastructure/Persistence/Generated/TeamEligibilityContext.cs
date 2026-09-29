using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<TeamEligibilityCheck> TeamEligibilityChecks { get; set; } = null!;
    public virtual DbSet<TeamEligibilityIssue> TeamEligibilityIssues { get; set; } = null!;

    private static void ConfigureTeamEligibilitySnapshots(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeamEligibilityCheck>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_team_eligibility_checks");

            entity.ToTable("team_eligibility_checks");

            entity.HasIndex(e => new { e.TeamId, e.ProjectId, e.RoundType, e.RevisionHistoryId, e.Id }, "ix_team_eligibility_checks_round_lookup").IsDescending(false, false, false, false, true);

            entity.HasIndex(e => new { e.TeamId, e.Fingerprint, e.Id }, "ix_team_eligibility_checks_team_fingerprint").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.TeamId, e.EvaluationKey }, "ux_team_eligibility_checks_team_evaluation_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AcademicScopeHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("academic_scope_hash");
            entity.Property(e => e.CheckedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("checked_at");
            entity.Property(e => e.CheckedBy).HasColumnName("checked_by");
            entity.Property(e => e.EvaluationKey)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("evaluation_key");
            entity.Property(e => e.Fingerprint)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("fingerprint");
            entity.Property(e => e.PolicyVersion)
                .HasMaxLength(100)
                .HasColumnName("policy_version");
            entity.Property(e => e.ProjectContextHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("project_context_hash");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.ProjectMode)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasColumnName("project_mode");
            entity.Property(e => e.ProjectPeriodId).HasColumnName("project_period_id");
            entity.Property(e => e.Result)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("result");
            entity.Property(e => e.RevisionHistoryId).HasColumnName("revision_history_id");
            entity.Property(e => e.RosterHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("roster_hash");
            entity.Property(e => e.RoundType)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("round_type");
            entity.Property(e => e.RuleVersion)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("rule_version");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.TemporalStateHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("temporal_state_hash");
            entity.Property(e => e.TriggerSource)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasColumnName("trigger_source");
            entity.Property(e => e.ValidUntilAt)
                .HasPrecision(0)
                .HasColumnName("valid_until_at");

            entity.HasOne<Team>().WithMany().HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_eligibility_checks_team");
            entity.HasOne<ProjectPeriod>().WithMany().HasForeignKey(e => e.ProjectPeriodId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_eligibility_checks_period");
            entity.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_eligibility_checks_project");
            entity.HasOne<ProjectStatusHistory>().WithMany().HasForeignKey(e => e.RevisionHistoryId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_eligibility_checks_revision_history");
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.CheckedBy)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_eligibility_checks_user");
        });

        modelBuilder.Entity<TeamEligibilityIssue>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_team_eligibility_issues");

            entity.ToTable("team_eligibility_issues");

            entity.HasIndex(e => e.EligibilityCheckId, "ix_team_eligibility_issues_check");

            entity.HasIndex(e => new { e.EligibilityCheckId, e.SortOrder }, "ux_team_eligibility_issues_check_sort").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ActualValue)
                .HasMaxLength(255)
                .HasColumnName("actual_value");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.EligibilityCheckId).HasColumnName("eligibility_check_id");
            entity.Property(e => e.ExpectedValue)
                .HasMaxLength(255)
                .HasColumnName("expected_value");
            entity.Property(e => e.MajorId).HasColumnName("major_id");
            entity.Property(e => e.Message)
                .HasMaxLength(1000)
                .HasColumnName("message");
            entity.Property(e => e.RuleCode)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("rule_code");
            entity.Property(e => e.Severity)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("ERROR")
                .HasColumnName("severity");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.EligibilityCheck).WithMany(p => p.TeamEligibilityIssues)
                .HasForeignKey(d => d.EligibilityCheckId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_team_eligibility_issues_check");
            entity.HasOne<Major>().WithMany().HasForeignKey(e => e.MajorId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_eligibility_issues_major");
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("fk_team_eligibility_issues_user");
        });
    }
}
