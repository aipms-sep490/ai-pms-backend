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
using AIPMS.Infrastructure.Persistence.Mappers;
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

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static void ValidateOptionalToken(Guid expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return;
        if (!Guid.TryParse(supplied, out var parsed) || parsed != expected)
            throw new ConflictException("Final lock draft changed concurrently. Reload before retrying.", WorkflowErrorCodes.StaleConcurrencyToken);
    }

    private async Task<List<string>> ComputePreDefenseBlockersAsync(long projectId, FinalSubmissionDraft? draft, CancellationToken ct)
    {
        var blockers = new List<string>();
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return blockers;
        if (project.Status != "ACTIVE") blockers.Add(WorkflowErrorCodes.ProjectNotActive);

        var unpassedBlocking = await db.ProjectGateInstances.AsNoTracking()
            .AnyAsync(g => g.ProjectId == projectId && (g.Code == "G3" || g.Code == "R3" || g.BlocksDefense) && g.Status != "PASSED", ct);
        if (unpassedBlocking) blockers.Add(WorkflowErrorCodes.Gate3NotPassed);

        var requirements = await db.Set<FinalSubmissionRequirements>().AsNoTracking()
            .Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.ProjectId == projectId, ct);
        if (requirements is not null && requirements.Items.Count > 0)
        {
            var draftVersionIds = draft?.Items.Select(i => i.DeliverableVersionId).ToArray() ?? [];
            if (draftVersionIds.Length == 0)
            {
                blockers.Add("EMPTY_PACKAGE");
            }
            else
            {
                var versions = await db.DeliverableVersions.AsNoTracking()
                    .Include(v => v.Files)
                    .Where(v => draftVersionIds.Contains(v.Id) && v.Deliverable.ProjectId == projectId)
                    .ToListAsync(ct);
                var eligibleByDeliverable = versions
                    .Where(v => v.Status is "SUBMITTED" or "ACCEPTED" && v.Files.Count > 0)
                    .Select(v => v.DeliverableId)
                    .ToHashSet();
                foreach (var req in requirements.Items)
                {
                    if (!eligibleByDeliverable.Contains(req.DeliverableId))
                        blockers.Add($"REQUIRED_DELIVERABLE_MISSING:{req.DeliverableId}");
                }
            }
        }

        var coldPkg = await db.Set<ColdPackage>().AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == projectId, ct);
        if (coldPkg is not null && coldPkg.Status == "OPEN")
        {
            var requiredColdItems = await db.Set<ColdChecklistItem>().AsNoTracking()
                .Where(i => i.PackageId == coldPkg.Id && i.Required && (i.WaivedStatus == null || i.WaivedStatus != "WAIVED_WITHDRAWN"))
                .ToListAsync(ct);
            if (requiredColdItems.Count > 0)
            {
                var itemIds = requiredColdItems.Select(i => i.Id).ToArray();
                var latestSubs = await db.Set<ColdSubmission>().AsNoTracking()
                    .Include(s => s.Files)
                    .Where(s => itemIds.Contains(s.ChecklistItemId)
                        && !db.Set<ColdSubmission>().Any(newer => newer.ChecklistItemId == s.ChecklistItemId && newer.Version > s.Version))
                    .ToDictionaryAsync(s => s.ChecklistItemId, ct);
                foreach (var ci in requiredColdItems)
                {
                    if (!latestSubs.TryGetValue(ci.Id, out var sub) || sub.Status != "SUBMITTED" || sub.Files.Count == 0)
                        blockers.Add($"COLD_REPORT_REQUIRED:{ci.Id}");
                }
            }
        }

        return blockers;
    }

    public async Task<PreDefenseLockStatusDto> GetPreDefenseLockStatusAsync(long projectId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var project = await db.Projects.AsNoTracking()
            .Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .Include(p => p.ProjectMajors).ThenInclude(pm => pm.Major)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var isMember = project.Team.TeamMembers.Any(m => m.UserId == ActorId && m.LeftAt == null);
        var isSupOrMentor = await db.SupervisorAssignments.AsNoTracking()
            .AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == ActorId, ct);
        var isDeptStaff = actor.IsStaff && actor.DepartmentId.HasValue
            && project.ProjectMajors.Any(pm => pm.Major.DepartmentId == actor.DepartmentId.Value);
        if (!actor.IsAdmin && !isMember && !isSupOrMentor && !isDeptStaff)
            throw new ForbiddenException("You do not have access to this project's pre-defense lock status.");

        var draft = await db.Set<FinalSubmissionDraft>().AsNoTracking().Include(d => d.Items)
            .SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        var finalSub = await db.Set<FinalSubmission>().AsNoTracking().SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
        var coldPkg = await db.Set<ColdPackage>().AsNoTracking().SingleOrDefaultAsync(c => c.ProjectId == projectId, ct);

        var status = finalSub is not null ? "LOCKED" : (draft?.LockWorkflowStatus ?? "OPEN");
        var blockers = finalSub is not null ? new List<string>() : await ComputePreDefenseBlockersAsync(projectId, draft, ct);

        return new PreDefenseLockStatusDto(
            projectId,
            project.Status,
            status,
            draft?.LockRequestedBy,
            draft?.LockRequestedAt.HasValue == true ? Utc(draft.LockRequestedAt.Value) : null,
            finalSub?.LockedBySupervisorId ?? draft?.LockedBySupervisorId,
            finalSub?.ForceLockedBy ?? draft?.ForceLockedBy,
            finalSub?.ForceLockReason ?? draft?.ForceLockReason,
            finalSub?.ColdManifestSha256,
            finalSub?.Id,
            coldPkg?.Id,
            draft?.ConcurrencyToken.ToString("N"),
            blockers);
    }

    public async Task<PreDefenseLockStatusDto> RequestFinalLockAsync(long projectId, string? concurrencyToken = null, CancellationToken ct = default)
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
            .AnyAsync(g => g.ProjectId == projectId && (g.Code == "G3" || g.Code == "R3" || g.BlocksDefense) && g.Status != "PASSED", ct);
        if (unpassedBlocking)
            throw new ConflictException("Gate 3 (and all defense-blocking gates) must be PASSED before requesting Final lock.", WorkflowErrorCodes.Gate3NotPassed);

        var draft = await db.Set<FinalSubmissionDraft>().Include(d => d.Items)
            .SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        if (draft is not null)
            ValidateOptionalToken(draft.ConcurrencyToken, concurrencyToken);

        var blockers = await ComputePreDefenseBlockersAsync(projectId, draft, ct);
        if (blockers.Any(b => b.StartsWith("COLD_REPORT_REQUIRED:", StringComparison.Ordinal)))
            throw new ConflictException("All required Cold reports must be SUBMITTED before requesting Final lock: " + string.Join(", ", blockers), WorkflowErrorCodes.ColdPackageIncomplete);
        if (blockers.Any(b => b == "EMPTY_PACKAGE" || b.StartsWith("REQUIRED_DELIVERABLE_MISSING:", StringComparison.Ordinal)))
            throw new ConflictException("Final submission draft does not satisfy configured required deliverables: " + string.Join(", ", blockers), WorkflowErrorCodes.FinalPackageRequired);

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

        if (draft.LockWorkflowStatus == "LOCKED" || await db.Set<FinalSubmission>().AsNoTracking().AnyAsync(s => s.ProjectId == projectId, ct))
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

        var primarySupervisors = await V4WorkflowNotificationHelper.GetActiveSupervisorAndMentorIdsAsync(db, projectId, primaryOnly: true, ct);
        await V4WorkflowNotificationHelper.EmitAsync(
            db, ActorId, "FINAL_LOCK_REQUESTED",
            $"Nhóm đã gửi yêu cầu ký khóa hồ sơ Tiền-Defense (Đồ án #{projectId})",
            "Trưởng nhóm đã hoàn tất hồ sơ Tiền-Defense và gửi yêu cầu GVHD Chính kiểm tra, ký khóa (Bước 2).",
            "PROJECT", projectId, primarySupervisors, now, enqueueEmail: true, ct);

        var coldPkgId = await db.Set<ColdPackage>().AsNoTracking().Where(c => c.ProjectId == projectId).Select(c => (long?)c.Id).SingleOrDefaultAsync(ct);
        return new PreDefenseLockStatusDto(projectId, project.Status, draft.LockWorkflowStatus,
            draft.LockRequestedBy, draft.LockRequestedAt, draft.LockedBySupervisorId, draft.ForceLockedBy, draft.ForceLockReason, null, null, coldPkgId,
            draft.ConcurrencyToken.ToString("N"), []);
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

        ValidateOptionalToken(draft.ConcurrencyToken, request.ConcurrencyToken);

        if (draft.LockWorkflowStatus != "PENDING_SUPERVISOR_LOCK")
            throw new ConflictException("Final submission draft must be in PENDING_SUPERVISOR_LOCK status.");

        if (decision == "RETURN_TO_OPEN")
        {
            if (string.IsNullOrWhiteSpace(request.Notes))
                throw new DomainException("Notes/reason is required when returning the Final Submission package to OPEN.", WorkflowErrorCodes.ReasonRequired);

            draft.LockWorkflowStatus = "OPEN";
            draft.UpdatedBy = ActorId;
            draft.UpdatedAt = Now;
            draft.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);

            await auditTrail.RecordAsync(new AuditEntry(ActorId, "FINAL_LOCK_RETURNED", "PROJECT", projectId,
                new Dictionary<string, object?> { ["draftId"] = draft.Id, ["notes"] = request.Notes.Trim() }), ct);

            var studentIds = await V4WorkflowNotificationHelper.GetActiveTeamStudentIdsAsync(db, projectId, ct);
            await V4WorkflowNotificationHelper.EmitAsync(
                db, ActorId, "FINAL_LOCK_RETURNED",
                $"GVHD Chính đã mở lại hồ sơ Tiền-Defense để chỉnh sửa (Đồ án #{projectId})",
                $"GVHD Chính trả lại hồ sơ Tiền-Defense: {request.Notes.Trim()}",
                "PROJECT", projectId, studentIds, Now, enqueueEmail: true, ct);

            var proj = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
            var coldPkgId = await db.Set<ColdPackage>().AsNoTracking().Where(c => c.ProjectId == projectId).Select(c => (long?)c.Id).SingleOrDefaultAsync(ct);
            return new PreDefenseLockStatusDto(projectId, proj.Status, draft.LockWorkflowStatus,
                draft.LockRequestedBy, draft.LockRequestedAt, null, null, null, null, null, coldPkgId,
                draft.ConcurrencyToken.ToString("N"), []);
        }

        return await CommitUnifiedFinalAndColdLockAsync(projectId, draft, lockedBySupervisorId: ActorId, forceLockedBy: null, forceReason: null, ct);
    }

    public async Task<PreDefenseLockStatusDto> ForceLockFinalAsync(long projectId, ForceLockFinalRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DomainException("Force-lock requires an explicit audit reason.", WorkflowErrorCodes.ReasonRequired);

        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (actor.IsAdmin && !actor.IsStaff)
            throw new ForbiddenException("System Administrators cannot force-lock academic submissions; only LeadScope Department Staff may force-lock.", WorkflowErrorCodes.AdminNotAcademicAuthority);
        if (!(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department staff can force-lock a Final Submission.", WorkflowErrorCodes.ScopeAccessDenied);

        var project = await db.Projects.Include(p => p.Team).SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        if (project.Status != "ACTIVE")
            throw new ConflictException("Project must be ACTIVE before force-locking final submission.", WorkflowErrorCodes.ProjectNotActive);

        var unpassedBlocking = await db.ProjectGateInstances.AsNoTracking()
            .AnyAsync(g => g.ProjectId == projectId && (g.Code == "G3" || g.Code == "R3" || g.BlocksDefense) && g.Status != "PASSED", ct);
        if (unpassedBlocking)
            throw new ConflictException("Gate 3 (and all defense-blocking gates) must be PASSED before force-locking Final submission.", WorkflowErrorCodes.Gate3NotPassed);

        var draft = await db.Set<FinalSubmissionDraft>().Include(d => d.Items)
            .SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        if (draft is not null)
            ValidateOptionalToken(draft.ConcurrencyToken, request.ConcurrencyToken);

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
        var ownsTx = db.Database.CurrentTransaction is null;
        await using var tx = ownsTx ? await db.Database.BeginTransactionAsync(ct) : null;

        await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (XLOCK, HOLDLOCK) WHERE id = {projectId}").ToListAsync(ct);

        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        if (project.Status != "ACTIVE")
            throw new ConflictException("Project must be ACTIVE to lock Final Submission.", WorkflowErrorCodes.ProjectNotActive);
        var now = Now;

        // Compute deterministic unified Cold + Final SHA-256 manifest
        var coldPkg = await db.Set<ColdPackage>().SingleOrDefaultAsync(c => c.ProjectId == projectId, ct);
        var coldFiles = coldPkg is null
            ? []
            : await db.Set<ColdSubmissionFile>().AsNoTracking()
                .Where(f => db.Set<ColdSubmission>().Any(s => s.Id == f.SubmissionId && db.Set<ColdChecklistItem>().Any(i => i.Id == s.ChecklistItemId && i.PackageId == coldPkg.Id)))
                .OrderBy(f => f.FileId).Select(f => $"{f.FileId}:{f.Sha256}").ToListAsync(ct);
        var draftVersionIds = draft.Items.OrderBy(i => i.DeliverableVersionId).Select(i => i.DeliverableVersionId).ToArray();
        var manifestInput = $"PROJECT:{projectId}|DRAFT:{string.Join(",", draftVersionIds)}|COLD:{string.Join(",", coldFiles)}";
        var manifestSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestInput))).ToLowerInvariant();

        // Freeze ColdPackage AND latest ColdSubmissions for all non-waived items
        if (coldPkg is not null && coldPkg.Status == "OPEN")
        {
            coldPkg.Status = "LOCKED";
            coldPkg.LockedBy = ActorId;
            coldPkg.LockedAt = now;
            coldPkg.ConcurrencyToken = Guid.NewGuid();

            var nonWaivedItemIds = await db.Set<ColdChecklistItem>().AsNoTracking()
                .Where(i => i.PackageId == coldPkg.Id && (i.WaivedStatus == null || i.WaivedStatus != "WAIVED_WITHDRAWN"))
                .Select(i => i.Id).ToArrayAsync(ct);
            if (nonWaivedItemIds.Length > 0)
            {
                var latestColdSubs = await db.Set<ColdSubmission>()
                    .Where(s => nonWaivedItemIds.Contains(s.ChecklistItemId)
                        && !db.Set<ColdSubmission>().Any(newer => newer.ChecklistItemId == s.ChecklistItemId && newer.Version > s.Version))
                    .ToListAsync(ct);
                foreach (var cs in latestColdSubs)
                {
                    if (cs.Status == "SUBMITTED")
                    {
                        cs.Status = "LOCKED";
                        cs.LockedAt = now;
                        cs.ConcurrencyToken = Guid.NewGuid();
                    }
                }
            }
        }

        // Build complete FinalSubmissionItem snapshot rows from draft.Items (unified manifest parity with legacy 1-step)
        var reqRecord = await db.Set<FinalSubmissionRequirements>().AsNoTracking()
            .Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.ProjectId == projectId, ct);
        var requiredDeliverableIds = reqRecord?.Items.Select(i => i.DeliverableId).ToHashSet() ?? new HashSet<long>();
        var snapshotItems = new List<FinalSubmissionItem>();
        if (draftVersionIds.Length > 0)
        {
            var versions = await db.DeliverableVersions.AsNoTracking()
                .Include(v => v.Deliverable)
                .Include(v => v.Files)
                .Where(v => draftVersionIds.Contains(v.Id) && v.Deliverable.ProjectId == projectId)
                .OrderBy(v => v.Id)
                .ToListAsync(ct);
            var uploaderIds = versions.SelectMany(v => v.Files).Select(f => f.UploadedBy).Distinct().ToArray();
            var uploaderNames = uploaderIds.Length == 0
                ? new Dictionary<long, string>()
                : await db.Users.AsNoTracking().Where(u => uploaderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

            foreach (var v in versions)
            {
                var snapFiles = v.Files.OrderBy(f => f.Id).Select(f => new AIPMS.Application.Features.FinalSubmissions.Models.FinalSnapshotFile(
                    f.ToDto(uploaderNames.GetValueOrDefault(f.UploadedBy)) with { CreatedAt = Utc(f.CreatedAt) },
                    f.StoragePath,
                    v.Deliverable.EvidenceScope,
                    v.Deliverable.TargetMajorId,
                    v.Deliverable.TargetStudentId)).ToArray();

                snapshotItems.Add(new FinalSubmissionItem
                {
                    DeliverableVersionId = v.Id,
                    DeliverableId = v.DeliverableId,
                    Title = v.Deliverable.Title,
                    VersionNumber = v.VersionNumber,
                    StatusAtSubmission = v.Status is "ACCEPTED" ? "ACCEPTED" : "SUBMITTED",
                    WasRequired = requiredDeliverableIds.Contains(v.DeliverableId),
                    FilesJson = JsonSerializer.Serialize(snapFiles)
                });
            }
        }

        // Create or update FinalSubmission snapshot
        var existingSub = await db.Set<FinalSubmission>().Include(s => s.Items).SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
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
                RequirementsConcurrencyToken = reqRecord?.ConcurrencyToken ?? Guid.NewGuid(),
                LockedBySupervisorId = lockedBySupervisorId,
                ForceLockedBy = forceLockedBy,
                ForceLockReason = forceReason,
                ColdManifestSha256 = manifestSha,
                Items = snapshotItems
            };
            db.Set<FinalSubmission>().Add(existingSub);
        }
        else
        {
            existingSub.LockedBySupervisorId = lockedBySupervisorId ?? existingSub.LockedBySupervisorId;
            existingSub.ForceLockedBy = forceLockedBy ?? existingSub.ForceLockedBy;
            existingSub.ForceLockReason = forceReason ?? existingSub.ForceLockReason;
            existingSub.ColdManifestSha256 = manifestSha;
            if (existingSub.Items.Count == 0 && snapshotItems.Count > 0)
            {
                foreach (var si in snapshotItems) existingSub.Items.Add(si);
            }
        }

        draft.LockWorkflowStatus = "LOCKED";
        draft.LockedBySupervisorId = lockedBySupervisorId;
        draft.ForceLockedBy = forceLockedBy;
        draft.ForceLockReason = forceReason;
        draft.UpdatedBy = ActorId;
        draft.UpdatedAt = now;
        draft.ConcurrencyToken = Guid.NewGuid();

        // Transition project to FINAL_SUBMISSION + record ProjectStatusHistory (independent of DefenseSession!)
        project.Status = "FINAL_SUBMISSION";
        project.UpdatedAt = now;
        db.ProjectStatusHistories.Add(new M.ProjectStatusHistory
        {
            ProjectId = projectId,
            OldStatus = "ACTIVE",
            NewStatus = "FINAL_SUBMISSION",
            ChangedBy = ActorId,
            ChangedAt = now,
            Reason = forceLockedBy.HasValue
                ? $"Final package force-locked by Lead Department: {forceReason}"
                : "Final package signed and locked by Primary Supervisor (2-step Pre-Defense lock)"
        });

        await db.SaveChangesAsync(ct);
        await PolicyVersions.CaptureAsync(db, draft.ProjectPeriodId, "FINAL_SUBMISSION", existingSub.Id, now, ct);

        await auditTrail.RecordAsync(new AuditEntry(ActorId,
            forceLockedBy.HasValue ? "FINAL_SUBMISSION_FORCE_LOCKED" : "FINAL_SUBMISSION_SUPERVISOR_LOCKED",
            "FINAL_SUBMISSION", existingSub.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["coldManifestSha256"] = manifestSha, ["itemCount"] = existingSub.Items.Count, ["forceReason"] = forceReason }), ct);

        var studentIds = await V4WorkflowNotificationHelper.GetActiveTeamStudentIdsAsync(db, projectId, ct);
        var lockScope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);
        var leadStaffIds = lockScope.LeadDepartmentId.HasValue
            ? await V4WorkflowNotificationHelper.GetDepartmentStaffIdsAsync(db, lockScope.LeadDepartmentId.Value, ct)
            : [];
        var primarySupIds = await V4WorkflowNotificationHelper.GetActiveSupervisorAndMentorIdsAsync(db, projectId, primaryOnly: true, ct);
        await V4WorkflowNotificationHelper.EmitAsync(
            db, ActorId, "FINAL_SUBMISSION_LOCKED",
            $"Hồ sơ bàn giao cuối kỳ đã được khóa (Đồ án #{projectId})",
            forceLockedBy.HasValue
                ? $"Khoa đã thực hiện khóa cưỡng chế gói bàn giao cuối kỳ & hồ sơ chấm nguội. Lý do: {forceReason}"
                : "GVHD Chính đã ký khóa gói bàn giao cuối kỳ & hồ sơ chấm nguội (Bước 2). Đồ án đã sẵn sàng chấm điểm.",
            "PROJECT", projectId, studentIds.Concat(leadStaffIds).Concat(primarySupIds), now, enqueueEmail: true, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        return new PreDefenseLockStatusDto(projectId, project.Status, draft.LockWorkflowStatus,
            draft.LockRequestedBy, draft.LockRequestedAt, draft.LockedBySupervisorId, draft.ForceLockedBy, draft.ForceLockReason,
            manifestSha, existingSub.Id, coldPkg?.Id, draft.ConcurrencyToken.ToString("N"), []);
    }

    public async Task<PreDefenseLockStatusDto> ReopenFinalLockAsync(long projectId, ReopenFinalLockRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10)
            throw new DomainException("Reopening a locked Final Submission requires an audit reason of at least 10 characters.", WorkflowErrorCodes.ReasonRequired);

        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (actor.IsAdmin && !actor.IsStaff)
            throw new ForbiddenException("System Administrators cannot reopen academic final submissions; only LeadScope Department Staff may reopen.", WorkflowErrorCodes.AdminNotAcademicAuthority);
        if (!(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department staff can reopen a locked Final Submission.", WorkflowErrorCodes.ScopeAccessDenied);

        var ownsTx = db.Database.CurrentTransaction is null;
        await using var tx = ownsTx ? await db.Database.BeginTransactionAsync(ct) : null;

        await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (XLOCK, HOLDLOCK) WHERE id = {projectId}").ToListAsync(ct);

        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        if (project.Status != "FINAL_SUBMISSION")
            throw new ConflictException("Only projects in FINAL_SUBMISSION status can reopen the final lock.");

        var finalSub = await db.Set<FinalSubmission>().Include(s => s.Items)
            .SingleOrDefaultAsync(s => s.ProjectId == projectId, ct)
            ?? throw new NotFoundException("FinalSubmission", projectId);

        var draft = await db.Set<FinalSubmissionDraft>().SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        if (draft is not null)
            ValidateOptionalToken(draft.ConcurrencyToken, request.ConcurrencyToken);

        // Guard: Block reopen if ANY evaluation, active assignment, or result already exists
        var hasEvalStarted = await db.Set<EvaluationAssignment>().AsNoTracking()
                .AnyAsync(a => a.ProjectId == projectId && a.Status != "REVOKED", ct)
            || await db.Evaluations.AsNoTracking().AnyAsync(e => e.ProjectId == projectId, ct)
            || await db.Set<ProjectResult>().AsNoTracking().AnyAsync(r => r.ProjectId == projectId, ct)
            || await db.Set<StudentResult>().AsNoTracking().AnyAsync(r => r.ProjectId == projectId, ct);
        if (hasEvalStarted)
            throw new ConflictException(
                "Cannot reopen Final Submission because evaluator assignments, evaluations, or results already exist for this project.",
                WorkflowErrorCodes.EvaluationAlreadyStarted);

        var now = Now;
        var reason = request.Reason.Trim();

        // Remove locked snapshot rows & reopen ColdPackage/ColdSubmissions + Draft + Project ACTIVE
        if (finalSub.Items.Count > 0)
            db.Set<FinalSubmissionItem>().RemoveRange(finalSub.Items);
        db.Set<FinalSubmission>().Remove(finalSub);

        var coldPkg = await db.Set<ColdPackage>().SingleOrDefaultAsync(c => c.ProjectId == projectId, ct);
        if (coldPkg is not null && coldPkg.Status == "LOCKED")
        {
            coldPkg.Status = "OPEN";
            coldPkg.LockedBy = null;
            coldPkg.LockedAt = null;
            coldPkg.ConcurrencyToken = Guid.NewGuid();

            var lockedSubs = await db.Set<ColdSubmission>()
                .Where(s => s.Status == "LOCKED" && db.Set<ColdChecklistItem>().Any(i => i.Id == s.ChecklistItemId && i.PackageId == coldPkg.Id))
                .ToListAsync(ct);
            foreach (var ls in lockedSubs)
            {
                ls.Status = "SUBMITTED";
                ls.LockedAt = null;
                ls.ConcurrencyToken = Guid.NewGuid();
            }
        }

        if (draft is not null)
        {
            draft.LockWorkflowStatus = "OPEN";
            draft.LockedBySupervisorId = null;
            draft.ForceLockedBy = null;
            draft.ForceLockReason = null;
            draft.UpdatedBy = ActorId;
            draft.UpdatedAt = now;
            draft.ConcurrencyToken = Guid.NewGuid();
        }

        project.Status = "ACTIVE";
        project.UpdatedAt = now;
        db.ProjectStatusHistories.Add(new M.ProjectStatusHistory
        {
            ProjectId = projectId,
            OldStatus = "FINAL_SUBMISSION",
            NewStatus = "ACTIVE",
            ChangedBy = ActorId,
            ChangedAt = now,
            Reason = $"Final submission reopened by Lead Department: {reason}"
        });

        await db.SaveChangesAsync(ct);
        await auditTrail.RecordAsync(new AuditEntry(ActorId, "FINAL_SUBMISSION_REOPENED", "PROJECT", projectId,
            new Dictionary<string, object?> { ["previousFinalSubmissionId"] = finalSub.Id, ["reason"] = reason }), ct);

        var studentIds = await V4WorkflowNotificationHelper.GetActiveTeamStudentIdsAsync(db, projectId, ct);
        var primarySupIds = await V4WorkflowNotificationHelper.GetActiveSupervisorAndMentorIdsAsync(db, projectId, primaryOnly: true, ct);
        await V4WorkflowNotificationHelper.EmitAsync(
            db, ActorId, "FINAL_SUBMISSION_REOPENED",
            $"Khoa đã mở lại hồ sơ bàn giao cuối kỳ (Đồ án #{projectId})",
            $"Khoa chủ trì đã mở khóa lại hồ sơ bàn giao cuối kỳ để bổ sung trước khi bắt đầu chấm điểm. Lý do: {reason}",
            "PROJECT", projectId, studentIds.Concat(primarySupIds), now, enqueueEmail: true, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        return new PreDefenseLockStatusDto(projectId, project.Status, "OPEN",
            draft?.LockRequestedBy, draft?.LockRequestedAt, null, null, null, null, null, coldPkg?.Id,
            draft?.ConcurrencyToken.ToString("N"), []);
    }
}
