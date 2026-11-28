using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Domain.Exceptions;

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
            throw new ConflictException("Only a draft evaluation can be finalized.", WorkflowErrorCodes.FinalizedAssignment);
        await Window(project, assignment.PeriodId, ct);
        await schemes.EnsureWritableAsync(assignment, ct);
        await Rubric(before.RubricId, project, assignment.DepartmentId, false, ct, assignment.Scope);
        var package = await submissions.GetAsync(project.Id, ct)
            ?? throw new ConflictException("A locked final-submission package is required.", WorkflowErrorCodes.FinalPackageRequired);
        if (package.Items.Count == 0 || package.Items.Any(i => i.Files.Count == 0))
            throw new ConflictException("The locked final-submission package has no usable evidence.", WorkflowErrorCodes.FinalPackageRequired);
        var total = EvaluationScoring.FinalTotal(before.Scores);
        var evidence = new EvaluationEvidenceRecord(package.Id, package.ProjectPeriodId, package.SubmittedAt,
            package.Items.Count, package.Items.Sum(i => i.Files.Count), package.Items.Select(i => i.DeliverableVersionId).Order().ToArray());
        evidence = await schemes.BindEvidenceAsync(assignment, evidence, ct);
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

    public Task<BatchFinalizeEvaluationsResponse> BatchFinalize(long projectId, BatchFinalizeEvaluationsRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        if (input.Items is null || input.Items.Count == 0 || input.Items.Select(i => i.EvaluationId).Distinct().Count() != input.Items.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["items"] = ["Provide at least one distinct evaluation to finalize."] });

        var actor = await Actor(ct);
        await repository.LockProjectAsync(projectId, ct);
        var project = await Project(projectId, ct);
        var package = await submissions.GetAsync(project.Id, ct)
            ?? throw new ConflictException("A locked final-submission package is required.", WorkflowErrorCodes.FinalPackageRequired);
        if (package.Items.Count == 0 || package.Items.Any(i => i.Files.Count == 0))
            throw new ConflictException("The locked final-submission package has no usable evidence.", WorkflowErrorCodes.FinalPackageRequired);

        var prepared = new List<(EvaluationDraftRecord Draft, EvaluationAssignmentRecord Assignment, decimal Total)>();
        var blocking = new List<BlockingRequirement>();

        foreach (var item in input.Items.OrderBy(i => i.EvaluationId))
        {
            var before = await Draft(item.EvaluationId, ct);
            if (before.ProjectId != projectId)
                throw new ForbiddenException("Evaluation does not belong to this project.", WorkflowErrorCodes.ScopeAccessDenied);
            var assignment = await Assignment(before.AssignmentId, ct);
            await Eligible(actor, assignment, project, ct);
            Current(before.ConcurrencyToken, item.ConcurrencyToken);
            if (before.Status != "DRAFT" || before.Finalization is not null)
                throw new ConflictException($"Evaluation #{before.Id} is already finalized.", WorkflowErrorCodes.FinalizedAssignment);
            await Window(project, assignment.PeriodId, ct);
            await schemes.EnsureWritableAsync(assignment, ct);
            await Rubric(before.RubricId, project, assignment.DepartmentId, false, ct, assignment.Scope);

            var preview = EvaluationScoring.Preview(before.Scores, 10m);
            if (preview.Total is not decimal total)
            {
                foreach (var criterionId in preview.MissingCriterionIds)
                    blocking.Add(new BlockingRequirement($"EVALUATION:{before.Id}:MISSING_CRITERION:{criterionId}",
                        $"Evaluation #{before.Id} is missing an explicit score for criterion #{criterionId}."));
                continue;
            }
            prepared.Add((before, assignment, total));
        }

        if (blocking.Count > 0)
            throw new AIPMS.Domain.Exceptions.DomainException(
                "Batch finalize blocked because one or more evaluations have unscored criteria.",
                WorkflowErrorCodes.IncompleteScores,
                new ProblemMetadata(BlockingRequirements: blocking));

        var now = clock.GetUtcNow().UtcDateTime;
        var baseEvidence = new EvaluationEvidenceRecord(package.Id, package.ProjectPeriodId, package.SubmittedAt,
            package.Items.Count, package.Items.Sum(i => i.Files.Count), package.Items.Select(i => i.DeliverableVersionId).Order().ToArray());

        var finalizedDtos = new List<EvaluationDraftDto>(prepared.Count);
        foreach (var (before, assignment, total) in prepared)
        {
            var evidence = await schemes.BindEvidenceAsync(assignment, baseEvidence, ct);
            var finalized = await repository.FinalizeAsync(before with { TotalScore = total }, evidence, actor.Id, now, ct);
            await Audit(actor.Id, "EVALUATION_FINALIZED", "EVALUATION", before.Id,
                new() { ["assignmentId"] = assignment.Id, ["projectId"] = project.Id, ["batch"] = true,
                    ["previousToken"] = before.ConcurrencyToken, ["snapshot"] = finalized.ToDto() }, ct);
            await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.EvaluationFinalized, before.Id, actor.Id, now), ct);
            await Window(project, assignment.PeriodId, ct);
            finalizedDtos.Add(finalized.ToDto());
        }

        return new BatchFinalizeEvaluationsResponse(projectId, finalizedDtos.Count, finalizedDtos);
    }, ct);
}
