using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Services;

public sealed class TeamEligibilityEvaluationService(
    ITeamEligibilityRepository eligibilityRepository,
    ITeamEligibilityHasher hasher,
    ITeamEligibilityFreshnessEvaluator freshnessEvaluator,
    ITeamRepository teamRepository,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
{
    public async Task<(TeamEligibilitySnapshotData Snapshot, bool WasInserted, string TeamStatus)> EvaluateAndPersistAsync(
        long teamId,
        long actorUserId,
        string triggerSource,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var team = await teamRepository.GetAsync(teamId, cancellationToken)
            ?? throw new NotFoundException("Team", teamId);

        if (team.Status is not ("FORMING" or "ELIGIBLE" or "LOCKED"))
        {
            throw new ConflictException($"Eligibility check is not allowed for team in status {team.Status}.");
        }

        var contextInput = await eligibilityRepository.BuildContextInputAsync(teamId, utcNow, cancellationToken);
        var hashes = hasher.ComputeHashes(contextInput, utcNow);

        var issues = GenerateIssues(contextInput, utcNow);
        var result = issues.Count == 0 ? "PASS" : "FAIL";

        var saveModel = new TeamEligibilitySaveModel(
            TeamId: contextInput.TeamId,
            ProjectPeriodId: contextInput.ProjectPeriodId,
            ProjectId: contextInput.ProjectId,
            RoundType: contextInput.RoundType,
            RevisionHistoryId: contextInput.RevisionHistoryId,
            ProjectMode: contextInput.ProjectMode,
            PolicyVersion: contextInput.PolicyVersion,
            RuleVersion: contextInput.RuleVersion,
            Hashes: hashes,
            Result: result,
            CheckedBy: actorUserId,
            CheckedAt: utcNow,
            TriggerSource: triggerSource,
            Issues: issues);

        var (snapshot, wasInserted) = await eligibilityRepository.SaveCheckAsync(saveModel, cancellationToken);

        if (wasInserted)
        {
            await auditTrail.RecordAsync(
                new AuditEntry(
                    actorUserId,
                    "TEAM_ELIGIBILITY_CHECKED",
                    "TEAM_ELIGIBILITY_CHECK",
                    snapshot.Id,
                    new Dictionary<string, object?>
                    {
                        ["teamId"] = team.Id,
                        ["roundType"] = snapshot.RoundType,
                        ["result"] = snapshot.Result,
                        ["triggerSource"] = triggerSource
                    }),
                cancellationToken);
        }

        // Apply status transition matrix:
        // FORMING + PASS => ELIGIBLE
        // FORMING + FAIL => FORMING
        // ELIGIBLE + PASS => ELIGIBLE
        // ELIGIBLE + FAIL => FORMING
        // LOCKED + PASS => LOCKED
        // LOCKED + FAIL => LOCKED
        var newTeamStatus = (team.Status, result) switch
        {
            ("FORMING", "PASS") => "ELIGIBLE",
            ("FORMING", "FAIL") => "FORMING",
            ("ELIGIBLE", "PASS") => "ELIGIBLE",
            ("ELIGIBLE", "FAIL") => "FORMING",
            ("LOCKED", _) => "LOCKED",
            _ => team.Status
        };

        if (newTeamStatus != team.Status)
        {
            await teamRepository.UpdateAsync(team.Id, team.Name, team.Description, newTeamStatus, utcNow, cancellationToken);
        }

        return (snapshot, wasInserted, newTeamStatus);
    }

    public async Task<TeamEligibilityCheckDto?> GetCurrentEligibilityAsync(
        long teamId,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var contextInput = await eligibilityRepository.BuildContextInputAsync(teamId, utcNow, cancellationToken);
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

        // 1. Resolve current qualifying snapshot matching current evaluation key and context
        var snapshot = await eligibilityRepository.GetCurrentSnapshotAsync(
            teamId,
            contextInput.ProjectPeriodId,
            contextInput.ProjectId,
            contextInput.RoundType,
            contextInput.RevisionHistoryId,
            hashes.EvaluationKey,
            cancellationToken);

        // 2. Fall back to latest check for this round (which will evaluate to STALE)
        snapshot ??= await eligibilityRepository.GetLatestCheckAsync(
            teamId,
            contextInput.RoundType,
            contextInput.RevisionHistoryId,
            cancellationToken);

        if (snapshot is null) return null;

        var freshness = freshnessEvaluator.EvaluateFreshness(snapshot, evaluationContext, utcNow);
        return MapToDto(snapshot, freshness);
    }

    public async Task<IReadOnlyList<TeamEligibilityCheckDto>> GetEligibilityHistoryAsync(
        long teamId,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        // 1. Load immutable stored snapshots first
        var history = await eligibilityRepository.GetHistoryAsync(teamId, cancellationToken);
        if (history.Count == 0) return Array.Empty<TeamEligibilityCheckDto>();

        // Cache evaluation contexts by ProjectPeriodId to avoid redundant DB reads
        var contextCache = new Dictionary<long, TeamEligibilityEvaluationContext?>();

        var result = new List<TeamEligibilityCheckDto>(history.Count);
        foreach (var s in history)
        {
            if (!contextCache.TryGetValue(s.ProjectPeriodId, out var evaluationContext))
            {
                var historicalInput = await eligibilityRepository.BuildHistoricalContextInputAsync(
                    teamId, s.ProjectPeriodId, utcNow, cancellationToken);

                if (historicalInput is not null)
                {
                    var hashes = hasher.ComputeHashes(historicalInput, utcNow);
                    evaluationContext = new TeamEligibilityEvaluationContext(
                        TeamId: historicalInput.TeamId,
                        ProjectPeriodId: historicalInput.ProjectPeriodId,
                        ProjectId: historicalInput.ProjectId,
                        RoundType: historicalInput.RoundType,
                        RevisionHistoryId: historicalInput.RevisionHistoryId,
                        ProjectMode: historicalInput.ProjectMode,
                        PolicyVersion: historicalInput.PolicyVersion,
                        RuleVersion: historicalInput.RuleVersion,
                        Hashes: hashes);
                }
                else
                {
                    evaluationContext = null;
                }

                contextCache[s.ProjectPeriodId] = evaluationContext;
            }

            var freshness = evaluationContext is not null
                ? freshnessEvaluator.EvaluateFreshness(s, evaluationContext, utcNow)
                : FreshnessStatus.Stale;

            result.Add(MapToDto(s, freshness));
        }

        return result;
    }

    public TeamEligibilityCheckDto MapToDto(TeamEligibilitySnapshotData snapshot, FreshnessStatus freshness)
    {
        return new TeamEligibilityCheckDto(
            CheckId: snapshot.Id,
            TeamId: snapshot.TeamId,
            ProjectId: snapshot.ProjectId,
            ProjectPeriodId: snapshot.ProjectPeriodId,
            RoundType: snapshot.RoundType,
            RevisionHistoryId: snapshot.RevisionHistoryId,
            ProjectMode: snapshot.ProjectMode,
            PolicyVersion: snapshot.PolicyVersion,
            RuleVersion: snapshot.RuleVersion,
            Result: snapshot.Result,
            Freshness: freshness.ToString().ToUpperInvariant(),
            CheckedAt: snapshot.CheckedAt,
            ValidUntilAt: snapshot.ValidUntilAt,
            CheckedBy: snapshot.CheckedBy,
            TriggerSource: snapshot.TriggerSource,
            Issues: snapshot.Issues);
    }

    private static List<TeamEligibilityIssueData> GenerateIssues(TeamEligibilityContextInput input, DateTime utcNow)
    {
        var issues = new List<TeamEligibilityIssueData>();
        var sortOrder = 1;

        if (!input.Policy.IsValid)
        {
            issues.Add(new TeamEligibilityIssueData(
                SortOrder: sortOrder++,
                RuleCode: "TEAM_POLICY_INVALID",
                Severity: "BLOCKER",
                MajorId: null,
                UserId: null,
                ExpectedValue: "Valid policy",
                ActualValue: "Invalid",
                Message: "The team formation policy is invalid or unconfigured."));
            return issues;
        }

        var organizationId = input.Members.FirstOrDefault(m => m.OrganizationId.HasValue)?.OrganizationId ?? 0;

        var participants = input.Members
            .Select(m => new TeamParticipant(
                m.UserId,
                m.FullName,
                m.MajorId,
                m.OrganizationId,
                m.IsEligibleStudent,
                m.IsLeader))
            .ToList();

        IReadOnlyList<string> errors;
        if (input.Scope is null)
        {
            errors = TeamRules.EligibilityErrors(participants, input.Policy, organizationId);
        }
        else
        {
            var academicScope = new TeamAcademicScope(
                input.Scope.ProjectMode,
                input.Scope.PrimaryMajorId,
                input.Scope.LeadDepartmentId,
                input.Scope.Requirements
                    .Select(r => new MajorRequirement(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility))
                    .ToList(),
                Guid.Empty);

            errors = HybridTeamRules.EligibilityErrors(participants, input.Policy, organizationId, academicScope);
        }

        foreach (var error in errors)
        {
            if (error.StartsWith("MAJOR_MIN_MEMBERS:", StringComparison.Ordinal))
            {
                var majorIdStr = error["MAJOR_MIN_MEMBERS:".Length..];
                long.TryParse(majorIdStr, out var majorId);
                var req = input.Scope?.Requirements.FirstOrDefault(r => r.MajorId == majorId);
                var currentCount = participants.Count(m => m.IsEligibleStudent && m.MajorId == majorId && m.OrganizationId == organizationId);

                issues.Add(new TeamEligibilityIssueData(
                    SortOrder: sortOrder++,
                    RuleCode: "MAJOR_MIN_MEMBERS",
                    Severity: "ERROR",
                    MajorId: majorId > 0 ? majorId : null,
                    UserId: null,
                    ExpectedValue: req?.MinMembers.ToString(),
                    ActualValue: currentCount.ToString(),
                    Message: $"Major quota minimum not reached for major {majorIdStr}."));
            }
            else if (error.StartsWith("MAJOR_MAX_MEMBERS:", StringComparison.Ordinal))
            {
                var majorIdStr = error["MAJOR_MAX_MEMBERS:".Length..];
                long.TryParse(majorIdStr, out var majorId);
                var req = input.Scope?.Requirements.FirstOrDefault(r => r.MajorId == majorId);
                var currentCount = participants.Count(m => m.IsEligibleStudent && m.MajorId == majorId && m.OrganizationId == organizationId);

                issues.Add(new TeamEligibilityIssueData(
                    SortOrder: sortOrder++,
                    RuleCode: "MAJOR_MAX_MEMBERS",
                    Severity: "ERROR",
                    MajorId: majorId > 0 ? majorId : null,
                    UserId: null,
                    ExpectedValue: req?.MaxMembers.ToString(),
                    ActualValue: currentCount.ToString(),
                    Message: $"Major quota maximum exceeded for major {majorIdStr}."));
            }
            else
            {
                issues.Add(new TeamEligibilityIssueData(
                    SortOrder: sortOrder++,
                    RuleCode: error,
                    Severity: "ERROR",
                    MajorId: null,
                    UserId: null,
                    ExpectedValue: null,
                    ActualValue: null,
                    Message: GetErrorMessage(error)));
            }
        }

        if (input.Policy.RequireStudentQualification)
        {
            foreach (var member in input.Members)
            {
                if (!member.QualificationEligible)
                {
                    issues.Add(new TeamEligibilityIssueData(
                        SortOrder: sortOrder++,
                        RuleCode: member.QualificationIssueCode ?? "QUALIFICATION_REQUIRED",
                        Severity: "ERROR",
                        MajorId: member.MajorId,
                        UserId: member.UserId,
                        ExpectedValue: "ELIGIBLE",
                        ActualValue: "INELIGIBLE",
                        Message: $"Student {member.FullName} does not meet the qualification requirements."));
                }
                else if (input.Policy.CheckQualificationExpiration
                    && member.QualificationValidUntilAt.HasValue
                    && utcNow >= member.QualificationValidUntilAt.Value)
                {
                    issues.Add(new TeamEligibilityIssueData(
                        SortOrder: sortOrder++,
                        RuleCode: "CERTIFICATE_EXPIRED",
                        Severity: "ERROR",
                        MajorId: member.MajorId,
                        UserId: member.UserId,
                        ExpectedValue: "> " + utcNow.ToString("o"),
                        ActualValue: member.QualificationValidUntilAt.Value.ToString("o"),
                        Message: $"Student {member.FullName}'s qualification certificate has expired."));
                }
            }
        }

        return issues
            .OrderBy(i => i.RuleCode, StringComparer.Ordinal)
            .ThenBy(i => i.MajorId ?? 0)
            .ThenBy(i => i.UserId ?? 0)
            .ThenBy(i => i.Message, StringComparer.Ordinal)
            .Select((issue, idx) => issue with { SortOrder = idx + 1 })
            .ToList();
    }

    private static string GetErrorMessage(string code) => code switch
    {
        "TOO_FEW_MEMBERS" => "The team has fewer members than the required minimum.",
        "TOO_MANY_MEMBERS" => "The team exceeds the maximum allowed number of members.",
        "EXACTLY_ONE_LEADER_REQUIRED" => "The team must have exactly one leader.",
        "INELIGIBLE_MEMBER" => "One or more members are not eligible students in the organization.",
        "TEAM_MUST_BE_SINGLE_MAJOR" => "All team members must belong to the same major.",
        "MEMBER_MAJOR_NOT_ALLOWED" => "One or more members belong to a major not permitted by the academic scope.",
        "UNSUPPORTED_HYBRID_POLICY" => "The team structure does not support the configured hybrid policy.",
        _ => code
    };
}
