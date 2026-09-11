using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Evaluations.Validators;

public sealed class RubricCriterionInputValidator : AbstractValidator<RubricCriterionInput>
{
    public RubricCriterionInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.WeightPercent).GreaterThan(0).LessThanOrEqualTo(100).PrecisionScale(5, 2, true);
        RuleFor(x => x.MaxScore).GreaterThan(0).LessThanOrEqualTo(999999.99m).PrecisionScale(8, 2, true);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 9999);
    }
}

public sealed class CreateRubricRequestValidator : AbstractValidator<CreateRubricRequest>
{
    public CreateRubricRequestValidator()
    {
        RuleFor(x => x.DepartmentId).GreaterThan(0);
        RuleFor(x => x.AcademicSemesterId).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9][A-Za-z0-9_.-]*$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Criteria).NotNull().Must(x => x is null || x.Count <= 100)
            .Must(x => x is null || (x.All(c => c is not null) && x.Select(c => c.SortOrder).Distinct().Count() == x.Count))
            .WithMessage("Criteria must be non-null and have unique sort orders.");
        RuleForEach(x => x.Criteria).SetValidator(new RubricCriterionInputValidator());
    }
}

public sealed class UpdateRubricRequestValidator : AbstractValidator<UpdateRubricRequest>
{
    public UpdateRubricRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ConcurrencyToken).Must(x => Guid.TryParse(x, out _)).WithMessage("A valid concurrency token is required.");
        RuleFor(x => x.Criteria).NotNull().Must(x => x is null || x.Count <= 100)
            .Must(x => x is null || (x.All(c => c is not null) && x.Select(c => c.SortOrder).Distinct().Count() == x.Count))
            .WithMessage("Criteria must be non-null and have unique sort orders.");
        RuleForEach(x => x.Criteria).SetValidator(new RubricCriterionInputValidator());
    }
}

public sealed class CreateRubricCommandValidator : AbstractValidator<CreateRubricCommand>
{
    public CreateRubricCommandValidator() => RuleFor(x => x.Input).NotNull().SetValidator(new CreateRubricRequestValidator());
}
public sealed class UpdateRubricCommandValidator : AbstractValidator<UpdateRubricCommand>
{
    public UpdateRubricCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Input).NotNull().SetValidator(new UpdateRubricRequestValidator());
    }
}
public sealed class PublishRubricCommandValidator : AbstractValidator<PublishRubricCommand>
{
    public PublishRubricCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ConcurrencyToken).Must(x => Guid.TryParse(x, out _));
    }
}
public sealed class RetireRubricCommandValidator : AbstractValidator<RetireRubricCommand>
{
    public RetireRubricCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ConcurrencyToken).Must(x => Guid.TryParse(x, out _));
    }
}
public sealed class DeleteRubricCommandValidator : AbstractValidator<DeleteRubricCommand>
{
    public DeleteRubricCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.ConcurrencyToken).Must(x => Guid.TryParse(x, out _));
    }
}
public sealed class CreateRubricVersionRequestValidator : AbstractValidator<CreateRubricVersionRequest>
{
    public CreateRubricVersionRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9][A-Za-z0-9_.-]*$");
        RuleFor(x => x.ConcurrencyToken).Must(x => Guid.TryParse(x, out _));
    }
}
public sealed class CreateRubricVersionCommandValidator : AbstractValidator<CreateRubricVersionCommand>
{
    public CreateRubricVersionCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Input).NotNull().SetValidator(new CreateRubricVersionRequestValidator());
    }
}
public sealed class GetRubricQueryValidator : AbstractValidator<GetRubricQuery>
{
    public GetRubricQueryValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
public sealed class GetRubricsQueryValidator : AbstractValidator<GetRubricsQuery>
{
    public GetRubricsQueryValidator()
    {
        RuleFor(x => x.DepartmentId).GreaterThan(0).When(x => x.DepartmentId.HasValue);
        RuleFor(x => x.AcademicSemesterId).GreaterThan(0).When(x => x.AcademicSemesterId.HasValue);
        RuleFor(x => x.Status).Must(x => x is null or RubricStatuses.Draft or RubricStatuses.Published or RubricStatuses.Retired);
        RuleFor(x => x.Search).MaximumLength(255);
        RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
