using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigurePolicyEvaluation(ModelBuilder b)
    {
        b.Entity<PeriodPolicyVersion>(e =>
        {
            e.ToTable("period_policy_versions"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectPeriodId, x.Version }).IsUnique();
            e.HasOne<Models.ProjectPeriod>().WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<PeriodPolicyUsage>(e =>
        {
            e.ToTable("period_policy_usages"); e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(40);
            e.HasIndex(x => new { x.EntityType, x.EntityId }).IsUnique();
            e.HasOne<PeriodPolicyVersion>().WithMany().HasForeignKey(x => x.PolicyVersionId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<EvaluationScheme>(e =>
        {
            e.ToTable("evaluation_schemes"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.CalculationRule).HasMaxLength(100);
            e.Property(x => x.PassThreshold).HasPrecision(5, 2);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
            e.HasMany(x => x.Components).WithOne().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PeriodPolicyVersion>().WithMany().HasForeignKey(x => x.PolicyVersionId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<EvaluationSchemeComponent>(e =>
        {
            e.ToTable("evaluation_scheme_components"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Scope).HasMaxLength(20);
            e.Property(x => x.ProjectWeightPercent).HasPrecision(9, 4); e.Property(x => x.StudentWeightPercent).HasPrecision(9, 4);
        });
        b.Entity<StudentResult>(e =>
        {
            e.ToTable("student_results"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.StudentId }).IsUnique();
            e.Property(x => x.TotalScore).HasPrecision(5, 2); e.Property(x => x.PassThreshold).HasPrecision(5, 2);
            e.Property(x => x.Outcome).HasMaxLength(20); e.Property(x => x.CalculationRule).HasMaxLength(100);
        });
        b.Entity<StudentResultEvaluation>(e =>
        {
            e.ToTable("student_result_evaluations"); e.HasKey(x => new { x.ResultId, x.EvaluationId });
            e.HasOne<StudentResult>().WithMany().HasForeignKey(x => x.ResultId).OnDelete(DeleteBehavior.NoAction);
        });
        // These database-first extension entities use the same snake_case convention as their SQL script.
        var types = new[] { typeof(PeriodPolicyVersion), typeof(PeriodPolicyUsage), typeof(EvaluationScheme),
            typeof(EvaluationSchemeComponent), typeof(StudentResult), typeof(StudentResultEvaluation) };
        foreach (var type in types)
            foreach (var property in b.Entity(type).Metadata.GetProperties())
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
        b.Entity<EvaluationAssignment>(e =>
        {
            e.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(20);
            e.Property(x => x.MajorId).HasColumnName("major_id"); e.Property(x => x.StudentId).HasColumnName("student_id");
            e.Property(x => x.ComponentId).HasColumnName("component_id"); e.Property(x => x.PolicyVersionId).HasColumnName("policy_version_id");
            e.Property(x => x.ScopeSnapshotJson).HasColumnName("scope_snapshot_json");
        });
    }
}
