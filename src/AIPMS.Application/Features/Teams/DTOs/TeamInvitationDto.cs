namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record TeamInvitationDto(long Id, long TeamId, long InvitedUserId,
    long InvitedBy, string Status, string? Message, DateTime? ExpiresAt,
    DateTime? RespondedAt, DateTime CreatedAt);
