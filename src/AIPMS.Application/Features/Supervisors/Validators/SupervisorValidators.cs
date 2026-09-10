using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Validators;

public sealed class GetSupervisorsQueryValidator : AbstractValidator<GetSupervisorsQuery>
{
    public GetSupervisorsQueryValidator()
    {
        RuleFor(q => q.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.DepartmentId).GreaterThan(0).When(q => q.DepartmentId.HasValue);
        RuleFor(q => q.Search).MaximumLength(255);
        RuleFor(q => q.Expertise).MaximumLength(255);
    }
}

public sealed class GetSupervisorByIdQueryValidator : AbstractValidator<GetSupervisorByIdQuery>
{
    public GetSupervisorByIdQueryValidator() => RuleFor(q => q.ProfileId).GreaterThan(0);
}

public sealed class UpdateSupervisorProfileCommandValidator : AbstractValidator<UpdateSupervisorProfileCommand>
{
    public UpdateSupervisorProfileCommandValidator()
    {
        RuleFor(c => c.UserId).GreaterThan(0);
        RuleFor(c => c.Bio).MaximumLength(4000);
    }
}

public sealed class SupervisorExpertiseValidator : AbstractValidator<SupervisorExpertiseDto>
{
    public SupervisorExpertiseValidator()
    {
        RuleFor(e => e.Name).NotEmpty().MaximumLength(255);
        RuleFor(e => e.ProficiencyLevel).MaximumLength(50);
    }
}

public sealed class ReplaceSupervisorExpertiseCommandValidator : AbstractValidator<ReplaceSupervisorExpertiseCommand>
{
    public ReplaceSupervisorExpertiseCommandValidator()
    {
        RuleFor(c => c.ProfileId).GreaterThan(0);
        RuleFor(c => c.Expertise).NotNull();
        When(c => c.Expertise is not null, () =>
        {
            RuleFor(c => c.Expertise).Must(items => items.Count <= 50).WithMessage("At most 50 expertise entries are allowed.");
            RuleForEach(c => c.Expertise).NotNull().SetValidator(new SupervisorExpertiseValidator());
            RuleFor(c => c.Expertise).Must(items => items.Where(e => e is not null)
                .Select(e => e.Name?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count)
                .WithMessage("Expertise names must be unique (ignoring case and surrounding whitespace).");
        });
    }
}
