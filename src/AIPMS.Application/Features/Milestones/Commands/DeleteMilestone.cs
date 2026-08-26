using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Commands;

public sealed record DeleteMilestoneCommand(long Id) : IRequest;

public sealed class DeleteMilestoneCommandValidator : AbstractValidator<DeleteMilestoneCommand>
{
    public DeleteMilestoneCommandValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Milestone ID must be greater than 0.");
    }
}

public sealed class DeleteMilestoneCommandHandler(
    IMilestoneRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<DeleteMilestoneCommand>
{
    public async Task Handle(
        DeleteMilestoneCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve existing milestone
        var milestone = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Milestone", request.Id);

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveForMilestoneAsync(request.Id, cancellationToken);

        // Verify user is authorized: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(milestone.ProjectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to delete milestones for this project.");
        }

        // Safe Deletion Policy: Reject if tasks exist
        if (await repository.HasTasksAsync(request.Id, cancellationToken))
        {
            throw new ConflictException("Milestone cannot be deleted because it contains tasks. Cancel the milestone instead.");
        }

        await repository.DeleteAsync(request.Id, cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "MILESTONE_DELETED",
                "MILESTONE",
                request.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = milestone.ProjectId,
                    ["title"] = milestone.Title
                }),
            cancellationToken);
    }
}
