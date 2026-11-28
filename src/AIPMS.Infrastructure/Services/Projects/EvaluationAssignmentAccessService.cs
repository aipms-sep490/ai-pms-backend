using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class EvaluationAssignmentAccessService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IEvaluationDraftRepository evaluations, TimeProvider clock, IEvaluationSchemeService schemes) : IEvaluationAssignmentAccessService
{
    private sealed record InternalEvidenceItem(EvaluationAssignmentEvidenceItemDto Dto, string? StorageKey);

    private bool IsWithinValidity(EvaluationAssignment row)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return row.Status == "ACTIVE"
            && (!row.ValidFrom.HasValue || row.ValidFrom.Value <= now)
            && (!row.ValidUntil.HasValue || now <= row.ValidUntil.Value);
    }

    private async Task<(EvaluationAssignment Row, bool CanScore)> Load(long id, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var row = await db.Set<EvaluationAssignment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("EvaluationAssignment", id);
        var account = await db.Users.AsNoTracking().Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") }).SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException("An active account is required.");
        var academicActor = await evaluations.GetActorAsync(actor, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(row.ProjectId, ct) ?? throw new NotFoundException("Project", row.ProjectId);
        var manager = account.Admin || AcademicAuthority.ManagesComponent(academicActor, project, row.DepartmentId);
        var withinValidity = IsWithinValidity(row);
        if (!manager && row.EvaluatorId == actor && !withinValidity)
            throw new ForbiddenException("This evaluation assignment has been revoked or expired.", WorkflowErrorCodes.AssignmentRevoked);
        var evaluator = account.Lecturer && row.EvaluatorId == actor && withinValidity && account.DepartmentId == row.DepartmentId
            && academicActor.IsLecturer && project.ActiveScope
            && (row.EvaluationType != "SUPERVISOR" || await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == row.ProjectId && a.SupervisorProfile.UserId == actor && a.IsPrimary && a.EndedAt == null, ct));
        if (!manager && !evaluator) throw new ForbiddenException("The assignment is outside your scope.", WorkflowErrorCodes.ScopeAccessDenied);
        return (row, evaluator && project.Status == "FINAL_SUBMISSION" && row.Scope is "COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL");
    }

    private async Task ValidateEvidenceAssignment(EvaluationAssignment row, CancellationToken ct)
    {
        if (!IsWithinValidity(row))
            throw new ForbiddenException("Revoked or expired assignments cannot access evidence.", WorkflowErrorCodes.AssignmentRevoked);
        if (row.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) return;
        var component = await db.Set<EvaluationSchemeComponent>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == row.ComponentId, ct);
        var scheme = component is null ? null : await db.Set<EvaluationScheme>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == component.SchemeId, ct);
        if (component is null || scheme is null || scheme.Status != "PUBLISHED" || scheme.ProjectId != row.ProjectId
            || scheme.ProjectPeriodId != row.ProjectPeriodId || scheme.PolicyVersionId != row.PolicyVersionId
            || component.Stage != row.Stage || component.Scope != row.Scope || component.MajorId != row.MajorId || component.RubricId != row.RubricId
            || !await db.Rubrics.AnyAsync(r => r.Id == component.RubricId && r.DepartmentId == row.DepartmentId, ct)
            || row.Scope == "COMMON" && (row.MajorId is not null || row.StudentId is not null)
            || row.Scope == "MAJOR_SPECIFIC" && (row.MajorId is null || row.StudentId is not null)
            || row.Scope == "INDIVIDUAL" && (row.StudentId is null || row.MajorId is null
                || !(JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson) ?? [])
                    .Any(s => s.StudentId == row.StudentId && s.MajorId == row.MajorId && s.DepartmentId == row.DepartmentId)))
            throw new ForbiddenException("Assignment does not match its published component and frozen target.");
    }

    public async Task<EvaluationAssignmentDetailDto> GetAsync(long assignmentId, CancellationToken ct = default)
    {
        var (row, canScore) = await Load(assignmentId, ct);
        var dueAt = await db.ProjectPeriods.AsNoTracking().Where(p => p.Id == row.ProjectPeriodId).Select(p => (DateTime?)p.EndAt).SingleOrDefaultAsync(ct);

        var component = row.ComponentId.HasValue
            ? await db.Set<EvaluationSchemeComponent>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == row.ComponentId.Value, ct)
            : null;

        var scheme = component is not null
            ? await db.Set<EvaluationScheme>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == component.SchemeId, ct)
            : (row.PolicyVersionId.HasValue
                ? await db.Set<EvaluationScheme>().AsNoTracking().Where(s => s.ProjectId == row.ProjectId && s.ProjectPeriodId == row.ProjectPeriodId && s.Status == "PUBLISHED").FirstOrDefaultAsync(ct)
                : null);

        var policy = scheme?.GovernanceJson is not null
            ? JsonSerializer.Deserialize<SchemePolicyInput>(scheme.GovernanceJson)
            : null;

        var stage = row.Stage ?? component?.Stage;
        var componentStudentWeightPercent = component?.StudentWeightPercent;
        var schemeIsDemo = policy?.IsDemo ?? (scheme is not null ? true : (bool?)null);
        var schemeApprovalStatus = policy?.ApprovalStatus ?? (scheme is not null ? "DEMO" : null);

        var dto = new EvaluationAssignmentDto(row.Id, row.ProjectId, row.EvaluatorId, row.RubricId, row.ProjectPeriodId, row.DepartmentId,
            row.EvaluationType, row.Status, row.AssignedBy, row.AssignedAt, row.RevokedAt, row.ConcurrencyToken.ToString("N"), row.Scope, row.MajorId, row.StudentId, row.ComponentId, row.PolicyVersionId,
            stage, row.ColdPackageId, null, null, null, null, null,
            dueAt.HasValue ? DateTime.SpecifyKind(dueAt.Value, DateTimeKind.Utc) : null,
            componentStudentWeightPercent, schemeApprovalStatus, schemeIsDemo);

        dto = dto with {
            ProjectTitle = await db.Projects.Where(p => p.Id == row.ProjectId).Select(p => p.Title).SingleOrDefaultAsync(ct),
            ComponentName = component?.Name ?? await db.Set<EvaluationSchemeComponent>().Where(c => c.Id == row.ComponentId).Select(c => c.Name).SingleOrDefaultAsync(ct),
            RubricName = await db.Rubrics.Where(r => r.Id == row.RubricId).Select(r => r.Name).SingleOrDefaultAsync(ct),
            MajorName = await db.Majors.Where(m => m.Id == row.MajorId).Select(m => m.Name).SingleOrDefaultAsync(ct),
            StudentName = await db.Users.Where(u => u.Id == row.StudentId).Select(u => u.FullName).SingleOrDefaultAsync(ct)
        };

        var criteria = await db.RubricCriteria.AsNoTracking()
            .Include(c => c.Criterion)
            .Include(c => c.Levels)
            .Where(c => c.RubricId == row.RubricId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var rubricCriteria = criteria.Select(c => new RubricCriterionRecord(
            c.Id, c.CriterionId, c.Criterion.Name, c.Criterion.Description,
            c.WeightPercent, c.MaxScore, c.SortOrder, c.IsRequired)
        {
            ParentId = c.ParentId,
            CriterionCode = c.Criterion.Code,
            LearningOutcome = c.LearningOutcome,
            EvidenceGuidance = c.EvidenceGuidance,
            Levels = (c.Levels ?? [])
                .OrderBy(l => l.LevelOrder)
                .Select(l => new RubricCriterionLevelDto(l.LevelOrder, l.Label, l.Descriptor, l.ScoreFrom, l.ScoreTo))
                .ToArray()
        }).ToArray();

        var weighted = RubricHierarchy.WithEffectiveWeights(rubricCriteria);
        var parentIds = criteria.Where(c => c.ParentId.HasValue).Select(c => c.ParentId!.Value).ToHashSet();
        var leaves = weighted.Where(c => !parentIds.Contains(c.Id)).ToArray();

        var parentNames = criteria.ToDictionary(r => r.Id, r => r.Criterion.Name);
        var items = leaves.Select(c => new EvaluationAssignmentItemDto(
            c.Id,
            c.Name,
            c.Description,
            c.WeightPercent,
            c.EffectiveWeightPercent,
            c.MaxScore ?? 0m,
            c.SortOrder,
            c.IsRequired,
            c.CriterionCode,
            c.LearningOutcome,
            c.EvidenceGuidance,
            c.Levels,
            c.ParentId,
            c.ParentId.HasValue ? parentNames.GetValueOrDefault(c.ParentId.Value) : null
        )).ToArray();

        var legacy = row.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL");
        string? reason = legacy ? "LEGACY_SCOPE_UNKNOWN" : null;
        if (canScore && !legacy)
        {
            var period = await evaluations.GetPeriodAsync(row.ProjectPeriodId, clock.GetUtcNow().UtcDateTime, ct);
            if (period?.IsOpen != true) { canScore = false; reason = "EVALUATION_WINDOW_CLOSED"; }
            else
            {
                try { await schemes.EnsureWritableAsync((await evaluations.GetAssignmentAsync(row.Id, ct))!, ct); }
                catch (ConflictException) { canScore = false; reason = "EVALUATION_EVIDENCE_OR_SCHEME_CHANGED"; }
            }
        }
                int? requiredEvaluators = component?.RequiredEvaluators;
        int? completedEvaluators = null;
        if (component is not null)
        {
            completedEvaluators = await (
                from a in db.Set<EvaluationAssignment>().AsNoTracking()
                where a.ProjectId == row.ProjectId
                   && a.ComponentId == row.ComponentId
                   && a.Status == "ACTIVE"
                   && (row.StudentId == null || a.StudentId == row.StudentId)
                   && (row.MajorId == null || a.MajorId == row.MajorId)
                join state in db.Set<EvaluationDraftState>().AsNoTracking() on a.Id equals state.AssignmentId
                join final in db.Set<EvaluationFinalization>().AsNoTracking() on state.EvaluationId equals final.EvaluationId
                select a.Id
            ).Distinct().CountAsync(ct);
        }

        return new(dto, canScore && !legacy, legacy, reason, stage, componentStudentWeightPercent, schemeApprovalStatus, schemeIsDemo, items, completedEvaluators, requiredEvaluators);
    }

    private async Task<(FinalSubmission? Final, IReadOnlyList<InternalEvidenceItem> AllItems, IReadOnlyList<InternalEvidenceItem> ScopedItems)> ResolveEvidenceItemsAsync(EvaluationAssignment row, CancellationToken ct)
    {
        await ValidateEvidenceAssignment(row, ct);
        if (row.Stage == "COLD")
        {
            var bound = await ColdEvaluationBinding.Validate(db, row, ct);
            var submissions = await db.Set<ColdSubmission>().AsNoTracking().Include(s => s.Files)
                .Where(s => bound.SubmissionIds.Contains(s.Id)).OrderBy(s => s.Id).ToArrayAsync(ct);
            var items = await db.Set<ColdChecklistItem>().AsNoTracking().Where(i => i.PackageId == bound.PackageId).ToDictionaryAsync(i => i.Id, ct);
            var evidence = submissions.SelectMany(s => s.Files.OrderBy(f => f.FileId).Select(f => new InternalEvidenceItem(
                new(f.FileId, items[s.ChecklistItemId].Name, $"COLD version {s.Version} - LOCKED", "COLD_SUBMISSION", s.Id,
                    f.FileId, f.FileName, f.ContentType, f.SizeBytes, items[s.ChecklistItemId].MajorId, items[s.ChecklistItemId].StudentId,
                    s.SubmittedAt.HasValue ? DateTime.SpecifyKind(s.SubmittedAt.Value, DateTimeKind.Utc) : null,
                    $"/api/v1/evaluation-assignments/{row.Id}/evidence/files/{f.FileId}", null, s.Version, f.Sha256, s.Id), f.StorageKey))).ToArray();
            return (null, evidence, evidence);
        }
        var final = await db.Set<FinalSubmission>().AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.ProjectId == row.ProjectId)
            .SingleOrDefaultAsync(ct);

        if (final == null)
        {
            return (null, [], []);
        }

        if (!string.IsNullOrWhiteSpace(row.ScopeSnapshotJson))
        {
            using var binding = JsonDocument.Parse(row.ScopeSnapshotJson);
            if (binding.RootElement.TryGetProperty("FinalSubmissionId", out var pinned)
                && (!pinned.TryGetInt64(out var pinnedId) || pinnedId != final.Id))
                throw new ForbiddenException("The locked package differs from this assignment's package.");
        }

        // Targets and object keys come only from the locked snapshot.
        var rawItems = new List<InternalEvidenceItem>();

        foreach (var item in final.Items)
        {
            long? studentId = null;
            long? majorId = null;

            FinalSnapshotFile[]? snapshotFiles = null;
            if (!string.IsNullOrWhiteSpace(item.FilesJson))
            {
                try
                {
                    snapshotFiles = JsonSerializer.Deserialize<FinalSnapshotFile[]>(item.FilesJson);
                }
                catch (JsonException)
                {
                    snapshotFiles = null;
                }
            }

            if (snapshotFiles != null && snapshotFiles.Length > 0)
            {
                foreach (var f in snapshotFiles)
                {
                    if (f?.Metadata is null || f.Metadata.ParentType is not ("VERSION" or "DELIVERABLE_VERSION")
                        || f.Metadata.ParentId != item.DeliverableVersionId)
                        continue;
                    var dto = new EvaluationAssignmentEvidenceItemDto(
                        f.Metadata.Id,
                        item.Title,
                        $"Version {item.VersionNumber} - {item.StatusAtSubmission}",
                        "DELIVERABLE",
                        item.DeliverableId,
                        f.Metadata.Id,
                        f.Metadata.FileName,
                        f.Metadata.ContentType,
                        f.Metadata.SizeBytes,
                        f.EvidenceScope is "MAJOR_SPECIFIC" or "INDIVIDUAL" ? f.TargetMajorId : null,
                        f.EvidenceScope == "INDIVIDUAL" ? f.TargetStudentId : null,
                        f.Metadata.CreatedAt != default ? f.Metadata.CreatedAt : final.SubmittedAt,
                        $"/api/v1/evaluation-assignments/{row.Id}/evidence/files/{f.Metadata.Id}",
                        item.DeliverableVersionId, item.VersionNumber, f.Metadata.Sha256);

                    rawItems.Add(new(dto, f.StorageKey));
                }
            }
            else
            {
                var dto = new EvaluationAssignmentEvidenceItemDto(
                    item.DeliverableVersionId,
                    item.Title,
                    $"Version {item.VersionNumber} - {item.StatusAtSubmission}",
                    "DELIVERABLE",
                    item.DeliverableId,
                    null,
                    null,
                    null,
                    null,
                    majorId,
                    studentId,
                    final.SubmittedAt,
                    null, item.DeliverableVersionId, item.VersionNumber);

                rawItems.Add(new(dto, null));
            }
        }

        // Apply assignment scope filtering:
        // COMMON      — all items from locked frozen package.
        // UNKNOWN / legacy — fail closed (0 items).
        List<InternalEvidenceItem> scopedItems;
        switch (row.Scope)
        {
            case "COMMON":
                scopedItems = rawItems;
                break;
            case "MAJOR_SPECIFIC" when row.MajorId.HasValue && row.MajorId.Value > 0:
                scopedItems = rawItems.Where(i => i.Dto.MajorId == row.MajorId.Value).ToList();
                break;
            case "INDIVIDUAL" when row.StudentId.HasValue && row.StudentId.Value > 0:
                scopedItems = rawItems.Where(i => i.Dto.StudentId == row.StudentId.Value && i.Dto.MajorId == row.MajorId).ToList();
                break;
            default:
                scopedItems = [];
                break;
        }

        return (final, rawItems, scopedItems);
    }

    public Task<EvaluationAssignmentEvidenceDto> EvidenceAsync(long assignmentId, CancellationToken ct = default) => evaluations.InTransactionAsync(async () =>
    {
        var (row, _) = await Load(assignmentId, ct);
        var (final, _, scopedItems) = await ResolveEvidenceItemsAsync(row, ct);
        var dtos = scopedItems.Select(x => x.Dto).ToList();
        return new EvaluationAssignmentEvidenceDto(row.Id, row.ProjectId, row.Scope, row.MajorId, row.StudentId, final?.Id, final?.SubmittedAt, dtos.Count, true, dtos, row.Stage, row.ColdPackageId);
    }, ct);

    public Task<FileDownload> DownloadEvidenceFileAsync(long assignmentId, long fileId, CancellationToken ct = default) => evaluations.InTransactionAsync(async () =>
    {
        var (row, _) = await Load(assignmentId, ct);
        if (row.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")
            || (row.Scope == "MAJOR_SPECIFIC" && (!row.MajorId.HasValue || row.MajorId.Value <= 0))
            || (row.Scope == "INDIVIDUAL" && (!row.StudentId.HasValue || row.StudentId.Value <= 0)))
        {
            throw new ForbiddenException("Legacy or unknown evaluation assignment scope cannot access evidence files.");
        }

        var (_, allItems, scopedItems) = await ResolveEvidenceItemsAsync(row, ct);

        var scopedMatch = scopedItems.FirstOrDefault(i => i.Dto.FileId == fileId);
        if (scopedMatch == null)
        {
            // Check if file belongs to the locked final submission package at all
            var inAllProjectItems = allItems.Any(i => i.Dto.FileId == fileId);
            if (inAllProjectItems)
            {
                // File belongs to project locked submission but outside the scope of THIS assignment
                throw new ForbiddenException("The requested file is outside the scope of this evaluation assignment.", WorkflowErrorCodes.ScopeAccessDenied);
            }

            throw new NotFoundException("File", fileId);
        }

        var storageKey = scopedMatch.StorageKey;
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ConflictException("The locked file has no verifiable storage reference.");
        }

        try
        {
            var stream = await storage.OpenReadAsync(storageKey, ct);
            return new FileDownload(stream, scopedMatch.Dto.ContentType ?? "application/octet-stream", scopedMatch.Dto.FileName ?? $"evidence_{fileId}");
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new NotFoundException("File content", fileId);
        }
    }, ct);

    public async Task<ProjectScoringWorkbenchDto> GetWorkbenchAsync(long projectId, CancellationToken ct = default)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var account = await db.Users.AsNoTracking().Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
            }).SingleOrDefaultAsync(ct) ?? throw new ForbiddenException("An active academic account is required.");

        if (!account.Admin && !account.Staff && !account.Lecturer)
            throw new ForbiddenException("Students cannot access the evaluator scoring workbench.", WorkflowErrorCodes.Forbidden);

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Title, p.Status, p.TeamId })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project", projectId);

        var academicActor = await evaluations.GetActorAsync(actor, ct);
        var evalProject = await evaluations.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var isManager = account.Admin || (academicActor is not null && AcademicAuthority.HasScope(academicActor, evalProject));

        var allAssignments = await db.Set<EvaluationAssignment>().AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        var actorActiveAssignments = allAssignments
            .Where(a => a.EvaluatorId == actor && IsWithinValidity(a))
            .ToList();

        if (!isManager && actorActiveAssignments.Count == 0)
        {
            var hadRevoked = allAssignments.Any(a => a.EvaluatorId == actor);
            throw new ForbiddenException(
                hadRevoked ? "Your evaluation assignment on this project has been revoked or expired." : "You do not have an active evaluation assignment on this project.",
                hadRevoked ? WorkflowErrorCodes.AssignmentRevoked : WorkflowErrorCodes.ScopeAccessDenied);
        }

        var visibleAssignments = isManager ? allAssignments.Where(IsWithinValidity).ToList() : actorActiveAssignments;
        var visibleAssignmentIds = visibleAssignments.Select(a => a.Id).ToArray();
        var componentIds = visibleAssignments.Where(a => a.ComponentId.HasValue).Select(a => a.ComponentId!.Value).Distinct().ToArray();
        var componentNames = componentIds.Length == 0
            ? new Dictionary<long, string>()
            : await db.Set<EvaluationSchemeComponent>().AsNoTracking()
                .Where(c => componentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var draftStates = visibleAssignmentIds.Length == 0
            ? []
            : await db.Set<EvaluationDraftState>().AsNoTracking()
                .Where(s => visibleAssignmentIds.Contains(s.AssignmentId))
                .ToListAsync(ct);
        var stateByAssignment = draftStates.ToDictionary(s => s.AssignmentId);
        var evaluationIds = draftStates.Select(s => s.EvaluationId).Distinct().ToArray();

        var evalRows = evaluationIds.Length == 0
            ? []
            : await db.Evaluations.AsNoTracking()
                .Include(e => e.EvaluationDetails)
                .Where(e => evaluationIds.Contains(e.Id))
                .ToListAsync(ct);
        var evalById = evalRows.ToDictionary(e => e.Id);

        var rubricIds = visibleAssignments.Select(a => a.RubricId).Distinct().ToArray();
        var leafCountByRubric = rubricIds.Length == 0
            ? new Dictionary<long, int>()
            : (await db.RubricCriteria.AsNoTracking()
                .Where(c => rubricIds.Contains(c.RubricId))
                .Select(c => new { c.RubricId, c.Id, c.ParentId })
                .ToListAsync(ct))
                .GroupBy(c => c.RubricId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var parentIds = g.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).ToHashSet();
                        return g.Count(x => !parentIds.Contains(x.Id));
                    });

        WorkbenchAssignmentSummaryDto ToSummary(EvaluationAssignment a)
        {
            stateByAssignment.TryGetValue(a.Id, out var st);
            AIPMS.Infrastructure.Persistence.Generated.Models.Evaluation? ev = st is not null && evalById.TryGetValue(st.EvaluationId, out var found) ? found : null;
            var expectedLeaves = leafCountByRubric.GetValueOrDefault(a.RubricId, 0);
            var scoredCount = ev?.EvaluationDetails.Count ?? 0;
            var missing = ev is null ? expectedLeaves : Math.Max(0, expectedLeaves - scoredCount);
            return new WorkbenchAssignmentSummaryDto(
                a.Id,
                ev?.Id,
                a.Scope,
                a.Stage,
                a.ComponentId.HasValue ? componentNames.GetValueOrDefault(a.ComponentId.Value) : null,
                ev?.Status ?? "NOT_STARTED",
                ev?.TotalScore,
                missing,
                st?.ConcurrencyToken.ToString("N"));
        }

        // Load active roster + task contribution stats
        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
            .OrderByDescending(m => m.IsLeader).ThenBy(m => m.UserId)
            .Select(m => new
            {
                m.UserId,
                m.User.StudentCode,
                m.User.FullName,
                m.User.MajorId,
                MajorCode = m.User.Major != null ? m.User.Major.Code : null,
                MajorName = m.User.Major != null ? m.User.Major.Name : null,
                m.IsLeader
            })
            .ToListAsync(ct);

        var doneAssignmentsByUser = await db.TaskAssignees.AsNoTracking()
            .Where(ta => ta.Task.Milestone.ProjectId == projectId && ta.Task.Status == "DONE")
            .GroupBy(ta => ta.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        var totalDone = doneAssignmentsByUser.Values.Sum();

        // Read-only finalized 20% GVHD cumulative scores
        var processRows = await db.Set<GateIndividualProcessScore>().AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.Status == "FINALIZED")
            .ToListAsync(ct);

        var roster = members.Select(m =>
        {
            var userDone = doneAssignmentsByUser.GetValueOrDefault(m.UserId, 0);
            var contributionPct = totalDone > 0
                ? decimal.Round((decimal)userDone * 100m / totalDone, 1, MidpointRounding.AwayFromZero)
                : (members.Count > 0 ? decimal.Round(100m / members.Count, 1, MidpointRounding.AwayFromZero) : 0m);

            var userProcess = processRows.Where(r => r.StudentId == m.UserId && r.Score.HasValue).ToList();
            var totalWeight = userProcess.Sum(r => r.GateWeightPercent);
            decimal? cumulativeProcess = totalWeight > 0
                ? decimal.Round(userProcess.Sum(r => r.Score!.Value * r.GateWeightPercent) / totalWeight, 2, MidpointRounding.AwayFromZero)
                : null;
            var isComplete = totalWeight >= 99.99m;

            var studentAssignments = visibleAssignments
                .Where(a => (a.Scope == "INDIVIDUAL" && a.StudentId == m.UserId)
                    || (a.Scope == "MAJOR_SPECIFIC" && a.MajorId.HasValue && a.MajorId == m.MajorId))
                .Select(ToSummary)
                .ToArray();

            return new ScoringWorkbenchRosterMemberDto(
                m.UserId, m.StudentCode, m.FullName, m.MajorId, m.MajorCode, m.MajorName,
                m.IsLeader, contributionPct, cumulativeProcess, isComplete, studentAssignments);
        }).ToArray();

        var projectLevelAssignments = visibleAssignments
            .Where(a => a.Scope == "COMMON")
            .Select(ToSummary)
            .ToArray();

        return new ProjectScoringWorkbenchDto(
            project.Id, project.Title, project.Status, 4.00m, roster, projectLevelAssignments);
    }
}
