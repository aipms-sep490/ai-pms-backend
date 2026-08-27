using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Queries.GetSupervisorById;

public sealed class GetSupervisorByIdQueryValidator : AbstractValidator<GetSupervisorByIdQuery>
{
    public GetSupervisorByIdQueryValidator() => RuleFor(x => x.Id).GreaterThan(0);
}
