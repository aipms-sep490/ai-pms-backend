using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;

namespace AIPMS.UnitTests.AI;

internal sealed class StubTextGenerationProvider : IAiTextGenerationProvider
{
    public string ReturnText { get; set; } = string.Empty;

    public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReturnText);
}

internal sealed class StubProjectProgressDataReader : IProjectProgressDataReader
{
    public bool ProjectExists { get; set; } = true;
    public ProjectProgressFacts? Facts { get; set; }

    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(ProjectExists);

    public Task<ProjectProgressFacts?> GetProjectProgressFactsAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult<ProjectProgressFacts?>(Facts ?? new ProjectProgressFacts(projectId, "ACTIVE", 1, 4, Array.Empty<MilestoneFact>(), Array.Empty<TaskFact>(), Array.Empty<ProgressReportFact>(), Array.Empty<MeetingFact>()));
}

internal sealed class StubProjectAccessService : IProjectAccessService
{
    public bool CanAccess { get; set; } = true;

    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CanAccess);
}

internal sealed class StubCrossProjectAccessService(long allowedProjectId) : IProjectAccessService
{
    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(projectId == allowedProjectId);
}

internal sealed class TestCurrentUser(long userId, string role) : ICurrentUser
{
    public long? UserId => userId;
    public string? Email => "test@example.com";
    public string? FullName => "Test User";
    public string? Role => role;
    public IReadOnlyCollection<string> Roles => new[] { role };
    public bool IsAuthenticated => true;
    public long? DepartmentId => null;
}

internal sealed class UnauthenticatedTestCurrentUser : ICurrentUser
{
    public long? UserId => null;
    public string? Email => null;
    public string? FullName => null;
    public string? Role => null;
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();
    public bool IsAuthenticated => false;
    public long? DepartmentId => null;
}

internal sealed class FakeTimeProvider(DateTime fixedTimeUtc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(fixedTimeUtc, TimeSpan.Zero);
}

internal sealed class StubProgressReportRepository : IProgressReportRepository
{
    public Dictionary<long, ProgressReportDetailDto> Reports { get; } = new();

