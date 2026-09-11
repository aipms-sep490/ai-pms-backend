using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Meetings.Queries;

public sealed record GetMeetingsQuery(
    long ProjectId,
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<MeetingDto>>;

public sealed class GetMeetingsQueryHandler(
    IMeetingRepository repository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetMeetingsQuery, PagedResult<MeetingDto>>
{
    public async Task<PagedResult<MeetingDto>> Handle(GetMeetingsQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = query.ProjectId;

        if (!await repository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's meetings.");

        return await repository.GetMeetingsAsync(
            projectId,
            query.Status,
            query.From,
            query.To,
            query.Page,
            query.PageSize,
            cancellationToken);
    }
}
