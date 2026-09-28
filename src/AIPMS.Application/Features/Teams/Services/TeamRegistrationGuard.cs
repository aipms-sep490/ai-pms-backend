using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Services;

public sealed class TeamRegistrationGuard(
    ITeamRepository repository,
    ITeamEligibilityRepository? eligibilityRepository,
    ITeamEligibilityHasher? hasher,
    ITeamEligibilityFreshnessEvaluator? freshnessEvaluator,
    TeamWorkflow workflow,
    TimeProvider? timeProvider = null)
    : ITeamRegistrationGuard
{
    public TeamRegistrationGuard(ITeamRepository repository, TeamWorkflow workflow)
        : this(repository, null, null, null, workflow, null)
    {
    }

    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
        repository.InTransactionAsync(action, cancellationToken);

    public Task ValidateAsync(long teamId, CancellationToken cancellationToken) =>
        workflow.ValidateRegistrationAsync(teamId, cancellationToken);

    public async Task ValidateSubmissionEligibilityAsync(
        long teamId,
        long projectId,
        string roundType,
        long? revisionHistoryId,
        CancellationToken cancellationToken)
    {
        if (eligibilityRepository is null || hasher is null || freshnessEvaluator is null)
        {
            await ValidateAsync(teamId, cancellationToken);
            return;
        }

        // Lock authoritative team aggregate input (respecting existing UPDLOCK, HOLDLOCK convention)
        var teamSnapshot = await repository.GetAsync(teamId, cancellationToken)
            ?? throw new NotFoundException("Team", teamId);

        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        // 1. Load latest explicit snapshot for current round
        var snapshot = await eligibilityRepository.GetLatestCheckAsync(
            teamId, roundType, revisionHistoryId, cancellationToken);

        if (snapshot is null)
        {
            var reasons = new List<string>();
            try
            {
                var input = await eligibilityRepository.BuildContextInputAsync(teamId, utcNow, cancellationToken);
                var participants = input.Members
                    .Select(m => new TeamParticipant(m.UserId, m.FullName, m.MajorId, m.OrganizationId, m.IsEligibleStudent, m.IsLeader))
                    .ToList();
                var orgId = input.Members.FirstOrDefault(m => m.OrganizationId.HasValue)?.OrganizationId ?? 0;
                if (input.Scope is null)
                    reasons.AddRange(Domain.Teams.TeamRules.EligibilityErrors(participants, input.Policy, orgId));
                else
                {
                    var academicScope = new TeamAcademicScope(
                        input.Scope.ProjectMode,
                        input.Scope.PrimaryMajorId,
                        input.Scope.LeadDepartmentId,
                        input.Scope.Requirements.Select(r => new MajorRequirement(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility)).ToList(),
                        Guid.Empty);
                    reasons.AddRange(Domain.Teams.HybridTeamRules.EligibilityErrors(participants, input.Policy, orgId, academicScope));
                }
            }
            catch
            {
                // Ignore any failure during diagnostic error collection
            }

            var extra = reasons.Count > 0 ? " (" + string.Join(", ", reasons.Distinct()) + ")" : "";
            throw new ConflictException($"An active eligibility check snapshot is required before project submission.{extra}");
        }

        // 2. Rebuild current eligibility context
        var contextInput = await eligibilityRepository.BuildContextInputAsync(teamId, utcNow, cancellationToken);

        // Aggregate consistency check
        if (contextInput.ProjectId != projectId
            || !string.Equals(contextInput.RoundType, roundType, StringComparison.Ordinal)
            || contextInput.RevisionHistoryId != revisionHistoryId)
        {
            throw new ConflictException("Project eligibility aggregate state mismatch.");
        }

        var hashes = hasher.ComputeHashes(contextInput, utcNow);
        var evaluationContext = new TeamEligibilityEvaluationContext(
            TeamId: contextInput.TeamId,
            ProjectPeriodId: contextInput.ProjectPeriodId,
            ProjectId: contextInput.ProjectId,
            RoundType: contextInput.RoundType,
            RevisionHistoryId: contextInput.RevisionHistoryId,
            ProjectMode: contextInput.ProjectMode,
            PolicyVersion: contextInput.PolicyVersion,
            RuleVersion: contextInput.RuleVersion,
            Hashes: hashes);

        // 3. Evaluate freshness
        var freshness = freshnessEvaluator.EvaluateFreshness(snapshot, evaluationContext, utcNow);

        // 4. Require CURRENT PASS
        if (freshness != FreshnessStatus.Current || snapshot.Result != "PASS")
        {
            var reasons = new System.Collections.Generic.List<string>();
            if (snapshot.Issues != null)
                reasons.AddRange(snapshot.Issues.Select(i => i.RuleCode));

            var participants = contextInput.Members
                .Select(m => new TeamParticipant(m.UserId, m.FullName, m.MajorId, m.OrganizationId, m.IsEligibleStudent, m.IsLeader))
                .ToList();
            var orgId = contextInput.Members.FirstOrDefault(m => m.OrganizationId.HasValue)?.OrganizationId ?? 0;
            if (contextInput.Scope is null)
                reasons.AddRange(Domain.Teams.TeamRules.EligibilityErrors(participants, contextInput.Policy, orgId));
            else
            {
                var academicScope = new TeamAcademicScope(
                    contextInput.Scope.ProjectMode,
                    contextInput.Scope.PrimaryMajorId,
                    contextInput.Scope.LeadDepartmentId,
                    contextInput.Scope.Requirements.Select(r => new MajorRequirement(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility)).ToList(),
                    System.Guid.Empty);
                reasons.AddRange(Domain.Teams.HybridTeamRules.EligibilityErrors(participants, contextInput.Policy, orgId, academicScope));
            }

            var extra = reasons.Count > 0 ? " (" + string.Join(", ", reasons.Distinct()) + ")" : "";
            throw new ConflictException($"Project submission requires a CURRENT PASS eligibility snapshot. Current snapshot is {freshness.ToString().ToUpperInvariant()} {snapshot.Result}.{extra}");
        }
    }
}
