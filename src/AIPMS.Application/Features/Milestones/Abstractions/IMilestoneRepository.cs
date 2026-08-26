using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Milestones.DTOs;

namespace AIPMS.Application.Features.Milestones.Abstractions;

public interface IMilestoneRepository
{
    Task<MilestoneDto?> GetByIdAsync(long id, CancellationToken cancellationToken);
    
    Task<IReadOnlyList<MilestoneDto>> GetProjectMilestonesAsync(long projectId, CancellationToken cancellationToken);
    
    Task<MilestoneDto> CreateAsync(
        long projectId,
        string title,
        string? description,
        DateOnly? startDate,
        DateOnly? dueDate,
        int sortOrder,
        long createdByUserId,
        CancellationToken cancellationToken);
        
    Task<MilestoneDto> UpdateAsync(
        long id,
        string title,
        string? description,
        DateOnly? startDate,
        DateOnly? dueDate,
        string status,
        int sortOrder,
        CancellationToken cancellationToken);
        
    Task DeleteAsync(long id, CancellationToken cancellationToken);
    
    Task<bool> HasTasksAsync(long id, CancellationToken cancellationToken);
    
    Task ReorderAsync(IEnumerable<(long MilestoneId, int SortOrder)> reorderItems, CancellationToken cancellationToken);

    Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MilestoneProgressDto>> GetMilestoneProgressAsync(long projectId, CancellationToken cancellationToken);
}
