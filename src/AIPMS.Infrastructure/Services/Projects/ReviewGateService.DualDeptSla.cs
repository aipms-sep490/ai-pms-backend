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
            if (gate.BlockNextGate) await OpenNextGate(gate, token);
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
        var now = Now;
        var events = new List<SlaEventSummaryDto>();

        // 1. Project dual-department reviews pending >= 48h (REMINDER_48H) and >= 72h (ESCALATION_72H to LeadScope)
        var pendingProjectReviews = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Include(s => s.Decisions)
            .Where(s => s.Decisions.Any(d => d.Decision == "PENDING"))
            .ToListAsync(token);

        var projectStatuses = pendingProjectReviews.Count == 0
            ? new Dictionary<long, string>()
            : await db.Projects.AsNoTracking()
                .Where(p => pendingProjectReviews.Select(s => s.ProjectId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Status, token);

        foreach (var snap in pendingProjectReviews)
        {
            if (!projectStatuses.TryGetValue(snap.ProjectId, out var status) || status is not ("SUBMITTED" or "UNDER_REVIEW"))
                continue;
            var elapsedHours = (now - snap.SubmittedAt).TotalHours;
            foreach (var dec in snap.Decisions.Where(d => d.Decision == "PENDING"))
            {
                if (elapsedHours >= 48.0)
                    await TryEmitSlaAsync("PROJECT_REVIEW", snap.Id, 1, dec.DepartmentId, "REMINDER_48H",
                        $"Nhắc SLA 48h: Hồ sơ dự án #{snap.ProjectId} chờ thẩm định",
                        $"Khoa của bạn có hồ sơ dự án #{snap.ProjectId} chờ duyệt quá 48 giờ.", now, events, token);
                if (elapsedHours >= 72.0)
                    await TryEmitSlaAsync("PROJECT_REVIEW", snap.Id, 1, snap.LeadDepartmentId, "ESCALATION_72H",
                        $"Leo thang SLA 72h: Hồ sơ dự án #{snap.ProjectId} quá hạn đồng duyệt",
                        $"Đơn vị phối hợp (#{dec.DepartmentId}) chưa phản hồi dự án #{snap.ProjectId} quá 72 giờ; đề nghị LeadScope điều phối.", now, events, token);
            }
        }

        // 2. Submitted Gate packages pending consensus >= 48h / >= 72h
        var openGatePackages = await db.Set<ReviewGatePackage>().AsNoTracking()
            .Join(db.ProjectGateInstances.AsNoTracking().Where(g => g.Status == "SUBMITTED" || g.Status == "IN_REVIEW"),
                p => p.GateInstanceId, g => g.Id,
                (p, g) => new { Package = p, Gate = g })
            .ToListAsync(token);

        foreach (var entry in openGatePackages)
        {
            var elapsedHours = (now - entry.Package.SubmittedAt).TotalHours;
            if (elapsedHours < 48.0) continue;

            var projScope = await ProjectAcademicScopeReader.ReadAsync(db, entry.Gate.ProjectId, token, requireFrozenScope: false);
            if (!projScope.LeadDepartmentId.HasValue) continue;

            var decidedScopes = await db.Set<ProjectGateRoundScopeDecision>().AsNoTracking()
                .Where(d => d.GateInstanceId == entry.Gate.Id && d.RoundNumber == entry.Package.RoundNumber && d.Decision != "PENDING")
                .Select(d => d.Scope)
                .ToListAsync(token);

            var targetDepts = new HashSet<long>();
            if (projScope.MajorDepartmentIds is { Count: > 0 })
            {
                foreach (var kv in projScope.MajorDepartmentIds)
                    if (!decidedScopes.Contains(kv.Key.ToString()))
                        targetDepts.Add(kv.Value);
            }
            if (targetDepts.Count == 0) targetDepts.Add(projScope.LeadDepartmentId.Value);

            foreach (var deptId in targetDepts)
            {
                if (elapsedHours >= 48.0)
                    await TryEmitSlaAsync("REVIEW_GATE", entry.Gate.Id, entry.Package.RoundNumber, deptId, "REMINDER_48H",
                        $"Nhắc SLA 48h: Cổng {entry.Gate.Code} dự án #{entry.Gate.ProjectId}",
                        $"Gói nộp vòng {entry.Package.RoundNumber} của cổng {entry.Gate.Code} đang chờ phản hồi quá 48 giờ.", now, events, token);
                if (elapsedHours >= 72.0)
                    await TryEmitSlaAsync("REVIEW_GATE", entry.Gate.Id, entry.Package.RoundNumber, projScope.LeadDepartmentId.Value, "ESCALATION_72H",
                        $"Leo thang SLA 72h: Cổng {entry.Gate.Code} dự án #{entry.Gate.ProjectId}",
                        $"Cổng {entry.Gate.Code} vòng {entry.Package.RoundNumber} quá 72 giờ chưa đủ đồng thuận 2 Khoa; đề nghị LeadScope xử lý.", now, events, token);
            }
        }

        await db.SaveChangesAsync(token);
        return new SlaSweepResultDto(
            events.Count(e => e.SlaTier == "REMINDER_48H"),
            events.Count(e => e.SlaTier == "ESCALATION_72H"),
            events);
    }, ct);

    private async Task TryEmitSlaAsync(
        string entityType,
        long entityId,
        int roundNumber,
        long departmentId,
        string slaTier,
        string title,
        string content,
        DateTime now,
        List<SlaEventSummaryDto> collector,
        CancellationToken ct)
    {
        var exists = collector.Any(e => e.EntityType == entityType && e.EntityId == entityId
                && e.RoundNumber == roundNumber && e.DepartmentId == departmentId && e.SlaTier == slaTier)
            || await db.Set<WorkflowSlaNotification>().AsNoTracking()
                .AnyAsync(x => x.EntityType == entityType && x.EntityId == entityId
                    && x.RoundNumber == roundNumber && x.DepartmentId == departmentId && x.SlaTier == slaTier, ct);
        if (exists) return;

        db.Set<WorkflowSlaNotification>().Add(new WorkflowSlaNotification
        {
            EntityType = entityType,
            EntityId = entityId,
            RoundNumber = roundNumber,
            DepartmentId = departmentId,
            SlaTier = slaTier,
            SentAt = now
        });

        var staffIds = await db.Users.AsNoTracking()
            .Where(u => u.Status == "ACTIVE" && u.DepartmentId == departmentId
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"))
            .Select(u => u.Id)
            .ToArrayAsync(ct);

        if (staffIds.Length > 0)
        {
            await V4WorkflowNotificationHelper.EmitAsync(
                db, Actor, $"SLA_{slaTier}", title, content, entityType, entityId, staffIds, now, enqueueEmail: true, ct, excludeActor: false);
        }

        collector.Add(new SlaEventSummaryDto(entityType, entityId, roundNumber, departmentId, slaTier, DateTime.SpecifyKind(now, DateTimeKind.Utc)));
    }
}
