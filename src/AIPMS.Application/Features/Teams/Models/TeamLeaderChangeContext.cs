namespace AIPMS.Application.Features.Teams.Models;

public sealed record TeamLeaderChangeContext(long TeamId, long? ProjectId,
    long CurrentLeaderUserId, long NewLeaderUserId, long? MentorProfileId,
    long? MentorUserId, string? MentorName);
