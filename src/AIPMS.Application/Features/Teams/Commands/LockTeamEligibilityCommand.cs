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
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var actorId = currentUser.UserId.Value;
        var team = await repository.GetAsync(request.TeamId, ct)
            ?? throw new NotFoundException("Team", request.TeamId);

        var leader = team.Members.SingleOrDefault(m => m.IsLeader);
        if (leader is null || leader.UserId != actorId)
            throw new ForbiddenException("Only the team leader can lock the team.");

        // Idempotent retry if already locked
        if (team.Status == "LOCKED")
        {
            return await workflow.GetAsync(team.Id, ct);
        }

        if (team.Status != "ELIGIBLE")
        {
            throw new ConflictException($"Team in status {team.Status} cannot be locked. Only ELIGIBLE teams can be locked.");
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var contextInput = await eligibilityRepository.BuildContextInputAsync(team.Id, utcNow, ct);

        var snapshot = await eligibilityRepository.GetLatestCheckAsync(
            team.Id, contextInput.RoundType, contextInput.RevisionHistoryId, ct);

        if (snapshot is null)
        {
            throw new ConflictException("An explicit eligibility check must be run before locking the team.");
        }

        var hashes = hasher.ComputeHashes(contextInput, utcNow);
        var evalContext = new TeamEligibilityEvaluationContext(
            TeamId: contextInput.TeamId,
            ProjectPeriodId: contextInput.ProjectPeriodId,
            ProjectId: contextInput.ProjectId,
            RoundType: contextInput.RoundType,
            RevisionHistoryId: contextInput.RevisionHistoryId,
            ProjectMode: contextInput.ProjectMode,
            PolicyVersion: contextInput.PolicyVersion,
            RuleVersion: contextInput.RuleVersion,
            Hashes: hashes);

        var freshness = freshnessEvaluator.EvaluateFreshness(snapshot, evalContext, utcNow);

        if (freshness != FreshnessStatus.Current || snapshot.Result != "PASS")
        {
            throw new ConflictException($"Team cannot be locked. Requires a CURRENT PASS snapshot. Current snapshot is {freshness.ToString().ToUpperInvariant()} {snapshot.Result}.");
        }

        await repository.UpdateAsync(team.Id, team.Name, team.Description, "LOCKED", utcNow, ct);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorId,
                "TEAM_ELIGIBILITY_LOCKED",
                "TEAM",
                team.Id,
                new Dictionary<string, object?>
                {
                    ["checkId"] = snapshot.Id,
                    ["roundType"] = snapshot.RoundType
                }),
            ct);

        return await workflow.GetAsync(team.Id, ct);
    }
}
