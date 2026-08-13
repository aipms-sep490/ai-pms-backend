using FluentValidation;
using MediatR;
using ApplicationValidationException = AIPMS.Application.Common.Exceptions.ValidationException;

namespace AIPMS.Application.Common.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestValidators = validators.ToArray();
        if (requestValidators.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(
            requestValidators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var errors = validationResults
            .SelectMany(static result => result.Errors)
            .Where(static failure => failure is not null)
            .GroupBy(static failure => failure.PropertyName)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static failure => failure.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray());

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        return await next();
    }
}
