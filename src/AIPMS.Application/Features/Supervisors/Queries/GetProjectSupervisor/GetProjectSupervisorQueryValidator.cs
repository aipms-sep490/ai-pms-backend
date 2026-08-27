using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Queries.GetProjectSupervisor;

public sealed class GetProjectSupervisorQueryValidator : AbstractValidator<GetProjectSupervisorQuery>
{
    public GetProjectSupervisorQueryValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}
