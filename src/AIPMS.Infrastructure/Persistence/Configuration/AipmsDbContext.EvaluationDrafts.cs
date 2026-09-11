using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureEvaluationDrafts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvaluationAssignment>(e =>
        {
            e.ToTable("evaluation_assignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.EvaluatorId).HasColumnName("evaluator_id");
            e.Property(x => x.RubricId).HasColumnName("rubric_id");
            e.Property(x => x.ProjectPeriodId).HasColumnName("project_period_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.EvaluationType).HasColumnName("evaluation_type").HasMaxLength(30);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(x => x.AssignedBy).HasColumnName("assigned_by");
            e.Property(x => x.AssignedAt).HasColumnName("assigned_at").HasPrecision(0);
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasPrecision(0);
            e.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(1000);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectId, x.EvaluatorId, x.EvaluationType }).IsUnique()
                .HasFilter("[status] = N'ACTIVE'").HasDatabaseName("uq_evaluation_assignments_active");
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.EvaluatorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Rubric>().WithMany().HasForeignKey(x => x.RubricId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ProjectPeriod>().WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<EvaluationDraftState>(e =>
        {
            e.ToTable("evaluation_draft_states");
            e.HasKey(x => x.EvaluationId);
            e.Property(x => x.EvaluationId).HasColumnName("evaluation_id").ValueGeneratedNever();
            e.Property(x => x.AssignmentId).HasColumnName("assignment_id");
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.Property(x => x.CalculationRule).HasColumnName("calculation_rule").HasMaxLength(50).IsUnicode(false);
            e.HasIndex(x => x.AssignmentId).IsUnique();
            e.HasOne<Evaluation>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<EvaluationAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
