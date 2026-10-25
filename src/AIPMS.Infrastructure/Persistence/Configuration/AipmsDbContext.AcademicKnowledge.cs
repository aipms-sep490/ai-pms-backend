using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureAcademicKnowledge(ModelBuilder model)
    {
        model.Entity<AssistantConversation>(e =>
        {
            e.ToTable("assistant_conversations"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Scope).HasMaxLength(20).IsRequired();
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => new { x.ActorId, x.ProjectId, x.Id });
            e.HasOne<AIPMS.Infrastructure.Persistence.Generated.Models.User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AIPMS.Infrastructure.Persistence.Generated.Models.Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<AssistantConversationTurn>(e =>
        {
            e.ToTable("assistant_conversation_turns"); e.HasKey(x => x.Id);
            e.Property(x => x.Query).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.ConversationId, x.Revision }).IsUnique();
            e.HasOne<AssistantConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AcademicKnowledgeQueryRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<AcademicKnowledgeDocument>(e =>
        {
            e.ToTable("academic_knowledge_documents"); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(255).IsRequired();
            e.Property(x => x.DocumentType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.CurrentVersion).HasMaxLength(64);
            e.HasIndex(x => new { x.Status, x.DepartmentId, x.AcademicSemesterId, x.ProjectPeriodId });
            e.HasMany(x => x.Versions).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AcademicKnowledgeDocumentVersion>(e =>
        {
            e.ToTable("academic_knowledge_document_versions"); e.HasKey(x => x.Id);
            e.Property(x => x.Version).HasMaxLength(64).IsRequired(); e.Property(x => x.Content).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); e.Property(x => x.SourceFileName).HasMaxLength(255);
            e.Property(x => x.SourceChecksum).HasMaxLength(128).IsUnicode(false);
            e.HasIndex(x => new { x.DocumentId, x.Version }).IsUnique();
            e.HasMany(x => x.Chunks).WithOne(x => x.DocumentVersion).HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AcademicKnowledgeChunk>(e =>
        {
            e.ToTable("academic_knowledge_chunks"); e.HasKey(x => x.Id);
            e.Property(x => x.Heading).HasMaxLength(500); e.Property(x => x.Content).IsRequired(); e.Property(x => x.SearchText).IsRequired();
            e.HasIndex(x => new { x.DocumentVersionId, x.Ordinal }).IsUnique();
        });
        model.Entity<AcademicKnowledgeIndexJob>(e =>
        {
            e.ToTable("academic_knowledge_index_jobs"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); e.Property(x => x.Error).HasMaxLength(2000);
            e.HasOne<AcademicKnowledgeDocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AcademicKnowledgePublication>(e =>
        {
            e.ToTable("academic_knowledge_publications"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(20).IsUnicode(false);
            e.HasOne<AcademicKnowledgeDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AcademicKnowledgeDocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AIPMS.Infrastructure.Persistence.Generated.Models.User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        });
        // Database-first SQL uses snake_case; restrict this convention to these entities.
        var knowledgeTypes = new[] { typeof(AssistantConversation), typeof(AssistantConversationTurn), typeof(AcademicKnowledgeDocument), typeof(AcademicKnowledgeDocumentVersion),
            typeof(AcademicKnowledgeChunk), typeof(AcademicKnowledgeIndexJob), typeof(AcademicKnowledgeQueryRun), typeof(AcademicKnowledgePublication) };
        foreach (var type in knowledgeTypes)
        foreach (var property in model.Entity(type).Metadata.GetProperties())
        {
            property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
            if (property.ClrType == typeof(string) && property.Name is "Status" or "Scope" or "QueryHash" or "DocumentType")
                property.SetIsUnicode(false);
        }
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
