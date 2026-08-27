namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Rubric
{
    public int VersionNumber { get; set; }
    public string ApprovalStatus { get; set; } = null!;
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public byte[] RowVersion { get; set; } = null!;
    public virtual User? ApprovedByNavigation { get; set; }
}
