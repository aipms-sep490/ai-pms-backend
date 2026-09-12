using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Notifications.Events;

namespace AIPMS.Application.Features.Evaluations.Services;

public sealed partial class EvaluationDraftWorkflow
{
    public Task<EvaluationDraftDto> Finalize(long id, FinalizeEvaluationRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var before = await Draft(id, ct);
        var assignment = await Assignment(before.AssignmentId, ct);
        var project = await Project(before.ProjectId, ct);
        await Eligible(actor, assignment, project, ct);
        await repository.LockProjectAsync(project.Id, ct);
        project = await Project(project.Id, ct);
        assignment = await Assignment(before.AssignmentId, ct);
        await Eligible(actor, assignment, project, ct);
        before = await Draft(id, ct);
        Current(before.ConcurrencyToken, input.ConcurrencyToken);
        if (before.Status != "DRAFT" || before.Finalization is not null)
            throw new ConflictException("Only a draft evaluation can be finalized.");
        await Window(project, assignment.PeriodId, ct);
        await Rubric(before.RubricId, project, assignment.DepartmentId, false, ct);
        var package = await submissions.GetAsync(project.Id, ct)
            ?? throw new ConflictException("A locked final-submission package is required.");
        if (package.Items.Count == 0 || package.Items.Any(i => i.Files.Count == 0))
            throw new ConflictException("The locked final-submission package has no usable evidence.");
        var total = EvaluationScoring.FinalTotal(before.Scores);
        var evidence = new EvaluationEvidenceRecord(package.Id, package.ProjectPeriodId, package.SubmittedAt,
            package.Items.Count, package.Items.Sum(i => i.Files.Count), package.Items.Select(i => i.DeliverableVersionId).Order().ToArray());
        var now = clock.GetUtcNow().UtcDateTime;
        var finalized = await repository.FinalizeAsync(before with { TotalScore = total }, evidence, actor.Id, now, ct);
        await Audit(actor.Id, "EVALUATION_FINALIZED", "EVALUATION", id,
            new() { ["assignmentId"] = assignment.Id, ["projectId"] = project.Id, ["previousToken"] = before.ConcurrencyToken,
                ["snapshot"] = finalized.ToDto() }, ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.EvaluationFinalized, id, actor.Id, now), ct);
        // Audit/notification persistence can cross the deadline; reject the entire transaction then.
        await Window(project, assignment.PeriodId, ct);
        return finalized.ToDto();
    }, ct);
}
