using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Application.Features.Tasks.DTOs;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class TaskHandlerTests
{
    [Fact]
    public async Task CreateTask_AssigneeOutsideTeam_ThrowsConflictException()
    {
        var repository = new StubTaskRepository
        {
            IsLeaderOrSupervisor = true,
            ActiveTeamMember = false // Assignee 10 is not in active team
        };
        var milestoneRepository = new StubMilestoneRepository
        {
            ExistingMilestone = new MilestoneDto(1, 1, "M1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User", DateTime.UtcNow, DateTime.UtcNow)
        };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(100, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new CreateTaskCommandHandler(repository, milestoneRepository, executionGuard, currentUser, auditTrail);
        var cmd = new CreateTaskCommand(1, null, "Task 1", "Desc", "NORMAL", null, null, [10]);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteTask_TaskWithHistoricalDataOrAssignees_ThrowsConflictException()
    {
        var repository = new StubTaskRepository
        {
            IsLeaderOrSupervisor = true,
            ExistingTask = new TaskDto(1, 1, null, "Task 1", "Desc", "TODO", "NORMAL", null, null, null, 100, "User 100", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()),
            HasHistoricalData = true // Has assignees or history
        };
        var milestoneRepository = new StubMilestoneRepository
        {
            ExistingMilestone = new MilestoneDto(1, 1, "M1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User", DateTime.UtcNow, DateTime.UtcNow)
        };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(100, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new DeleteTaskCommandHandler(repository, milestoneRepository, executionGuard, currentUser, auditTrail);
        var cmd = new DeleteTaskCommand(1);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateTaskStatus_InvalidTransition_ThrowsConflictException()
    {
        var repository = new StubTaskRepository
        {
            IsLeaderOrSupervisor = true,
            IsAssignee = true,
            ExistingTask = new TaskDto(1, 1, null, "Task 1", "Desc", "TODO", "NORMAL", null, null, null, 100, "User 100", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>())
        };
        var milestoneRepository = new StubMilestoneRepository
        {
            ExistingMilestone = new MilestoneDto(1, 1, "M1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User", DateTime.UtcNow, DateTime.UtcNow)
        };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(100, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new UpdateTaskStatusCommandHandler(repository, milestoneRepository, executionGuard, currentUser, auditTrail);
        // Invalid state transition: TODO -> DONE is not allowed directly
        var cmd = new UpdateTaskStatusCommand(1, "DONE", null);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateTaskStatus_AssigneeWhoLeftTeam_ThrowsForbiddenException()
    {
        // Regression: user is task assignee but left the team (LeftAt != null)
        // → must be rejected even though task_assignees row still exists
        var repository = new StubTaskRepository
        {
            IsLeaderOrSupervisor = false,
            IsAssignee = true,       // has task_assignees row
            ActiveTeamMember = false, // but no longer active in team_members
            ExistingTask = new TaskDto(1, 1, null, "Task 1", "Desc", "TODO", "NORMAL", null, null, null, 100, "User 100", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>())
        };
        var milestoneRepository = new StubMilestoneRepository
        {
            ExistingMilestone = new MilestoneDto(1, 1, "M1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User", DateTime.UtcNow, DateTime.UtcNow)
        };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(200, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new UpdateTaskStatusCommandHandler(repository, milestoneRepository, executionGuard, currentUser, auditTrail);
        var cmd = new UpdateTaskStatusCommand(1, "IN_PROGRESS", null);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateTaskStatus_ActiveAssignee_PassesAuthorizationGuard()
    {
        // Active assignee (still on team) should pass the guard — will hit state machine
        var repository = new StubTaskRepository
        {
            IsLeaderOrSupervisor = false,
            IsAssignee = true,
            ActiveTeamMember = true, // still active
            ExistingTask = new TaskDto(1, 1, null, "Task 1", "Desc", "TODO", "NORMAL", null, null, null, 100, "User 100", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>())
        };
        var milestoneRepository = new StubMilestoneRepository
        {
            ExistingMilestone = new MilestoneDto(1, 1, "M1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User", DateTime.UtcNow, DateTime.UtcNow)
        };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(200, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new UpdateTaskStatusCommandHandler(repository, milestoneRepository, executionGuard, currentUser, auditTrail);
        // TODO → IN_PROGRESS is a valid transition — handler must complete without exception
        var cmd = new UpdateTaskStatusCommand(1, "IN_PROGRESS", null);

        // Should not throw — active assignee is authorized and transition is valid
        var result = await handler.Handle(cmd, CancellationToken.None);
        Assert.NotNull(result);
    }
}

internal sealed class StubTaskRepository : ITaskRepository
{
    public bool IsLeaderOrSupervisor { get; set; } = true;
    public bool IsAssignee { get; set; } = true;
    public bool ActiveTeamMember { get; set; } = true;
    public bool HasHistoricalData { get; set; } = false;
    public TaskDto? ExistingTask { get; set; }

    public Task<TaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingTask?.Id == id ? ExistingTask : null);

    public Task<PagedResult<TaskDto>> GetTasksAsync(long projectId, long? milestoneId, string? status, string? priority, long? assigneeUserId, string? search, DateTime? dueFrom, DateTime? dueTo, bool? isOverdue, bool? isBlocked, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<TaskDto>(Array.Empty<TaskDto>(), page, pageSize, 0));

    public Task<TaskDto> CreateAsync(long milestoneId, long? parentTaskId, string title, string? description, string? priority, DateTime? startAt, DateTime? dueAt, IReadOnlyList<long> assigneeUserIds, long createdByUserId, CancellationToken cancellationToken) =>
        Task.FromResult(new TaskDto(1, milestoneId, parentTaskId, title, description, "TODO", priority ?? "NORMAL", startAt, dueAt, null, createdByUserId, "User", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));

    public Task<TaskDto> UpdateAsync(long id, long milestoneId, long? parentTaskId, string title, string? description, string? priority, DateTime? startAt, DateTime? dueAt, CancellationToken cancellationToken) =>
        Task.FromResult(new TaskDto(id, milestoneId, parentTaskId, title, description, "TODO", priority ?? "NORMAL", startAt, dueAt, null, 100, "User", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));

    public Task DeleteAsync(long id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> HasHistoricalDataAsync(long id, CancellationToken cancellationToken) => Task.FromResult(HasHistoricalData);

    public Task SetAssigneesAsync(long taskId, IEnumerable<long> userIds, long assignedByUserId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddDependencyAsync(long taskId, long dependsOnTaskId, string dependencyType, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveDependencyAsync(long taskId, long dependsOnTaskId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateStatusAsync(long taskId, string newStatus, string? reason, long actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<TaskStatusHistoryDto>> GetStatusHistoryAsync(long taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskStatusHistoryDto>>(Array.Empty<TaskStatusHistoryDto>());

    public Task<bool> IsUserActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(ActiveTeamMember);

    public Task<bool> TaskBelongsToProjectAsync(long taskId, long projectId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> MilestoneBelongsToProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(IsLeaderOrSupervisor);

    public Task<bool> IsTaskAssigneeAsync(long taskId, long userId, CancellationToken cancellationToken) => Task.FromResult(IsAssignee);

    public Task<long?> GetParentTaskIdAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);

    public Task<IEnumerable<long>> GetDependsOnTaskIdsAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<long>>(Array.Empty<long>());

    public Task<(IReadOnlyList<TaskDto> Overdue, IReadOnlyList<TaskDto> Blocked)> GetOverdueAndBlockedAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(( (IReadOnlyList<TaskDto>)Array.Empty<TaskDto>(), (IReadOnlyList<TaskDto>)Array.Empty<TaskDto>() ));
}
