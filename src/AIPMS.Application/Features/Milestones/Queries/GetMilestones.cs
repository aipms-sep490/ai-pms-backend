using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Queries;

public sealed record GetMilestonesQuery(long ProjectId) : IRequest<IReadOnlyList<MilestoneDto>>;

public sealed class GetMilestonesQueryValidator : AbstractValidator<GetMilestonesQuery>
{
    public GetMilestonesQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}

public sealed class GetMilestonesQueryHandler(
    IMilestoneRepository repository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetMilestonesQuery, IReadOnlyList<MilestoneDto>>
{
    public async Task<IReadOnlyList<MilestoneDto>> Handle(
        GetMilestonesQuery request,
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

        return await repository.GetProjectMilestonesAsync(request.ProjectId, cancellationToken);
    }
}
