using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.Projects.Abstractions;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Queries;

public sealed record GetMilestoneProgressQuery(long ProjectId) : IRequest<IReadOnlyList<MilestoneProgressDto>>;

public sealed class GetMilestoneProgressQueryValidator : AbstractValidator<GetMilestoneProgressQuery>
{
    public GetMilestoneProgressQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}

public sealed class GetMilestoneProgressQueryHandler(
    IMilestoneRepository repository,
    IProjectRepository projectRepository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetMilestoneProgressQuery, IReadOnlyList<MilestoneProgressDto>>
{
    public async Task<IReadOnlyList<MilestoneProgressDto>> Handle(
        GetMilestoneProgressQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Verify project existence
        var project = await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, request.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        return await repository.GetMilestoneProgressAsync(request.ProjectId, cancellationToken);
    }
}
