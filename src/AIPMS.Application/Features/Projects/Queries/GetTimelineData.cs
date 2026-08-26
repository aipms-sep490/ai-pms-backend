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

public sealed record GetTimelineDataQuery(long ProjectId) : IRequest<ProjectTimelineDataDto>;

public sealed class GetTimelineDataQueryValidator : AbstractValidator<GetTimelineDataQuery>
{
    public GetTimelineDataQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}

public sealed class GetTimelineDataQueryHandler(
    IProjectRepository repository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetTimelineDataQuery, ProjectTimelineDataDto>
{
    public async Task<ProjectTimelineDataDto> Handle(
        GetTimelineDataQuery request,
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

        return await repository.GetTimelineDataAsync(request.ProjectId, cancellationToken);
    }
}
