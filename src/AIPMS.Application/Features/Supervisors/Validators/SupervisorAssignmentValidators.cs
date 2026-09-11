using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Validators;

public sealed class EndSupervisorAssignmentCommandValidator : AbstractValidator<EndSupervisorAssignmentCommand>
{
    public EndSupervisorAssignmentCommandValidator()
    {
        RuleFor(r => r.AssignmentId).GreaterThan(0);
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(2000);
    }
}

public sealed class GetSupervisorAssignmentQueryValidator : AbstractValidator<GetSupervisorAssignmentQuery>
{
    public GetSupervisorAssignmentQueryValidator() => RuleFor(r => r.AssignmentId).GreaterThan(0);
}

public sealed class GetSupervisorAssignmentsQueryValidator : AbstractValidator<GetSupervisorAssignmentsQuery>
{
    public GetSupervisorAssignmentsQueryValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0).When(r => r.ProjectId.HasValue);
        RuleFor(r => r.Status).Must(s => s is null or "ACTIVE" or "ENDED");
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
