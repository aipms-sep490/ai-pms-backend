using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<MilestoneDependency> MilestoneDependencies { get; set; } = null!;

    private static void ConfigureMilestoneDependencies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MilestoneDependency>(entity =>
        {
            entity.ToTable("milestone_dependencies");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MilestoneId).HasColumnName("milestone_id");
            entity.Property(e => e.DependsOnMilestoneId).HasColumnName("depends_on_milestone_id");
            entity.Property(e => e.DependencyType)
                .HasMaxLength(30)
                .HasDefaultValue("FINISH_TO_START")
                .HasColumnName("dependency_type");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");

            entity.HasIndex(e => new { e.MilestoneId, e.DependsOnMilestoneId }, "uq_milestone_dependencies_pair")
                .IsUnique();

            entity.HasOne(d => d.Milestone)
                .WithMany(p => p.MilestoneDependencies)
                .HasForeignKey(d => d.MilestoneId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_milestone_dependencies_milestone");

            entity.HasOne(d => d.DependsOnMilestone)
                .WithMany(p => p.MilestoneDependencyDependsOnMilestones)
                .HasForeignKey(d => d.DependsOnMilestoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_milestone_dependencies_depends_on");
        });
    }
}
