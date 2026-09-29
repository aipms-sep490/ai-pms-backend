using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<ProjectMajorRequirement> ProjectMajorRequirements => Set<ProjectMajorRequirement>();

    private static void ConfigureProjectRequirements(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectMajorRequirement>(e =>
        {
            e.ToTable("project_major_requirements", t =>
            {
                t.HasCheckConstraint("ck_project_major_requirements_bounds", "min_members >= 1 AND max_members >= min_members");
                t.HasCheckConstraint("ck_project_major_requirements_responsibility", "LEN(LTRIM(RTRIM(responsibility))) > 0");
            });
            e.HasKey(x => x.Id).HasName("pk_project_major_requirements");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.MinMembers).HasColumnName("min_members");
            e.Property(x => x.MaxMembers).HasColumnName("max_members");
            e.Property(x => x.Responsibility).HasColumnName("responsibility").HasMaxLength(2000);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").HasDefaultValueSql("(newid())").IsConcurrencyToken();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(7).HasDefaultValueSql("(sysutcdatetime())");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(7).HasDefaultValueSql("(sysutcdatetime())");
            e.HasIndex(x => new { x.ProjectId, x.MajorId }).IsUnique().HasDatabaseName("uq_project_major_requirements");
            e.HasIndex(x => x.MajorId).HasDatabaseName("ix_project_major_requirements_major");
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_project_major_requirements_project");
            e.HasOne<Major>().WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_project_major_requirements_major");
        });
    }
}
