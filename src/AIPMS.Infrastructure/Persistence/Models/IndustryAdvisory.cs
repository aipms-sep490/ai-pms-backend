namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class IndustryExpert
{
    public long UserId { get; set; }
    public string Organization { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? EnrolledAt { get; set; }
}
public sealed class IndustryInvitation
{
    public long Id { get; set; }
    public long ExpertId { get; set; }
    public string Email { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string? ProtectedToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long NotificationId { get; set; }
}
public sealed class IndustryChallenge
{
    public Guid Id { get; set; }
    public string NonceHash { get; set; } = "";
    public string BrowserHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
public sealed class IndustryAssignment
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long ExpertId { get; set; }
    public string Scope { get; set; } = "";
    public long? MajorId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public long AssignedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
public sealed class IndustryEvidenceShare
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public long DeliverableVersionId { get; set; }
    public long FileId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public long SharedBy { get; set; }
    public DateTime SharedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
public sealed class IndustryFeedback
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public int Version { get; set; }
    public string Content { get; set; } = "";
    public long SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
}
