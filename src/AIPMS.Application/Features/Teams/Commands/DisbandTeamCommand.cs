using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record DisbandTeamCommand(long TeamId) : IRequest<TeamDto>;

public sealed class DisbandTeamCommandHandler(
    ITeamRepository repository,
    TeamWorkflow workflow,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider clock) : IRequestHandler<DisbandTeamCommand, TeamDto>
{
    public Task<TeamDto> Handle(DisbandTeamCommand request, CancellationToken cancellationToken) =>
        repository.InTransactionAsync(async ct =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId)
                throw new UnauthorizedException();
            if (!currentUser.Roles.Contains(AppRoles.Student))
                throw new ForbiddenException("Only student team leaders can disband their team.");

            var team = await repository.GetAsync(request.TeamId, ct)
                ?? throw new NotFoundException("Team", request.TeamId);

            if (team.Status == "DISBANDED")
                return await workflow.GetAsync(team.Id, ct);

            var leader = team.Members.SingleOrDefault(m => m.IsLeader);
            if (leader is null || leader.UserId != actorId)
                throw new ForbiddenException("Only the current team leader can disband the team.");

            if (team.Members.Count != 1)
                throw new ConflictException("Only a 1-member team can be disbanded. Remove other members or transfer leadership first.");

            if (team.ProjectStatuses.Any(s => !string.Equals(s, "REJECTED", StringComparison.OrdinalIgnoreCase)))
                throw new ConflictException(
                    "Cannot disband a team that already has a non-rejected project.",
                    WorkflowErrorCodes.TeamHasActiveProject);

            var now = clock.GetUtcNow().UtcDateTime;
            var window = await repository.GetOpenWindowAsync(team.SemesterId, now, ct);
            if (window is null)
            {
                if (await repository.HasExpiredRegistrationWindowAsync(team.SemesterId, now, ct))
                    throw new ConflictException("The registration window has closed (N1).", WorkflowErrorCodes.RegistrationWindowClosed);
                throw new ConflictException("No unambiguous active registration window exists in this semester.");
            }

            await repository.CancelAllPendingInvitationsAsync(team.Id, now, ct);
            await repository.RemoveMemberAsync(team.Id, actorId, now, ct);
            await repository.UpdateAsync(team.Id, team.Name, team.Description, "DISBANDED", now, ct);

            await auditTrail.RecordAsync(new AuditEntry(
                actorId,
                "TEAM_DISBANDED",
                "TEAM",
                team.Id,
                new Dictionary<string, object?>
                {
                    ["semesterId"] = team.SemesterId,
                    ["code"] = team.Code
                }), ct);

            return new TeamDto(
                team.Id,
                team.SemesterId,
                team.Code,
                team.Name,
                team.Description,
                "DISBANDED",
                [],
                new TeamEligibilityDto(false, true, window.PeriodId, null, ["TEAM_DISBANDED"]),
                team.AcademicScope is null ? null : TeamAcademicScopeDto.FromScope(team.AcademicScope));
        }, cancellationToken);
}
