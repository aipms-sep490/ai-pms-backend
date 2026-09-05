using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectProgressSummaryQuery(long ProjectId) : IRequest<ProjectProgressSummaryDto>;

public sealed class GetProjectProgressSummaryQueryValidator : AbstractValidator<GetProjectProgressSummaryQuery>
{
    public GetProjectProgressSummaryQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}

public sealed class GetProjectProgressSummaryQueryHandler(
    IProjectRepository repository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetProjectProgressSummaryQuery, ProjectProgressSummaryDto>
{
    public async Task<ProjectProgressSummaryDto> Handle(
        GetProjectProgressSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Verify project existence
        var project = await repository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, request.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        return await repository.GetProjectProgressSummaryAsync(request.ProjectId, cancellationToken);
    }
}
