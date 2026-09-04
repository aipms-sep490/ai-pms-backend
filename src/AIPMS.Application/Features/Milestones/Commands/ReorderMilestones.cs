using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Commands;

public sealed record MilestoneReorderItem(long MilestoneId, int SortOrder);

public sealed record ReorderMilestonesCommand(
    long ProjectId,
    IReadOnlyList<MilestoneReorderItem> Items) : IRequest;

public sealed class ReorderMilestonesCommandValidator : AbstractValidator<ReorderMilestonesCommand>
{
    public ReorderMilestonesCommandValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");

        RuleFor(static x => x.Items)
            .NotEmpty().WithMessage("Reorder items cannot be empty.")
            .Must(static items => items != null && items.Select(static i => i.MilestoneId).Distinct().Count() == items.Count)
            .WithMessage("Milestone IDs in reorder items must be unique.")
            .Must(static items => items != null && items.All(static i => i.SortOrder >= 0))
            .WithMessage("SortOrder cannot be negative.")
            .Must(static items => items != null && items.Select(static i => i.SortOrder).Distinct().Count() == items.Count)
            .WithMessage("SortOrder values in reorder items must be unique.");
    }
}

public sealed class ReorderMilestonesCommandHandler(
    IMilestoneRepository repository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<ReorderMilestonesCommand>
{
    public async Task Handle(
        ReorderMilestonesCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveAsync(request.ProjectId, cancellationToken);

        // Verify user is authorized: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(request.ProjectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to reorder milestones for this project.");
        }

        // Verify all milestones in request belong to the project
        var projectMilestones = await repository.GetProjectMilestonesAsync(request.ProjectId, cancellationToken);
        var projectMilestoneIds = projectMilestones.Select(static m => m.Id).ToHashSet();

        if (request.Items.Any(i => !projectMilestoneIds.Contains(i.MilestoneId)))
        {
            throw new ConflictException("One or more milestone IDs do not belong to this project.");
        }

        var reorderItems = request.Items.Select(static i => (i.MilestoneId, i.SortOrder)).ToArray();
        await repository.ReorderAsync(reorderItems, cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "MILESTONES_REORDERED",
                "PROJECT",
                request.ProjectId,
                new Dictionary<string, object?>
                {
                    ["projectId"] = request.ProjectId,
                    ["milestoneCount"] = request.Items.Count
                }),
            cancellationToken);
    }
}
