using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using MediatR;

namespace AIPMS.Application.Features.ProgressReports.Commands;

public sealed record UpdateReportingCycleCommand(
    long ProjectId,
    long CycleId,
    UpdateReportingCycleRequest Request) : IRequest<ReportingCycleDto>;

public sealed class UpdateReportingCycleCommandHandler(
    IReportingCycleRepository repository,
    IProgressReportRepository progressReportRepository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<UpdateReportingCycleCommand, ReportingCycleDto>
{
    public async Task<ReportingCycleDto> Handle(UpdateReportingCycleCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = command.ProjectId;
        var cycleId = command.CycleId;

        if (!await progressReportRepository.ProjectExistsAsync(projectId, cancellationToken))
            throw new NotFoundException("Project", projectId);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        var existing = await repository.GetByIdAsync(cycleId, cancellationToken)
            ?? throw new NotFoundException("ProgressReportPeriod", cycleId);

        if (existing.ProjectId != projectId)
            throw new NotFoundException("ProgressReportPeriod", cycleId);

        var isAdmin = await repository.HasAdminRoleInDbAsync(actorId, cancellationToken);
        var isStaff = await repository.HasStaffRoleInDbAsync(actorId, cancellationToken);

        if (!isAdmin && !isStaff)
            throw new ForbiddenException("Only Admin or Department Staff can update reporting cycles.");

        if (isStaff && !isAdmin)
        {
            if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
                throw new ForbiddenException("You can only update reporting cycles for projects in your department.");
        }

        Guid? expectedToken = null;
        if (!string.IsNullOrWhiteSpace(command.Request.ConcurrencyToken))
        {
            if (Guid.TryParse(command.Request.ConcurrencyToken, out var parsed))
            {
                expectedToken = parsed;
            }
            else
            {
                throw new ConflictException("Invalid concurrency token format.", WorkflowErrorCodes.StaleConcurrencyToken);
            }
        }

        // Normalize optional DateTimeOffset instants to UTC DateTime before repository call.
        var periodStart = command.Request.PeriodStart?.UtcDateTime;
        var periodEnd = command.Request.PeriodEnd?.UtcDateTime;
        var deadline = command.Request.Deadline?.UtcDateTime;

        var now = clock.GetUtcNow().UtcDateTime;
        return await repository.UpdateAsync(
            cycleId,
            periodStart,
            periodEnd,
            deadline,
            command.Request.LatePolicy,
            expectedToken,
            now,
            onUpdated: async updated =>
            {
                await audit.RecordAsync(new AuditEntry(
                    actorId,
                    "REPORTING_CYCLE_UPDATED",
                    "PROGRESS_REPORT_PERIOD",
                    updated.Id,
                    new Dictionary<string, object?>
                    {
                        ["projectId"] = projectId,
                        ["periodStart"] = updated.PeriodStart.ToString("o"),
                        ["periodEnd"] = updated.PeriodEnd.ToString("o"),
                        ["deadline"] = updated.Deadline.ToString("o"),
                        ["latePolicy"] = updated.LatePolicy
                    }), cancellationToken);
            },
            cancellationToken: cancellationToken);
    }
}
