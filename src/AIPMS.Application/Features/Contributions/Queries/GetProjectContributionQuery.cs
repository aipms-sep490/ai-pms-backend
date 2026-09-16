using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Services;
using AIPMS.Application.Features.Projects.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Contributions.Queries;

public sealed record GetProjectContributionQuery(long ProjectId) : IRequest<ContributionSummaryDto>;
public sealed record GetContributionEvidenceQuery(long ProjectId, long UserId) : IRequest<IReadOnlyList<ContributionEvidenceDto>>;

public sealed class GetProjectContributionQueryHandler(IContributionRepository repository, IProjectAccessService access,
    IProjectRepository projects, ICurrentUser currentUser) : IRequestHandler<GetProjectContributionQuery, ContributionSummaryDto>
{
    public async Task<ContributionSummaryDto> Handle(GetProjectContributionQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        if (await projects.GetByIdAsync(request.ProjectId, ct) is null) throw new NotFoundException("Project", request.ProjectId);
        if (!await access.CanAccessAsync(currentUser.UserId.Value, request.ProjectId, ct)) throw new ForbiddenException("You do not have access to this project.");
        var members = await repository.GetProjectSummaryAsync(request.ProjectId, ct);
        return ContributionScoring.Summarize(members);
    }
}

public sealed class GetContributionEvidenceQueryHandler(IContributionRepository repository, IProjectAccessService access,
    IProjectRepository projects, ICurrentUser currentUser) : IRequestHandler<GetContributionEvidenceQuery, IReadOnlyList<ContributionEvidenceDto>>
{
    public async Task<IReadOnlyList<ContributionEvidenceDto>> Handle(GetContributionEvidenceQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) throw new UnauthorizedException();
        if (await projects.GetByIdAsync(request.ProjectId, ct) is null) throw new NotFoundException("Project", request.ProjectId);
        if (!await access.CanAccessAsync(currentUser.UserId.Value, request.ProjectId, ct)) throw new ForbiddenException("You do not have access to this project.");
        return await repository.GetEvidenceAsync(request.ProjectId, request.UserId, ct);
    }
}
