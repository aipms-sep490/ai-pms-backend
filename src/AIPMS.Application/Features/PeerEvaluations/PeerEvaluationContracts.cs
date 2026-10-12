namespace AIPMS.Application.Features.PeerEvaluations;

public sealed class PeerEvaluationSettings { public bool Enabled { get; set; } }
public sealed record CreatePeerRoundRequest(string Title, DateTimeOffset OpensAt, DateTimeOffset ClosesAt);
public sealed record PeerTokenRequest(string ConcurrencyToken);
public sealed record PeerRatingInput(long TargetUserId, int Rating, string? Comment);
public sealed record SavePeerEvaluationRequest(string? ConcurrencyToken, IReadOnlyList<PeerRatingInput> Items);
public sealed record PeerRosterMember(long UserId, long MajorId, string FullName);
public sealed record PeerRoundDto(long Id, long ProjectId, string Title, string Status, DateTime OpensAt, DateTime ClosesAt,
    DateTime? ClosedAt, string ConcurrencyToken, int MemberCount, bool CanManage, bool CanSubmit,
    IReadOnlyList<string> Reasons, IReadOnlyList<PeerRosterMember> Roster);
public sealed record PeerEvaluationDto(long Id, long RoundId, long AuthorId, string Status, string ConcurrencyToken,
    DateTime UpdatedAt, DateTime? SubmittedAt, IReadOnlyList<PeerRatingInput> Items, bool AffectsOfficialResult = false);
public sealed record MyPeerEvaluationDto(long? RoundId, string Status, PeerEvaluationDto? Submission);
