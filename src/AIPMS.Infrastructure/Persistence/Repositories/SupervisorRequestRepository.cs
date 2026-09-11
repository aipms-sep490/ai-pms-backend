using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Domain.Projects;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SupervisorRequest = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorRequest;
using SupervisorAssignment = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorAssignment;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class SupervisorRequestRepository(AipmsDbContext context) : ISupervisorRequestRepository
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is not null) return await action(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            if (IsConflict(ex))
                throw new ConflictException("The request, project or supervisor changed concurrently. Reload and retry.");
            throw;
        }
    }

    public async Task LockRequestAsync(long requestId, CancellationToken ct)
    {
        RequireTransaction();
        await context.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.supervisor_requests WITH (UPDLOCK, HOLDLOCK) WHERE id = {requestId}")
            .ToListAsync(ct);
    }

    public async Task LockSupervisorAndProjectAsync(long profileId, long projectId, CancellationToken ct)
    {
        RequireTransaction();
        // Serialize capacity checks for the same supervisor across different projects,
        // then protect the project's single active assignment even for different supervisors.
        var profiles = await context.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.supervisor_profiles WITH (UPDLOCK, HOLDLOCK) WHERE id = {profileId}")
            .ToListAsync(ct);
        if (profiles.Count == 0) throw new NotFoundException("SupervisorProfile", profileId);
        var projects = await context.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}")
            .ToListAsync(ct);
        if (projects.Count == 0) throw new NotFoundException("Project", projectId);
    }

    public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken ct) =>
        context.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId
            && p.Team.TeamMembers.Any(m => m.UserId == userId && m.IsLeader && m.LeftAt == null), ct);

    public Task<SupervisorRequestModel?> GetAsync(long requestId, CancellationToken ct) =>
        context.SupervisorRequests.AsNoTracking().Where(r => r.Id == requestId)
            .Select(SupervisorRequestMapper.Projection).SingleOrDefaultAsync(ct);

    public async Task<PagedResult<SupervisorRequestModel>> SearchAsync(SupervisorRequestSearch search, CancellationToken ct)
    {
        if (search.ProjectId is null && search.SupervisorUserId is null)
            throw new InvalidOperationException("Supervisor request queries must have a project or inbox scope.");
        var query = context.SupervisorRequests.AsNoTracking();
        if (search.ProjectId.HasValue) query = query.Where(r => r.ProjectId == search.ProjectId);
        if (search.SupervisorUserId.HasValue) query = query.Where(r => r.SupervisorProfile.UserId == search.SupervisorUserId);
        if (search.Status is not null) query = query.Where(r => r.Status == search.Status);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(SupervisorRequestMapper.Projection).ToListAsync(ct);
        return new(items, search.Page, search.PageSize, count);
    }

    public Task<bool> HasPendingAsync(long projectId, long profileId, CancellationToken ct) =>
        context.SupervisorRequests.AnyAsync(r => r.ProjectId == projectId
            && r.SupervisorProfileId == profileId && r.Status == "PENDING", ct);

    public async Task<SupervisorWorkload> GetWorkloadAsync(long profileId, long semesterId, CancellationToken ct) =>
        await context.SupervisorProfiles.AsNoTracking().Where(p => p.Id == profileId)
            .Select(p => new SupervisorWorkload(p.MaxActiveProjects,
                p.SupervisorAssignments.Where(a => a.EndedAt == null).Select(a => a.ProjectId).Distinct().Count(),
                p.SupervisorAssignments.Where(a => a.EndedAt == null && a.Project.Team.AcademicSemesterId == semesterId)
                    .Select(a => a.ProjectId).Distinct().Count())).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("SupervisorProfile", profileId);

    public async Task<SupervisorRequestModel> CreateAsync(long projectId, long profileId, long actorId,
        string? message, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var request = new SupervisorRequest { ProjectId = projectId, SupervisorProfileId = profileId,
            RequestedBy = actorId, Status = "PENDING", RequestMessage = message,
            RequestedAt = now, CreatedAt = now, UpdatedAt = now };
        context.SupervisorRequests.Add(request);
        await context.SaveChangesAsync(ct);
        return (await GetAsync(request.Id, ct))!;
    }

    public async Task<SupervisorRequestModel> RespondAsync(long requestId, string status, string? message, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var request = await context.SupervisorRequests.SingleAsync(r => r.Id == requestId, ct);
        if (request.Status != "PENDING") throw new ConflictException("The request has already been processed.");
        request.Status = status;
        request.ResponseMessage = message;
        request.RespondedAt = now;
        request.UpdatedAt = now;
        await context.SaveChangesAsync(ct);
        return (await GetAsync(request.Id, ct))!;
    }

    public async Task<long> AssignAndActivateAsync(SupervisorRequestModel request, long actorId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var project = await context.Projects.SingleAsync(p => p.Id == request.ProjectId, ct);
        void Transition(string next)
        {
            var currentStatus = Enum.Parse<ProjectStatus>(project.Status.Replace("_", ""), true);
            var nextStatus = Enum.Parse<ProjectStatus>(next.Replace("_", ""), true);
            if (!ProjectStateMachine.CanTransition(currentStatus, nextStatus))
                throw new ConflictException("The project cannot be activated from its current state.");
            context.ProjectStatusHistories.Add(new() { ProjectId = project.Id, OldStatus = project.Status,
                NewStatus = next, ChangedBy = actorId, ChangedAt = now, Reason = $"Supervisor request {request.Id} accepted." });
            project.Status = next;
        }
        if (project.Status == "APPROVED") Transition("SUPERVISOR_PENDING");
        Transition("ACTIVE");
        project.UpdatedAt = now;
        var assignment = new SupervisorAssignment { ProjectId = request.ProjectId,
            SupervisorProfileId = request.SupervisorProfileId, SupervisorRequestId = request.Id,
            IsPrimary = true, AssignedAt = now, CreatedAt = now, UpdatedAt = now };
        context.SupervisorAssignments.Add(assignment);
        await context.SaveChangesAsync(ct);
        return assignment.Id;
    }

    public async Task<IReadOnlyList<SupervisorRequestModel>> GetOtherPendingAsync(long projectId, long requestId, CancellationToken ct) =>
        await context.SupervisorRequests.AsNoTracking().Where(r => r.ProjectId == projectId && r.Id != requestId && r.Status == "PENDING")
            .OrderBy(r => r.Id).Select(SupervisorRequestMapper.Projection).ToListAsync(ct);

    private static bool IsConflict(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException || current is SqlException { Number: 1205 or 1222 or 2601 or 2627 }) return true;
        return false;
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Supervisor request mutations require a transaction including permission checks and audit.");
    }
}
