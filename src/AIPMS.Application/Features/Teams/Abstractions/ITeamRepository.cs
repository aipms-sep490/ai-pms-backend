using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamRepository
{
    Task ValidateAcademicScopeAsync(TeamAcademicScope scope, long organizationId, CancellationToken ct);
    Task SetAcademicScopeAsync(long teamId, TeamAcademicScope scope, Guid? expectedToken, CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task<TeamSnapshot?> GetAsync(long teamId, CancellationToken cancellationToken);
    Task<long?> GetCurrentTeamIdAsync(long semesterId, long userId, CancellationToken cancellationToken);
    Task<TeamParticipant?> GetStudentAsync(long userId, CancellationToken cancellationToken);
    Task<TeamRegistrationWindow?> GetOpenWindowAsync(long semesterId, DateTime now, CancellationToken cancellationToken);
    Task<bool> HasExpiredRegistrationWindowAsync(long semesterId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(false);
    Task<StudentQualificationEligibility> GetQualificationEligibilityAsync(
        long userId, long semesterId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(new StudentQualificationEligibility(false, true, "NOT_REQUIRED", null));
    Task<long> CreateAsync(long semesterId, string code, string name, string? description,
        long actorId, DateTime now, CancellationToken cancellationToken);
    Task UpdateAsync(long teamId, string name, string? description, string status, DateTime now, CancellationToken cancellationToken);
    Task AddMemberAsync(long teamId, long semesterId, long userId, bool isLeader, DateTime now, CancellationToken cancellationToken);
    Task RemoveMemberAsync(long teamId, long userId, DateTime now, CancellationToken cancellationToken);
    Task TransferLeaderAsync(long teamId, long oldLeaderId, long newLeaderId, DateTime now, CancellationToken cancellationToken);
    Task<TeamInvitationData?> GetInvitationAsync(long invitationId, CancellationToken cancellationToken);
    Task<TeamInvitationData?> GetPendingInvitationAsync(long teamId, long userId, CancellationToken cancellationToken);
    Task<TeamInvitationData> InviteAsync(long teamId, long invitedUserId, long actorId,
        string? message, DateTime expiresAt, DateTime now, CancellationToken cancellationToken);
    Task RespondAsync(long invitationId, string status, DateTime now, CancellationToken cancellationToken);
    Task<PagedResult<TeamInvitationData>> GetInvitationsAsync(long userId, long? teamId,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<bool> CanUserViewTeamAsync(long teamId, long userId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(true);
    Task CancelAllPendingInvitationsAsync(long teamId, DateTime now, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    Task<long?> GetTeamLeadDepartmentIdAsync(long teamId, CancellationToken cancellationToken) =>
        Task.FromResult<long?>(null);
    Task<(bool IsStaff, bool IsAdmin, long? DepartmentId)> GetStaffActorScopeAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult<(bool, bool, long?)>((false, false, null));
    Task NotifyTeamMembersAsync(long teamId, long actorUserId, string notificationType, string title, string content, DateTime now, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
