using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProjectChecklists;
using AIPMS.Application.Features.ProjectChecklists.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ProjectChecklistService
{
    public async Task<ExpandStudentRequirementsResultDto> ExpandEachStudentRequirementsAsync(long projectId, string gateCode, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department staff or admin can expand EACH_STUDENT gate requirements.");

        var normalizedGate = gateCode.Trim().ToUpperInvariant();
        var gate = await db.ProjectGateInstances.Include(g => g.Requirements)
            .SingleOrDefaultAsync(g => g.ProjectId == projectId && g.Code == normalizedGate, ct)
            ?? throw new NotFoundException("ProjectGateInstance", normalizedGate);

        var snapJson = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Id).Select(s => s.SnapshotJson).FirstOrDefaultAsync(ct);
        var frozenMembers = snapJson is null ? [] : ProjectAcademicScopeReader.ParseEvidence(snapJson)?.Members ?? [];

        var activeIds = await db.TeamMembers.AsNoTracking()
            .Where(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.LeftAt == null)
            .Select(m => m.UserId).ToListAsync(ct);

        var targetStudents = frozenMembers.Count > 0
            ? frozenMembers.Where(m => activeIds.Count == 0 || activeIds.Contains(m.UserId)).Select(m => (m.UserId, (long?)m.MajorId)).ToList()
            : await db.TeamMembers.AsNoTracking()
                .Where(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.LeftAt == null)
                .Select(m => new { m.UserId, m.User.MajorId })
                .ToListAsync(ct).ContinueWith(t => t.Result.Select(x => (x.UserId, x.MajorId)).ToList(), ct);

        var templates = gate.Requirements
            .Where(r => r.TargetCardinality == "EACH_STUDENT" && r.TargetStudentId == null)
            .ToList();

        var created = 0;
        foreach (var tpl in templates)
        {
            foreach (var (studentId, majorId) in targetStudents)
            {
                var studentCode = $"{tpl.Code}-S{studentId}";
                if (gate.Requirements.Any(r => r.Code == studentCode || (r.TargetStudentId == studentId && r.CarryForwardFromCode == tpl.Code)))
                    continue;
                gate.Requirements.Add(new ProjectGateRequirement
                {
                    GateInstanceId = gate.Id,
                    Code = studentCode,
                    Title = $"{tpl.Title} (Student #{studentId})",
                    Type = tpl.Type,
                    Scope = "INDIVIDUAL",
                    MajorId = majorId,
                    TargetStudentId = studentId,
                    Required = tpl.Required,
                    VerificationStatus = "PENDING",
                    TargetCardinality = "EACH_STUDENT",
                    DeliveryStage = tpl.DeliveryStage,
                    FeedsCold = tpl.FeedsCold,
                    FeedsDefense = tpl.FeedsDefense,
                    InputKind = tpl.InputKind,
                    TemplateDocumentId = tpl.TemplateDocumentId,
                    CarryForwardFromCode = tpl.Code,
                    PolicyTag = tpl.PolicyTag,
                    RubricCriterionRef = tpl.RubricCriterionRef
                });
                created++;
            }
            // Mark the blueprint placeholder non-required once expanded so only concrete per-student slots gate submission
            tpl.Required = false;
        }
        await db.SaveChangesAsync(ct);
        return new ExpandStudentRequirementsResultDto(projectId, gate.Code, created, targetStudents.Count);
    }

    public async Task<WithdrawRosterMemberResultDto> WithdrawRosterMemberAsync(long projectId, WithdrawRosterMemberRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DomainException("Withdrawal reason is required.", "REASON_REQUIRED");

        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department staff or admin can record a mid-semester student withdrawal.");

        var member = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.UserId == request.StudentUserId && m.LeftAt == null, ct)
            ?? throw new NotFoundException("ActiveTeamMember", request.StudentUserId);

        var now = Now;
        member.LeftAt = now;
        member.UpdatedAt = now;

        var gateReqs = await db.ProjectGateRequirements
            .Where(r => r.GateInstance.ProjectId == projectId && r.TargetStudentId == request.StudentUserId
                && r.VerificationStatus != "ACCEPTED" && r.VerificationStatus != "WAIVED_WITHDRAWN")
            .ToListAsync(ct);
        foreach (var req in gateReqs)
        {
            req.VerificationStatus = "WAIVED_WITHDRAWN";
            req.ReviewerFeedback = $"Waived due to mid-semester withdrawal: {request.Reason.Trim()}";
        }

        var coldItems = await db.Set<ColdChecklistItem>()
            .Where(c => c.StudentId == request.StudentUserId && db.Set<ColdPackage>().Any(p => p.Id == c.PackageId && p.ProjectId == projectId))
            .ToListAsync(ct);
        foreach (var item in coldItems)
            item.WaivedStatus = "WAIVED_WITHDRAWN";

        await db.SaveChangesAsync(ct);
        await auditTrail.RecordAsync(new AuditEntry(ActorId, "ROSTER_MEMBER_WITHDRAWN", "PROJECT", projectId,
            new Dictionary<string, object?> { ["studentUserId"] = request.StudentUserId, ["reason"] = request.Reason.Trim(), ["waivedGateReqs"] = gateReqs.Count, ["waivedColdItems"] = coldItems.Count }), ct);

        return new WithdrawRosterMemberResultDto(projectId, request.StudentUserId, gateReqs.Count, coldItems.Count, now);
    }

    public async Task<PreDefenseLockStatusDto> RequestFinalLockAsync(long projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);

        var isLeader = project.Team.TeamMembers.Any(m => m.UserId == ActorId && m.LeftAt == null && m.IsLeader);
        if (!isLeader)
            throw new ForbiddenException("Only the active project team leader can request a 2-step Final Submission lock.");

        if (project.Status != "ACTIVE")
            throw new ConflictException("Project must be ACTIVE before requesting final lock.", WorkflowErrorCodes.ProjectNotActive);

        // Enforce Gate 3 PASSED (and all blocksDefense gates PASSED)
        var unpassedBlocking = await db.ProjectGateInstances.AsNoTracking()
            .AnyAsync(g => g.ProjectId == projectId && (g.Code == "G3" || g.BlocksDefense) && g.Status != "PASSED", ct);
        if (unpassedBlocking)
            throw new ConflictException("Gate 3 (and all defense-blocking gates) must be PASSED before requesting Final lock.", WorkflowErrorCodes.Gate3NotPassed);

        var draft = await db.Set<FinalSubmissionDraft>().SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        var now = Now;
        if (draft is null)
        {
            var finalPeriod = await db.ProjectPeriods.AsNoTracking()
                .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == "FINAL_SUBMISSION")
                .OrderByDescending(p => p.Id).FirstOrDefaultAsync(ct)
                ?? await db.ProjectPeriods.AsNoTracking().Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId).OrderByDescending(p => p.Id).FirstAsync(ct);
            draft = new FinalSubmissionDraft
            {
                ProjectId = projectId,
                ProjectPeriodId = finalPeriod.Id,
                CreatedBy = ActorId,
                UpdatedBy = ActorId,
                CreatedAt = now,
                UpdatedAt = now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Set<FinalSubmissionDraft>().Add(draft);
        }

        if (draft.LockWorkflowStatus == "LOCKED")
            throw new ConflictException("Final submission package is already LOCKED.");

        draft.LockWorkflowStatus = "PENDING_SUPERVISOR_LOCK";
        draft.LockRequestedBy = ActorId;
        draft.LockRequestedAt = now;
        draft.UpdatedBy = ActorId;
        draft.UpdatedAt = now;
        draft.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);

        await auditTrail.RecordAsync(new AuditEntry(ActorId, "FINAL_LOCK_REQUESTED", "PROJECT", projectId,
            new Dictionary<string, object?> { ["draftId"] = draft.Id }), ct);

        return new PreDefenseLockStatusDto(projectId, project.Status, draft.LockWorkflowStatus,
            draft.LockRequestedBy, draft.LockRequestedAt, draft.LockedBySupervisorId, draft.ForceLockedBy, draft.ForceLockReason, null, null, null);
    }

    public async Task<PreDefenseLockStatusDto> SupervisorDecideFinalLockAsync(long projectId, SupervisorFinalLockDecisionRequest request, CancellationToken ct = default)
    {
        var isPrimarySupervisor = await db.SupervisorAssignments.AsNoTracking()
            .AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.IsPrimary && a.AssignmentType == "PRIMARY" && a.SupervisorProfile.UserId == ActorId, ct);
        if (!isPrimarySupervisor)
            throw new ForbiddenException("Only the active Primary Supervisor can sign and lock the Final Submission package.");

        var decision = (request.Decision ?? "").Trim().ToUpperInvariant();
        if (decision is not ("LOCK" or "RETURN_TO_OPEN"))
            throw new DomainException("Decision must be LOCK or RETURN_TO_OPEN.", "INVALID_LOCK_DECISION");

        var draft = await db.Set<FinalSubmissionDraft>().Include(d => d.Items)
            .SingleOrDefaultAsync(d => d.ProjectId == projectId, ct)
            ?? throw new NotFoundException("FinalSubmissionDraft", projectId);

        if (draft.LockWorkflowStatus != "PENDING_SUPERVISOR_LOCK")
            throw new ConflictException("Final submission draft must be in PENDING_SUPERVISOR_LOCK status.");

        if (decision == "RETURN_TO_OPEN")
        {
            draft.LockWorkflowStatus = "OPEN";
            draft.UpdatedBy = ActorId;
            draft.UpdatedAt = Now;
            draft.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            var proj = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
            return new PreDefenseLockStatusDto(projectId, proj.Status, draft.LockWorkflowStatus,
                draft.LockRequestedBy, draft.LockRequestedAt, null, null, null, null, null, null);
        }

        return await CommitUnifiedFinalAndColdLockAsync(projectId, draft, lockedBySupervisorId: ActorId, forceLockedBy: null, forceReason: null, ct);
    }

    public async Task<PreDefenseLockStatusDto> ForceLockFinalAsync(long projectId, ForceLockFinalRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DomainException("Force-lock requires an explicit audit reason.", "REASON_REQUIRED");

        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department staff or admin can force-lock a Final Submission.");

        var draft = await db.Set<FinalSubmissionDraft>().Include(d => d.Items)
            .SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        var project = await db.Projects.Include(p => p.Team).SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        var now = Now;
        if (draft is null)
        {
            var finalPeriod = await db.ProjectPeriods.AsNoTracking()
                .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == "FINAL_SUBMISSION")
                .OrderByDescending(p => p.Id).FirstOrDefaultAsync(ct)
                ?? await db.ProjectPeriods.AsNoTracking().Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId).OrderByDescending(p => p.Id).FirstAsync(ct);
            draft = new FinalSubmissionDraft
            {
                ProjectId = projectId,
                ProjectPeriodId = finalPeriod.Id,
                CreatedBy = ActorId,
                UpdatedBy = ActorId,
                CreatedAt = now,
                UpdatedAt = now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Set<FinalSubmissionDraft>().Add(draft);
            await db.SaveChangesAsync(ct);
        }

        return await CommitUnifiedFinalAndColdLockAsync(projectId, draft, lockedBySupervisorId: null, forceLockedBy: ActorId, forceReason: request.Reason.Trim(), ct);
    }

    private async Task<PreDefenseLockStatusDto> CommitUnifiedFinalAndColdLockAsync(
        long projectId,
        FinalSubmissionDraft draft,
        long? lockedBySupervisorId,
        long? forceLockedBy,
        string? forceReason,
        CancellationToken ct)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        var now = Now;

        // Compute deterministic unified Cold + Final SHA-256 manifest
        var coldPkg = await db.Set<ColdPackage>().SingleOrDefaultAsync(c => c.ProjectId == projectId, ct);
        var coldFiles = coldPkg is null
            ? []
            : await db.Set<ColdSubmissionFile>().AsNoTracking()
                .Where(f => db.Set<ColdSubmission>().Any(s => s.Id == f.SubmissionId && db.Set<ColdChecklistItem>().Any(i => i.Id == s.ChecklistItemId && i.PackageId == coldPkg.Id)))
                .OrderBy(f => f.FileId).Select(f => $"{f.FileId}:{f.Sha256}").ToListAsync(ct);
        var draftVersionIds = draft.Items.OrderBy(i => i.DeliverableVersionId).Select(i => i.DeliverableVersionId.ToString()).ToList();
        var manifestInput = $"PROJECT:{projectId}|DRAFT:{string.Join(",", draftVersionIds)}|COLD:{string.Join(",", coldFiles)}";
        var manifestSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestInput))).ToLowerInvariant();

        // Freeze ColdPackage if present & still OPEN
        if (coldPkg is not null && coldPkg.Status == "OPEN")
        {
            coldPkg.Status = "LOCKED";
            coldPkg.LockedBy = ActorId;
            coldPkg.LockedAt = now;
            coldPkg.ConcurrencyToken = Guid.NewGuid();
        }

        // Create or update FinalSubmission snapshot
        var existingSub = await db.Set<FinalSubmission>().SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
        if (existingSub is null)
        {
            var period = await db.ProjectPeriods.AsNoTracking().SingleAsync(p => p.Id == draft.ProjectPeriodId, ct);
            existingSub = new FinalSubmission
            {
                ProjectId = projectId,
                ProjectPeriodId = draft.ProjectPeriodId,
                SubmittedBy = draft.LockRequestedBy ?? ActorId,
                SubmittedAt = now,
                Deadline = period.EndAt,
                Notes = draft.Notes,
                DraftConcurrencyToken = draft.ConcurrencyToken,
                RequirementsConcurrencyToken = Guid.NewGuid(),
                LockedBySupervisorId = lockedBySupervisorId,
                ForceLockedBy = forceLockedBy,
                ForceLockReason = forceReason,
                ColdManifestSha256 = manifestSha
            };
            db.Set<FinalSubmission>().Add(existingSub);
        }
        else
        {
            existingSub.LockedBySupervisorId = lockedBySupervisorId ?? existingSub.LockedBySupervisorId;
            existingSub.ForceLockedBy = forceLockedBy ?? existingSub.ForceLockedBy;
            existingSub.ForceLockReason = forceReason ?? existingSub.ForceLockReason;
            existingSub.ColdManifestSha256 = manifestSha;
        }

        draft.LockWorkflowStatus = "LOCKED";
        draft.LockedBySupervisorId = lockedBySupervisorId;
        draft.ForceLockedBy = forceLockedBy;
        draft.ForceLockReason = forceReason;
        draft.UpdatedBy = ActorId;
        draft.UpdatedAt = now;
        draft.ConcurrencyToken = Guid.NewGuid();

        // Transition project to FINAL_SUBMISSION (independent of any DefenseSession!)
        if (project.Status == "ACTIVE")
        {
            project.Status = "FINAL_SUBMISSION";
            project.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        await auditTrail.RecordAsync(new AuditEntry(ActorId,
            forceLockedBy.HasValue ? "FINAL_SUBMISSION_FORCE_LOCKED" : "FINAL_SUBMISSION_SUPERVISOR_LOCKED",
            "FINAL_SUBMISSION", existingSub.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["coldManifestSha256"] = manifestSha, ["forceReason"] = forceReason }), ct);

        return new PreDefenseLockStatusDto(projectId, project.Status, draft.LockWorkflowStatus,
            draft.LockRequestedBy, draft.LockRequestedAt, draft.LockedBySupervisorId, draft.ForceLockedBy, draft.ForceLockReason,
            manifestSha, existingSub.Id, coldPkg?.Id);
    }
}
