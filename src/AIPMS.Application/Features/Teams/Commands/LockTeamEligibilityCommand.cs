using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record LockTeamEligibilityCommand(long TeamId) : IRequest<TeamDto>;

public sealed class LockTeamEligibilityCommandHandler(
    ITeamRepository repository,
    ITeamEligibilityRepository eligibilityRepository,
    ITeamEligibilityHasher hasher,
    ITeamEligibilityFreshnessEvaluator freshnessEvaluator,
    TeamWorkflow workflow,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<LockTeamEligibilityCommand, TeamDto>
{
    public async Task<TeamDto> Handle(LockTeamEligibilityCommand request, CancellationToken cancellationToken)
    {
        try
        {
            return await repository.InTransactionAsync(
                token => HandleInTransactionAsync(request, token), cancellationToken);
        }
        catch (ConflictException ex)
        {
            if (currentUser.IsAuthenticated && currentUser.UserId.HasValue)
            {
                await auditTrail.RecordAsync(
                    new AuditEntry(
                        currentUser.UserId.Value,
                        "TEAM_ELIGIBILITY_LOCK_DENIED",
                        "TEAM",
                        request.TeamId,
                        new Dictionary<string, object?>
                        {
                            ["reason"] = ex.Message
                        },
                        Outcome: "DENIED"),
                    CancellationToken.None);
            }
            throw;
        }
    }

    private async Task<TeamDto> HandleInTransactionAsync(LockTeamEligibilityCommand request, CancellationToken ct)
    {
        _ = (eligibilityRepository, hasher, freshnessEvaluator, workflow, timeProvider);
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var actorId = currentUser.UserId.Value;
        var team = await repository.GetAsync(request.TeamId, ct)
            ?? throw new NotFoundException("Team", request.TeamId);

        var leader = team.Members.SingleOrDefault(m => m.IsLeader);
        if (leader is null || leader.UserId != actorId)
            throw new ForbiddenException("Only the team leader can lock the team.");

        // TK-W1-11 (Q-X-20): Students cannot manually lock a team before proposal submission;
        // teams lock automatically when submitting a proposal or when registration window N1 closes.
        throw new ConflictException(
            "Manual team locking by students is disabled. Teams lock automatically when submitting a project proposal.",
            WorkflowErrorCodes.TeamManualLockDisabled);
    }
}
