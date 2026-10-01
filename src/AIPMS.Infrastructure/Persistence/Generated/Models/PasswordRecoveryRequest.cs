namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class PasswordRecoveryRequest
{
    public long Id { get; set; }
    public string EmailHash { get; set; } = "";
    public string? ProtectedPayload { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public long? ResetTokenId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
}
