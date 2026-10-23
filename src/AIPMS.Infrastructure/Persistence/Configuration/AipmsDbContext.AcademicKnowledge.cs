using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureAcademicKnowledge(ModelBuilder model)
    {
        model.Entity<AcademicKnowledgeDocument>(e =>
        {
            e.ToTable("academic_knowledge_documents"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(255).IsRequired();
            e.Property(x => x.DocumentType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.Status, x.DepartmentId, x.AcademicSemesterId });
            e.HasMany(x => x.Versions).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AcademicKnowledgeDocumentVersion>(e =>
        {
            e.ToTable("academic_knowledge_document_versions"); e.HasKey(x => x.Id);
            e.Property(x => x.Version).HasMaxLength(64).IsRequired(); e.Property(x => x.Content).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); e.Property(x => x.SourceFileName).HasMaxLength(255);
            e.HasIndex(x => new { x.DocumentId, x.Version }).IsUnique();
            e.HasMany(x => x.Chunks).WithOne(x => x.DocumentVersion).HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AcademicKnowledgeChunk>(e =>
        {
            e.ToTable("academic_knowledge_chunks"); e.HasKey(x => x.Id);
            e.Property(x => x.Heading).HasMaxLength(500); e.Property(x => x.Content).IsRequired(); e.Property(x => x.SearchText).IsRequired();
            e.HasIndex(x => new { x.DocumentVersionId, x.Ordinal }).IsUnique();
            e.HasIndex(x => x.SearchText);
        });
        model.Entity<AcademicKnowledgeIndexJob>(e =>
        {
            e.ToTable("academic_knowledge_index_jobs"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => new { x.DocumentVersionId, x.CreatedAt });
        });
        model.Entity<AcademicKnowledgeQueryRun>(e =>
        {
            e.ToTable("academic_knowledge_query_runs"); e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasMaxLength(20).IsRequired(); e.Property(x => x.QueryHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(24).IsRequired(); e.Property(x => x.ModelVersion).HasMaxLength(128).IsRequired();
            e.Property(x => x.EvidenceJson).IsRequired(); e.Property(x => x.ResultJson).IsRequired();
            e.HasIndex(x => new { x.ActorId, x.Id });
            e.HasOne<AIPMS.Infrastructure.Persistence.Generated.Models.User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AIPMS.Infrastructure.Persistence.Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
