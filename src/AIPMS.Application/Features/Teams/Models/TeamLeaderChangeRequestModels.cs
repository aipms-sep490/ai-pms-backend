using AIPMS.Application.Features.Teams.DTOs;

namespace AIPMS.Application.Features.Teams.Models;

public sealed record TeamLeaderChangeRequestModel(
    long Id, long TeamId, long ProjectId, long RequestedBy, long CurrentLeaderUserId,
    long NewLeaderUserId, long MentorProfileId, long MentorUserId, string MentorName,
    string Status, string? RequestMessage, string? ResponseMessage,
    DateTime RequestedAt, DateTime? RespondedAt)
{
    public TeamLeaderChangeRequestDto ToDto() => new(Id, TeamId, ProjectId, RequestedBy,
        CurrentLeaderUserId, NewLeaderUserId, MentorProfileId, MentorUserId, MentorName,
        Status, RequestMessage, ResponseMessage, RequestedAt, RespondedAt);
}

public sealed record TeamLeaderChangeRequestSearch(long? TeamId, long? MentorUserId,
    string? Status, int Page, int PageSize);
