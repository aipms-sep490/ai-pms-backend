using FluentValidation;

namespace AIPMS.Application.Features.Academic.Validators;

internal static class AcademicValidationRules
{
    public static IRuleBuilderOptions<T, string> ValidAcademicCode<T>(
        this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty()
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$")
            .WithMessage(
                "'{PropertyName}' may contain only letters, numbers, underscores and hyphens.");

    public static IRuleBuilderOptions<T, string> ValidAcademicName<T>(
        this IRuleBuilder<T, string> ruleBuilder) =>
        ruleBuilder
            .NotEmpty()
            .MaximumLength(255);

    public static void AddDescriptionRule<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string?>> expression) =>
        validator.RuleFor(expression).MaximumLength(1000);

    public static void AddPagingRules<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, int>> pageExpression,
        System.Linq.Expressions.Expression<Func<T, int>> pageSizeExpression,
        System.Linq.Expressions.Expression<Func<T, string?>> searchExpression)
    {
        validator.RuleFor(pageExpression).GreaterThanOrEqualTo(1);
        validator.RuleFor(pageSizeExpression).InclusiveBetween(1, 100);
        validator.RuleFor(searchExpression).MaximumLength(255);
    }
}
