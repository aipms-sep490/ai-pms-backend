using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Queries;

public sealed record GetTasksQuery(
    long ProjectId,
    long? MilestoneId = null,
    string? Status = null,
    string? Priority = null,
    long? AssigneeUserId = null,
    string? Search = null,
    DateTime? DueFrom = null,
    DateTime? DueTo = null,
    bool? IsOverdue = null,
    bool? IsBlocked = null,
    int Page = 1,
    int PageSize = 10) : IRequest<PagedResult<TaskDto>>;

public sealed class GetTasksQueryValidator : AbstractValidator<GetTasksQuery>
{
    public GetTasksQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(static x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be greater than or equal to 1.");

        RuleFor(static x => x.PageSize)
            .GreaterThanOrEqualTo(1).WithMessage("PageSize must be greater than or equal to 1.")
            .LessThanOrEqualTo(100).WithMessage("PageSize must not exceed 100.");
    }
}

public sealed class GetTasksQueryHandler(
    ITaskRepository repository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetTasksQuery, PagedResult<TaskDto>>
{
    public async Task<PagedResult<TaskDto>> Handle(
        GetTasksQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, request.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        return await repository.GetTasksAsync(
            request.ProjectId,
            request.MilestoneId,
            request.Status,
            request.Priority,
            request.AssigneeUserId,
            request.Search,
            request.DueFrom,
            request.DueTo,
            request.IsOverdue,
            request.IsBlocked,
            request.Page,
            request.PageSize,
            cancellationToken);
    }
}
