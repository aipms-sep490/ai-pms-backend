using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using DomainModels = AIPMS.Domain.Entities;
using DbModels = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class SupervisorRequestRepository(AipmsDbContext dbContext) : ISupervisorRequestRepository
{
    public async Task AddAsync(DomainModels.SupervisorRequest request, CancellationToken cancellationToken)
    {
        var dbRequest = new DbModels.SupervisorRequest
        {
            ProjectId = request.ProjectId,
            SupervisorProfileId = request.SupervisorProfileId,
            RequestedBy = request.RequestedBy,
            Status = request.Status,
            RequestMessage = request.RequestMessage,
            ResponseMessage = request.ResponseMessage,
            RequestedAt = request.RequestedAt,
            RespondedAt = request.RespondedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await dbContext.SupervisorRequests.AddAsync(dbRequest, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        request.Id = dbRequest.Id;
    }

    public async Task<DomainModels.SupervisorRequest?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var r = await dbContext.SupervisorRequests
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (r == null) return null;

        return new DomainModels.SupervisorRequest
        {
            Id = r.Id,
            ProjectId = r.ProjectId,
            SupervisorProfileId = r.SupervisorProfileId,
            RequestedBy = r.RequestedBy,
            Status = r.Status,
            RequestMessage = r.RequestMessage,
            ResponseMessage = r.ResponseMessage,
            RequestedAt = r.RequestedAt,
            RespondedAt = r.RespondedAt,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }

    public async Task<DomainModels.SupervisorRequest?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken)
    {
        var r = await dbContext.SupervisorRequests
            .FromSqlInterpolated($"SELECT * FROM dbo.supervisor_requests WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return r == null ? null : new DomainModels.SupervisorRequest
        {
            Id = r.Id,
            ProjectId = r.ProjectId,
            SupervisorProfileId = r.SupervisorProfileId,
            RequestedBy = r.RequestedBy,
            Status = r.Status,
            RequestMessage = r.RequestMessage,
            ResponseMessage = r.ResponseMessage,
            RequestedAt = r.RequestedAt,
            RespondedAt = r.RespondedAt,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }

    public async Task<bool> HasPendingRequestAsync(long projectId, long supervisorProfileId, CancellationToken cancellationToken)
    {
        return await dbContext.SupervisorRequests
            .AnyAsync(x => x.ProjectId == projectId
                        && x.SupervisorProfileId == supervisorProfileId
                        && x.Status == "PENDING", cancellationToken);
    }
    public async Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken)
    {
        return await dbContext.Projects
            .AnyAsync(x => x.Id == projectId, cancellationToken);
    }
    public async Task<bool> IsProjectApprovedAsync(long projectId, CancellationToken cancellationToken)
    {
        return await dbContext.Projects
            .AnyAsync(x => x.Id == projectId && x.Status == "APPROVED", cancellationToken);
    }

    public async Task ActivateProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        if (project == null) return;

        project.Status = "ACTIVE";
        project.UpdatedAt = DateTime.UtcNow;
    }

    public async Task InitializeProjectWorkspaceAsync(
        long projectId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        const string workspaceTitle = "Project Workspace";
        if (await dbContext.Milestones.AnyAsync(
                x => x.ProjectId == projectId && x.Title == workspaceTitle,
                cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;
        await dbContext.Milestones.AddAsync(new DbModels.Milestone
        {
            ProjectId = projectId,
            Title = workspaceTitle,
            Description = "Default workspace initialized when the supervisor assignment is accepted.",
            Status = "PLANNED",
            SortOrder = 0,
            CreatedBy = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);
    }

    public async Task UpdateAsync(DomainModels.SupervisorRequest request, CancellationToken cancellationToken)
    {
        var r = await dbContext.SupervisorRequests
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

        if (r == null) return;

        r.Status = request.Status;
        r.ResponseMessage = request.ResponseMessage;
        r.RespondedAt = request.RespondedAt;
        r.UpdatedAt = DateTime.UtcNow;

        dbContext.SupervisorRequests.Update(r);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
