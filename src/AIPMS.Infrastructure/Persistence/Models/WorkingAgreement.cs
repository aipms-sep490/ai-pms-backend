namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class WorkingAgreement
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string ContentJson { get; set; } = "{}";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public ICollection<AgreementMember> Members { get; set; } = new List<AgreementMember>();
}
public sealed class AgreementMember
{
    public long AgreementId { get; set; }
    public long UserId { get; set; }
    public long TeamMemberId { get; set; }
    public DateTime? AcceptedAt { get; set; }
}
