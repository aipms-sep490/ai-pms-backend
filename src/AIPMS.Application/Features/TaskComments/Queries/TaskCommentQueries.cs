using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.TaskComments.Abstractions;
using AIPMS.Application.Features.TaskComments.DTOs;
using AIPMS.Application.Features.TaskComments.Models;
using MediatR;
namespace AIPMS.Application.Features.TaskComments.Queries;
public sealed record GetTaskCommentsQuery(long TaskId, int Page = 1, int PageSize = 20) : IRequest<PagedResult<TaskCommentDto>>;
public sealed class GetTaskCommentsHandler(ITaskCommentRepository repository, ICurrentUser currentUser) : IRequestHandler<GetTaskCommentsQuery, PagedResult<TaskCommentDto>>
{
    public async Task<PagedResult<TaskCommentDto>> Handle(GetTaskCommentsQuery r, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var access = await repository.GetAccessAsync(r.TaskId, actor, ct) ?? throw new NotFoundException("Task", r.TaskId);
        if (!access.IsActiveMember && !access.IsLeader && !access.IsMentor) throw new ForbiddenException("You cannot view comments on this task.");
        return await repository.ListAsync(new(r.TaskId, r.Page, r.PageSize), ct);
    }
}
