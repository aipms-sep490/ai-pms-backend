using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public DbSet<AiCopilotSnapshot> AiCopilotSnapshots => Set<AiCopilotSnapshot>();

    private static void ConfigureAiCopilotSnapshots(ModelBuilder builder)
    {
        builder.Entity<AiCopilotSnapshot>(e =>
        {
            e.ToTable("ai_copilot_snapshots", t =>
            {
                t.HasCheckConstraint("ck_ai_copilot_snapshots_status", "[status] IN ('COMPLETED','FALLBACK')");
                t.HasCheckConstraint("ck_ai_copilot_snapshots_scope",
                    "[scope_type] IN ('PROJECT','TEAM','EVALUATION','APPEAL','DEPARTMENT','SEMESTER','GATE_INSTANCE','TASK','SPRINT')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FeatureKey).HasColumnName("feature_key").HasMaxLength(48).IsUnicode(false);
            e.Property(x => x.ScopeType).HasColumnName("scope_type").HasMaxLength(24).IsUnicode(false);
            e.Property(x => x.ScopeId).HasColumnName("scope_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ModelVersion).HasColumnName("model_version").HasMaxLength(120);
            e.Property(x => x.Headline).HasColumnName("headline").HasMaxLength(400);
            e.Property(x => x.PayloadJson).HasColumnName("payload_json");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(7);

            e.HasIndex(x => new { x.FeatureKey, x.ScopeType, x.ScopeId, x.CreatedAt, x.Id })
                .HasDatabaseName("ix_ai_copilot_snapshots_lookup");
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt })
                .HasFilter("[project_id] IS NOT NULL")
                .HasDatabaseName("ix_ai_copilot_snapshots_project");

            e.HasOne<M.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<M.User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
