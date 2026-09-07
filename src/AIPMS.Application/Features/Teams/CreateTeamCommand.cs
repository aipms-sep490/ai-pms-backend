using AIPMS.Application.Common.Models;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Teams;

public sealed record CreateTeamCommand(long AcademicSemesterId, string Code, string Name, string? Description) : IRequest<TeamDto>;

public sealed class CreateTeamCommandHandler(TeamWorkflow workflow)
    : IRequestHandler<CreateTeamCommand, TeamDto>
{
    public Task<TeamDto> Handle(CreateTeamCommand request, CancellationToken cancellationToken) =>
        workflow.CreateAsync(request, cancellationToken);
}

public sealed class CreateTeamCommandValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamCommandValidator()
    {
        RuleFor(x => x.AcademicSemesterId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
