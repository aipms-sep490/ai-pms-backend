using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureSupervisorRecommendations(ModelBuilder b)
    {
        b.Entity<SupervisorRecommendation>(e => {
            e.ToTable("supervisor_recommendation_runs"); e.HasKey(x => x.Id);
            e.Property(x => x.EngineVersion).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.AssignmentType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Search).HasMaxLength(255);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Generated.Models.Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<SupervisorRecommendationEntry>(e => {
            e.ToTable("supervisor_recommendation_items"); e.HasKey(x => new { x.RunId, x.Position });
            e.HasIndex(x => new { x.RunId, x.SupervisorProfileId }).IsUnique();
            e.HasOne<Generated.Models.SupervisorProfile>().WithMany().HasForeignKey(x => x.SupervisorProfileId).OnDelete(DeleteBehavior.NoAction);
        });
        foreach (var type in new[] { typeof(SupervisorRecommendation), typeof(SupervisorRecommendationEntry) })
            foreach (var p in b.Entity(type).Metadata.GetProperties())
            {
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
                if (p.ClrType == typeof(DateTime)) p.SetPrecision(0);
            }
        b.Entity<Generated.Models.SupervisorRequest>()
            .Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasPrecision(0);
    }
}
