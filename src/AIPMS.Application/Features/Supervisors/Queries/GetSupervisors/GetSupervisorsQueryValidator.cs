using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Queries.GetSupervisors;

public sealed class GetSupervisorsQueryValidator : AbstractValidator<GetSupervisorsQuery>
{
    public GetSupervisorsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThan(0)
            .WithMessage("Page number must be greater than 0.");

        RuleFor(query => query.PageSize)
            .GreaterThan(0)
            .WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100)
            .WithMessage("Page size cannot exceed 100.");
    }
}
