using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectReviewQueueQuery(
    string? Search,
    int Page = 1,
    int PageSize = 10) : IRequest<PagedResult<ProjectSummaryDto>>;

public sealed class GetProjectReviewQueueQueryHandler(
    IProjectRepository repository,
    IAcademicStructureRepository academicRepository,
    ICurrentUser currentUser)
    : IRequestHandler<GetProjectReviewQueueQuery, PagedResult<ProjectSummaryDto>>
{
    public async Task<PagedResult<ProjectSummaryDto>> Handle(
        GetProjectReviewQueueQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Authorization check: Admin or Department Staff
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal);

        if (!isAdmin && !isStaff)
        {
            throw new ForbiddenException("Only Admin or Department Staff can access the review queue.");
        }

        long? departmentId = null;

        if (isStaff && !isAdmin)
        {
            var staffScope = await academicRepository.GetUserScopeAsync(actorUserId, cancellationToken);
            if (staffScope is null)
            {
                throw new ForbiddenException("Your account does not have a configured academic department scope.");
            }

            departmentId = staffScope.DepartmentId;
        }

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 10 : request.PageSize;

        var result = await repository.GetReviewQueueAsync(
            departmentId,
            request.Search,
            page,
            pageSize,
            cancellationToken);

        return result;
    }
}
