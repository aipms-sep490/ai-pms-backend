namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class AcademicKnowledgeDocument
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string DocumentType { get; set; } = "GENERAL";
    public long? DepartmentId { get; set; }
    public long? AcademicSemesterId { get; set; }
    public long? ProjectPeriodId { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string CurrentVersion { get; set; } = "1.0";
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<AcademicKnowledgeDocumentVersion> Versions { get; set; } = new List<AcademicKnowledgeDocumentVersion>();
}

public sealed class AcademicKnowledgeDocumentVersion
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public string Version { get; set; } = "1.0";
    public string Status { get; set; } = "DRAFT";
    public string Content { get; set; } = "";
    public string? SourceFileName { get; set; }
    public string? SourceChecksum { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public long CreatedBy { get; set; }
    public long? PublishedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public AcademicKnowledgeDocument Document { get; set; } = null!;
    public ICollection<AcademicKnowledgeChunk> Chunks { get; set; } = new List<AcademicKnowledgeChunk>();
}

public sealed class AcademicKnowledgeChunk
{
    public long Id { get; set; }
    public long DocumentVersionId { get; set; }
    public int Ordinal { get; set; }
    public string? Heading { get; set; }
    public int? PageNumber { get; set; }
    public string Content { get; set; } = "";
    public string SearchText { get; set; } = "";
    public AcademicKnowledgeDocumentVersion DocumentVersion { get; set; } = null!;
}

public sealed class AcademicKnowledgeIndexJob
{
    public long Id { get; set; }
    public long DocumentVersionId { get; set; }
    public string Status { get; set; } = "PENDING";
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class AcademicKnowledgeQueryRun
{
    public long Id { get; set; }
    public long ActorId { get; set; }
    public long? ProjectId { get; set; }
    public string Scope { get; set; } = "ACADEMIC";
    public string QueryHash { get; set; } = "";
    public string Status { get; set; } = "COMPLETED";
    public string ModelVersion { get; set; } = "";
    public string EvidenceJson { get; set; } = "[]";
    public string ResultJson { get; set; } = "{}";
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
}
