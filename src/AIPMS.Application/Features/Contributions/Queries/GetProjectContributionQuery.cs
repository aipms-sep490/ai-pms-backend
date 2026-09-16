using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Projects.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Contributions.Queries;

public sealed record GetProjectContributionQuery(long ProjectId) : IRequest<ContributionSummaryDto>;

public sealed class GetProjectContributionQueryHandler(IContributionRepository repository, IProjectAccessService access,
    IProjectRepository projects, ICurrentUser currentUser) : IRequestHandler<GetProjectContributionQuery, ContributionSummaryDto>
{
    public async Task<ContributionSummaryDto> Handle(GetProjectContributionQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        if (await projects.GetByIdAsync(request.ProjectId, ct) is null) throw new NotFoundException("Project", request.ProjectId);
        if (!await access.CanAccessAsync(currentUser.UserId.Value, request.ProjectId, ct)) throw new ForbiddenException("You do not have access to this project.");
        var members = await repository.GetProjectSummaryAsync(request.ProjectId, ct);
        var values = members.Select(m => m.ActivityScore).ToArray();
        if (values.Length < 2 || values.Sum() < 3) return new("INSUFFICIENT_DATA", null, members);
        var average = values.Average();
        return new("SUFFICIENT", values.Select(v => Math.Pow(v - average, 2)).Average(), members);
    }
}
