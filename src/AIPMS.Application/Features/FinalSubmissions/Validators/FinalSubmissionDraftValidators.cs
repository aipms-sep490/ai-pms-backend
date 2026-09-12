using AIPMS.Application.Features.FinalSubmissions.Commands;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.FinalSubmissions.Validators;

public sealed class CreateFinalSubmissionDraftRequestValidator : AbstractValidator<CreateFinalSubmissionDraftRequest>
{
    public CreateFinalSubmissionDraftRequestValidator()
    {
        RuleFor(x => x.ProjectPeriodId).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(10000);
        RuleFor(x => x.DeliverableVersionIds).NotNull()
            .Must(ids => ids is null || (ids.Count <= 100 && ids.All(id => id > 0) && ids.Distinct().Count() == ids.Count))
            .WithMessage("Select at most 100 distinct positive deliverable version IDs.");
    }
}
public sealed class UpdateFinalSubmissionDraftRequestValidator : AbstractValidator<UpdateFinalSubmissionDraftRequest>
{
    public UpdateFinalSubmissionDraftRequestValidator()
    {
        RuleFor(x => new CreateFinalSubmissionDraftRequest(x.ProjectPeriodId, x.Notes, x.DeliverableVersionIds))
            .SetValidator(new CreateFinalSubmissionDraftRequestValidator());
        RuleFor(x => x.ConcurrencyToken).Must(t => Guid.TryParse(t, out _));
    }
}
public sealed class CreateFinalSubmissionDraftCommandValidator : AbstractValidator<CreateFinalSubmissionDraftCommand>
{
    public CreateFinalSubmissionDraftCommandValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Input).NotNull().SetValidator(new CreateFinalSubmissionDraftRequestValidator());
    }
}
public sealed class UpdateFinalSubmissionDraftCommandValidator : AbstractValidator<UpdateFinalSubmissionDraftCommand>
{
    public UpdateFinalSubmissionDraftCommandValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Input).NotNull().SetValidator(new UpdateFinalSubmissionDraftRequestValidator());
    }
}
public sealed class GetFinalSubmissionDraftQueryValidator : AbstractValidator<GetFinalSubmissionDraftQuery>
{
    public GetFinalSubmissionDraftQueryValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}
public sealed class GetFinalSubmissionPeriodsQueryValidator : AbstractValidator<GetFinalSubmissionPeriodsQuery>
{
    public GetFinalSubmissionPeriodsQueryValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
