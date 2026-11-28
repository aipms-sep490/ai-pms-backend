using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicBlueprints;
using AIPMS.Application.Features.AcademicBlueprints.Abstractions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Domain.Exceptions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class AcademicBlueprintService
{
    private async Task<(M.Project Project, ProjectAcademicScope Scope, AcademicBlueprint Blueprint, AcademicBlueprintConfig Config, long? ExecutionPeriodId, DateTime AnchorUtc)>
        ResolveProjectBlueprintContextAsync(long projectId, long? explicitBlueprintId, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.Team).SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);

        var trackCode = AcademicTrackCodes.Normalize(project.TrackCode);
        if (trackCode is null)
        {
            var teamConfig = await db.Set<TeamAcademicConfiguration>().AsNoTracking()
                .SingleOrDefaultAsync(c => c.TeamId == project.TeamId, ct);
            trackCode = AcademicTrackCodes.Normalize(teamConfig?.TrackCode)
                ?? (teamConfig?.ProjectMode == "INTERDISCIPLINARY" ? AcademicTrackCodes.ItMkt : AcademicTrackCodes.ItOnly);
        }

        AcademicBlueprint? bp;
        if (explicitBlueprintId.HasValue)
        {
            bp = await db.AcademicBlueprints.AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == explicitBlueprintId.Value && b.Status == "PUBLISHED", ct)
                ?? throw new ConflictException("Specified AcademicBlueprint must exist and be PUBLISHED.", WorkflowErrorCodes.BlueprintNotPublished);
        }
        else
        {
            var candidates = await db.AcademicBlueprints.AsNoTracking()
                .Where(b => b.AcademicSemesterId == project.Team.AcademicSemesterId && b.TrackCode == trackCode && b.Status == "PUBLISHED")
                .OrderByDescending(b => b.Version)
                .ToListAsync(ct);
            bp = (scope.LeadDepartmentId.HasValue
                    ? candidates.FirstOrDefault(b => b.LeadDepartmentId == scope.LeadDepartmentId.Value)
                    : null)
                ?? candidates.FirstOrDefault();
            if (bp is null)
                throw new ConflictException($"No PUBLISHED AcademicBlueprint found for track {trackCode} in semester {project.Team.AcademicSemesterId}.", WorkflowErrorCodes.BlueprintNotPublished);
        }

        var execPeriod = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == PeriodTypes.Execution && p.Status != SemesterStatuses.Archived)
            .OrderBy(p => p.StartAt).ThenBy(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var anchorUtc = execPeriod?.StartAt ?? project.ApprovedAt ?? clock.GetUtcNow().UtcDateTime;
        return (project, scope, bp, ParseConfig(bp.ConfigJson), execPeriod?.Id, DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc));
    }

    private DateTime ShiftWorkingDays(DateTime fromUtc, int days, long? executionPeriodId)
    {
        if (days <= 0) return fromUtc;
        var dto = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        return executionPeriodId.HasValue
            ? workingDays.AddWorkingDays(dto, days, executionPeriodId.Value).UtcDateTime
            : fromUtc.AddDays(days);
    }

    private static string TruncateReqCode(string code) => code.Length <= 50 ? code : code[..50];

    private static void PopulateGateRequirements(
        ProjectGateInstance gate,
        BlueprintGateRule gr,
        AcademicBlueprintConfig config,
        ReviewGateDefinition? matchedDef,
        IReadOnlyList<long> activeMemberIds)
    {
        var seenCodes = new HashSet<string>(gate.Requirements.Select(r => r.Code), StringComparer.OrdinalIgnoreCase);

        if (matchedDef is not null && matchedDef.Requirements.Count > 0)
        {
            foreach (var r in matchedDef.Requirements.OrderBy(x => x.Id))
            {
                if (string.Equals(r.TargetCardinality, "EACH_STUDENT", StringComparison.OrdinalIgnoreCase) && activeMemberIds.Count > 0)
                {
                    foreach (var memberId in activeMemberIds)
                    {
                        var code = TruncateReqCode($"{r.Code}-S{memberId}");
                        if (!seenCodes.Add(code)) continue;
                        gate.Requirements.Add(new ProjectGateRequirement
                        {
                            Code = code,
                            Title = r.Title,
                            Type = r.Type,
                            Scope = r.Scope,
                            MajorId = r.MajorId,
                            TargetStudentId = memberId,
                            Required = r.Required,
                            VerificationStatus = "PENDING",
                            TargetCardinality = r.TargetCardinality,
                            DeliveryStage = r.DeliveryStage,
                            FeedsCold = r.FeedsCold,
                            FeedsDefense = r.FeedsDefense,
                            InputKind = r.InputKind,
                            TemplateDocumentId = r.TemplateDocumentId,
                            CarryForwardFromCode = r.CarryForwardFromCode,
                            PolicyTag = r.PolicyTag,
                            RubricCriterionRef = r.RubricCriterionRef
                        });
                    }
                }
                else
                {
                    var code = TruncateReqCode(r.Code);
                    if (!seenCodes.Add(code)) continue;
                    gate.Requirements.Add(new ProjectGateRequirement
                    {
                        Code = code,
                        Title = r.Title,
                        Type = r.Type,
                        Scope = r.Scope,
                        MajorId = r.MajorId,
                        TargetStudentId = r.TargetStudentId,
                        Required = r.Required,
                        VerificationStatus = "PENDING",
                        TargetCardinality = r.TargetCardinality,
                        DeliveryStage = r.DeliveryStage,
                        FeedsCold = r.FeedsCold,
                        FeedsDefense = r.FeedsDefense,
                        InputKind = r.InputKind,
                        TemplateDocumentId = r.TemplateDocumentId,
                        CarryForwardFromCode = r.CarryForwardFromCode,
                        PolicyTag = r.PolicyTag,
                        RubricCriterionRef = r.RubricCriterionRef
                    });
                }
            }
            return;
        }

        var deliverables = gr.RequiredDeliverables ?? [];
        for (var dIdx = 0; dIdx < deliverables.Count; dIdx++)
        {
            var title = deliverables[dIdx]?.Trim();
            if (string.IsNullOrWhiteSpace(title)) continue;
            var code = TruncateReqCode($"{gr.Code}-DEL-{dIdx + 1}");
            if (!seenCodes.Add(code)) continue;
            gate.Requirements.Add(new ProjectGateRequirement
            {
                Code = code,
                Title = title.Length <= 255 ? title : title[..255],
                Type = "DOCUMENT",
                Scope = "COMMON",
                Required = true,
                VerificationStatus = "PENDING",
                TargetCardinality = "PROJECT"
            });
        }

        foreach (var intDel in config.IntegrationDeliverables ?? [])
        {
            if (string.IsNullOrWhiteSpace(intDel.Code) || intDel.Stages is null) continue;
            if (!intDel.Stages.Any(s => string.Equals(s, gr.Code, StringComparison.OrdinalIgnoreCase))) continue;
            var code = TruncateReqCode(intDel.Code.Trim().ToUpperInvariant());
            if (!seenCodes.Add(code)) continue;
            var title = string.IsNullOrWhiteSpace(intDel.Title) ? code : intDel.Title.Trim();
            gate.Requirements.Add(new ProjectGateRequirement
            {
                Code = code,
                Title = title.Length <= 255 ? title : title[..255],
                Type = "DOCUMENT",
                Scope = "COMMON",
                Required = true,
                VerificationStatus = "PENDING",
                TargetCardinality = "PROJECT",
                PolicyTag = "POL-05"
            });
        }
    }

    public async Task<BlueprintDryRunDiffDto> DryRunProjectPlanAsync(long projectId, long? blueprintId = null, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        var (_, scope, bp, config, execPeriodId, anchorUtc) = await ResolveProjectBlueprintContextAsync(projectId, blueprintId, ct);

        if (actor.IsAdmin)
        {
            // Admin may inspect read-only dry-run preview.
        }
        else if (actor.IsStaff)
        {
            if (!actor.DepartmentId.HasValue || !scope.DepartmentIds.Contains(actor.DepartmentId.Value))
                throw new ForbiddenException("Department staff can only preview blueprint plans for projects within their department scope.");
        }
        else
        {
            var isSupervisor = await db.SupervisorAssignments.AsNoTracking()
                .AnyAsync(sa => sa.ProjectId == projectId && sa.SupervisorProfile.UserId == ActorId && sa.EndedAt == null, ct);
            if (!isSupervisor) throw new ForbiddenException("Only department staff, admins, or assigned supervisors can preview blueprint plans.");
        }

        var alreadyApplied = await db.ProjectBlueprintSnapshots.AsNoTracking().AnyAsync(s => s.ProjectId == projectId, ct);

        var existingGates = await db.ProjectGateInstances.AsNoTracking().Where(g => g.ProjectId == projectId).ToDictionaryAsync(g => g.Code, ct);
        var existingSprints = await db.Set<Sprint>().AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(ct);
        var existingMilestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).ToListAsync(ct);
        var existingTasks = await db.Tasks.AsNoTracking().Where(t => t.Milestone.ProjectId == projectId).ToListAsync(ct);

        var preserved = new List<string>();
        var gateDiffs = new List<PlannedGateDiffItem>();
        foreach (var gr in (config.GateRules ?? []).OrderBy(g => g.Sequence))
        {
            var sub = ShiftWorkingDays(anchorUtc, gr.SubmissionOffsetWorkingDays, execPeriodId);
            var revEnd = ShiftWorkingDays(sub, gr.ReviewDurationWorkingDays, execPeriodId);
            var fixDue = ShiftWorkingDays(revEnd, gr.RevisionDurationWorkingDays, execPeriodId);
            var action = existingGates.ContainsKey(gr.Code) ? "UPDATE_DEADLINES" : "CREATE";
            gateDiffs.Add(new PlannedGateDiffItem(gr.Code, gr.Title, action, sub, revEnd, fixDue));
        }

        var sprintDiffs = new List<PlannedScheduleDiffItem>();
        foreach (var sr in config.SprintRules ?? [])
        {
            var start = ShiftWorkingDays(anchorUtc, sr.StartOffsetWorkingDays, execPeriodId);
            var end = ShiftWorkingDays(start, sr.DurationWorkingDays, execPeriodId);
            var match = existingSprints.FirstOrDefault(s => s.TemplateOriginId == sr.OriginId);
            if (match is { IsUserCustomized: true })
            {
                preserved.Add($"SPRINT:{sr.OriginId}:{match.Name}");
                sprintDiffs.Add(new PlannedScheduleDiffItem("SPRINT", sr.OriginId, match.Name, "PRESERVE_CUSTOMIZED", match.StartAt, match.EndAt));
            }
            else
            {
                sprintDiffs.Add(new PlannedScheduleDiffItem("SPRINT", sr.OriginId, sr.Name, match is null ? "CREATE" : "UPDATE", start, end));
            }
        }

        var msDiffs = new List<PlannedScheduleDiffItem>();
        var taskDiffs = new List<PlannedScheduleDiffItem>();
        foreach (var mr in (config.MilestoneRules ?? []).OrderBy(m => m.SortOrder))
        {
            var start = ShiftWorkingDays(anchorUtc, mr.StartOffsetWorkingDays, execPeriodId);
            var due = ShiftWorkingDays(anchorUtc, mr.DueOffsetWorkingDays, execPeriodId);
            var match = existingMilestones.FirstOrDefault(m => m.TemplateOriginId == mr.OriginId);
            if (match is { IsUserCustomized: true })
            {
                preserved.Add($"MILESTONE:{mr.OriginId}:{match.Title}");
                msDiffs.Add(new PlannedScheduleDiffItem("MILESTONE", mr.OriginId, match.Title, "PRESERVE_CUSTOMIZED",
                    match.StartDate?.ToDateTime(TimeOnly.MinValue), match.DueDate?.ToDateTime(TimeOnly.MinValue)));
            }
            else
            {
                msDiffs.Add(new PlannedScheduleDiffItem("MILESTONE", mr.OriginId, mr.Title, match is null ? "CREATE" : "UPDATE", start, due));
            }

            foreach (var tr in mr.CoreTasks ?? [])
            {
                var taskDue = tr.DueOffsetWorkingDays.HasValue ? ShiftWorkingDays(anchorUtc, tr.DueOffsetWorkingDays.Value, execPeriodId) : due;
                var existingTask = existingTasks.FirstOrDefault(t => t.TemplateOriginId == tr.OriginId);
                if (existingTask is { IsUserCustomized: true })
                {
                    preserved.Add($"TASK:{tr.OriginId}:{existingTask.Title}");
                    taskDiffs.Add(new PlannedScheduleDiffItem("TASK", tr.OriginId, existingTask.Title, "PRESERVE_CUSTOMIZED", existingTask.StartAt, existingTask.DueAt, existingTask.IsCoreGateTask));
                }
                else
                {
                    taskDiffs.Add(new PlannedScheduleDiffItem("TASK", tr.OriginId, tr.Title, existingTask is null ? "CREATE" : "UPDATE", start, taskDue, tr.IsCoreGateTask));
                }
            }
        }

        return new BlueprintDryRunDiffDto(projectId, bp.Id, bp.Version, bp.TrackCode, execPeriodId, anchorUtc, alreadyApplied,
            gateDiffs, sprintDiffs, msDiffs, taskDiffs, preserved);
    }

    public async Task<ProjectBlueprintSnapshotDto?> ApplyBlueprintToProjectAsync(long projectId, long actorUserId, DateTime activatedAtUtc, long? blueprintId = null, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        try
        {
            // Lock project row to serialize parallel activations / apply retries
            var locked = await db.Projects
                .FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}")
                .AsNoTracking().SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException("Project", projectId);

            var existingSnap = await db.ProjectBlueprintSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
            if (existingSnap is not null)
            {
                if (tx is not null) await tx.CommitAsync(ct);
                return new ProjectBlueprintSnapshotDto(existingSnap.Id, existingSnap.ProjectId, existingSnap.BlueprintId,
                    existingSnap.BlueprintVersion, existingSnap.AppliedBy, existingSnap.AppliedAt, 0, 0, 0, 0, true);
            }

            if (locked.Status != "ACTIVE")
                throw new ConflictException("Only ACTIVE projects can apply an AcademicBlueprint plan.", WorkflowErrorCodes.ProjectNotActive);

            // Try to resolve blueprint; if invoked automatically on activation and none configured, skip gracefully
            M.Project project;
            ProjectAcademicScope scope;
            AcademicBlueprint bp;
            AcademicBlueprintConfig config;
            long? execPeriodId;
            DateTime anchorUtc;
            try
            {
                (project, scope, bp, config, execPeriodId, anchorUtc) = await ResolveProjectBlueprintContextAsync(projectId, blueprintId, ct);
            }
            catch (ConflictException ex) when (blueprintId is null && ex.Code == WorkflowErrorCodes.BlueprintNotPublished)
            {
                if (tx is not null) await tx.CommitAsync(ct);
                return null;
            }

            if (currentUser.IsAuthenticated && currentUser.UserId == actorUserId
                && currentUser.Roles.Contains("DEPARTMENT_STAFF") && !currentUser.Roles.Contains("ADMIN"))
            {
                var actorScope = await GetActorScopeAsync(ct);
                if (!actorScope.DepartmentId.HasValue || !scope.DepartmentIds.Contains(actorScope.DepartmentId.Value))
                    throw new ForbiddenException("Department staff can only apply blueprints to projects within their department scope.");
            }

            int gatesCreated = 0, sprintsCreated = 0, milestonesCreated = 0, tasksCreated = 0;

            // Load linked ReviewGateTemplate (with gates & requirements) if specified on blueprint
            ReviewGateTemplate? linkedTemplate = null;
            if (bp.GateTemplateId.HasValue)
            {
                linkedTemplate = await db.ReviewGateTemplates.AsNoTracking()
                    .Include(t => t.Gates).ThenInclude(g => g.Requirements)
                    .SingleOrDefaultAsync(t => t.Id == bp.GateTemplateId.Value, ct);
            }

            // Ensure a backing ReviewGateTemplate exists if gate rules are provided and blueprint didn't link one
            long? gateTemplateId = linkedTemplate?.Id ?? bp.GateTemplateId;
            if ((config.GateRules?.Count ?? 0) > 0 && !gateTemplateId.HasValue)
            {
                var autoTplCode = $"BP-{bp.Code}-V{bp.Version}";
                var existingTpl = await db.ReviewGateTemplates.SingleOrDefaultAsync(t => t.Code == autoTplCode && t.Version == bp.Version, ct);
                if (existingTpl is null)
                {
                    existingTpl = new ReviewGateTemplate
                    {
                        Code = autoTplCode,
                        Name = $"{bp.Name} Gate Template",
                        Mode = bp.TrackCode == AcademicTrackCodes.ItMkt ? "INTERDISCIPLINARY" : "SINGLE_MAJOR",
                        Major = bp.TrackCode,
                        Status = "PUBLISHED",
                        Version = bp.Version,
                        CreatedBy = actorUserId,
                        CreatedAt = activatedAtUtc,
                        UpdatedAt = activatedAtUtc
                    };
                    db.ReviewGateTemplates.Add(existingTpl);
                    await db.SaveChangesAsync(ct);
                }
                gateTemplateId = existingTpl.Id;
            }

            var activeMemberIds = await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
                .OrderBy(m => m.UserId)
                .Select(m => m.UserId)
                .ToArrayAsync(ct);

            var existingGates = await db.ProjectGateInstances
                .Include(g => g.Requirements)
                .Where(g => g.ProjectId == projectId)
                .ToDictionaryAsync(g => g.Code, StringComparer.OrdinalIgnoreCase, ct);
            var orderedGates = (config.GateRules ?? []).OrderBy(g => g.Sequence).ToArray();
            var trackedGates = new List<ProjectGateInstance>(existingGates.Values);
            for (var i = 0; i < orderedGates.Length; i++)
            {
                var gr = orderedGates[i];
                var sub = ShiftWorkingDays(anchorUtc, gr.SubmissionOffsetWorkingDays, execPeriodId);
                var revEnd = ShiftWorkingDays(sub, gr.ReviewDurationWorkingDays, execPeriodId);
                var fixDue = ShiftWorkingDays(revEnd, gr.RevisionDurationWorkingDays, execPeriodId);
                var offsetJson = JsonSerializer.Serialize(new { gr.SubmissionOffsetWorkingDays, gr.ReviewDurationWorkingDays, gr.RevisionDurationWorkingDays }, JsonOpts);
                var matchedDef = linkedTemplate?.Gates.FirstOrDefault(g =>
                    string.Equals(g.Code, gr.Code, StringComparison.OrdinalIgnoreCase) || g.Sequence == gr.Sequence);

                if (existingGates.TryGetValue(gr.Code, out var existingGate))
                {
                    existingGate.SubmissionDueAt = sub;
                    existingGate.ReviewWindowEndAt = revEnd;
                    existingGate.RevisionDueAt = fixDue;
                    existingGate.WindowStart ??= anchorUtc;
                    existingGate.WindowEnd = fixDue;
                    existingGate.OffsetRuleJson = offsetJson;
                    if (existingGate.Requirements.Count == 0)
                        PopulateGateRequirements(existingGate, gr, config, matchedDef, activeMemberIds);
                }
                else if (gateTemplateId.HasValue)
                {
                    var blockNextGate = matchedDef?.BlockNextGate ?? true;
                    var openImmediately = i == 0 || !blockNextGate;
                    var newGate = new ProjectGateInstance
                    {
                        ProjectId = projectId,
                        TemplateId = gateTemplateId.Value,
                        TemplateVersion = linkedTemplate?.Version ?? bp.Version,
                        Code = gr.Code,
                        Sequence = gr.Sequence,
                        Title = gr.Title,
                        Objective = gr.Objective,
                        Status = openImmediately ? "PLANNED" : "LOCKED",
                        ReviewEligibility = openImmediately ? "READY" : "BLOCKED",
                        Required = matchedDef?.Required ?? true,
                        BlockNextGate = blockNextGate,
                        BlocksDefense = gr.BlocksDefense,
                        Weight = gr.Weight,
                        WindowStart = anchorUtc,
                        WindowEnd = fixDue,
                        SubmissionDueAt = sub,
                        ReviewWindowEndAt = revEnd,
                        RevisionDueAt = fixDue,
                        OffsetRuleJson = offsetJson,
                        CreatedAt = activatedAtUtc
                    };
                    PopulateGateRequirements(newGate, gr, config, matchedDef, activeMemberIds);
                    db.ProjectGateInstances.Add(newGate);
                    trackedGates.Add(newGate);
                    gatesCreated++;
                }
            }

            var existingSprints = await db.Set<Sprint>().Where(s => s.ProjectId == projectId).ToListAsync(ct);
            foreach (var sr in config.SprintRules ?? [])
            {
                var start = ShiftWorkingDays(anchorUtc, sr.StartOffsetWorkingDays, execPeriodId);
                var end = ShiftWorkingDays(start, sr.DurationWorkingDays, execPeriodId);
                var match = existingSprints.FirstOrDefault(s => s.TemplateOriginId == sr.OriginId);
                if (match is { IsUserCustomized: true }) continue;
                if (match is null)
                {
                    var sprint = new Sprint
                    {
                        ProjectId = projectId,
                        Name = sr.Name,
                        Status = "PLANNED",
                        StartAt = start,
                        EndAt = end,
                        TemplateOriginId = sr.OriginId,
                        IsUserCustomized = false,
                        CreatedBy = actorUserId,
                        CreatedAt = activatedAtUtc,
                        UpdatedAt = activatedAtUtc
                    };
                    db.Set<Sprint>().Add(sprint);
                    existingSprints.Add(sprint);
                    sprintsCreated++;
                }
                else
                {
                    match.Name = sr.Name;
                    match.StartAt = start;
                    match.EndAt = end;
                    match.UpdatedAt = activatedAtUtc;
                }
            }

            // Persist gates/requirements and sprints before creating core tasks so FK ids are assigned
            await db.SaveChangesAsync(ct);

            var gateFirstRequirementId = trackedGates
                .GroupBy(g => g.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    grp => grp.Key,
                    grp => grp.First().Requirements
                        .OrderByDescending(r => r.Required)
                        .ThenBy(r => r.Id)
                        .Select(r => (long?)r.Id)
                        .FirstOrDefault(),
                    StringComparer.OrdinalIgnoreCase);

            var existingMilestones = await db.Milestones.Where(m => m.ProjectId == projectId).ToListAsync(ct);
            var existingTasks = await db.Tasks.Where(t => t.Milestone.ProjectId == projectId).ToListAsync(ct);
            var newlyCreatedCoreTasks = new List<M.Task>();
            foreach (var mr in (config.MilestoneRules ?? []).OrderBy(m => m.SortOrder))
            {
                var start = ShiftWorkingDays(anchorUtc, mr.StartOffsetWorkingDays, execPeriodId);
                var due = ShiftWorkingDays(anchorUtc, mr.DueOffsetWorkingDays, execPeriodId);
                var ms = existingMilestones.FirstOrDefault(m => m.TemplateOriginId == mr.OriginId);
                if (ms is null)
                {
                    ms = new M.Milestone
                    {
                        ProjectId = projectId,
                        Title = mr.Title,
                        Description = mr.Description,
                        StartDate = DateOnly.FromDateTime(start),
                        DueDate = DateOnly.FromDateTime(due),
                        Status = "PLANNED",
                        SortOrder = mr.SortOrder,
                        TemplateOriginId = mr.OriginId,
                        IsUserCustomized = false,
                        CreatedBy = actorUserId,
                        CreatedAt = activatedAtUtc,
                        UpdatedAt = activatedAtUtc,
                        ConcurrencyToken = Guid.NewGuid()
                    };
                    db.Milestones.Add(ms);
                    await db.SaveChangesAsync(ct);
                    existingMilestones.Add(ms);
                    milestonesCreated++;
                }
                else if (!ms.IsUserCustomized)
                {
                    ms.Title = mr.Title;
                    ms.StartDate = DateOnly.FromDateTime(start);
                    ms.DueDate = DateOnly.FromDateTime(due);
                    ms.UpdatedAt = activatedAtUtc;
                }

                foreach (var tr in mr.CoreTasks ?? [])
                {
                    var taskDue = tr.DueOffsetWorkingDays.HasValue ? ShiftWorkingDays(anchorUtc, tr.DueOffsetWorkingDays.Value, execPeriodId) : due;
                    long? linkedGateReqId = !string.IsNullOrWhiteSpace(tr.GateCode)
                        && gateFirstRequirementId.TryGetValue(tr.GateCode.Trim(), out var reqId)
                        ? reqId
                        : null;
                    long? linkedSprintId = existingSprints
                        .Where(s => s.StartAt <= taskDue && taskDue <= s.EndAt)
                        .OrderBy(s => s.StartAt)
                        .Select(s => (long?)s.Id)
                        .FirstOrDefault();

                    var task = existingTasks.FirstOrDefault(t => t.TemplateOriginId == tr.OriginId);
                    if (task is { IsUserCustomized: true }) continue;
                    if (task is null)
                    {
                        var createdTask = new M.Task
                        {
                            MilestoneId = ms.Id,
                            SprintId = linkedSprintId,
                            GateRequirementId = linkedGateReqId,
                            Title = tr.Title,
                            Description = tr.Description,
                            Status = "TODO",
                            Priority = tr.Priority,
                            StartAt = start,
                            DueAt = taskDue,
                            TemplateOriginId = tr.OriginId,
                            IsUserCustomized = false,
                            IsCoreGateTask = tr.IsCoreGateTask,
                            CreatedBy = actorUserId,
                            CreatedAt = activatedAtUtc,
                            UpdatedAt = activatedAtUtc,
                            ConcurrencyToken = Guid.NewGuid()
                        };
                        db.Tasks.Add(createdTask);
                        newlyCreatedCoreTasks.Add(createdTask);
                        tasksCreated++;
                    }
                    else
                    {
                        task.Title = tr.Title;
                        task.DueAt = taskDue;
                        task.IsCoreGateTask = tr.IsCoreGateTask;
                        task.GateRequirementId ??= linkedGateReqId;
                        task.SprintId ??= linkedSprintId;
                        task.UpdatedAt = activatedAtUtc;
                    }
                }
            }

            if (newlyCreatedCoreTasks.Count > 0 && scope.MajorIds.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                foreach (var createdTask in newlyCreatedCoreTasks)
                {
                    for (var mIdx = 0; mIdx < scope.MajorIds.Count; mIdx++)
                    {
                        db.TaskDisciplines.Add(new M.TaskDiscipline
                        {
                            TaskId = createdTask.Id,
                            MajorId = scope.MajorIds[mIdx],
                            Role = mIdx == 0 ? "PRIMARY" : "SUPPORTING",
                            CreatedBy = actorUserId,
                            CreatedAt = activatedAtUtc
                        });
                    }
                }
            }

            var snap = new ProjectBlueprintSnapshot
            {
                ProjectId = projectId,
                BlueprintId = bp.Id,
                BlueprintVersion = bp.Version,
                SnapshotJson = bp.ConfigJson,
                AppliedBy = actorUserId,
                AppliedAt = activatedAtUtc
            };
            db.ProjectBlueprintSnapshots.Add(snap);
            await db.SaveChangesAsync(ct);

            await auditTrail.RecordAsync(new AuditEntry(actorUserId, "ACADEMIC_BLUEPRINT_APPLIED", "PROJECT", projectId,
                new Dictionary<string, object?> { ["blueprintId"] = bp.Id, ["version"] = bp.Version, ["gatesCreated"] = gatesCreated, ["sprintsCreated"] = sprintsCreated, ["milestonesCreated"] = milestonesCreated, ["tasksCreated"] = tasksCreated }), ct);

            if (tx is not null) await tx.CommitAsync(ct);
            return new ProjectBlueprintSnapshotDto(snap.Id, snap.ProjectId, snap.BlueprintId, snap.BlueprintVersion,
                snap.AppliedBy, snap.AppliedAt, gatesCreated, sprintsCreated, milestonesCreated, tasksCreated, false);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
