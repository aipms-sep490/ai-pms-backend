using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Queries;

public sealed record GetMilestoneByIdQuery(long Id) : IRequest<MilestoneDto>;

public sealed class GetMilestoneByIdQueryValidator : AbstractValidator<GetMilestoneByIdQuery>
{
    public GetMilestoneByIdQueryValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Milestone ID must be greater than 0.");
    }
}

public sealed class GetMilestoneByIdQueryHandler(
    IMilestoneRepository repository,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetMilestoneByIdQuery, MilestoneDto>
{
    public async Task<MilestoneDto> Handle(
        GetMilestoneByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        var milestone = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Milestone", request.Id);

        // Verify project access
        if (!await projectAccessService.CanAccessAsync(actorUserId, milestone.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project.");
        }

        return milestone;
    }
}
