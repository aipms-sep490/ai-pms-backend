using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.Results.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Results.Services;

public sealed class ProjectResultWorkflow(IProjectResultRepository repository, IEvaluationDraftRepository evaluations,
    IFinalSubmissionRepository submissions, ICurrentUser currentUser, IAuditTrail audit, IPublisher publisher, TimeProvider clock)
{
    private async Task<EvaluationActor> Actor(CancellationToken ct) => currentUser.IsAuthenticated && currentUser.UserId is long id
        ? await evaluations.GetActorAsync(id, ct) ?? throw new ForbiddenException() : throw new UnauthorizedException();
    private async Task<EvaluationProject> Project(long id, CancellationToken ct) =>
        await evaluations.GetProjectAsync(id, ct) ?? throw new NotFoundException("Project", id);
    private static void Manager(EvaluationActor actor, EvaluationProject project, long? department = null)
    {
        if (!(actor.IsAdmin || actor.IsStaff && actor.DepartmentId.HasValue && project.ActiveScope
            && project.DepartmentIds.Contains(actor.DepartmentId.Value) && (!department.HasValue || department == actor.DepartmentId)))
            throw new ForbiddenException("Only authorized department staff or administrator can manage project results.");
    }
    private async Task Scope(EvaluationActor actor, EvaluationProject project, ResultPolicyDto? policy, CancellationToken ct)
    {
        Manager(actor, project);
        foreach (var item in policy?.Assignments ?? [])
        {
            var assignment = await evaluations.GetAssignmentAsync(item.AssignmentId, ct) ?? throw new ConflictException("Policy assignment no longer exists.");
            Manager(actor, project, assignment.DepartmentId);
        }
    }
    public Task<ResultPolicyDto?> Policy(long projectId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var policy = await repository.PolicyAsync(projectId, ct);
        await Scope(await Actor(ct), await Project(projectId, ct), policy, ct);
        return policy;
    }, ct);
    public Task<ResultPolicyDto> Configure(long projectId, ConfigureResultPolicyRequest input, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        await evaluations.LockProjectAsync(projectId, ct);
        var project = await Project(projectId, ct);
        var before = await repository.PolicyAsync(projectId, ct);
        await Scope(actor, project, before, ct);
        if (project.Status != "FINAL_SUBMISSION" || !project.ActiveScope || await repository.AnyFinalizedAsync(projectId, ct)
            || await repository.GetAsync(projectId, ct) is not null)
            throw new ConflictException("Configure result policy before the first finalized evaluation on a final-submitted project.");
        if (before is null ? input.ConcurrencyToken is not null : !Guid.TryParse(input.ConcurrencyToken, out var token) || token != Guid.Parse(before.ConcurrencyToken))
            throw new ConflictException("Result policy changed. Reload before saving.");
        if (!await evaluations.HasLockedSubmissionAsync(projectId, ct)) throw new ConflictException("Locked final package required.");
        foreach (var item in input.Assignments)
        {
            var assignment = await evaluations.GetAssignmentAsync(item.AssignmentId, ct);
            if (assignment is null || assignment.ProjectId != projectId || assignment.Status != "ACTIVE")
                throw new ConflictException("Every required assignment must be active and belong to this project.");
            Manager(actor, project, assignment.DepartmentId);
            var evaluator = await evaluations.GetActorAsync(assignment.EvaluatorId, ct);
            if (evaluator is null || !evaluator.IsLecturer || evaluator.DepartmentId != assignment.DepartmentId
                || !project.DepartmentIds.Contains(assignment.DepartmentId)
                || assignment.EvaluationType == "SUPERVISOR" && !await evaluations.IsCurrentSupervisorAsync(projectId, assignment.EvaluatorId, ct))
                throw new ConflictException("A required evaluator is no longer eligible.");
        }
        var saved = await repository.ConfigureAsync(projectId, input, actor.Id, clock.GetUtcNow().UtcDateTime, ct);
        await audit.RecordAsync(new(actor.Id, "PROJECT_RESULT_POLICY_CONFIGURED", "PROJECT", projectId,
            new Dictionary<string, object?> { ["before"] = before, ["after"] = saved }), ct);
        return saved;
    }, ct);

    private async Task<(ProjectResultPreviewDto Preview, long? PackageId)> Check(long projectId, EvaluationActor actor, CancellationToken ct)
    {
        var project = await Project(projectId, ct);
        var policy = await repository.PolicyAsync(projectId, ct);
        await Scope(actor, project, policy, ct);
        var blockers = new List<string>();
        if (project.Status != "FINAL_SUBMISSION") blockers.Add("PROJECT_NOT_FINAL_SUBMISSION");
        if (!project.ActiveScope) blockers.Add("ACADEMIC_SCOPE_INACTIVE");
        if (await repository.GetAsync(projectId, ct) is not null) blockers.Add("ALREADY_PUBLISHED");
        var package = await submissions.GetAsync(projectId, ct);
        if (package is null || package.Items.Count == 0 || package.Items.Any(i => i.Files.Count == 0)) blockers.Add("LOCKED_PACKAGE_REQUIRED");
        if (policy is null || policy.Assignments.Count == 0) blockers.Add("RESULT_POLICY_REQUIRED");
        var contributions = new List<ResultContributionDto>();
        foreach (var item in policy?.Assignments ?? [])
        {
            var assignment = await evaluations.GetAssignmentAsync(item.AssignmentId, ct);
            if (assignment is null || assignment.ProjectId != projectId || assignment.Status != "ACTIVE")
            { blockers.Add($"ASSIGNMENT_INACTIVE:{item.AssignmentId}"); continue; }
            var evaluation = await evaluations.FindDraftAsync(item.AssignmentId, ct);
            if (evaluation is null || evaluation.Status != "FINALIZED" || evaluation.Finalization is null)
            { blockers.Add($"EVALUATION_NOT_FINALIZED:{item.AssignmentId}"); continue; }
            if (evaluation.ProjectId != projectId || evaluation.EvaluatorId != assignment.EvaluatorId
                || evaluation.RubricId != assignment.RubricId || evaluation.Finalization.Evidence.FinalSubmissionId != package?.Id)
            { blockers.Add($"EVALUATION_EVIDENCE_MISMATCH:{item.AssignmentId}"); continue; }
            var recomputed = EvaluationScoring.FinalTotal(evaluation.Scores);
            if (evaluation.TotalScore != recomputed) { blockers.Add($"EVALUATION_TOTAL_MISMATCH:{item.AssignmentId}"); continue; }
            contributions.Add(new(item.AssignmentId, evaluation.Id, evaluation.EvaluatorId, evaluation.RubricId,
                item.WeightPercent, recomputed, evaluation.ConcurrencyToken));
        }
        decimal? total = null;
        if (policy is not null && contributions.Count == policy.Assignments.Count && contributions.Count > 0)
            total = ResultScoring.Total(contributions, policy.PassThreshold);
        var outcome = total.HasValue ? total >= policy!.PassThreshold ? "PASSED" : "FAILED" : null;
        // Confirm exactly the policy, finalized inputs and state shown in the preview.
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            projectId, project.Status, policy, PackageId = package?.Id, contributions, blockers }))));
        return (new(projectId, blockers.Count == 0 && total.HasValue, blockers, total, policy?.PassThreshold, outcome, token, contributions), package?.Id);
    }
    public Task<ProjectResultPreviewDto> Preview(long projectId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
        (await Check(projectId, await Actor(ct), ct)).Preview, ct);
    public Task<ProjectResultDto> Publish(long projectId, PublishProjectResultRequest input, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        await evaluations.LockProjectAsync(projectId, ct);
        var check = await Check(projectId, actor, ct);
        if (!check.Preview.CanPublish) throw new ConflictException(string.Join(", ", check.Preview.Blockers));
        if (!string.Equals(input.ConfirmationToken, check.Preview.ConfirmationToken, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Publication preview changed. Reload and confirm again.");
        var policy = (await repository.PolicyAsync(projectId, ct))!;
        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.PublishAsync(new(0, projectId, check.PackageId!.Value, check.Preview.TotalScore!.Value,
            policy.PassThreshold, check.Preview.Outcome!, ResultScoring.Rule, actor.Id, now, policy.ConcurrencyToken, check.Preview.Contributions), ct);
        await audit.RecordAsync(new(actor.Id, "PROJECT_RESULT_PUBLISHED", "PROJECT_RESULT", result.Id,
            new Dictionary<string, object?> { ["beforeStatus"] = "FINAL_SUBMISSION", ["afterStatus"] = "COMPLETED", ["result"] = result }), ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.ProjectResultPublished, result.Id, actor.Id, now), ct);
        return result;
    }, ct);
    public Task<ProjectResultDto> Get(long projectId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        if (!await submissions.CanReadAsync(projectId, actor.Id, ct)) throw new ForbiddenException();
        return await repository.GetAsync(projectId, ct) ?? throw new NotFoundException("ProjectResult", projectId);
    }, ct);
}
