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

internal sealed class ProjectChecklistService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider clock) : IProjectChecklistService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId.HasValue
        ? currentUser.UserId.Value
        : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<(bool IsAdmin, bool IsStaff, bool IsLecturer, bool IsStudent, long? DepartmentId)> GetActorAsync(CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == ActorId && x.Status == "ACTIVE")
            .Select(x => new
            {
                x.DepartmentId,
                IsAdmin = x.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                IsStaff = x.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                IsLecturer = x.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"),
                IsStudent = x.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        return (u.IsAdmin, u.IsStaff, u.IsLecturer, u.IsStudent, u.DepartmentId);
    }

    private static IReadOnlyList<MasterChecklistItemDefinition> ParseItems(string json)
    {
        try { return JsonSerializer.Deserialize<List<MasterChecklistItemDefinition>>(json, JsonOpts) ?? []; }
        catch { return []; }
    }

    private static MasterChecklistTemplateDto ToTemplateDto(ReviewGateChecklistTemplate t) => new(
        t.Id, t.AcademicSemesterId, t.TrackCode, t.Code, t.Name, t.Version, t.Status, t.LeadDepartmentId,
        ParseItems(t.ItemsJson), t.ConcurrencyToken.ToString("N"), t.CreatedAt, t.PublishedAt);

    public static ChecklistNormalizedScoreDto ComputeNormalizedScore(IEnumerable<ProjectGateChecklistItem> items)
    {
        var list = items.ToList();
        var applicable = list.Where(i => i.IsApplicable).ToList();
        var scored = applicable.Where(i => i.ReviewerScore.HasValue).ToList();
        var totalWeight = applicable.Sum(i => i.Weight);
        var earned = scored.Sum(i => i.ReviewerScore!.Value * i.Weight);
        decimal? normalized = null;
        var band = "Unscored";
        if (totalWeight > 0m && scored.Count > 0)
        {
            normalized = Math.Round((earned / totalWeight) * 10m, 2, MidpointRounding.AwayFromZero);
            if (normalized > 10.00m) normalized = 10.00m;
            band = normalized.Value switch
            {
                >= 8.50m => "Excellent",
                >= 7.00m => "Good",
                >= 5.00m => "Acceptable",
                _ => "Fail"
            };
        }
        return new ChecklistNormalizedScoreDto(totalWeight, earned, normalized, band, applicable.Count, scored.Count, list.Count - applicable.Count);
    }

    private static string ComputeConsensus(string advisorRec, string reviewerRec)
    {
        if (advisorRec == "PENDING" || reviewerRec == "PENDING")
            return "PENDING_RECOMMENDATIONS";
        return string.Equals(advisorRec, reviewerRec, StringComparison.Ordinal) ? "AGREE" : "DISAGREE";
    }

    private static ProjectGateChecklistDto ToChecklistDto(
        ProjectGateChecklist c,
        string gateCode,
        IEnumerable<ProjectGateChecklistItem> visibleItems,
        bool maskEvaluations)
    {
        var itemsList = visibleItems.OrderBy(i => i.Id).ToList();
        var score = maskEvaluations ? null : ComputeNormalizedScore(itemsList);
        return new ProjectGateChecklistDto(
            c.Id, c.ProjectId, c.GateInstanceId, gateCode, c.TemplateId, c.Version, c.Status,
            maskEvaluations ? "PENDING" : c.AdvisorRecommendation,
            maskEvaluations ? "PENDING" : c.ReviewerRecommendation,
            maskEvaluations ? "PENDING_RECOMMENDATIONS" : c.Review3ConsensusStatus,
            c.PublishedBy, c.PublishedAt, c.ConcurrencyToken.ToString("N"),
            score,
            itemsList.Select(i => new ProjectChecklistItemDto(
                i.Id, i.ItemCode, i.Section, i.TrackScope, i.MajorId, i.ConditionTag, i.CoreQuestion,
                i.Priority, i.Weight, i.IsApplicable, i.NaReason,
                i.TailoredCheckpoints, i.TailoredEvidence, i.ReviewerFocusPrompt, i.SourceRef,
                maskEvaluations ? null : i.AdvisoryStatus,
                maskEvaluations ? null : i.AdvisoryNotes,
                maskEvaluations ? null : i.AdvisoryBy,
                maskEvaluations ? null : i.ReviewerScore,
                maskEvaluations ? null : i.ReviewerNotes,
                maskEvaluations ? null : i.ReviewerBy)).ToArray());
    }

    public async Task<IReadOnlyList<MasterChecklistTemplateDto>> ListTemplatesAsync(long? semesterId, string? trackCode, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();

        var q = db.ReviewGateChecklistTemplates.AsNoTracking().AsQueryable();
        if (semesterId.HasValue) q = q.Where(t => t.AcademicSemesterId == semesterId.Value);
        if (!string.IsNullOrWhiteSpace(trackCode)) q = q.Where(t => t.TrackCode == trackCode.Trim().ToUpperInvariant());
        if (!actor.IsAdmin && actor.DepartmentId.HasValue) q = q.Where(t => t.LeadDepartmentId == actor.DepartmentId.Value);
        return (await q.OrderByDescending(t => t.CreatedAt).ToListAsync(ct)).Select(ToTemplateDto).ToArray();
    }

    public async Task<MasterChecklistTemplateDto> CreateTemplateAsync(CreateMasterChecklistTemplateRequest request, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == request.LeadDepartmentId))
            throw new ForbiddenException("Only lead department staff or admin can author master checklist templates.");

        var track = AcademicTrackCodes.Normalize(request.TrackCode)
            ?? throw new DomainException("TrackCode must be IT_ONLY, MKT_ONLY, or IT_MKT.", "INVALID_TRACK_CODE");
        if (request.Items is null || request.Items.Count == 0)
            throw new DomainException("Master checklist template must contain at least one item.", "CHECKLIST_ITEMS_REQUIRED");
        if (request.Items.Select(i => i.ItemCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Items.Count)
            throw new DomainException("Checklist item codes must be unique within a template.", "DUPLICATE_ITEM_CODE");
        if (request.Items.Any(i => i.Weight <= 0m || i.Priority is not ("Mandatory" or "Recommended" or "Conditional")))
            throw new DomainException("Every checklist item must have Weight > 0 and valid Priority (Mandatory/Recommended/Conditional).", "INVALID_CHECKLIST_ITEM");

        var now = Now;
        var entity = new ReviewGateChecklistTemplate
        {
            AcademicSemesterId = request.AcademicSemesterId,
            TrackCode = track,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Version = 1,
            Status = "DRAFT",
            LeadDepartmentId = request.LeadDepartmentId,
            ItemsJson = JsonSerializer.Serialize(request.Items, JsonOpts),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = ActorId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ReviewGateChecklistTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToTemplateDto(entity);
    }

    public async Task<MasterChecklistTemplateDto> PublishTemplateAsync(long templateId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var tpl = await db.ReviewGateChecklistTemplates.SingleOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException("ReviewGateChecklistTemplate", templateId);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == tpl.LeadDepartmentId))
            throw new ForbiddenException();
        if (tpl.Status == "PUBLISHED") return ToTemplateDto(tpl);
        if (tpl.Status == "RETIRED")
            throw new ConflictException("Retired checklist template is immutable.", WorkflowErrorCodes.ImmutableVersion);

        var now = Now;
        tpl.Status = "PUBLISHED";
        tpl.PublishedAt = now;
        tpl.UpdatedAt = now;
        tpl.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToTemplateDto(tpl);
    }

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
