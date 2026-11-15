namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class DepartmentDocumentTemplate
{
    public long Id { get; set; }
    public long DepartmentId { get; set; }
    public long MajorId { get; set; }
    public long? ProjectPeriodId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Version { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}
