using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.Results.Abstractions;
using MediatR;
using AIPMS.Application.Features.Notifications.Events;

namespace AIPMS.Application.Features.Evaluations.Services;

public sealed partial class EvaluationDraftWorkflow(IEvaluationDraftRepository repository, IRubricRepository rubrics,
    ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock, IFinalSubmissionRepository submissions, IPublisher publisher,
    IProjectResultRepository results, IEvaluationSchemeService schemes)
{
    private async Task<EvaluationActor> Actor(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        return await repository.GetActorAsync(currentUser.UserId.Value, ct)
            ?? throw new ForbiddenException("An active academic account is required.");
    }

    private async Task<EvaluationProject> Project(long id, CancellationToken ct) =>
        await repository.GetProjectAsync(id, ct) ?? throw new NotFoundException("Project", id);

    private static bool Manages(EvaluationActor actor, EvaluationProject project, long? department = null) =>
        department.HasValue ? AcademicAuthority.ManagesComponent(actor, project, department.Value)
            : AcademicAuthority.HasScope(actor, project);

    private static void Current(string expected, string supplied)
    {
        if (!Guid.TryParse(supplied, out var token) || !Guid.TryParse(expected, out var current) || token != current)
            throw new ConflictException("The evaluation or assignment has changed. Reload before retrying.", WorkflowErrorCodes.StaleConcurrencyToken);
    }

    private async Task<EvaluationAssignmentRecord> Assignment(long id, CancellationToken ct) =>
        await repository.GetAssignmentAsync(id, ct) ?? throw new NotFoundException("EvaluationAssignment", id);

    private async Task<EvaluationDraftRecord> Draft(long id, CancellationToken ct) =>
        await repository.GetDraftAsync(id, ct) ?? throw new NotFoundException("Evaluation", id);

    private async Task Eligible(EvaluationActor actor, EvaluationAssignmentRecord assignment, EvaluationProject project, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var isRevokedOrExpired = assignment.Status != "ACTIVE"
            || (assignment.ValidFrom.HasValue && now < assignment.ValidFrom.Value)
            || (assignment.ValidUntil.HasValue && now > assignment.ValidUntil.Value);
        if (actor.Id == assignment.EvaluatorId && isRevokedOrExpired)
            throw new ForbiddenException("This evaluation assignment has been revoked or expired.", WorkflowErrorCodes.AssignmentRevoked);
        if (!actor.IsLecturer || actor.Id != assignment.EvaluatorId || actor.DepartmentId != assignment.DepartmentId
            || !project.ActiveScope || !project.DepartmentIds.Contains(assignment.DepartmentId)
            || isRevokedOrExpired)
            throw new ForbiddenException("Only the currently assigned evaluator can access this draft.", WorkflowErrorCodes.ScopeAccessDenied);
        if (assignment.EvaluationType == "SUPERVISOR"
            && !await repository.IsCurrentSupervisorAsync(project.Id, actor.Id, ct))
            throw new ForbiddenException("The supervisor assignment is no longer active.", WorkflowErrorCodes.AssignmentRevoked);
    }

    private async Task<EvaluationPeriod> Window(EvaluationProject project, long periodId, CancellationToken ct)
    {
        if (project.Status != "FINAL_SUBMISSION" || !project.ActiveScope)
            throw new ConflictException("Draft evaluation requires a project in FINAL_SUBMISSION with active academic scope.", WorkflowErrorCodes.FinalPackageRequired);
        if (!await repository.HasLockedSubmissionAsync(project.Id, ct))
            throw new ConflictException("A locked final-submission package is required before assignment or draft scoring.", WorkflowErrorCodes.FinalPackageRequired);
        var period = await repository.GetPeriodAsync(periodId, clock.GetUtcNow().UtcDateTime, ct);
        if (period is null || period.SemesterId != project.SemesterId || !period.IsOpen)
            throw new ConflictException("A single active evaluation window in the project's semester is required.", WorkflowErrorCodes.EvaluationWindowClosed);
        return period;
    }

    private async Task<RubricRecord> Rubric(long id, EvaluationProject project, long department, bool newAssignment, CancellationToken ct, string? assignmentScope = null)
    {
        var rubric = await rubrics.GetAsync(id, new RubricActor(0, true, null), true, ct)
            ?? throw new ConflictException("The assigned rubric no longer exists.", WorkflowErrorCodes.PublishedRubricRequired);
        var departmentAllowed = string.Equals(assignmentScope, "COMMON", StringComparison.OrdinalIgnoreCase)
            ? rubric.DepartmentId.HasValue && project.DepartmentIds.Contains(rubric.DepartmentId.Value)
            : rubric.DepartmentId == department;
        if (!departmentAllowed || rubric.AcademicSemesterId != project.SemesterId
            || (newAssignment ? rubric.Status != "PUBLISHED" : rubric.Status is not ("PUBLISHED" or "RETIRED")))
            throw new ConflictException("A protected rubric in the project's department and semester is required.", WorkflowErrorCodes.PublishedRubricRequired);
        RubricRules.EnsurePublishable(rubric.Criteria);
        return rubric;
    }

    private Task Audit(long actor, string action, string entity, long id, Dictionary<string, object?> details, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(actor, action, entity, id, details), ct);

    public Task<PagedResult<EligibleEvaluatorDto>> Candidates(long projectId, long periodId, int page, int pageSize, CancellationToken ct,
        long? componentId = null, string? scope = null, long? majorId = null, long? studentId = null) => repository.InTransactionAsync(async () =>
    {
        if (componentId.HasValue)
            return await schemes.CandidatesAsync(projectId, periodId, componentId.Value, scope, majorId, studentId, page, pageSize, ct);
        var actor = await Actor(ct);
        var project = await Project(projectId, ct);
        if (!Manages(actor, project)) throw new ForbiddenException();
        var period = await Window(project, periodId, ct);
        if (period.RubricId is not long rubricId)
            throw new ConflictException("The evaluation period has no configured rubric.", WorkflowErrorCodes.PublishedRubricRequired);
        var rubric = await rubrics.GetAsync(rubricId, new RubricActor(0, true, null), true, ct);
        if (rubric?.DepartmentId is not long department || !project.DepartmentIds.Contains(department))
            throw new ConflictException("A published rubric in the project scope is required.", WorkflowErrorCodes.PublishedRubricRequired);
        if (!Manages(actor, project, department)) throw new ForbiddenException();
        await Rubric(rubricId, project, department, true, ct);
        return await repository.GetEligibleEvaluatorsAsync(projectId, department, page, pageSize, ct);
    }, ct);

    public Task<EvaluationAssignmentDto> Assign(long projectId, AssignEvaluatorRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var project = await Project(projectId, ct);
        if (!Manages(actor, project)) throw new ForbiddenException();
        await repository.LockProjectAsync(projectId, ct);
        project = await Project(projectId, ct);
        if (input.EvaluationType == "SUPERVISOR")
            throw new ConflictException("New SUPERVISOR evaluation assignments are retired; existing assignments may complete under legacy rules.", "SUPERVISOR_EVALUATION_TYPE_RETIRED");
        if (await repository.HasBeenPrimarySupervisorAsync(projectId, input.EvaluatorId, ct))
            throw new ConflictException("A current or former primary supervisor or discipline mentor cannot evaluate the same project (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);
        var period = await Window(project, input.ProjectPeriodId, ct);
        var evaluator = await repository.GetActorAsync(input.EvaluatorId, ct);
        if (evaluator is null || !evaluator.IsLecturer || evaluator.DepartmentId is null
            || !project.DepartmentIds.Contains(evaluator.DepartmentId.Value)
            || !Manages(actor, project, evaluator.DepartmentId))
            throw new ConflictException("The evaluator must be an active lecturer in the managed project department.", WorkflowErrorCodes.EvaluatorIneligible);
        if (input.ComponentId is null && period.RubricId is null)
            throw new ConflictException("The evaluation period has no configured rubric.", WorkflowErrorCodes.PublishedRubricRequired);
        if (input.ComponentId is null && period.RubricId.HasValue)
            await Rubric(period.RubricId.Value, project, evaluator.DepartmentId.Value, true, ct);
        var scope = await schemes.ResolveAssignmentAsync(projectId, input, ct);
        var rubricId = scope.RubricId;
        await Rubric(rubricId, project, evaluator.DepartmentId.Value, true, ct, scope.Scope);
        var assigned = await repository.AssignScopedAsync(projectId, evaluator.Id, period.Id,
            evaluator.DepartmentId.Value, input.EvaluationType, actor.Id, clock.GetUtcNow().UtcDateTime, scope, ct);
        await Audit(actor.Id, "EVALUATOR_ASSIGNED", "EVALUATION_ASSIGNMENT", assigned.Id,
            new() { ["projectId"] = projectId, ["evaluatorId"] = evaluator.Id, ["rubricId"] = rubricId, ["periodId"] = period.Id }, ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.EvaluatorAssigned, assigned.Id,
            actor.Id, assigned.AssignedAt), ct);
        await Window(project, period.Id, ct);
        return assigned.ToDto();
    }, ct);

    public Task<EvaluationAssignmentDto> Revoke(long id, RevokeEvaluatorRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var before = await Assignment(id, ct);
        var project = await Project(before.ProjectId, ct);
        if (!Manages(actor, project, before.DepartmentId)) throw new ForbiddenException();
        await repository.LockProjectAsync(project.Id, ct);
        before = await Assignment(id, ct);
        project = await Project(project.Id, ct);
        if (!Manages(actor, project, before.DepartmentId)) throw new ForbiddenException();
        Current(before.ConcurrencyToken, input.ConcurrencyToken);
        if (project.Status is "COMPLETED" or "ARCHIVED") throw new ConflictException("Completed or archived projects are read-only.");
        if (before.Status == "REVOKED") return before.ToDto();
        if (await results.HasPublishedResultAsync(project.Id, ct))
            throw new ConflictException("Published project or student results cannot have evaluator assignments revoked.", WorkflowErrorCodes.FinalizedAssignment);
        if (!before.ComponentId.HasValue && await results.IsRequiredAsync(id, ct) && await results.AnyFinalizedAsync(project.Id, ct))
            throw new ConflictException("Required evaluators are frozen after the first finalized evaluation.", WorkflowErrorCodes.FinalizedAssignment);
        var draft = await repository.FindDraftAsync(id, ct);
        if (before.HasFinalizedEvaluation || (draft is not null && draft.Status != "DRAFT"))
            throw new ConflictException("Submitted or finalized evaluations cannot be revoked through the draft workflow.", WorkflowErrorCodes.FinalizedAssignment);
        var result = await repository.RevokeAsync(id, input.Reason.Trim(), clock.GetUtcNow().UtcDateTime, ct);
        await Audit(actor.Id, "EVALUATOR_REVOKED", "EVALUATION_ASSIGNMENT", id,
            new() { ["projectId"] = before.ProjectId, ["reason"] = input.Reason.Trim() }, ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.EvaluatorRevoked, id, actor.Id, clock.GetUtcNow().UtcDateTime), ct);
        return result.ToDto();
    }, ct);

    public Task<ReplaceEvaluatorResponse> Replace(long id, ReplaceEvaluatorRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var before = await Assignment(id, ct);
        var project = await Project(before.ProjectId, ct);
        if (!Manages(actor, project, before.DepartmentId)) throw new ForbiddenException();
        await repository.LockProjectAsync(project.Id, ct);
        before = await Assignment(id, ct);
        project = await Project(project.Id, ct);
        if (!Manages(actor, project, before.DepartmentId)) throw new ForbiddenException();
        Current(before.ConcurrencyToken, input.ConcurrencyToken);
        if (project.Status is "COMPLETED" or "ARCHIVED")
            throw new ConflictException("Completed or archived projects are read-only.");
        if (before.Status != "ACTIVE")
            throw new ConflictException("Only an active evaluation assignment can be replaced.", WorkflowErrorCodes.FinalizedAssignment);
        if (!before.ComponentId.HasValue || before.Scope == "UNKNOWN")
            throw new ConflictException("Only scoped evaluation assignments can be replaced atomically.");
        if (await results.HasPublishedResultAsync(project.Id, ct))
            throw new ConflictException("Published project or student results cannot replace evaluator assignments.", WorkflowErrorCodes.FinalizedAssignment);
        var draft = await repository.FindDraftAsync(id, ct);
        if (before.HasFinalizedEvaluation || (draft is not null && draft.Status != "DRAFT"))
            throw new ConflictException("Submitted or finalized evaluations cannot be replaced.", WorkflowErrorCodes.FinalizedAssignment);
        if (input.ReplacementEvaluatorId == before.EvaluatorId)
            throw new ConflictException("Replacement evaluator must differ from the current evaluator.", WorkflowErrorCodes.EvaluatorIneligible);
        if (await repository.HasBeenPrimarySupervisorAsync(project.Id, input.ReplacementEvaluatorId, ct))
            throw new ConflictException("A current or former primary supervisor or discipline mentor cannot evaluate the same project (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);

        var period = await Window(project, before.PeriodId, ct);
        var evaluator = await repository.GetActorAsync(input.ReplacementEvaluatorId, ct);
        if (evaluator is null || !evaluator.IsLecturer || evaluator.DepartmentId is null
            || !project.DepartmentIds.Contains(evaluator.DepartmentId.Value)
            || !Manages(actor, project, evaluator.DepartmentId))
            throw new ConflictException("The replacement evaluator must be an active lecturer in the managed project department.", WorkflowErrorCodes.EvaluatorIneligible);

        if (input.UpdateCommitteeIfNeeded && before.Stage is "COLD" or "DEFENSE")
            await schemes.EnsureCommitteeMemberForReplacementAsync(project.Id, evaluator.Id, before.Scope, before.MajorId, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var trimmedReason = input.Reason.Trim();
        var revoked = await repository.RevokeAsync(id, $"Replaced: {trimmedReason}", now, ct);

        var assignInput = new AssignEvaluatorRequest(
            evaluator.Id,
            before.PeriodId,
            before.EvaluationType == "SUPERVISOR" ? "COMMITTEE" : before.EvaluationType,
            before.Scope,
            before.MajorId,
            before.StudentId,
            before.ComponentId);
        var scope = await schemes.ResolveAssignmentAsync(project.Id, assignInput, ct);
        await Rubric(scope.RubricId, project, evaluator.DepartmentId.Value, true, ct, scope.Scope);
        var replacement = await repository.AssignScopedAsync(project.Id, evaluator.Id, period.Id,
            evaluator.DepartmentId.Value, assignInput.EvaluationType, actor.Id, now, scope, ct);

        await Audit(actor.Id, "EVALUATOR_REPLACED", "EVALUATION_ASSIGNMENT", replacement.Id,
            new()
            {
                ["projectId"] = project.Id,
                ["revokedAssignmentId"] = revoked.Id,
                ["replacedEvaluatorId"] = before.EvaluatorId,
                ["replacementEvaluatorId"] = evaluator.Id,
                ["reason"] = trimmedReason,
                ["componentId"] = before.ComponentId
            }, ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.EvaluatorAssigned, replacement.Id,
            actor.Id, replacement.AssignedAt), ct);
        await Window(project, period.Id, ct);
        return new ReplaceEvaluatorResponse(revoked.ToDto(), replacement.ToDto());
    }, ct);

    public Task<EvaluationDraftDto> Create(long assignmentId, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var assignment = await Assignment(assignmentId, ct);
        var project = await Project(assignment.ProjectId, ct);
        await Eligible(actor, assignment, project, ct);
        await repository.LockProjectAsync(project.Id, ct);
        assignment = await Assignment(assignmentId, ct);
        await Eligible(actor, assignment, project, ct);
        await Window(project, assignment.PeriodId, ct);
        await Rubric(assignment.RubricId, project, assignment.DepartmentId, false, ct, assignment.Scope);
        var existing = await repository.FindDraftAsync(assignmentId, ct);
        await schemes.EnsureWritableAsync(assignment, ct);
        if (existing is not null) throw new ConflictException("An evaluation already exists for this assignment.");
        var draft = await repository.CreateDraftAsync(assignment, clock.GetUtcNow().UtcDateTime, ct);
        await Audit(actor.Id, "EVALUATION_DRAFT_CREATED", "EVALUATION", draft.Id,
            new() { ["assignmentId"] = assignment.Id, ["rubricId"] = assignment.RubricId, ["projectId"] = project.Id }, ct);
        return draft.ToDto();
    }, ct);

    public Task<EvaluationDraftDto> Save(long id, SaveEvaluationDraftRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var before = await Draft(id, ct);
        var assignment = await Assignment(before.AssignmentId, ct);
        var project = await Project(before.ProjectId, ct);
        await Eligible(actor, assignment, project, ct);
        await repository.LockProjectAsync(project.Id, ct);
        assignment = await Assignment(before.AssignmentId, ct);
        await Eligible(actor, assignment, project, ct);
        await Window(project, assignment.PeriodId, ct);
        await Rubric(before.RubricId, project, assignment.DepartmentId, false, ct, assignment.Scope);
        before = await Draft(id, ct);
        Current(before.ConcurrencyToken, input.ConcurrencyToken);
        if (before.Status != "DRAFT") throw new ConflictException("Only draft evaluations can be edited.", WorkflowErrorCodes.FinalizedAssignment);
        await schemes.EnsureWritableAsync(assignment, ct);
        var unknown = input.Scores.Any(s => before.Scores.All(c => c.RubricCriterionId != s.RubricCriterionId));
        if (unknown) throw new ConflictException("Scores must refer to leaf criteria in this rubric version; groups cannot be scored.");
        var values = input.Scores.ToDictionary(s => s.RubricCriterionId);
        var scores = before.Scores.Select(c => c with { Score = values.GetValueOrDefault(c.RubricCriterionId)?.Score,
            Comments = values.GetValueOrDefault(c.RubricCriterionId)?.Comments?.Trim() }).ToArray();
        var preview = EvaluationScoring.Preview(scores, 10m);
        var after = await repository.SaveAsync(id, input.Scores, input.Comments?.Trim(), preview.Total, clock.GetUtcNow().UtcDateTime, ct);
        await Audit(actor.Id, "EVALUATION_DRAFT_SAVED", "EVALUATION", id,
            new() { ["assignmentId"] = assignment.Id, ["rubricId"] = before.RubricId,
                ["previousToken"] = before.ConcurrencyToken, ["token"] = after.ConcurrencyToken,
                ["scoredCriteria"] = scores.Count(c => c.Score.HasValue), ["totalPreview"] = preview.Total }, ct);
        return after.ToDto();
    }, ct);

    public Task<EvaluationDraftDto> Get(long id, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var draft = await Draft(id, ct);
        var assignment = await Assignment(draft.AssignmentId, ct);
        var project = await Project(draft.ProjectId, ct);
        if (!Manages(actor, project, assignment.DepartmentId)) await Eligible(actor, assignment, project, ct);
        return draft.ToDto();
    }, ct);

    public Task<PagedResult<EvaluationDraftDto>> List(long projectId, int page, int size, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var project = await Project(projectId, ct);
        var manager = Manages(actor, project);
        if (!manager && !actor.IsLecturer) throw new ForbiddenException();
        var result = await repository.ListDraftsAsync(projectId, actor, page, size, ct);
        var visible = new List<EvaluationDraftDto>();
        foreach (var item in result.Items)
        {
            var assignment = await Assignment(item.AssignmentId, ct);
            if (!Manages(actor, project, assignment.DepartmentId)) await Eligible(actor, assignment, project, ct);
            visible.Add(item.ToDto());
        }
        return new PagedResult<EvaluationDraftDto>(visible, page, size, result.TotalCount);
    }, ct);

    public Task<PagedResult<EvaluationAssignmentDto>> Assignments(long? projectId, string? status, int page, int size, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        if (projectId.HasValue)
        {
            if (!AcademicAuthority.CanReadAssignments(actor, await Project(projectId.Value, ct))) throw new ForbiddenException();
        }
        else if (!actor.IsLecturer) throw new ForbiddenException();
        var result = await repository.ListAssignmentsAsync(projectId, actor, status, page, size, ct);
        return new PagedResult<EvaluationAssignmentDto>(result.Items.Select(a => a.ToDto()).ToArray(), page, size, result.TotalCount);
    }, ct);
}
