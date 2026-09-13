using FluentValidation;
using AIPMS.Application.Features.ProgressReports.Commands;
using AIPMS.Application.Features.ProgressReports.Queries;

namespace AIPMS.Application.Features.ProgressReports.Validators;

public sealed class CreateProgressReportValidator : AbstractValidator<CreateProgressReportCommand>
{
    public CreateProgressReportValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Request.ReportType)
            .NotEmpty()
            .Must(t => t is "WEEKLY" or "MONTHLY")
            .WithMessage("Report type must be either WEEKLY or MONTHLY.");
        RuleFor(x => x.Request.PeriodEnd)
            .GreaterThanOrEqualTo(x => x.Request.PeriodStart)
            .WithMessage("PeriodEnd must be greater than or equal to PeriodStart.");
        RuleFor(x => x.Request.Summary).NotEmpty().WithMessage("Summary is required.");
    }
}

public sealed class UpdateProgressReportValidator : AbstractValidator<UpdateProgressReportCommand>
{
    public UpdateProgressReportValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request.Summary).NotEmpty().WithMessage("Summary is required.");
    }
}

public sealed class SubmitProgressReportValidator : AbstractValidator<SubmitProgressReportCommand>
{
    public SubmitProgressReportValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public sealed class AddProgressReportFeedbackValidator : AbstractValidator<AddProgressReportFeedbackCommand>
{
    public AddProgressReportFeedbackValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request.FeedbackText).NotEmpty().WithMessage("Feedback text is required.");
    }
}

public sealed class GetProgressReportsQueryValidator : AbstractValidator<GetProgressReportsQuery>
{
    public GetProgressReportsQueryValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.ReportType)
            .Must(t => t == null || t is "WEEKLY" or "MONTHLY")
            .WithMessage("Report type must be either WEEKLY or MONTHLY.");
        When(x => x.From.HasValue && x.To.HasValue, () =>
        {
            RuleFor(x => x.To!.Value)
                .GreaterThanOrEqualTo(x => x.From!.Value)
                .WithMessage("To date must be greater than or equal to From date.");
        });
    }
}
