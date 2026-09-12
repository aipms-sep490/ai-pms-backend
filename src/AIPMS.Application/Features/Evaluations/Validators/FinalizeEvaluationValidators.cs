using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using FluentValidation;

namespace AIPMS.Application.Features.Evaluations.Validators;

public sealed class FinalizeEvaluationRequestValidator : AbstractValidator<FinalizeEvaluationRequest>
{
    public FinalizeEvaluationRequestValidator() => RuleFor(r => r.ConcurrencyToken).Must(t => Guid.TryParse(t, out _));
}
public sealed class FinalizeEvaluationCommandValidator : AbstractValidator<FinalizeEvaluationCommand>
{
    public FinalizeEvaluationCommandValidator()
    {
        RuleFor(r => r.Id).GreaterThan(0);
        RuleFor(r => r.Input).NotNull().SetValidator(new FinalizeEvaluationRequestValidator());
    }
}
