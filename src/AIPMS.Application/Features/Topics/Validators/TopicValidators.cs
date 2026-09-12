using AIPMS.Application.Features.Topics.Commands;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Topics.Validators;

public sealed class TopicContentValidator : AbstractValidator<TopicContentRequest>
{
    public TopicContentValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.ProblemStatement).MaximumLength(4000);
        RuleFor(x => x.Objectives).MaximumLength(4000);
        RuleFor(x => x.ExpectedOutput).MaximumLength(4000);
        RuleFor(x => x.Domain).MaximumLength(200);
        RuleFor(x => x.ProjectMode).Must(x => x is "SINGLE_MAJOR" or "INTERDISCIPLINARY");
        RuleFor(x => x.PrimaryMajorId).GreaterThan(0).When(x => x.PrimaryMajorId.HasValue);
        RuleFor(x => x.Technologies).NotNull().Must(x => x is null || x.Count <= 20);
        RuleForEach(x => x.Technologies).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Keywords).NotNull().Must(x => x is null || x.Count <= 20);
        RuleForEach(x => x.Keywords).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Requirements).NotEmpty().Must(x => x is null || x.Count <= 20);
        RuleForEach(x => x.Requirements).NotNull().ChildRules(r =>
        {
            r.RuleFor(x => x.MajorId).GreaterThan(0);
            r.RuleFor(x => x.MinMembers).InclusiveBetween(1, 100);
            r.RuleFor(x => x.MaxMembers).InclusiveBetween(1, 100).GreaterThanOrEqualTo(x => x.MinMembers);
            r.RuleFor(x => x.Responsibility).NotEmpty().MaximumLength(1000);
        });
    }
}
public sealed class CreateTopicCommandValidator : AbstractValidator<CreateTopicCommand>
{
    public CreateTopicCommandValidator()
    {
        RuleFor(x => x.Request).NotNull();
        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.ProjectPeriodId).GreaterThan(0);
            RuleFor(x => x.Request.LeadDepartmentId).GreaterThan(0);
            RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$");
            RuleFor(x => x.Request.Content).NotNull().SetValidator(new TopicContentValidator());
        });
    }
}
public sealed class UpdateTopicCommandValidator : AbstractValidator<UpdateTopicCommand>
{
    public UpdateTopicCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request).NotNull();
        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.ConcurrencyToken).NotEmpty();
            RuleFor(x => x.Request.Content).NotNull().SetValidator(new TopicContentValidator());
        });
    }
}
public sealed class PublishTopicCommandValidator : AbstractValidator<PublishTopicCommand>
{
    public PublishTopicCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request).NotNull();
        When(x => x.Request is not null, () => RuleFor(x => x.Request.ConcurrencyToken).NotEmpty());
    }
}
public sealed class CloseTopicCommandValidator : AbstractValidator<CloseTopicCommand>
{
    public CloseTopicCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request).NotNull();
        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.ConcurrencyToken).NotEmpty();
            RuleFor(x => x.Request.Reason).NotEmpty().MaximumLength(2000);
        });
    }
}
public sealed class GetTopicQueryValidator : AbstractValidator<GetTopicQuery>
{
    public GetTopicQueryValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
public sealed class ListTopicsQueryValidator : AbstractValidator<ListTopicsQuery>
{
    public ListTopicsQueryValidator()
    {
        RuleFor(x => x.Filter).NotNull();
        When(x => x.Filter is not null, () =>
        {
            RuleFor(x => x.Filter.Page).GreaterThan(0);
            RuleFor(x => x.Filter.PageSize).InclusiveBetween(1, 100);
            RuleFor(x => x.Filter.AcademicSemesterId).GreaterThan(0).When(x => x.Filter.AcademicSemesterId.HasValue);
            RuleFor(x => x.Filter.ProjectPeriodId).GreaterThan(0).When(x => x.Filter.ProjectPeriodId.HasValue);
            RuleFor(x => x.Filter.DepartmentId).GreaterThan(0).When(x => x.Filter.DepartmentId.HasValue);
            RuleFor(x => x.Filter.MajorId).GreaterThan(0).When(x => x.Filter.MajorId.HasValue);
            RuleFor(x => x.Filter.ProjectMode).Must(x => x is null or "SINGLE_MAJOR" or "INTERDISCIPLINARY");
            RuleFor(x => x.Filter.Status).Must(x => x is "DRAFT" or "PUBLISHED" or "CLOSED");
            RuleFor(x => x.Filter.Search).MaximumLength(200);
        });
    }
}
