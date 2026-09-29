using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Results.Services;

public sealed class ProjectResultWorkflow(IProjectResultRepository repository, IEvaluationDraftRepository evaluations,
    IFinalSubmissionRepository submissions, ICurrentUser currentUser, IEvaluationSchemeService schemes)
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
    public Task<ResultPolicyDto> Configure(long projectId, ConfigureResultPolicyRequest input, CancellationToken ct) => evaluations.InTransactionAsync<ResultPolicyDto>(async () =>
    {
        await Scope(await Actor(ct), await Project(projectId, ct), await repository.PolicyAsync(projectId, ct), ct);
        throw new ConflictException("Use a published scoped evaluation scheme. Legacy result policies are read-only.");
    }, ct);
    public Task<ProjectResultPreviewDto> Preview(long projectId, CancellationToken ct) => schemes.PreviewAsync(projectId, null, ct);
    public Task<ProjectResultDto> Publish(long projectId, PublishProjectResultRequest input, CancellationToken ct) => schemes.PublishProjectAsync(projectId, input.ConfirmationToken, ct);
    public Task<ProjectResultDto> Get(long projectId, CancellationToken ct) => evaluations.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        if (!await submissions.CanReadAsync(projectId, actor.Id, ct)) throw new ForbiddenException();
        var result = await repository.GetAsync(projectId, ct) ?? throw new NotFoundException("ProjectResult", projectId);
        return result with { FrozenInputs = null };
    }, ct);
}
