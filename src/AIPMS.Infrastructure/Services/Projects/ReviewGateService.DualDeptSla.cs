using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ReviewGateService
{
    public async Task<GateRoundConsensusDto> GetRoundScopeConsensus(long projectId, string gateCode, int? roundNumber, CancellationToken ct)
    {
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, true);
        await RequireProjectRead(projectId, scope, ct);
        var gate = await db.ProjectGateInstances.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, ct)
            ?? throw new NotFoundException("ProjectGate", 0);

        var latestRound = await db.Set<ReviewGatePackage>().AsNoTracking()
            .Where(p => p.GateInstanceId == gate.Id)
            .Select(p => (int?)p.RoundNumber)
            .MaxAsync(ct) ?? 1;
        var targetRound = roundNumber ?? latestRound;
        return await BuildRoundConsensusAsync(projectId, gate, scope, targetRound, ct);
    }

    public Task<GateRoundConsensusDto> RecordRoundScopeDecision(
        long projectId,
        string gateCode,
        RecordRoundScopeDecisionRequest input,
        CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true);
        await RequireProjectRead(projectId, scope, token);

        var gate = await db.ProjectGateInstances
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token)
            ?? throw new NotFoundException("ProjectGate", 0);

        var latestRound = await db.Set<ReviewGatePackage>().AsNoTracking()
            .Where(p => p.GateInstanceId == gate.Id)
            .Select(p => (int?)p.RoundNumber)
            .MaxAsync(token) ?? 1;

        if (input.RoundNumber != latestRound)
            throw new ConflictException(
                $"Stale review round {input.RoundNumber}. Latest active review round for {gateCode} is {latestRound}.",
                WorkflowErrorCodes.StaleReviewRound);

        if (input.Decision is not ("PASSED" or "REVISION_REQUIRED" or "OVERRIDDEN_PASS"))
            throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["Unsupported round scope decision."] });

        var majorId = long.TryParse(input.Scope, out var parsed)
            ? parsed
            : await db.Majors.Where(x => x.Code == input.Scope || x.Name == input.Scope)
                .Select(x => (long?)x.Id).SingleOrDefaultAsync(token);

        var normalized = input.Scope.Equals("COMMON", StringComparison.OrdinalIgnoreCase)
            ? "COMMON"
            : majorId.HasValue && scope.MajorIds.Contains(majorId.Value)
                ? majorId.Value.ToString()
                : throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Scope is outside the frozen project scope."] });

        var actorDept = await db.Users.AsNoTracking()
            .Where(u => u.Id == Actor && u.Status == "ACTIVE" && u.Department != null && u.Department.IsActive)
            .Select(u => u.DepartmentId)
            .SingleOrDefaultAsync(token);

        var isLeadStaff = scope.LeadDepartmentId.HasValue && actorDept == scope.LeadDepartmentId.Value;

        if (input.IsLeadOverride || input.Decision == "OVERRIDDEN_PASS")
        {
            if (!isLeadStaff)
                throw new ForbiddenException("Only LeadScope department staff may override another department's gate scope decision.", WorkflowErrorCodes.ScopeAccessDenied);
            if (string.IsNullOrWhiteSpace(input.Notes) || input.Notes.Trim().Length < 20)
                throw new ValidationException(new Dictionary<string, string[]> { ["notes"] = ["Lead override requires an audit reason of at least 20 characters."] });
        }
        else
        {
            await RequireScopeMutationAuthority(scope, normalized, token);
            if (input.Decision == "REVISION_REQUIRED" && string.IsNullOrWhiteSpace(input.Notes))
                throw new ValidationException(new Dictionary<string, string[]> { ["notes"] = ["Revision feedback notes are required."] });
            // TK-W2-06: Department scope sign-off cannot bypass the 2-reviewer panel to push gate PASSED.
            if (input.Decision == "PASSED" && gate.Sequence is >= 1 and <= 3)
            {
                var panelAssignments = await db.GateReviewerAssignments.AsNoTracking()
                    .Where(a => a.GateInstanceId == gate.Id && a.Status != "REVOKED")
                    .ToArrayAsync(token);
                var distinctReviewers = panelAssignments.Select(a => a.ReviewerId).Distinct().Count();
                if (distinctReviewers < 2 || panelAssignments.Any(a => a.LatestDecision != "PASSED"))
                {
                    throw new ConflictException(
                        "Gate 1–3 require both assigned reviewers to pass before recording a PASSED scope decision; use Lead Override for an audited exception.",
                        WorkflowErrorCodes.GateReviewPanelRequired);
                }
            }
        }

        var existing = await db.Set<ProjectGateRoundScopeDecision>()
            .SingleOrDefaultAsync(x => x.GateInstanceId == gate.Id && x.RoundNumber == latestRound && x.Scope == normalized, token);

        if (existing is not null && !input.IsLeadOverride && existing.Decision != "PENDING" && !string.IsNullOrWhiteSpace(input.ConcurrencyToken))
            CheckToken(existing.ConcurrencyToken, input.ConcurrencyToken);

        // Prevent ParticipatingScope from mutating or overriding a LeadScope COMMON/Lead-major decision
        if (existing is not null && !isLeadStaff && existing.DepartmentId == scope.LeadDepartmentId && existing.Decision != "PENDING")
            throw new ForbiddenException("ParticipatingScope cannot override a LeadScope decision.", WorkflowErrorCodes.ScopeAccessDenied);

        long? resolvedDeptId = normalized == "COMMON"
            ? scope.LeadDepartmentId
            : (scope.MajorDepartmentIds is not null && majorId.HasValue && scope.MajorDepartmentIds.TryGetValue(majorId.Value, out var dId)
                ? dId
                : actorDept);

        var effectiveDecision = input.IsLeadOverride && input.Decision == "PASSED" ? "OVERRIDDEN_PASS" : input.Decision;
        if (existing is null)
        {
            existing = new ProjectGateRoundScopeDecision
            {
                ProjectId = projectId,
                GateInstanceId = gate.Id,
                RoundNumber = latestRound,
                Scope = normalized,
                DepartmentId = resolvedDeptId,
                MajorId = normalized == "COMMON" ? null : majorId,
                Decision = effectiveDecision,
                Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
                DecidedBy = Actor,
                DecidedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Set<ProjectGateRoundScopeDecision>().Add(existing);
        }
        else
        {
            existing.Decision = effectiveDecision;
            existing.Notes = string.IsNullOrWhiteSpace(input.Notes) ? existing.Notes : input.Notes.Trim();
            existing.DecidedBy = Actor;
            existing.DecidedAt = Now;
            existing.ConcurrencyToken = Guid.NewGuid();
        }

        await db.SaveChangesAsync(token);

        var consensus = await BuildRoundConsensusAsync(projectId, gate, scope, latestRound, token);

        if (consensus.AllRequiredScopesPassed)
        {
            gate.Status = "PASSED";
            gate.ReviewEligibility = "READY";
            gate.DecisionSource = consensus.Decisions.Any(d => d.Decision == "OVERRIDDEN_PASS") ? "OVERRIDE" : "REVIEW";
            gate.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(token);
            await OpenNextGate(gate, token);
        }
        else if (consensus.Decisions.Any(d => d.Decision == "REVISION_REQUIRED"))
        {
            gate.Status = "REVISION_REQUIRED";
            gate.ReviewEligibility = "BLOCKED";
            gate.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(token);
        }
        else
        {
            gate.Status = "IN_REVIEW";
            gate.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(token);
        }

        await audit.RecordAsync(new(Actor,
            input.IsLeadOverride ? "GATE_ROUND_SCOPE_LEAD_OVERRIDE" : "GATE_ROUND_SCOPE_DECIDED",
            "PROJECT_GATE",
            gate.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["gateCode"] = gate.Code,
                ["roundNumber"] = latestRound,
                ["scope"] = normalized,
                ["decision"] = effectiveDecision,
                ["allRequiredPassed"] = consensus.AllRequiredScopesPassed
            }), token);

        return consensus with { GateStatus = gate.Status };
    }, ct);

    private async Task<GateRoundConsensusDto> BuildRoundConsensusAsync(
        long projectId,
        ProjectGateInstance gate,
        ProjectAcademicScope scope,
        int roundNumber,
        CancellationToken ct)
    {
        var rows = await db.Set<ProjectGateRoundScopeDecision>().AsNoTracking()
            .Where(x => x.GateInstanceId == gate.Id && x.RoundNumber == roundNumber)
            .OrderBy(x => x.Scope)
            .ToListAsync(ct);

        var requiredScopes = scope.MajorIds.Count >= 2
            ? scope.MajorIds.Select(id => id.ToString()).ToArray()
            : ["COMMON"];

        var allPassed = requiredScopes.All(reqScope =>
            rows.Any(r => r.Scope == reqScope && r.Decision is "PASSED" or "OVERRIDDEN_PASS"));

        var revisionNotes = rows
            .Where(r => r.Decision == "REVISION_REQUIRED" && !string.IsNullOrWhiteSpace(r.Notes))
            .Select(r => $"[{r.Scope}] {r.Notes!.Trim()}")
            .ToArray();
        var consolidated = revisionNotes.Length > 0 ? string.Join(" | ", revisionNotes) : null;

        var dtos = rows.Select(r => new RoundScopeDecisionDto(
            r.Id, r.GateInstanceId, r.RoundNumber, r.Scope, r.DepartmentId, r.MajorId,
            r.Decision, r.Notes, r.DecidedBy,
            r.DecidedAt.HasValue ? DateTime.SpecifyKind(r.DecidedAt.Value, DateTimeKind.Utc) : null,
            r.ConcurrencyToken.ToString("N"))).ToArray();

        return new GateRoundConsensusDto(projectId, gate.Code, roundNumber, gate.Status, allPassed, consolidated, dtos);
    }

    public Task<SlaSweepResultDto> SweepSlaNotifications(CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        _ = workingDayCalculator;
        var slaService = new DualDeptSlaService(db, clock);
        return await slaService.SweepAsync(Actor, token);
    }, ct);
}
