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

public sealed record SubmitProgressReportCommand(long Id) : IRequest<ProgressReportDto>;

public sealed class SubmitProgressReportCommandHandler(
    IProgressReportRepository repository,
    IProjectAccessService projectAccess,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IRequestHandler<SubmitProgressReportCommand, ProgressReportDto>
{
    public async Task<ProgressReportDto> Handle(SubmitProgressReportCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var projectId = await repository.GetProjectIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("ProgressReport", command.Id);

        await executionGuard.MustBeActiveAsync(projectId, cancellationToken);

        if (!await projectAccess.CanAccessAsync(actorId, projectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's progress reports.");

        if (!currentUser.Roles.Contains(AppRoles.Admin) && !await repository.IsTeamLeaderAsync(projectId, actorId, cancellationToken))
            throw new ForbiddenException("Only the team leader can submit progress reports.");

        var status = await repository.GetStatusAsync(command.Id, cancellationToken);
        if (status != "DRAFT")
            throw new ConflictException("Progress report is already submitted.");

        var now = clock.GetUtcNow().UtcDateTime;
        var result = await repository.SubmitAsync(command.Id, actorId, now, cancellationToken);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "PROGRESS_REPORT_SUBMITTED",
            "PROGRESS_REPORT",
            result.Id,
            new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["submittedAt"] = result.SubmittedAt?.ToString("o"),
                ["isLate"] = result.IsLate
            }), cancellationToken);

        return result;
    }
}
