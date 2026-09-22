using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.TaskComments.Abstractions;
using AIPMS.Application.Features.TaskComments.DTOs;
using AIPMS.Application.Features.TaskComments.Models;
using MediatR;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Common.Security;
namespace AIPMS.Application.Features.TaskComments.Queries;
public sealed record GetTaskCommentsQuery(long TaskId, int Page = 1, int PageSize = 20) : IRequest<PagedResult<TaskCommentDto>>;
public sealed class GetTaskCommentsHandler(ITaskCommentRepository repository, ICurrentUser currentUser, IProjectAccessService projectAccess, ISupervisorProfileRepository accounts) : IRequestHandler<GetTaskCommentsQuery, PagedResult<TaskCommentDto>>
{
    public async Task<PagedResult<TaskCommentDto>> Handle(GetTaskCommentsQuery r, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var account = await accounts.GetAccountAsync(actor, ct);
        if (account is null || !account.IsActive || (!account.Roles.Contains(AppRoles.Admin) && !account.HasActiveAcademicScope))
            throw new ForbiddenException("An active account and academic scope are required.");
        var access = await repository.GetAccessAsync(r.TaskId, actor, ct) ?? throw new NotFoundException("Task", r.TaskId);
        if (!await projectAccess.CanAccessAsync(actor, access.ProjectId, ct)) throw new ForbiddenException("You cannot view comments on this task.");
        if (r.Page is < 1 or > 1_000_000 || r.PageSize is < 1 or > 100) throw new ValidationException(new Dictionary<string, string[]> { ["page"] = ["Page must be between 1 and 1000000."], ["pageSize"] = ["Page size must be between 1 and 100."] });
        return await repository.ListAsync(new(r.TaskId, r.Page, r.PageSize), ct);
    }
}
