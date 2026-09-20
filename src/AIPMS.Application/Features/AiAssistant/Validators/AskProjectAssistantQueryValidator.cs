using AIPMS.Application.Features.AiAssistant.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.AiAssistant.Validators;

public sealed class AskProjectAssistantQueryValidator : AbstractValidator<AskProjectAssistantQuery>
{
    public AskProjectAssistantQueryValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0)
            .WithMessage("Project ID must be greater than 0.");

        RuleFor(x => x.Query)
            .NotEmpty()
            .WithMessage("Query cannot be empty.")
            .Must(q => !string.IsNullOrWhiteSpace(q))
            .WithMessage("Query cannot be whitespace.")
            .MaximumLength(1000)
            .WithMessage("Query cannot exceed 1000 characters.");
    }
}
