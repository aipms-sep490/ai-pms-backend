using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectsQuery(
    string? Status,
    long? TeamId,
    long? SemesterId,
    long? MajorId,
    string? Tag,
    string? Search,
    int Page = 1,
    int PageSize = 10) : IRequest<PagedResult<ProjectSummaryDto>>;

public sealed class GetProjectsQueryHandler(
    IProjectRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<GetProjectsQuery, PagedResult<ProjectSummaryDto>>
{
    public async Task<PagedResult<ProjectSummaryDto>> Handle(
        GetProjectsQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 10 : request.PageSize;

        var result = await repository.GetProjectsAsync(
            request.Status,
            request.TeamId,
            request.SemesterId,
            request.MajorId,
            request.Tag,
            request.Search,
            page,
            pageSize,
            cancellationToken);

        return result;
    }
}
