using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ActionItems.Abstractions;
using AIPMS.Application.Features.ActionItems.DTOs;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.ActionItems.Queries;

public sealed record GetProjectActionItemsQuery(
    long ProjectId,
    string? SourceType = null,
    long? MeetingId = null,
    long? ProgressReportId = null,
    string? Status = null,
    long? OwnerId = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProjectActionItemDto>>;

public sealed class GetProjectActionItemsQueryHandler(
    IProjectActionItemRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetProjectActionItemsQuery, PagedResult<ProjectActionItemDto>>
{
    public async Task<PagedResult<ProjectActionItemDto>> Handle(GetProjectActionItemsQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = query.ProjectId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's action items.");

        return await repository.ListAsync(
            projectId,
            query.SourceType,
            query.MeetingId,
            query.ProgressReportId,
            query.Status,
            query.OwnerId,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}

public sealed record GetProjectActionItemByIdQuery(
    long ProjectId,
    long ActionItemId) : IRequest<ProjectActionItemDto>;

public sealed class GetProjectActionItemByIdQueryHandler(
    IProjectActionItemRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetProjectActionItemByIdQuery, ProjectActionItemDto>
{
    public async Task<ProjectActionItemDto> Handle(GetProjectActionItemByIdQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = query.ProjectId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's action items.");

        var item = await repository.GetByIdAsync(query.ActionItemId, cancellationToken)
            ?? throw new NotFoundException("ProjectActionItem", query.ActionItemId);

        if (item.ProjectId != projectId)
            throw new NotFoundException("ProjectActionItem", query.ActionItemId);

        return item;
    }
}
