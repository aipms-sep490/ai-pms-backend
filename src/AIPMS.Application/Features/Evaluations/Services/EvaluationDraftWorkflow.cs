using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public sealed class EvaluationDraftWorkflow(IEvaluationDraftRepository repository, IRubricRepository rubrics,
    ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock)
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
        actor.IsAdmin || (actor.IsStaff && actor.DepartmentId.HasValue
            && project.ActiveScope && project.DepartmentIds.Contains(actor.DepartmentId.Value)
            && (!department.HasValue || actor.DepartmentId == department));

    private static void Current(string expected, string supplied)
    {
        if (!Guid.TryParse(supplied, out var token) || !Guid.TryParse(expected, out var current) || token != current)
            throw new ConflictException("The evaluation or assignment has changed. Reload before retrying.");
    }

    private async Task<EvaluationAssignmentRecord> Assignment(long id, CancellationToken ct) =>
        await repository.GetAssignmentAsync(id, ct) ?? throw new NotFoundException("EvaluationAssignment", id);

    private async Task<EvaluationDraftRecord> Draft(long id, CancellationToken ct) =>
        await repository.GetDraftAsync(id, ct) ?? throw new NotFoundException("Evaluation", id);

    private async Task Eligible(EvaluationActor actor, EvaluationAssignmentRecord assignment, EvaluationProject project, CancellationToken ct)
    {
        if (!actor.IsLecturer || actor.Id != assignment.EvaluatorId || actor.DepartmentId != assignment.DepartmentId
            || !project.ActiveScope || !project.DepartmentIds.Contains(assignment.DepartmentId)
            || assignment.Status != "ACTIVE")
            throw new ForbiddenException("Only the currently assigned evaluator can access this draft.");
        if (assignment.EvaluationType == "SUPERVISOR"
            && !await repository.IsCurrentSupervisorAsync(project.Id, actor.Id, ct))
            throw new ForbiddenException("The supervisor assignment is no longer active.");
    }

    private async Task<EvaluationPeriod> Window(EvaluationProject project, long periodId, CancellationToken ct)
    {
        if (project.Status != "FINAL_SUBMISSION" || !project.ActiveScope)
            throw new ConflictException("Draft evaluation requires a project in FINAL_SUBMISSION with active academic scope.");
        if (!await repository.HasLockedSubmissionAsync(project.Id, ct))
            throw new ConflictException("A locked final-submission package is required before assignment or draft scoring.");
        var period = await repository.GetPeriodAsync(periodId, clock.GetUtcNow().UtcDateTime, ct);
        if (period is null || period.SemesterId != project.SemesterId || !period.IsOpen)
            throw new ConflictException("A single active evaluation window in the project's semester is required.");
        return period;
    }

    private async Task<RubricRecord> Rubric(long id, EvaluationProject project, long department, bool newAssignment, CancellationToken ct)
    {
        var rubric = await rubrics.GetAsync(id, new RubricActor(0, true, null), true, ct)
            ?? throw new ConflictException("The assigned rubric no longer exists.");
        if (rubric.DepartmentId != department || rubric.AcademicSemesterId != project.SemesterId
            || (newAssignment ? rubric.Status != "PUBLISHED" : rubric.Status is not ("PUBLISHED" or "RETIRED")))
            throw new ConflictException("A protected rubric in the project's department and semester is required.");
        RubricRules.EnsurePublishable(rubric.Criteria);
        return rubric;
    }

    private Task Audit(long actor, string action, string entity, long id, Dictionary<string, object?> details, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(actor, action, entity, id, details), ct);

    public Task<EvaluationAssignmentDto> Assign(long projectId, AssignEvaluatorRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var project = await Project(projectId, ct);
        if (!Manages(actor, project)) throw new ForbiddenException();
        await repository.LockProjectAsync(projectId, ct);
        project = await Project(projectId, ct);
        var period = await Window(project, input.ProjectPeriodId, ct);
        var evaluator = await repository.GetActorAsync(input.EvaluatorId, ct);
        if (evaluator is null || !evaluator.IsLecturer || evaluator.DepartmentId is null
            || !project.DepartmentIds.Contains(evaluator.DepartmentId.Value)
            || !Manages(actor, project, evaluator.DepartmentId))
            throw new ConflictException("The evaluator must be an active lecturer in the managed project department.");
        if (input.EvaluationType == "SUPERVISOR" && !await repository.IsCurrentSupervisorAsync(projectId, evaluator.Id, ct))
            throw new ConflictException("SUPERVISOR evaluation requires the active project supervisor.");
        if (period.RubricId is not long rubricId) throw new ConflictException("The evaluation period has no configured rubric.");
        await Rubric(rubricId, project, evaluator.DepartmentId.Value, true, ct);
        var assigned = await repository.AssignAsync(projectId, evaluator.Id, period.Id, rubricId,
            evaluator.DepartmentId.Value, input.EvaluationType, actor.Id, clock.GetUtcNow().UtcDateTime, ct);
        await Audit(actor.Id, "EVALUATOR_ASSIGNED", "EVALUATION_ASSIGNMENT", assigned.Id,
            new() { ["projectId"] = projectId, ["evaluatorId"] = evaluator.Id, ["rubricId"] = rubricId, ["periodId"] = period.Id }, ct);
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
        Current(before.ConcurrencyToken, input.ConcurrencyToken);
        if (project.Status is "COMPLETED" or "ARCHIVED") throw new ConflictException("Completed or archived projects are read-only.");
        if (before.Status == "REVOKED") return before.ToDto();
        var draft = await repository.FindDraftAsync(id, ct);
        if (draft is not null && draft.Status != "DRAFT") throw new ConflictException("Submitted or finalized evaluations cannot be revoked through the draft workflow.");
        var result = await repository.RevokeAsync(id, input.Reason.Trim(), clock.GetUtcNow().UtcDateTime, ct);
        await Audit(actor.Id, "EVALUATOR_REVOKED", "EVALUATION_ASSIGNMENT", id,
            new() { ["projectId"] = before.ProjectId, ["reason"] = input.Reason.Trim() }, ct);
        return result.ToDto();
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
        await Rubric(assignment.RubricId, project, assignment.DepartmentId, false, ct);
        var existing = await repository.FindDraftAsync(assignmentId, ct);
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
        await Rubric(before.RubricId, project, assignment.DepartmentId, false, ct);
        before = await Draft(id, ct);
        Current(before.ConcurrencyToken, input.ConcurrencyToken);
        if (before.Status != "DRAFT") throw new ConflictException("Only draft evaluations can be edited.");
        var unknown = input.Scores.Any(s => before.Scores.All(c => c.RubricCriterionId != s.RubricCriterionId));
        if (unknown) throw new ConflictException("A score refers to a criterion outside this rubric version.");
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
            if (!Manages(actor, await Project(projectId.Value, ct))) throw new ForbiddenException();
        }
        else if (!actor.IsLecturer) throw new ForbiddenException();
        var result = await repository.ListAssignmentsAsync(projectId, actor, status, page, size, ct);
        return new PagedResult<EvaluationAssignmentDto>(result.Items.Select(a => a.ToDto()).ToArray(), page, size, result.TotalCount);
    }, ct);
}
