using AIPMS.Application.Features.FinalSubmissions.Commands;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.FinalSubmissions.Validators;

public sealed class ConfigureFinalRequirementsRequestValidator : AbstractValidator<ConfigureFinalRequirementsRequest>
{
    public ConfigureFinalRequirementsRequestValidator()
    {
        RuleFor(x => x.DeliverableIds).NotNull().Must(ids => ids is null ||
            ids.Count is > 0 and <= 100 && ids.All(id => id > 0) && ids.Distinct().Count() == ids.Count)
            .WithMessage("Configure 1 to 100 distinct positive deliverable IDs.");
        RuleFor(x => x.ConcurrencyToken).Must(t => t is null || Guid.TryParse(t, out _));
    }
}
public sealed class SubmitFinalSubmissionRequestValidator : AbstractValidator<SubmitFinalSubmissionRequest>
{
    public SubmitFinalSubmissionRequestValidator()
    {
        RuleFor(x => x.DraftConcurrencyToken).Must(t => Guid.TryParse(t, out _));
        RuleFor(x => x.RequirementsConcurrencyToken).Must(t => Guid.TryParse(t, out _));
    }
}
public sealed class ConfigureFinalRequirementsCommandValidator : AbstractValidator<ConfigureFinalRequirementsCommand>
{
    public ConfigureFinalRequirementsCommandValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Input).NotNull().SetValidator(new ConfigureFinalRequirementsRequestValidator());
    }
}
public sealed class SubmitFinalSubmissionCommandValidator : AbstractValidator<SubmitFinalSubmissionCommand>
{
    public SubmitFinalSubmissionCommandValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Input).NotNull().SetValidator(new SubmitFinalSubmissionRequestValidator());
    }
}
public sealed class GetFinalRequirementsQueryValidator : AbstractValidator<GetFinalRequirementsQuery>
{
    public GetFinalRequirementsQueryValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}
public sealed class GetFinalSubmissionChecklistQueryValidator : AbstractValidator<GetFinalSubmissionChecklistQuery>
{
    public GetFinalSubmissionChecklistQueryValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}
public sealed class GetFinalSubmissionQueryValidator : AbstractValidator<GetFinalSubmissionQuery>
{
    public GetFinalSubmissionQueryValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}
public sealed class DownloadFinalSubmissionFileQueryValidator : AbstractValidator<DownloadFinalSubmissionFileQuery>
{
    public DownloadFinalSubmissionFileQueryValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.FileId).GreaterThan(0);
    }
}
