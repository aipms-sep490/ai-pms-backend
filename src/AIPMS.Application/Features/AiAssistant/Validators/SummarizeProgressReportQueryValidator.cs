using AIPMS.Application.Features.AiAssistant.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.AiAssistant.Validators;

public sealed class SummarizeProgressReportQueryValidator : AbstractValidator<SummarizeProgressReportQuery>
{
    public SummarizeProgressReportQueryValidator()
    {
        RuleFor(x => x.ProjectId)
            .GreaterThan(0)
            .WithMessage("Project ID must be greater than 0.");

        RuleFor(x => x.ReportId)
            .GreaterThan(0)
            .WithMessage("Report ID must be greater than 0.");
    }
}
