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

public sealed class SupervisorAssignmentRepository(AipmsDbContext dbContext) : ISupervisorAssignmentRepository
{
    public async Task AddAsync(DomainModels.SupervisorAssignment assignment, CancellationToken cancellationToken)
    {
        var dbAssignment = new DbModels.SupervisorAssignment
        {
            ProjectId = assignment.ProjectId,
            SupervisorProfileId = assignment.SupervisorProfileId,
            SupervisorRequestId = assignment.SupervisorRequestId,
            IsPrimary = assignment.IsPrimary,
            AssignedAt = assignment.AssignedAt,
            EndedAt = assignment.EndedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await dbContext.SupervisorAssignments.AddAsync(dbAssignment, cancellationToken);
    }

    public async Task<int> CountActiveAssignmentsAsync(long supervisorProfileId, CancellationToken cancellationToken)
    {
        return await dbContext.SupervisorAssignments
            .CountAsync(x => x.SupervisorProfileId == supervisorProfileId && x.EndedAt == null, cancellationToken);
    }

    public async Task<DomainModels.SupervisorAssignment?> GetActiveAssignmentByProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        var a = await dbContext.SupervisorAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.EndedAt == null, cancellationToken);

        if (a == null) return null;

        return new DomainModels.SupervisorAssignment
        {
            Id = a.Id,
            ProjectId = a.ProjectId,
            SupervisorProfileId = a.SupervisorProfileId,
            SupervisorRequestId = a.SupervisorRequestId,
            IsPrimary = a.IsPrimary,
            AssignedAt = a.AssignedAt,
            EndedAt = a.EndedAt,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt
        };
    }

    public async Task<DomainModels.SupervisorAssignment?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var a = await dbContext.SupervisorAssignments
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (a == null) return null;

        return new DomainModels.SupervisorAssignment
        {
            Id = a.Id,
            ProjectId = a.ProjectId,
            SupervisorProfileId = a.SupervisorProfileId,
            SupervisorRequestId = a.SupervisorRequestId,
            IsPrimary = a.IsPrimary,
            AssignedAt = a.AssignedAt,
            EndedAt = a.EndedAt,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt
        };
    }

    public async Task UpdateAsync(DomainModels.SupervisorAssignment assignment, CancellationToken cancellationToken)
    {
        var a = await dbContext.SupervisorAssignments
            .FirstOrDefaultAsync(x => x.Id == assignment.Id, cancellationToken);

        if (a == null) return;

        a.EndedAt = assignment.EndedAt;
        a.UpdatedAt = DateTime.UtcNow;

        dbContext.SupervisorAssignments.Update(a);
    }
}
