namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class PeerRound
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "OPEN";
    public DateTime OpensAt { get; set; }
    public DateTime ClosesAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public long CreatedBy { get; set; }
    public long PrimaryAssignmentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<PeerRoundMember> Members { get; set; } = [];
    public ICollection<PeerSubmission> Submissions { get; set; } = [];
}
public sealed class PeerRoundMember
{
    public long RoundId { get; set; }
    public long UserId { get; set; }
    public long MajorId { get; set; }
    public long DepartmentId { get; set; }
    public long TeamMemberId { get; set; }
    public Guid MembershipGeneration { get; set; }
    public DateTime MembershipJoinedAt { get; set; }
    public string FullName { get; set; } = "";
}
public sealed class PeerSubmission
{
    public long Id { get; set; }
    public long RoundId { get; set; }
    public long AuthorId { get; set; }
    public string Status { get; set; } = "DRAFT";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime UpdatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public ICollection<PeerSubmissionItem> Items { get; set; } = [];
}
public sealed class PeerSubmissionItem
{
    public long SubmissionId { get; set; }
    public long TargetUserId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
}
