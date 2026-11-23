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
    public async Task<ProjectGateChecklistDto> CreateProjectChecklistFromTemplateAsync(long projectId, string gateCode, long templateId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department staff or admin can initialize a tailored project checklist.");

        var tpl = await db.ReviewGateChecklistTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException("ReviewGateChecklistTemplate", templateId);
        if (tpl.Status != "PUBLISHED")
            throw new ConflictException("Master checklist template must be PUBLISHED before instantiating for a project.", WorkflowErrorCodes.BlueprintNotPublished);

        var normalizedGate = gateCode.Trim().ToUpperInvariant();
        var gate = await db.ProjectGateInstances.AsNoTracking()
            .SingleOrDefaultAsync(g => g.ProjectId == projectId && g.Code == normalizedGate, ct)
            ?? throw new NotFoundException("ProjectGateInstance", normalizedGate);

        var maxVer = await db.ProjectGateChecklists.AsNoTracking()
            .Where(c => c.GateInstanceId == gate.Id)
            .MaxAsync(c => (int?)c.Version, ct) ?? 0;

        var now = Now;
        var checklist = new ProjectGateChecklist
        {
            ProjectId = projectId,
            GateInstanceId = gate.Id,
            TemplateId = tpl.Id,
            Version = maxVer + 1,
            Status = "DRAFT",
            AdvisorRecommendation = "PENDING",
            ReviewerRecommendation = "PENDING",
            Review3ConsensusStatus = "PENDING_RECOMMENDATIONS",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var def in ParseItems(tpl.ItemsJson))
        {
            checklist.Items.Add(new ProjectGateChecklistItem
            {
                ItemCode = def.ItemCode.Trim(),
                Section = def.Section.Trim(),
                TrackScope = def.TrackScope.Trim().ToUpperInvariant(),
                MajorId = def.MajorId,
                ConditionTag = string.IsNullOrWhiteSpace(def.ConditionTag) ? "NONE" : def.ConditionTag.Trim().ToUpperInvariant(),
                CoreQuestion = def.CoreQuestion.Trim(),
                Priority = def.Priority,
                Weight = def.Weight,
                IsApplicable = true
            });
        }
        db.ProjectGateChecklists.Add(checklist);
        await db.SaveChangesAsync(ct);
        return ToChecklistDto(checklist, gate.Code, checklist.Items, maskEvaluations: false);
    }

    public async Task<ProjectGateChecklistDto> GetLatestChecklistAsync(long projectId, string gateCode, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var normalizedGate = gateCode.Trim().ToUpperInvariant();
        var gate = await db.ProjectGateInstances.AsNoTracking()
            .SingleOrDefaultAsync(g => g.ProjectId == projectId && g.Code == normalizedGate, ct)
            ?? throw new NotFoundException("ProjectGateInstance", normalizedGate);

        var checklist = await db.ProjectGateChecklists.AsNoTracking().Include(c => c.Items)
            .Where(c => c.GateInstanceId == gate.Id)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectGateChecklist", gateCode);

        var isMember = await db.TeamMembers.AsNoTracking()
            .AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.UserId == ActorId && m.LeftAt == null, ct);
        if (isMember && !actor.IsAdmin && !actor.IsStaff)
        {
            if (checklist.Status != "PUBLISHED")
                throw new ForbiddenException("Students can only view PUBLISHED project checklists.");
            return ToChecklistDto(checklist, gate.Code, checklist.Items, maskEvaluations: true);
        }

        // Discipline mentor sees ONLY their majorScope + COMMON/INTEGRATION
        var supAssignments = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == ActorId)
            .Select(a => new { a.IsPrimary, a.AssignmentType, a.MajorId })
            .ToListAsync(ct);
        if (supAssignments.Count > 0 && !supAssignments.Any(a => a.IsPrimary) && !actor.IsAdmin && !actor.IsStaff)
        {
            var mentorMajors = supAssignments.Where(a => a.MajorId.HasValue).Select(a => a.MajorId!.Value).ToHashSet();
            var filtered = checklist.Items.Where(i =>
                i.TrackScope is "COMMON" or "INTEGRATION"
                || (i.MajorId.HasValue && mentorMajors.Contains(i.MajorId.Value))).ToList();
            return ToChecklistDto(checklist, gate.Code, filtered, maskEvaluations: false);
        }

        return ToChecklistDto(checklist, gate.Code, checklist.Items, maskEvaluations: false);
    }

    public async Task<ProjectGateChecklistDto> TailorChecklistAsync(long projectId, long checklistId, TailorProjectChecklistRequest request, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department head/staff or admin can tailor a project checklist.");

        var checklist = await db.ProjectGateChecklists.Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == checklistId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectGateChecklist", checklistId);
        if (checklist.Status == "PUBLISHED")
            throw new ConflictException("Published project checklists are immutable. Create a new version instead.", WorkflowErrorCodes.ImmutableVersion);
        if (!Guid.TryParse(request.ConcurrencyToken, out var token) || token != checklist.ConcurrencyToken)
            throw new ConflictException("Project checklist was modified concurrently.", WorkflowErrorCodes.StaleConcurrencyToken);

        var byCode = checklist.Items.ToDictionary(i => i.ItemCode, StringComparer.OrdinalIgnoreCase);
        foreach (var patch in request.Items ?? [])
        {
            if (!byCode.TryGetValue(patch.ItemCode.Trim(), out var item))
                throw new DomainException($"Checklist item '{patch.ItemCode}' does not exist in Master template.", "UNKNOWN_CHECKLIST_ITEM");

            // Immutable core rule: cannot modify original weight or mark Mandatory item N/A
            if (patch.Weight.HasValue && patch.Weight.Value != item.Weight)
                throw new DomainException($"Cannot alter core weight of checklist item '{item.ItemCode}'.", "IMMUTABLE_CORE_CHECKLIST_ITEM");
            if (!patch.IsApplicable)
            {
                if (!string.Equals(item.Priority, "Conditional", StringComparison.OrdinalIgnoreCase))
                    throw new DomainException($"Only Conditional checklist items can be marked N/A. '{item.ItemCode}' is {item.Priority}.", "MANDATORY_ITEM_CANNOT_BE_NA");
                if (string.IsNullOrWhiteSpace(patch.NaReason))
                    throw new DomainException($"N/A reason is required for conditional item '{item.ItemCode}'.", "NA_REASON_REQUIRED");
            }

            item.IsApplicable = patch.IsApplicable;
            item.NaReason = patch.IsApplicable ? null : patch.NaReason?.Trim();
            item.TailoredCheckpoints = patch.TailoredCheckpoints?.Trim();
            item.TailoredEvidence = patch.TailoredEvidence?.Trim();
            item.ReviewerFocusPrompt = patch.ReviewerFocusPrompt?.Trim();
            item.SourceRef = patch.SourceRef?.Trim();
        }

        checklist.UpdatedAt = Now;
        checklist.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);

        var gate = await db.ProjectGateInstances.AsNoTracking().SingleAsync(g => g.Id == checklist.GateInstanceId, ct);
        return ToChecklistDto(checklist, gate.Code, checklist.Items, maskEvaluations: false);
    }

    public async Task<ChecklistDiffPreviewDto> GetDiffPreviewAsync(long projectId, long checklistId, CancellationToken ct = default)
    {
        var checklist = await db.ProjectGateChecklists.AsNoTracking().Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == checklistId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectGateChecklist", checklistId);
        var entries = checklist.Items.OrderBy(i => i.Id).Select(i => new ChecklistDiffEntryDto(
            i.ItemCode, i.Section, i.Priority, i.IsApplicable, i.NaReason,
            !string.IsNullOrWhiteSpace(i.TailoredCheckpoints),
            !string.IsNullOrWhiteSpace(i.TailoredEvidence),
            !string.IsNullOrWhiteSpace(i.ReviewerFocusPrompt),
            i.CoreQuestion, i.TailoredCheckpoints, i.TailoredEvidence)).ToArray();
        return new ChecklistDiffPreviewDto(
            checklist.Id, checklist.Version, checklist.Status, entries.Length,
            entries.Count(e => e.IsApplicable),
            entries.Count(e => !e.IsApplicable),
            entries.Count(e => !e.IsApplicable || e.HasTailoredCheckpoints || e.HasTailoredEvidence || e.HasReviewerFocusPrompt),
            entries);
    }

    public async Task<ProjectGateChecklistDto> PublishChecklistAsync(long projectId, long checklistId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == scope.LeadDepartmentId))
            throw new ForbiddenException("Only LeadScope department head/staff or admin can publish a project checklist.");

        var checklist = await db.ProjectGateChecklists.Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == checklistId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectGateChecklist", checklistId);
        if (checklist.Status == "PUBLISHED")
        {
            var g = await db.ProjectGateInstances.AsNoTracking().SingleAsync(x => x.Id == checklist.GateInstanceId, ct);
            return ToChecklistDto(checklist, g.Code, checklist.Items, maskEvaluations: false);
        }

        var now = Now;
        checklist.Status = "PUBLISHED";
        checklist.PublishedBy = ActorId;
        checklist.PublishedAt = now;
        checklist.UpdatedAt = now;
        checklist.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);

        var studentIds = await V4WorkflowNotificationHelper.GetActiveTeamStudentIdsAsync(db, projectId, ct);
        var supervisorAndMentorIds = await V4WorkflowNotificationHelper.GetActiveSupervisorAndMentorIdsAsync(db, projectId, primaryOnly: false, ct);
        var reviewerIds = await db.GateReviewerAssignments.AsNoTracking()
            .Where(a => a.GateInstanceId == checklist.GateInstanceId && a.Status != "REVOKED")
            .Select(a => a.ReviewerId)
            .Distinct()
            .ToArrayAsync(ct);
        var recipients = studentIds.Concat(supervisorAndMentorIds).Concat(reviewerIds).Distinct();
        await V4WorkflowNotificationHelper.EmitAsync(
            db, ActorId, "CHECKLIST_PUBLISHED",
            $"Review Checklist cá nhân hóa đã được phát hành (Đồ án #{projectId})",
            "Khoa đã phát hành phiên bản Review Checklist chính thức cho nhóm, GVHD, Mentor phụ và Phản biện Gate.",
            "PROJECT_GATE", checklist.GateInstanceId, recipients, now, enqueueEmail: false, ct);

        await auditTrail.RecordAsync(new AuditEntry(ActorId, "PROJECT_CHECKLIST_PUBLISHED", "PROJECT_GATE_CHECKLIST", checklist.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["gateInstanceId"] = checklist.GateInstanceId, ["version"] = checklist.Version }), ct);

        var gate = await db.ProjectGateInstances.AsNoTracking().SingleAsync(g => g.Id == checklist.GateInstanceId, ct);
        return ToChecklistDto(checklist, gate.Code, checklist.Items, maskEvaluations: false);
    }

    public async Task<ProjectGateChecklistDto> SubmitAdvisoryEvaluationAsync(long projectId, long checklistId, SubmitAdvisoryEvaluationRequest request, CancellationToken ct = default)
    {
        var checklist = await db.ProjectGateChecklists.Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == checklistId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectGateChecklist", checklistId);
        if (checklist.Status != "PUBLISHED")
            throw new ConflictException("Checklist must be PUBLISHED before recording advisory feedback.");

        var assignments = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == ActorId)
            .Select(a => new { a.IsPrimary, a.AssignmentType, a.MajorId })
            .ToListAsync(ct);
        if (assignments.Count == 0)
            throw new ForbiddenException("Only assigned primary supervisors or discipline mentors can submit advisory checklist feedback.");

        var isPrimary = assignments.Any(a => a.IsPrimary && a.AssignmentType == "PRIMARY");
        var mentorMajors = assignments.Where(a => a.MajorId.HasValue).Select(a => a.MajorId!.Value).ToHashSet();

        var byCode = checklist.Items.ToDictionary(i => i.ItemCode, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in request.Items ?? [])
        {
            if (!byCode.TryGetValue(entry.ItemCode.Trim(), out var item))
                throw new DomainException($"Unknown checklist item '{entry.ItemCode}'.", "UNKNOWN_CHECKLIST_ITEM");

            if (!isPrimary)
            {
                var allowed = item.TrackScope is "COMMON" or "INTEGRATION"
                    || (item.MajorId.HasValue && mentorMajors.Contains(item.MajorId.Value));
                if (!allowed)
                    throw new ForbiddenException($"Discipline mentor cannot access or advise checklist item '{item.ItemCode}' outside their assigned majorScope.");
            }
            item.AdvisoryStatus = entry.Status.Trim().ToUpperInvariant();
            item.AdvisoryNotes = entry.Notes?.Trim();
            item.AdvisoryBy = ActorId;
        }

        if (!string.IsNullOrWhiteSpace(request.AdvisorRecommendation))
        {
            if (!isPrimary)
                throw new ForbiddenException("Only the primary supervisor can record the advisor defense recommendation.");
            var rec = request.AdvisorRecommendation.Trim().ToUpperInvariant();
            if (rec is not ("PENDING" or "RECOMMEND_DEFENSE" or "NOT_RECOMMENDED"))
                throw new DomainException("Invalid advisor recommendation.", "INVALID_RECOMMENDATION");
            checklist.AdvisorRecommendation = rec;
            checklist.Review3ConsensusStatus = ComputeConsensus(checklist.AdvisorRecommendation, checklist.ReviewerRecommendation);
        }

        checklist.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        var gate = await db.ProjectGateInstances.AsNoTracking().SingleAsync(g => g.Id == checklist.GateInstanceId, ct);
        return ToChecklistDto(checklist, gate.Code, checklist.Items, maskEvaluations: false);
    }

    public async Task<ProjectGateChecklistDto> SubmitReviewerEvaluationAsync(long projectId, long checklistId, SubmitReviewerEvaluationRequest request, CancellationToken ct = default)
    {
        var checklist = await db.ProjectGateChecklists.Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.Id == checklistId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectGateChecklist", checklistId);
        if (checklist.Status != "PUBLISHED")
            throw new ConflictException("Checklist must be PUBLISHED before independent review scoring.");

        // Enforce D17 COI & mentor column separation
        var supervisorLinks = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.SupervisorProfile.UserId == ActorId)
            .Select(a => new { a.IsPrimary, a.AssignmentType, a.EndedAt })
            .ToListAsync(ct);
        if (supervisorLinks.Any(a => a.IsPrimary && a.AssignmentType == "PRIMARY"))
            throw new ConflictException("Primary supervisor (active or historical) cannot score independent GATE_REVIEWER checklist column (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);
        if (supervisorLinks.Any(a => a.EndedAt == null))
            throw new ForbiddenException("Discipline mentors cannot write to the independent reviewer checklist column.");

        var now = Now;
        var hasReviewerAssignment = await db.GateReviewerAssignments.AsNoTracking()
            .AnyAsync(a => a.GateInstanceId == checklist.GateInstanceId && a.ReviewerId == ActorId
                && a.Status != "REVOKED"
                && (a.ValidFrom == null || a.ValidFrom <= now)
                && (a.ValidUntil == null || now <= a.ValidUntil), ct);
        if (!hasReviewerAssignment)
            throw new ForbiddenException("An active GATE_REVIEWER assignment on this gate is required to score the independent checklist column.");

        var byCode = checklist.Items.ToDictionary(i => i.ItemCode, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in request.Items ?? [])
        {
            if (!byCode.TryGetValue(entry.ItemCode.Trim(), out var item))
                throw new DomainException($"Unknown checklist item '{entry.ItemCode}'.", "UNKNOWN_CHECKLIST_ITEM");
            if (!item.IsApplicable)
                throw new DomainException($"Item '{item.ItemCode}' is marked N/A and excluded from scoring.", "ITEM_NOT_APPLICABLE");
            if (entry.Score is not (0.0m or 0.5m or 1.0m))
                throw new DomainException("Reviewer score must be 1.0 (Met), 0.5 (Partial), or 0.0 (Not Met).", "INVALID_CHECKLIST_SCORE");
            item.ReviewerScore = entry.Score;
            item.ReviewerNotes = entry.Notes?.Trim();
            item.ReviewerBy = ActorId;
        }

        if (!string.IsNullOrWhiteSpace(request.ReviewerRecommendation))
        {
            var rec = request.ReviewerRecommendation.Trim().ToUpperInvariant();
            if (rec is not ("PENDING" or "RECOMMEND_DEFENSE" or "NOT_RECOMMENDED"))
                throw new DomainException("Invalid reviewer recommendation.", "INVALID_RECOMMENDATION");
            checklist.ReviewerRecommendation = rec;
            checklist.Review3ConsensusStatus = ComputeConsensus(checklist.AdvisorRecommendation, checklist.ReviewerRecommendation);
        }

        checklist.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        var gate = await db.ProjectGateInstances.AsNoTracking().SingleAsync(g => g.Id == checklist.GateInstanceId, ct);
        return ToChecklistDto(checklist, gate.Code, checklist.Items, maskEvaluations: false);
    }
}
