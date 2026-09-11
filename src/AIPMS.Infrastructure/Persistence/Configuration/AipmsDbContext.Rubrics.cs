using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RubricVersion>(entity =>
        {
            entity.ToTable("rubric_versions", table =>
            {
                table.HasCheckConstraint("ck_rubric_versions_status", "[status] IN ('DRAFT','PUBLISHED','RETIRED')");
                table.HasCheckConstraint("ck_rubric_versions_number", "[version_number] > 0");
            });
            entity.HasKey(x => x.RubricId).HasName("pk_rubric_versions");
            entity.Property(x => x.RubricId).HasColumnName("rubric_id").ValueGeneratedNever();
            entity.Property(x => x.RootRubricId).HasColumnName("root_rubric_id");
            entity.Property(x => x.VersionNumber).HasColumnName("version_number");
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            entity.HasIndex(x => new { x.RootRubricId, x.VersionNumber }).IsUnique().HasDatabaseName("uq_rubric_versions_family_number");
            entity.HasOne<Rubric>().WithMany().HasForeignKey(x => x.RubricId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Rubric>().WithMany().HasForeignKey(x => x.RootRubricId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
