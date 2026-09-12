using AIPMS.Application.Features.WorkflowContext.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.WorkflowContext.Validators;

public sealed class GetUserWorkflowContextQueryValidator : AbstractValidator<GetUserWorkflowContextQuery>
{
    public GetUserWorkflowContextQueryValidator() => RuleFor(x => x.AcademicSemesterId).GreaterThan(0).When(x => x.AcademicSemesterId.HasValue);
}
public sealed class GetTeamWorkflowActionsQueryValidator : AbstractValidator<GetTeamWorkflowActionsQuery>
{
    public GetTeamWorkflowActionsQueryValidator() => RuleFor(x => x.TeamId).GreaterThan(0);
}
public sealed class GetProjectWorkflowActionsQueryValidator : AbstractValidator<GetProjectWorkflowActionsQuery>
{
    public GetProjectWorkflowActionsQueryValidator() => RuleFor(x => x.ProjectId).GreaterThan(0);
}
