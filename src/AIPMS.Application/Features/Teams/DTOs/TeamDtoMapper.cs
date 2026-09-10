using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.DTOs;

internal static class TeamDtoMapper
{
    public static TeamMemberDto ToDto(this TeamParticipant member) =>
        new(member.UserId, member.FullName, member.MajorId, member.OrganizationId,
            member.IsEligibleStudent, member.IsLeader);

    public static TeamInvitationDto ToDto(this TeamInvitationData invitation) =>
        new(invitation.Id, invitation.TeamId, invitation.InvitedUserId,
            invitation.InvitedBy, invitation.Status, invitation.Message,
            invitation.ExpiresAt, invitation.RespondedAt, invitation.CreatedAt);
}
