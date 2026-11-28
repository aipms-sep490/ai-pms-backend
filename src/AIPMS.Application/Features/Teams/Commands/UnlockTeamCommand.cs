using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using AIPMS.Domain.Teams;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record UnlockTeamCommand(long TeamId, string Reason) : IRequest<TeamDto>;

public sealed class UnlockTeamCommandHandler(
    ITeamRepository repository,
    TeamWorkflow workflow,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider clock) : IRequestHandler<UnlockTeamCommand, TeamDto>
{
    public Task<TeamDto> Handle(UnlockTeamCommand request, CancellationToken cancellationToken) =>
        repository.InTransactionAsync(async ct =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId)
                throw new UnauthorizedException();

            var (isStaff, isAdmin, actorDeptId) = await repository.GetStaffActorScopeAsync(actorId, ct);
            if (!isStaff)
            {
                throw new ForbiddenException(
                    isAdmin
                        ? "Platform administrators without DEPARTMENT_STAFF role cannot unlock student teams."
                        : "Only lead department staff can unlock a locked team.",
                    isAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : null);
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["A reason is required to unlock a team."] });

            var team = await repository.GetAsync(request.TeamId, ct)
                ?? throw new NotFoundException("Team", request.TeamId);

            var leadDeptId = await repository.GetTeamLeadDepartmentIdAsync(team.Id, ct);
            if (leadDeptId.HasValue && actorDeptId != leadDeptId.Value)
                throw new ForbiddenException("Only staff of the team's lead department can unlock this team.");

            if (team.Status == "DISBANDED")
                throw new ConflictException("A disbanded team cannot be unlocked.");

            if (team.ProjectStatuses.Any(TeamRules.ProjectLocksRoster))
                throw new ConflictException(
                    "Cannot unlock a team whose project is already submitted, under review, approved, or active.",
                    WorkflowErrorCodes.TeamHasActiveProject);

            var now = clock.GetUtcNow().UtcDateTime;
            var trimmedReason = request.Reason.Trim();
            if (team.Status == "LOCKED")
            {
                await repository.UpdateAsync(team.Id, team.Name, team.Description, "FORMING", now, ct);
                await auditTrail.RecordAsync(new AuditEntry(
                    actorId,
                    "TEAM_UNLOCKED_BY_DEPARTMENT",
                    "TEAM",
                    team.Id,
                    new Dictionary<string, object?>
                    {
                        ["previousStatus"] = team.Status,
                        ["newStatus"] = "FORMING",
                        ["reason"] = trimmedReason
                    }), ct);
                await repository.NotifyTeamMembersAsync(
                    team.Id,
                    actorId,
                    "TEAM_UNLOCKED_BY_DEPARTMENT",
                    $"Khoa đã mở khóa nhóm {team.Name}",
                    $"Nhóm {team.Name} đã được Khoa chủ trì mở khóa đội hình. Lý do: {trimmedReason}",
                    now,
                    ct);
            }

            return await workflow.GetAsync(team.Id, ct);
        }, cancellationToken);
}
