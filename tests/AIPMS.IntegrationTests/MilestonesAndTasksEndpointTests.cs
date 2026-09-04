using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.Commands;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.Milestones.Queries;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.Commands;
using AIPMS.Application.Features.Tasks.DTOs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AIPMS.IntegrationTests;

public sealed record UpdateTaskStatusRequest(string NewStatus, string? Reason);

public sealed class MilestonesAndTasksEndpointTests : IClassFixture<MilestonesAndTasksEndpointTests.MilestoneWebApplicationFactory>
{
    public class MilestoneWebApplicationFactory : AipmsWebApplicationFactory
    {
        public TestMilestoneRepo MilestoneRepository { get; } = new();
        public TestTaskRepo TaskRepository { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMilestoneRepository>();
                services.AddSingleton<IMilestoneRepository>(MilestoneRepository);

                services.RemoveAll<ITaskRepository>();
                services.AddSingleton<ITaskRepository>(TaskRepository);
            });
        }
    }

    private readonly MilestoneWebApplicationFactory _factory;

    public MilestonesAndTasksEndpointTests(MilestoneWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Milestone Endpoint Tests ─────────────────────────────────────────────

    [Fact]
    public async Task CreateMilestone_AuthorizedLeader_Returns201Created()
    {
        var client = _factory.CreateAuthenticatedClient(10, "leader@aipms.test", "Leader", AppRoles.Student);

        var request = new CreateMilestoneCommand(1, "Sprint 1", "Initial Phase", null, null, 0);
        var response = await client.PostAsJsonAsync("api/v1/milestones", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<MilestoneDto>();
        Assert.NotNull(dto);
        Assert.Equal("Sprint 1", dto.Title);
    }

    [Fact]
    public async Task GetMilestones_Returns200OKList()
    {
        var projectId = 1L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        var response = await client.GetAsync($"api/v1/milestones/project/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var milestones = await response.Content.ReadFromJsonAsync<IReadOnlyList<MilestoneDto>>();
        Assert.NotNull(milestones);
    }

    [Fact]
    public async Task ReorderMilestones_ValidItems_Returns204NoContent()
    {
        var projectId = 1L;
        var client = _factory.CreateAuthenticatedClient(10, "leader@aipms.test", "Leader", AppRoles.Student);

        var items = new[]
        {
            new MilestoneReorderItem(1, 0),
            new MilestoneReorderItem(2, 1)
        };

        var response = await client.PostAsJsonAsync($"api/v1/milestones/project/{projectId}/reorder", items);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Task Endpoint Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateTask_AuthorizedLeader_Returns201Created()
    {
        var client = _factory.CreateAuthenticatedClient(10, "leader@aipms.test", "Leader", AppRoles.Student);

        var command = new CreateTaskCommand(1, null, "Setup DB", "Schema creation", "HIGH", null, null, [10]);
        var response = await client.PostAsJsonAsync("api/v1/tasks", command);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(dto);
        Assert.Equal("Setup DB", dto.Title);
    }

    [Fact]
    public async Task UpdateTaskStatus_ValidTransition_Returns200OK()
    {
        var taskId = 1L;
        var client = _factory.CreateAuthenticatedClient(10, "leader@aipms.test", "Leader", AppRoles.Student);

        var request = new UpdateTaskStatusRequest("IN_PROGRESS", null);
        var response = await client.PutAsJsonAsync($"api/v1/tasks/{taskId}/status", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetTaskAssignees_ValidAssignees_Returns200OK()
    {
        var taskId = 1L;
        var client = _factory.CreateAuthenticatedClient(10, "leader@aipms.test", "Leader", AppRoles.Student);

        var assigneeUserIds = new[] { 10L, 11L };
        var response = await client.PostAsJsonAsync($"api/v1/tasks/{taskId}/assignees", assigneeUserIds);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddTaskDependency_ValidIds_Returns200OK()
    {
        var client = _factory.CreateAuthenticatedClient(10, "leader@aipms.test", "Leader", AppRoles.Student);

        var command = new AddTaskDependencyCommand(2L, 1L, "FINISH_TO_START");
        var response = await client.PostAsJsonAsync("api/v1/tasks/dependency", command);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemoveTaskDependency_ValidIds_Returns200OK()
    {
        var taskId = 2L;
        var dependsOnId = 1L;
        var client = _factory.CreateAuthenticatedClient(10, "leader@aipms.test", "Leader", AppRoles.Student);

        var response = await client.DeleteAsync($"api/v1/tasks/{taskId}/dependency/{dependsOnId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_Returns200OK()
    {
        var projectId = 1L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        var response = await client.GetAsync($"api/v1/tasks/project/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

// ── Test Double Repositories for Endpoint Testing ─────────────────────────────

public class TestMilestoneRepo : IMilestoneRepository
{
    public List<MilestoneDto> Milestones { get; } = new()
    {
        new(1, 1, "Milestone 1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow),
        new(2, 1, "Milestone 2", "Desc", null, null, "IN_PROGRESS", 1, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow)
    };

    public Task<MilestoneDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Milestones.FirstOrDefault(m => m.Id == id));

    public Task<IReadOnlyList<MilestoneDto>> GetProjectMilestonesAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MilestoneDto>>(Milestones.Where(m => m.ProjectId == projectId).ToList());

    public Task<MilestoneDto> CreateAsync(long projectId, string title, string? description, DateOnly? startDate, DateOnly? dueDate, int sortOrder, long createdByUserId, CancellationToken cancellationToken)
    {
        var milestone = new MilestoneDto(Milestones.Count + 1, projectId, title, description, startDate, dueDate, "IN_PROGRESS", sortOrder, createdByUserId, "User", DateTime.UtcNow, DateTime.UtcNow);
        Milestones.Add(milestone);
        return Task.FromResult(milestone);
    }

    public Task<MilestoneDto> UpdateAsync(long id, string title, string? description, DateOnly? startDate, DateOnly? dueDate, string status, int sortOrder, CancellationToken cancellationToken) =>
        Task.FromResult(new MilestoneDto(id, 1, title, description, startDate, dueDate, status, sortOrder, 10, "User", DateTime.UtcNow, DateTime.UtcNow));

    public Task DeleteAsync(long id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReorderAsync(IEnumerable<(long MilestoneId, int SortOrder)> items, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> HasTasksAsync(long milestoneId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<IReadOnlyList<MilestoneProgressDto>> GetMilestoneProgressAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MilestoneProgressDto>>(Array.Empty<MilestoneProgressDto>());
}

public class TestTaskRepo : ITaskRepository
{
    public List<TaskDto> Tasks { get; } = new()
    {
        new(1, 1, null, "Task 1", "Desc", "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()),
        new(2, 1, null, "Task 2", "Desc", "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>())
    };

    public Task<TaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Tasks.FirstOrDefault(t => t.Id == id));

    public Task<PagedResult<TaskDto>> GetTasksAsync(long projectId, long? milestoneId, string? status, string? priority, long? assigneeUserId, string? search, DateTime? dueFrom, DateTime? dueTo, bool? isOverdue, bool? isBlocked, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<TaskDto>(Tasks, Tasks.Count, page, pageSize));

    public Task<TaskDto> CreateAsync(long milestoneId, long? parentTaskId, string title, string? description, string? priority, DateTime? startAt, DateTime? dueAt, IReadOnlyList<long> assigneeUserIds, long createdByUserId, CancellationToken cancellationToken)
    {
        var task = new TaskDto(Tasks.Count + 1, milestoneId, parentTaskId, title, description, "TODO", priority ?? "NORMAL", startAt, dueAt, null, createdByUserId, "User", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        Tasks.Add(task);
        return Task.FromResult(task);
    }

    public Task<TaskDto> UpdateAsync(long id, long milestoneId, long? parentTaskId, string title, string? description, string? priority, DateTime? startAt, DateTime? dueAt, CancellationToken cancellationToken) =>
        Task.FromResult(new TaskDto(id, milestoneId, parentTaskId, title, description, "TODO", priority ?? "NORMAL", startAt, dueAt, null, 10, "User", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));

    public Task DeleteAsync(long id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> HasHistoricalDataAsync(long id, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task SetAssigneesAsync(long taskId, IEnumerable<long> userIds, long assignedByUserId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddDependencyAsync(long taskId, long dependsOnTaskId, string dependencyType, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveDependencyAsync(long taskId, long dependsOnTaskId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UpdateStatusAsync(long taskId, string newStatus, string? reason, long actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<TaskStatusHistoryDto>> GetStatusHistoryAsync(long taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskStatusHistoryDto>>(Array.Empty<TaskStatusHistoryDto>());

    public Task<bool> IsUserActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> TaskBelongsToProjectAsync(long taskId, long projectId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> MilestoneBelongsToProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> IsTaskAssigneeAsync(long taskId, long userId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<long?> GetParentTaskIdAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<long?>(null);

    public Task<IEnumerable<long>> GetDependsOnTaskIdsAsync(long taskId, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<long>>(Array.Empty<long>());

    public Task<(IReadOnlyList<TaskDto> Overdue, IReadOnlyList<TaskDto> Blocked)> GetOverdueAndBlockedAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(( (IReadOnlyList<TaskDto>)Array.Empty<TaskDto>(), (IReadOnlyList<TaskDto>)Array.Empty<TaskDto>() ));
}
