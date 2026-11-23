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
    private async Task<(M.Project Project, AcademicBlueprint Blueprint, AcademicBlueprintConfig Config, long? ExecutionPeriodId, DateTime AnchorUtc)>
        ResolveProjectBlueprintContextAsync(long projectId, long? explicitBlueprintId, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.Team).SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

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
            bp = await db.AcademicBlueprints.AsNoTracking()
                .Where(b => b.AcademicSemesterId == project.Team.AcademicSemesterId && b.TrackCode == trackCode && b.Status == "PUBLISHED")
                .OrderByDescending(b => b.Version)
                .FirstOrDefaultAsync(ct);
            if (bp is null)
                throw new ConflictException($"No PUBLISHED AcademicBlueprint found for track {trackCode} in semester {project.Team.AcademicSemesterId}.", WorkflowErrorCodes.BlueprintNotPublished);
        }

        var execPeriod = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == PeriodTypes.Execution && p.Status != SemesterStatuses.Archived)
            .OrderBy(p => p.StartAt).ThenBy(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var anchorUtc = execPeriod?.StartAt ?? project.ApprovedAt ?? clock.GetUtcNow().UtcDateTime;
        return (project, bp, ParseConfig(bp.ConfigJson), execPeriod?.Id, DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc));
    }

    private DateTime ShiftWorkingDays(DateTime fromUtc, int days, long? executionPeriodId)
    {
        if (days <= 0) return fromUtc;
        var dto = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        return executionPeriodId.HasValue
            ? workingDays.AddWorkingDays(dto, days, executionPeriodId.Value).UtcDateTime
            : fromUtc.AddDays(days);
    }

    public async Task<BlueprintDryRunDiffDto> DryRunProjectPlanAsync(long projectId, long? blueprintId = null, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff)
        {
            var isSupervisor = await db.SupervisorAssignments.AsNoTracking()
                .AnyAsync(sa => sa.ProjectId == projectId && sa.SupervisorProfile.UserId == ActorId && sa.EndedAt == null, ct);
            if (!isSupervisor) throw new ForbiddenException("Only department staff, admins, or assigned supervisors can preview blueprint plans.");
        }

        var (_, bp, config, execPeriodId, anchorUtc) = await ResolveProjectBlueprintContextAsync(projectId, blueprintId, ct);
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
            AcademicBlueprint? bp;
            AcademicBlueprintConfig config;
            long? execPeriodId;
            DateTime anchorUtc;
            try
            {
                (_, bp, config, execPeriodId, anchorUtc) = await ResolveProjectBlueprintContextAsync(projectId, blueprintId, ct);
            }
            catch (ConflictException ex) when (blueprintId is null && ex.Code == WorkflowErrorCodes.BlueprintNotPublished)
            {
                if (tx is not null) await tx.CommitAsync(ct);
                return null;
            }

            int gatesCreated = 0, sprintsCreated = 0, milestonesCreated = 0, tasksCreated = 0;

            // Ensure a backing ReviewGateTemplate exists if gate rules are provided and blueprint didn't link one
            long? gateTemplateId = bp.GateTemplateId;
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

            var existingGates = await db.ProjectGateInstances.Where(g => g.ProjectId == projectId).ToDictionaryAsync(g => g.Code, ct);
            var orderedGates = (config.GateRules ?? []).OrderBy(g => g.Sequence).ToArray();
            for (var i = 0; i < orderedGates.Length; i++)
            {
                var gr = orderedGates[i];
                var sub = ShiftWorkingDays(anchorUtc, gr.SubmissionOffsetWorkingDays, execPeriodId);
                var revEnd = ShiftWorkingDays(sub, gr.ReviewDurationWorkingDays, execPeriodId);
                var fixDue = ShiftWorkingDays(revEnd, gr.RevisionDurationWorkingDays, execPeriodId);
                var offsetJson = JsonSerializer.Serialize(new { gr.SubmissionOffsetWorkingDays, gr.ReviewDurationWorkingDays, gr.RevisionDurationWorkingDays }, JsonOpts);

                if (existingGates.TryGetValue(gr.Code, out var existingGate))
                {
                    existingGate.SubmissionDueAt = sub;
                    existingGate.ReviewWindowEndAt = revEnd;
                    existingGate.RevisionDueAt = fixDue;
                    existingGate.WindowStart ??= anchorUtc;
                    existingGate.WindowEnd = fixDue;
                    existingGate.OffsetRuleJson = offsetJson;
                }
                else if (gateTemplateId.HasValue)
                {
                    db.ProjectGateInstances.Add(new ProjectGateInstance
                    {
                        ProjectId = projectId,
                        TemplateId = gateTemplateId.Value,
                        TemplateVersion = bp.Version,
                        Code = gr.Code,
                        Sequence = gr.Sequence,
                        Title = gr.Title,
                        Objective = gr.Objective,
                        Status = "PLANNED",
                        ReviewEligibility = i == 0 ? "READY" : "BLOCKED",
                        Required = true,
                        BlockNextGate = true,
                        BlocksDefense = gr.BlocksDefense,
                        Weight = gr.Weight,
                        WindowStart = anchorUtc,
                        WindowEnd = fixDue,
                        SubmissionDueAt = sub,
                        ReviewWindowEndAt = revEnd,
                        RevisionDueAt = fixDue,
                        OffsetRuleJson = offsetJson,
                        CreatedAt = activatedAtUtc
                    });
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
                    db.Set<Sprint>().Add(new Sprint
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
                    });
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

            var existingMilestones = await db.Milestones.Where(m => m.ProjectId == projectId).ToListAsync(ct);
            var existingTasks = await db.Tasks.Where(t => t.Milestone.ProjectId == projectId).ToListAsync(ct);
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
                    var task = existingTasks.FirstOrDefault(t => t.TemplateOriginId == tr.OriginId);
                    if (task is { IsUserCustomized: true }) continue;
                    if (task is null)
                    {
                        db.Tasks.Add(new M.Task
                        {
                            MilestoneId = ms.Id,
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
                        });
                        tasksCreated++;
                    }
                    else
                    {
                        task.Title = tr.Title;
                        task.DueAt = taskDue;
                        task.IsCoreGateTask = tr.IsCoreGateTask;
                        task.UpdatedAt = activatedAtUtc;
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
