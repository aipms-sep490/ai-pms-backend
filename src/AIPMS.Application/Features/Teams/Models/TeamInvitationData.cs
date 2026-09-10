using AIPMS.Application.Common.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Models;

public sealed record TeamInvitationData(long Id, long TeamId, long InvitedUserId,
    long InvitedBy, string Status, string? Message, DateTime? ExpiresAt,
    DateTime? RespondedAt, DateTime CreatedAt);
