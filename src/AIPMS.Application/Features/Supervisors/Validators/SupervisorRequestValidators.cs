using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Supervisors.Validators;

public sealed class SendSupervisorRequestCommandValidator : AbstractValidator<SendSupervisorRequestCommand>
{
    public SendSupervisorRequestCommandValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.SupervisorProfileId).GreaterThan(0);
        RuleFor(r => r.Message).MaximumLength(2000);
    }
}
public sealed class CancelSupervisorRequestCommandValidator : AbstractValidator<CancelSupervisorRequestCommand>
{
    public CancelSupervisorRequestCommandValidator() => RuleFor(r => r.RequestId).GreaterThan(0);
}
public sealed class AcceptSupervisorRequestCommandValidator : AbstractValidator<AcceptSupervisorRequestCommand>
{
    public AcceptSupervisorRequestCommandValidator()
    {
        RuleFor(r => r.RequestId).GreaterThan(0);
        RuleFor(r => r.Message).MaximumLength(2000);
    }
}
public sealed class RejectSupervisorRequestCommandValidator : AbstractValidator<RejectSupervisorRequestCommand>
{
    public RejectSupervisorRequestCommandValidator()
    {
        RuleFor(r => r.RequestId).GreaterThan(0);
        RuleFor(r => r.Message).MaximumLength(2000);
    }
}
public sealed class GetSupervisorRequestsQueryValidator : AbstractValidator<GetSupervisorRequestsQuery>
{
    public GetSupervisorRequestsQueryValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0).When(r => r.ProjectId.HasValue);
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
        RuleFor(r => r.Status).Must(s => s is null or "PENDING" or "ACCEPTED" or "REJECTED" or "CANCELLED");
    }
}
