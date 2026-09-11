using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.Services;

public sealed class SupervisorAccessService(ICurrentUser currentUser, ISupervisorProfileRepository repository)
{
    public async Task<SupervisorAccount> EnsureCanReadAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long userId)
            throw new UnauthorizedException();
        var actor = await repository.GetAccountAsync(userId, ct);
        if (actor is null || !actor.IsActive) throw new ForbiddenException("The account is not active.");
        return actor;
    }

    public async Task<long> EnsureCanEditAsync(long targetUserId, CancellationToken ct)
    {
        var actor = await EnsureCanReadAsync(ct);
        var target = await repository.GetAccountAsync(targetUserId, ct)
            ?? throw new NotFoundException("User", targetUserId);
        // Check scope from persisted account data rather than trusting client-provided department IDs.
        var permitted = actor.Roles.Contains(AppRoles.Admin)
            || (actor.UserId == target.UserId && actor.Roles.Contains(AppRoles.Lecturer))
            || (actor.Roles.Contains(AppRoles.DepartmentStaff) && actor.HasActiveAcademicScope
                && actor.DepartmentId is not null && actor.DepartmentId == target.DepartmentId);
        if (!permitted) throw new ForbiddenException("You cannot manage this supervisor profile.");
        if (!target.IsActive || !target.HasActiveAcademicScope || !target.Roles.Contains(AppRoles.Lecturer))
            throw new ConflictException("A supervisor must be an active lecturer in an active department and organization.");
        return actor.UserId;
    }
}
