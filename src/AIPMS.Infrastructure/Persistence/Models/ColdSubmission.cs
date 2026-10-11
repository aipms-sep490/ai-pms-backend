namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ColdReportTemplate
{
    public long Id { get; set; }
    public long MajorId { get; set; }
    public long ProjectPeriodId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string ReportsJson { get; set; } = "[]";
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class ColdPackage
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long ProjectPeriodId { get; set; }
    public DateTime Deadline { get; set; }
    public string Status { get; set; } = "OPEN";
    public string RegistrationSnapshotJson { get; set; } = "{}";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? LockedBy { get; set; }
    public DateTime? LockedAt { get; set; }
}
public sealed class ColdChecklistItem
{
    public long Id { get; set; }
    public long PackageId { get; set; }
    public long TemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public string ReportType { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Required { get; set; }
    public string Scope { get; set; } = "";
    public long MajorId { get; set; }
    public long? StudentId { get; set; }
}
public sealed class ColdSubmission
{
    public long Id { get; set; }
    public long ChecklistItemId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string? Note { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? LockedAt { get; set; }
    public ICollection<ColdSubmissionFile> Files { get; set; } = new List<ColdSubmissionFile>();
}
public sealed class ColdSubmissionFile
{
    public long SubmissionId { get; set; }
    public long FileId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
}
