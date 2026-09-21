namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class AcademicProfileVerification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Status { get; set; } = null!;
    public long? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}
