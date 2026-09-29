using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ActionItems.DTOs;

namespace AIPMS.Application.Features.ActionItems.Abstractions;

public interface IProjectActionItemRepository
{
    Task<ProjectActionItemDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<ProjectActionItemDto>> ListAsync(
        long projectId,
        string? sourceType,
        long? meetingId,
        long? progressReportId,
        string? status,
        long? ownerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ProjectActionItemDto> CreateAsync(
        long projectId,
        string sourceType,
        long? meetingId,
        long? progressReportId,
        string title,
        string? description,
        long? ownerId,
        long? taskId,
        long? milestoneId,
        DateTime? dueAt,
        long createdBy,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<ProjectActionItemDto> UpdateDetailsAsync(
        long id,
        string title,
        string? description,
        long? ownerId,
        long? taskId,
        long? milestoneId,
        DateTime? dueAt,
        Guid? expectedToken,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<ProjectActionItemDto> UpdateStatusAsync(
        long id,
        string newStatus,
        Guid? expectedToken,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<bool> IsMeetingInProjectAsync(long meetingId, long projectId, CancellationToken cancellationToken = default);

    Task<bool> IsProgressReportInProjectAsync(long reportId, long projectId, CancellationToken cancellationToken = default);

    Task<bool> IsTaskInProjectAsync(long taskId, long projectId, CancellationToken cancellationToken = default);

    Task<bool> IsMilestoneInProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken = default);

    Task<bool> IsEligibleOwnerAsync(long projectId, long userId, CancellationToken cancellationToken = default);

    Task<bool> IsMeetingParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken = default);

    Task<bool> IsMeetingCreatorAsync(long meetingId, long userId, CancellationToken cancellationToken = default);

    Task<bool> IsProjectLeaderAsync(long projectId, long userId, CancellationToken cancellationToken = default);

    Task<bool> IsAssignedSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken = default);

    Task<bool> IsTaskBelongsToMilestoneAsync(long taskId, long milestoneId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default);
}
