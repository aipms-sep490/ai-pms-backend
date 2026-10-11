using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureWeeklyAssessments(ModelBuilder b)
    {
        b.Entity<WeeklyAssessment>(e => {
            e.ToTable("weekly_assessments"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.TimeZoneId).HasMaxLength(100).IsUnicode(false);
            e.Property(x => x.TotalScore).HasPrecision(5, 2);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.Property(x => x.Comments).HasMaxLength(2000); e.Property(x => x.RevisionReason).HasMaxLength(1000);
            e.HasMany(x => x.Scores).WithOne().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.Rubric>().WithMany().HasForeignKey(x => x.RubricId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ProjectId, x.StudentId, x.WeekNumber, x.Version }).IsUnique();
        });
        b.Entity<WeeklyAssessmentScore>(e => {
            e.ToTable("weekly_assessment_scores"); e.HasKey(x => new { x.AssessmentId, x.RubricCriterionId });
            e.Property(x => x.Score).HasPrecision(8, 2); e.Property(x => x.Comments).HasMaxLength(2000);
        });
        foreach (var type in new[] { typeof(WeeklyAssessment), typeof(WeeklyAssessmentScore) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)) p.SetPrecision(0);
            }
    }
}
