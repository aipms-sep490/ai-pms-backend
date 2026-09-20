namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record RequestTeamLeaderChange(string? Message);
public sealed record RespondToTeamLeaderChange(string? Message);

public sealed record TeamLeaderChangeRequestDto(long Id, long TeamId, long ProjectId,
    long RequestedBy, long CurrentLeaderUserId, long NewLeaderUserId,
    long MentorProfileId, long MentorUserId, string MentorName, string Status,
    string? RequestMessage, string? ResponseMessage, DateTime RequestedAt,
    DateTime? RespondedAt);