    public Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Reports.TryGetValue(id, out var r) ? r : null);

    public Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult<ProgressReportDto?>(null);

    public Task<PagedResult<ProgressReportDto>> GetReportsAsync(long projectId, string? reportType, string? status, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var list = new List<ProgressReportDto>();
        foreach (var r in Reports.Values)
        {
            if (r.ProjectId == projectId)
            {
                list.Add(new ProgressReportDto(r.Id, r.ProjectId, r.SubmittedBy, r.SubmittedByName, r.ReportType, r.PeriodStart, r.PeriodEnd, r.Summary, r.CompletedWork, r.PlannedWork, r.IssuesAndRisks, r.Status, r.SubmittedAt, r.IsLate, r.CreatedAt, r.UpdatedAt));
            }
        }
        return Task.FromResult(new PagedResult<ProgressReportDto>(list, page, pageSize, list.Count));
    }

    public Task<bool> ExistsForPeriodAsync(long projectId, string reportType, DateOnly periodStart, DateOnly periodEnd, long? excludeId, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, Func<ProgressReportDto, Task>? onCreated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProgressReportDto> UpdateAsync(long id, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ProgressReportDto> UpdateAsync(long id, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, Func<ProgressReportDto, Task>? onUpdated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProgressReportDto> SubmitAsync(long id, long actorId, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ProgressReportDto> SubmitAsync(long id, long actorId, DateTime now, Func<ProgressReportDto, Task>? onSubmitted, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, Func<ProgressReportFeedbackDto, Task>? onAdded, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<long?> GetProjectIdAsync(long reportId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
    public Task<string?> GetStatusAsync(long reportId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
}

internal sealed class StubMilestoneRepository : IMilestoneRepository
{
    public List<MilestoneDto> Milestones { get; } = new();
    public Task<IReadOnlyList<MilestoneDto>> GetProjectMilestonesAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MilestoneDto>>(Milestones);
    public Task<MilestoneDto?> GetByIdAsync(long id, CancellationToken cancellationToken) => Task.FromResult<MilestoneDto?>(null);
    public Task<MilestoneDto> CreateAsync(long projectId, string title, string? description, DateOnly? startDate, DateOnly? dueDate, int sortOrder, long createdByUserId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MilestoneDto> UpdateAsync(long id, string title, string? description, DateOnly? startDate, DateOnly? dueDate, string status, int sortOrder, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task DeleteAsync(long id, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<bool> HasTasksAsync(long id, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task ReorderAsync(IEnumerable<(long MilestoneId, int SortOrder)> reorderItems, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<IReadOnlyList<MilestoneProgressDto>> GetMilestoneProgressAsync(long projectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MilestoneProgressDto>>(Array.Empty<MilestoneProgressDto>());
}

internal class StubTaskRepository : ITaskRepository
{
    public List<TaskDto> Tasks { get; } = new();
    public long? LastQueriedProjectId { get; set; }
    public int UpdateStatusCallCount { get; set; }
    public int DeleteCallCount { get; set; }
    public int CreateCallCount { get; set; }

    public Task<PagedResult<TaskDto>> GetTasksAsync(long projectId, long? milestoneId, string? status, string? priority, long? assigneeUserId, string? search, DateTime? dueFrom, DateTime? dueTo, bool? isOverdue, bool? isBlocked, int page, int pageSize, CancellationToken cancellationToken)
    {
        LastQueriedProjectId = projectId;
        var query = Tasks.AsEnumerable();
        if (isBlocked.HasValue)
        {
            query = query.Where(t => (string.Equals(t.Status, "BLOCKED", StringComparison.OrdinalIgnoreCase)) == isBlocked.Value);
        }
        var list = query.ToList();
        var pagedItems = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<TaskDto>(pagedItems, page, pageSize, list.Count));
    }

    public Task<TaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken) => Task.FromResult<TaskDto?>(null);
    public Task<TaskDto> CreateAsync(long milestoneId, long? parentTaskId, string title, string? description, string? priority, DateTime? startAt, DateTime? dueAt, IReadOnlyList<long> assigneeUserIds, long createdByUserId, CancellationToken cancellationToken)
    {
        CreateCallCount++;
        return Task.FromResult<TaskDto>(null!);
    }
    public Task<TaskDto> UpdateAsync(long id, long milestoneId, long? parentTaskId, string title, string? description, string? priority, DateTime? startAt, DateTime? dueAt, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        DeleteCallCount++;
        return Task.CompletedTask;
    }
    public Task<bool> HasHistoricalDataAsync(long id, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task SetAssigneesAsync(long taskId, IEnumerable<long> userIds, long assignedByUserId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AddDependencyAsync(long taskId, long dependsOnTaskId, string dependencyType, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RemoveDependencyAsync(long taskId, long dependsOnTaskId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<TaskDependencyDto>> GetDependenciesAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TaskDependencyDto>>(Array.Empty<TaskDependencyDto>());
    public Task<IReadOnlyList<TaskDependencyDto>> GetDependentsAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TaskDependencyDto>>(Array.Empty<TaskDependencyDto>());
    public Task UpdateStatusAsync(long taskId, string newStatus, string? reason, long actorUserId, CancellationToken cancellationToken)
    {
        UpdateStatusCallCount++;
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<TaskStatusHistoryDto>> GetStatusHistoryAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TaskStatusHistoryDto>>(Array.Empty<TaskStatusHistoryDto>());
    public Task<bool> MilestoneBelongsToProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> TaskBelongsToProjectAsync(long taskId, long projectId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<long?> GetProjectIdForTaskAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
    public Task<bool> IsUserActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> IsTaskAssigneeAsync(long taskId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<long?> GetParentTaskIdAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
    public Task<IEnumerable<long>> GetDependsOnTaskIdsAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<long>>(Array.Empty<long>());
    public Task<(IReadOnlyList<TaskDto> Overdue, IReadOnlyList<TaskDto> Blocked)> GetOverdueAndBlockedAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(((IReadOnlyList<TaskDto>)Array.Empty<TaskDto>(), (IReadOnlyList<TaskDto>)Array.Empty<TaskDto>()));
}

internal sealed class StubMeetingRepository : IMeetingRepository
{
    public List<MeetingDto> Meetings { get; } = new();
    public Task<PagedResult<MeetingDto>> GetMeetingsAsync(long projectId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<MeetingDto>(Meetings, page, pageSize, Meetings.Count));
    public Task<MeetingDto?> GetByIdAsync(long id, CancellationToken cancellationToken) => Task.FromResult<MeetingDto?>(null);
    public Task<MeetingDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken) => Task.FromResult<MeetingDetailDto?>(null);
    public Task<MeetingDto> CreateAsync(long projectId, long createdBy, string title, string? agenda, DateTime startAt, DateTime? endAt, string? location, string? onlineUrl, IReadOnlyList<long>? participantUserIds, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MeetingDto> CreateAsync(long projectId, long createdBy, string title, string? agenda, DateTime startAt, DateTime? endAt, string? location, string? onlineUrl, IReadOnlyList<long>? participantUserIds, DateTime now, Func<MeetingDto, Task>? onCreated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<MeetingDto> UpdateAsync(long id, string title, string? agenda, DateTime startAt, DateTime? endAt, string? location, string? onlineUrl, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MeetingDto> UpdateAsync(long id, string title, string? agenda, DateTime startAt, DateTime? endAt, string? location, string? onlineUrl, DateTime now, Func<MeetingDto, Task>? onUpdated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<MeetingDto> CancelAsync(long id, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MeetingDto> CancelAsync(long id, DateTime now, Func<MeetingDto, Task>? onCancelled, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<MeetingDto> CompleteAsync(long id, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MeetingDto> CompleteAsync(long id, DateTime now, Func<MeetingDto, Task>? onCompleted, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<MeetingDto> UpdateNotesAsync(long id, string? meetingNotes, IReadOnlyList<ParticipantAttendanceUpdate>? attendances, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MeetingDto> UpdateNotesAsync(long id, string? meetingNotes, IReadOnlyList<ParticipantAttendanceUpdate>? attendances, DateTime now, Func<MeetingDto, Task>? onNotesUpdated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<MeetingParticipantDto> AddParticipantAsync(long meetingId, long userId, string? attendanceStatus, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MeetingParticipantDto> AddParticipantAsync(long meetingId, long userId, string? attendanceStatus, DateTime now, Func<MeetingParticipantDto, Task>? onAdded, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task RemoveParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task RemoveParticipantAsync(long meetingId, long userId, Func<Task>? onRemoved, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<MeetingFeedbackDto> AddFeedbackAsync(long meetingId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<MeetingFeedbackDto> AddFeedbackAsync(long meetingId, long supervisorAssignmentId, string feedbackText, DateTime now, Func<MeetingFeedbackDto, Task>? onAdded, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<long?> GetProjectIdAsync(long meetingId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
    public Task<string?> GetStatusAsync(long meetingId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);
    public Task<bool> UserBelongsToProjectTeamAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> IsProjectMemberOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> CanManageMeetingAsync(long meetingId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> CanScheduleMeetingAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> IsParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class StubContributionRepository : IContributionRepository
{
    public ContributionSummaryDto? Summary { get; set; }
    public Task<ContributionSummaryDto> GetSummaryAsync(long projectId, bool storedOnly, CancellationToken ct) =>
        Task.FromResult(Summary ?? new ContributionSummaryDto("ACTIVE", 0.1, Array.Empty<ContributionMemberDto>(), 1, 20, 0, "v1", null, DateTime.UtcNow));
    public Task<T> InProjectTransactionAsync<T>(long projectId, Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);
    public Task<bool> CanRebuildAsync(long projectId, long actorId, CancellationToken ct) => Task.FromResult(true);
    public Task<bool> IsActiveUserAsync(long actorId, CancellationToken ct) => Task.FromResult(true);
    public Task<IReadOnlyList<ContributionEvidenceDto>> GetEvidenceAsync(long projectId, long userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ContributionEvidenceDto>>(Array.Empty<ContributionEvidenceDto>());
    public Task<ContributionRebuildResult> RebuildSnapshotAsync(long projectId, DateTime snapshotAt, CancellationToken ct) => Task.FromResult(new ContributionRebuildResult(Summary!, true));
}
