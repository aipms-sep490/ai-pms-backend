using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProgressMeetings.Abstractions;

namespace AIPMS.Application.Features.ProgressMeetings;

internal static class ProgressMeetingAccess
{
    internal static long UserId(ICurrentUser currentUser) =>
        currentUser.UserId ?? throw new ForbiddenException("An authenticated user is required.");

    internal static async Task EnsureProjectAccess(long userId, long projectId,
        IProgressMeetingRepository repository, CancellationToken ct)
    {
        if (!await repository.IsProjectMemberAsync(userId, projectId, ct)
            && await repository.GetActiveSupervisorAssignmentAsync(userId, projectId, ct) is null)
        {
            throw new ForbiddenException();
        }
    }

    internal static async Task EnsureScheduler(long userId, long projectId,
        IProgressMeetingRepository repository, CancellationToken ct)
    {
        if (!await repository.IsProjectLeaderAsync(userId, projectId, ct)
            && await repository.GetActiveSupervisorAssignmentAsync(userId, projectId, ct) is null)
        {
            throw new ForbiddenException("Only the project leader or an assigned supervisor can schedule meetings.");
        }
    }
}
