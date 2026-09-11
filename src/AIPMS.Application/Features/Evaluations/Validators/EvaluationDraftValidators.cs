using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Evaluations.Validators;

public sealed class AssignEvaluatorRequestValidator : AbstractValidator<AssignEvaluatorRequest>
{
    public AssignEvaluatorRequestValidator()
    {
        RuleFor(r => r.EvaluatorId).GreaterThan(0);
        RuleFor(r => r.ProjectPeriodId).GreaterThan(0);
        RuleFor(r => r.EvaluationType).Must(t => t is "SUPERVISOR" or "LECTURER");
    }
}
public sealed class AssignEvaluatorCommandValidator : AbstractValidator<AssignEvaluatorCommand>
{
    public AssignEvaluatorCommandValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.Input).NotNull().SetValidator(new AssignEvaluatorRequestValidator());
    }
}
public sealed class RevokeEvaluatorRequestValidator : AbstractValidator<RevokeEvaluatorRequest>
{
    public RevokeEvaluatorRequestValidator()
    {
        RuleFor(r => r.ConcurrencyToken).Must(t => Guid.TryParse(t, out _));
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
    }
}
public sealed class RevokeEvaluatorCommandValidator : AbstractValidator<RevokeEvaluatorCommand>
{
    public RevokeEvaluatorCommandValidator()
    {
        RuleFor(r => r.Id).GreaterThan(0);
        RuleFor(r => r.Input).NotNull().SetValidator(new RevokeEvaluatorRequestValidator());
    }
}
public sealed class EvaluationScoreInputValidator : AbstractValidator<EvaluationScoreInput>
{
    public EvaluationScoreInputValidator()
    {
        RuleFor(s => s.RubricCriterionId).GreaterThan(0);
        RuleFor(s => s.Score).NotNull().GreaterThanOrEqualTo(0).LessThanOrEqualTo(999999.99m)
            .Must(s => !s.HasValue || decimal.Round(s.Value, 2) == s.Value);
        RuleFor(s => s.Comments).MaximumLength(2000);
    }
}
public sealed class SaveEvaluationDraftRequestValidator : AbstractValidator<SaveEvaluationDraftRequest>
{
    public SaveEvaluationDraftRequestValidator()
    {
        RuleFor(r => r.ConcurrencyToken).Must(t => Guid.TryParse(t, out _));
        RuleFor(r => r.Comments).MaximumLength(10000);
        RuleFor(r => r.Scores).NotNull().Must(s => s is null || s.Count <= 100)
            .Must(s => s is null || (s.All(x => x is not null) && s.Select(x => x.RubricCriterionId).Distinct().Count() == s.Count))
            .WithMessage("Each score must be non-null and refer to a distinct criterion.");
        RuleForEach(r => r.Scores).SetValidator(new EvaluationScoreInputValidator());
    }
}
public sealed class CreateEvaluationDraftCommandValidator : AbstractValidator<CreateEvaluationDraftCommand>
{
    public CreateEvaluationDraftCommandValidator() => RuleFor(r => r.AssignmentId).GreaterThan(0);
}
public sealed class SaveEvaluationDraftCommandValidator : AbstractValidator<SaveEvaluationDraftCommand>
{
    public SaveEvaluationDraftCommandValidator()
    {
        RuleFor(r => r.Id).GreaterThan(0);
        RuleFor(r => r.Input).NotNull().SetValidator(new SaveEvaluationDraftRequestValidator());
    }
}
public sealed class GetEvaluationDraftQueryValidator : AbstractValidator<GetEvaluationDraftQuery>
{
    public GetEvaluationDraftQueryValidator() => RuleFor(r => r.Id).GreaterThan(0);
}
public sealed class GetProjectEvaluationsQueryValidator : AbstractValidator<GetProjectEvaluationsQuery>
{
    public GetProjectEvaluationsQueryValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.Page).InclusiveBetween(1, 1000000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
public sealed class GetEvaluationAssignmentsQueryValidator : AbstractValidator<GetEvaluationAssignmentsQuery>
{
    public GetEvaluationAssignmentsQueryValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0).When(r => r.ProjectId.HasValue);
        RuleFor(r => r.Status).Must(s => s is null or "ACTIVE" or "REVOKED");
        RuleFor(r => r.Page).InclusiveBetween(1, 1000000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
