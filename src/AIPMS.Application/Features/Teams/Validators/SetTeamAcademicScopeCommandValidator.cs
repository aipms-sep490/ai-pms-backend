using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.DTOs;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class TeamAcademicScopeRequestValidator : AbstractValidator<TeamAcademicScopeRequest>
{
    public TeamAcademicScopeRequestValidator()
    {
        RuleFor(x => x.ProjectMode).Must(x => x is "SINGLE_MAJOR" or "INTERDISCIPLINARY");
        RuleFor(x => x.LeadDepartmentId).GreaterThan(0);
        RuleFor(x => x.Requirements).NotEmpty().Must(x => x is null || x.Count <= 50);
        RuleForEach(x => x.Requirements).NotNull().ChildRules(r =>
        {
            r.RuleFor(x => x.MajorId).GreaterThan(0);
            r.RuleFor(x => x.MinMembers).InclusiveBetween(1, 1000);
            r.RuleFor(x => x.MaxMembers).InclusiveBetween(1, 1000);
            r.RuleFor(x => x.Responsibility).NotEmpty().MaximumLength(1000);
        });
    }
}

public sealed class SetTeamAcademicScopeCommandValidator : AbstractValidator<SetTeamAcademicScopeCommand>
{
    public SetTeamAcademicScopeCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.Scope).NotNull().SetValidator(new TeamAcademicScopeRequestValidator());
    }
}
