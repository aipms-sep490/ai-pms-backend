using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record UpdateTeamCommand(long TeamId, string Name, string? Description) : IRequest<TeamDto>;

public sealed class UpdateTeamCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<UpdateTeamCommand, TeamDto>
{
    public Task<TeamDto> Handle(UpdateTeamCommand request, CancellationToken cancellationToken) =>
        workflow.UpdateAsync(request, cancellationToken);
}

public sealed class UpdateTeamCommandValidator : AbstractValidator<UpdateTeamCommand>
{
    public UpdateTeamCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
