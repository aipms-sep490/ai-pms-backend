using AIPMS.Application.Features.Teams.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Teams.Validators;

public sealed class RequestTeamLeaderChangeCommandValidator : AbstractValidator<RequestTeamLeaderChangeCommand>
{
    public RequestTeamLeaderChangeCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.NewLeaderUserId).GreaterThan(0);
        RuleFor(x => x.Message).MaximumLength(2000);
    }
}

public sealed class RequestOrTransferTeamLeaderCommandValidator
    : AbstractValidator<RequestOrTransferTeamLeaderCommand>
{
    public RequestOrTransferTeamLeaderCommandValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0);
        RuleFor(x => x.NewLeaderUserId).GreaterThan(0);
        RuleFor(x => x.Message).MaximumLength(2000);
    }
}

public abstract class RespondToTeamLeaderChangeValidator<T> : AbstractValidator<T>
    where T : class
{
    protected RespondToTeamLeaderChangeValidator()
    {
        RuleFor(x => GetRequestId(x)).GreaterThan(0);
        RuleFor(x => GetMessage(x)).MaximumLength(2000);
    }

    private static long GetRequestId(T value) => value switch
    {
        ApproveTeamLeaderChangeCommand approve => approve.RequestId,
        RejectTeamLeaderChangeCommand reject => reject.RequestId,
        _ => 0
    };

    private static string? GetMessage(T value) => value switch
    {
        ApproveTeamLeaderChangeCommand approve => approve.Message,
        RejectTeamLeaderChangeCommand reject => reject.Message,
        _ => null
    };
}

public sealed class ApproveTeamLeaderChangeCommandValidator
    : RespondToTeamLeaderChangeValidator<ApproveTeamLeaderChangeCommand> { }

public sealed class RejectTeamLeaderChangeCommandValidator
    : RespondToTeamLeaderChangeValidator<RejectTeamLeaderChangeCommand> { }

public sealed class GetTeamLeaderChangeRequestsQueryValidator
    : AbstractValidator<GetTeamLeaderChangeRequestsQuery>
{
    public GetTeamLeaderChangeRequestsQueryValidator()
    {
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId.HasValue);
        RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).Must(value => value is null or "PENDING" or "APPROVED" or "REJECTED" or "CANCELLED")
            .WithMessage("Status must be PENDING, APPROVED, REJECTED or CANCELLED.");
    }
}
