using AIPMS.Application.Features.Tasks.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Tasks.Validators;

public sealed class ResolveTaskDependencyImpactCommandValidator : AbstractValidator<ResolveTaskDependencyImpactCommand>
{
    public ResolveTaskDependencyImpactCommandValidator()
    {
        RuleFor(x => x.TaskId).GreaterThan(0);
        RuleFor(x => x.ImpactId).GreaterThan(0);
        RuleFor(x => x.Decision).Must(x => x is "CONFIRMED_VALID" or "REWORK_REQUIRED");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
