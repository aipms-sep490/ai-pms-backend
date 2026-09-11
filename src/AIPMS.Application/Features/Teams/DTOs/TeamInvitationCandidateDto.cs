namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record TeamInvitationCandidateDto(long UserId, string FullName, string Email,
    string? StudentCode, long MajorId, string MajorCode, string MajorName,
    string InvitationStatus, long? PendingInvitationId, DateTime? PendingInvitationExpiresAt,
    bool CanInvite);
