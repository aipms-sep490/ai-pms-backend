using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.DTOs;

internal static class TeamDtoMapper
{
    public static TeamInvitationCandidateDto ToDto(this TeamInvitationCandidate candidate) =>
        new(candidate.UserId, candidate.FullName, candidate.Email, candidate.StudentCode,
            candidate.MajorId, candidate.MajorCode, candidate.MajorName,
            candidate.PendingInvitationId.HasValue ? "PENDING" : "NONE", candidate.PendingInvitationId,
            candidate.PendingInvitationExpiresAt.HasValue
                ? DateTime.SpecifyKind(candidate.PendingInvitationExpiresAt.Value, DateTimeKind.Utc) : null,
            !candidate.PendingInvitationId.HasValue);

    public static TeamMemberDto ToDto(this TeamParticipant member) =>
        new(member.UserId, member.FullName, member.MajorId, member.OrganizationId,
            member.IsEligibleStudent, member.IsLeader);

    public static TeamInvitationDto ToDto(this TeamInvitationData invitation) =>
        new(invitation.Id, invitation.TeamId, invitation.InvitedUserId,
            invitation.InvitedBy, invitation.Status, invitation.Message,
            invitation.ExpiresAt, invitation.RespondedAt, invitation.CreatedAt);
}
