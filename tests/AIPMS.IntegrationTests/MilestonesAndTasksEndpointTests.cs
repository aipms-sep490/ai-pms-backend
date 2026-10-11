using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
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
                // This suite uses in-memory repositories; SQL atomicity is tested separately.
                services.RemoveAll<AIPMS.Application.Features.Disciplines.Abstractions.IDisciplineService>();
                foreach (var descriptor in services.Where(d => d.ImplementationType == typeof(AIPMS.Infrastructure.Services.Projects.ExecutionConcurrencyBehavior<,>)).ToArray())
                    services.Remove(descriptor);
                services.RemoveAll<IMilestoneRepository>();
                services.AddSingleton<IMilestoneRepository>(MilestoneRepository);

                services.RemoveAll<ITaskRepository>();
                services.AddSingleton<ITaskRepository>(TaskRepository);

                services.RemoveAll<IProjectExecutionGuard>();
                services.AddSingleton<IProjectExecutionGuard, NoOpProjectExecutionGuard>();

                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail, NoOpAuditTrail>();

                services.RemoveAll<IProjectAccessService>();
                services.AddSingleton<IProjectAccessService, AllowAllProjectAccessService>();
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

    [Fact]
    public async Task GetTasks_PaginationMetadata_IsCorrect()
    {
        // TestTaskRepo seeds 2 tasks; request page=1, pageSize=10
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        var response = await client.GetAsync("api/v1/tasks/project/1?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.TotalCount);   // 2 seeded tasks
        Assert.Equal(1, result.TotalPages);   // ceil(2/10) = 1
    }

    [Fact]
    public async Task GetTasks_DepartmentStaffOutsideScope_ReturnsForbidden()
    {
        // AllowAllProjectAccessService is replaced by a scoped factory that returns Forbidden
        using var factory = new DeniedAccessWebApplicationFactory();
        var client = factory.CreateAuthenticatedClient(99, "staff@other.test", "Other Staff", AppRoles.DepartmentStaff);

        var response = await client.GetAsync("api/v1/tasks/project/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_WithValidMajorId_Returns200OK()
    {
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        var response = await client.GetAsync("api/v1/tasks/project/1?majorId=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_WithInvalidMajorId_Returns400BadRequest()
    {
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        var response = await client.GetAsync("api/v1/tasks/project/1?majorId=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_SingleMajor_ReturnsOnlyMappedTasks()
    {
        const long projectId = 101L;
        const long majorA = 11L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        _factory.TaskRepository.ProjectMajors.Add((majorA, projectId));

        var taskA = new TaskDto(1001, 1, null, "Task A", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        var taskOther = new TaskDto(1002, 1, null, "Task Other", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        _factory.TaskRepository.Tasks.Add(taskA);
        _factory.TaskRepository.Tasks.Add(taskOther);
        _factory.TaskRepository.TaskProjects[1001] = projectId;
        _factory.TaskRepository.TaskProjects[1002] = projectId;
        _factory.TaskRepository.TaskDisciplines[1001] = new HashSet<long> { majorA };

        var response = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(paged);
        Assert.Single(paged.Items);
        Assert.Equal(1001L, paged.Items[0].Id);
    }

    [Fact]
    public async Task GetTasks_Interdisciplinary_SeparatesMajors()
    {
        const long projectId = 102L;
        const long majorA = 21L;
        const long majorB = 22L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        _factory.TaskRepository.ProjectMajors.Add((majorA, projectId));
        _factory.TaskRepository.ProjectMajors.Add((majorB, projectId));

        var taskA = new TaskDto(2001, 1, null, "Task A", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        var taskB = new TaskDto(2002, 1, null, "Task B", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        _factory.TaskRepository.Tasks.Add(taskA);
        _factory.TaskRepository.Tasks.Add(taskB);
        _factory.TaskRepository.TaskProjects[2001] = projectId;
        _factory.TaskRepository.TaskProjects[2002] = projectId;
        _factory.TaskRepository.TaskDisciplines[2001] = new HashSet<long> { majorA };
        _factory.TaskRepository.TaskDisciplines[2002] = new HashSet<long> { majorB };

        // ?majorId=A returns A, not B
        var responseA = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorA}");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var pagedA = await responseA.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(pagedA);
        Assert.Single(pagedA.Items);
        Assert.Equal(2001L, pagedA.Items[0].Id);

        // ?majorId=B returns B, not A
        var responseB = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorB}");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var pagedB = await responseB.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(pagedB);
        Assert.Single(pagedB.Items);
        Assert.Equal(2002L, pagedB.Items[0].Id);
    }

    [Fact]
    public async Task GetTasks_MultiDisciplineTask_DoesNotDuplicate()
    {
        const long projectId = 103L;
        const long majorA = 31L;
        const long majorB = 32L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        _factory.TaskRepository.ProjectMajors.Add((majorA, projectId));
        _factory.TaskRepository.ProjectMajors.Add((majorB, projectId));

        var taskMulti = new TaskDto(3001, 1, null, "Task Shared", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        _factory.TaskRepository.Tasks.Add(taskMulti);
        _factory.TaskRepository.TaskProjects[3001] = projectId;
        _factory.TaskRepository.TaskDisciplines[3001] = new HashSet<long> { majorA, majorB };

        // Appears exactly once when filtering by major A
        var responseA = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorA}");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var pagedA = await responseA.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(pagedA);
        Assert.Single(pagedA.Items);
        Assert.Equal(3001L, pagedA.Items[0].Id);

        // Appears exactly once when filtering by major B
        var responseB = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorB}");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var pagedB = await responseB.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(pagedB);
        Assert.Single(pagedB.Items);
        Assert.Equal(3001L, pagedB.Items[0].Id);
    }

    [Fact]
    public async Task GetTasks_PaginationAndTotalCount_ReflectsMajorFilter()
    {
        const long projectId = 104L;
        const long majorA = 41L;
        const long majorB = 42L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        _factory.TaskRepository.ProjectMajors.Add((majorA, projectId));
        _factory.TaskRepository.ProjectMajors.Add((majorB, projectId));

        var t1 = new TaskDto(4001, 1, null, "Task 1", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        var t2 = new TaskDto(4002, 1, null, "Task 2", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        var t3 = new TaskDto(4003, 1, null, "Task 3", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        var t4 = new TaskDto(4004, 1, null, "Task 4", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        var t5 = new TaskDto(4005, 1, null, "Task 5", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());

        _factory.TaskRepository.Tasks.AddRange([t1, t2, t3, t4, t5]);
        _factory.TaskRepository.TaskProjects[4001] = projectId;
        _factory.TaskRepository.TaskProjects[4002] = projectId;
        _factory.TaskRepository.TaskProjects[4003] = projectId;
        _factory.TaskRepository.TaskProjects[4004] = projectId;
        _factory.TaskRepository.TaskProjects[4005] = projectId;

        _factory.TaskRepository.TaskDisciplines[4001] = new HashSet<long> { majorA };
        _factory.TaskRepository.TaskDisciplines[4002] = new HashSet<long> { majorA };
        _factory.TaskRepository.TaskDisciplines[4003] = new HashSet<long> { majorA };
        _factory.TaskRepository.TaskDisciplines[4004] = new HashSet<long> { majorB };
        _factory.TaskRepository.TaskDisciplines[4005] = new HashSet<long> { majorB };

        // Page 1
        var resP1 = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorA}&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, resP1.StatusCode);
        var p1 = await resP1.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(p1);
        Assert.Equal(3, p1.TotalCount);
        Assert.Equal(2, p1.TotalPages);
        Assert.Equal(2, p1.Items.Count);
        Assert.All(p1.Items, item => Assert.Contains(item.Id, new[] { 4001L, 4002L }));

        // Page 2
        var resP2 = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorA}&page=2&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, resP2.StatusCode);
        var p2 = await resP2.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(p2);
        Assert.Equal(3, p2.TotalCount);
        Assert.Equal(2, p2.TotalPages);
        Assert.Single(p2.Items);
        Assert.Equal(4003L, p2.Items[0].Id);
    }

    [Fact]
    public async Task GetTasks_CombinedFilter_AppliesMajorAndStatusPredicates()
    {
        const long projectId = 105L;
        const long majorA = 51L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        _factory.TaskRepository.ProjectMajors.Add((majorA, projectId));

        var t1 = new TaskDto(5001, 1, null, "Frontend Architecture", null, "TODO", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        var t2 = new TaskDto(5002, 1, null, "Database Architecture", null, "DONE", "NORMAL", null, null, null, 10, "User 10", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());

        _factory.TaskRepository.Tasks.AddRange([t1, t2]);
        _factory.TaskRepository.TaskProjects[5001] = projectId;
        _factory.TaskRepository.TaskProjects[5002] = projectId;
        _factory.TaskRepository.TaskDisciplines[5001] = new HashSet<long> { majorA };
        _factory.TaskRepository.TaskDisciplines[5002] = new HashSet<long> { majorA };

        // Filter by majorId + status
        var statusRes = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorA}&status=TODO");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
        var statusPaged = await statusRes.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(statusPaged);
        Assert.Single(statusPaged.Items);
        Assert.Equal(5001L, statusPaged.Items[0].Id);

        // Filter by majorId + search
        var searchRes = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={majorA}&search=Database");
        Assert.Equal(HttpStatusCode.OK, searchRes.StatusCode);
        var searchPaged = await searchRes.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(searchPaged);
        Assert.Single(searchPaged.Items);
        Assert.Equal(5002L, searchPaged.Items[0].Id);
    }

    [Fact]
    public async Task GetTasks_WrongProjectMajor_ReturnsForbidden()
    {
        const long projectId = 106L;
        const long otherProjectId = 107L;
        const long foreignMajor = 71L;
        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        _factory.TaskRepository.ProjectMajors.Add((61L, projectId));
        _factory.TaskRepository.ProjectMajors.Add((foreignMajor, otherProjectId));

        var response = await client.GetAsync($"api/v1/tasks/project/{projectId}?majorId={foreignMajor}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

/// <summary>
/// Variant factory where IProjectAccessService always denies — simulates
/// a DepartmentStaff user whose department does not match the project's majors.
/// </summary>
internal sealed class DeniedAccessWebApplicationFactory : MilestonesAndTasksEndpointTests.MilestoneWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectAccessService>();
            services.AddSingleton<IProjectAccessService, DenyAllProjectAccessService>();
        });
    }
}

internal sealed class DenyAllProjectAccessService : IProjectAccessService
{
    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
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

    public Dictionary<long, long> TaskProjects { get; } = new()
    {
        [1] = 1,
        [2] = 1
    };

    public HashSet<(long MajorId, long ProjectId)> ProjectMajors { get; } = new();
    public Dictionary<long, HashSet<long>> TaskDisciplines { get; } = new();

    public Task<bool> MajorBelongsToProjectAsync(long majorId, long projectId, CancellationToken cancellationToken = default)
    {
        if (ProjectMajors.Any(pm => pm.ProjectId == projectId))
        {
            return Task.FromResult(ProjectMajors.Contains((majorId, projectId)));
        }
        return Task.FromResult(true);
    }

    public Task<TaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Tasks.FirstOrDefault(t => t.Id == id));

    public Task<PagedResult<TaskDto>> GetTasksAsync(long projectId, long? milestoneId, string? status, string? priority, long? assigneeUserId, string? search, DateTime? dueFrom, DateTime? dueTo, bool? isOverdue, bool? isBlocked, int page, int pageSize, long? majorId, CancellationToken cancellationToken)
    {
        var filtered = Tasks.Where(t => !TaskProjects.TryGetValue(t.Id, out var pId) || pId == projectId);

        if (milestoneId.HasValue)
            filtered = filtered.Where(t => t.MilestoneId == milestoneId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            filtered = filtered.Where(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(priority))
            filtered = filtered.Where(t => string.Equals(t.Priority, priority, StringComparison.OrdinalIgnoreCase));

        if (assigneeUserId.HasValue)
            filtered = filtered.Where(t => t.Assignees.Any(a => a.UserId == assigneeUserId.Value));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            filtered = filtered.Where(t => t.Title.ToLower().Contains(searchLower)
                                        || (t.Description != null && t.Description.ToLower().Contains(searchLower)));
        }

        if (dueFrom.HasValue)
            filtered = filtered.Where(t => t.DueAt >= dueFrom.Value);

        if (dueTo.HasValue)
            filtered = filtered.Where(t => t.DueAt <= dueTo.Value);

        if (isOverdue.HasValue)
        {
            var utcNow = DateTime.UtcNow;
            if (isOverdue.Value)
                filtered = filtered.Where(t => t.DueAt < utcNow && t.Status != "DONE" && t.Status != "CANCELLED");
            else
                filtered = filtered.Where(t => t.DueAt == null || t.DueAt >= utcNow || t.Status == "DONE" || t.Status == "CANCELLED");
        }

        if (isBlocked.HasValue)
        {
            if (isBlocked.Value)
                filtered = filtered.Where(t => t.Status == "BLOCKED");
            else
                filtered = filtered.Where(t => t.Status != "BLOCKED");
        }

        if (majorId.HasValue)
        {
            filtered = filtered.Where(t => TaskDisciplines.TryGetValue(t.Id, out var majors) && majors.Contains(majorId.Value));
        }

        var ordered = filtered
            .OrderBy(t => t.DueAt == null ? 1 : 0)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.Id)
            .ToList();

        var totalCount = ordered.Count;
        var pagedItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(new PagedResult<TaskDto>(pagedItems, page, pageSize, totalCount));
    }

    public Task<TaskDto> CreateAsync(long milestoneId, long? parentTaskId, string title, string? description, string? priority, DateTime? startAt, DateTime? dueAt, IReadOnlyList<long> assigneeUserIds, long createdByUserId, CancellationToken cancellationToken)
    {
        var task = new TaskDto(Tasks.Count + 1, milestoneId, parentTaskId, title, description, "TODO", priority ?? "NORMAL", startAt, dueAt, null, createdByUserId, "User", DateTime.UtcNow, DateTime.UtcNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>());
        Tasks.Add(task);
        TaskProjects[task.Id] = 1;
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

internal sealed class NoOpProjectExecutionGuard : IProjectExecutionGuard
{
    public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class AllowAllProjectAccessService : IProjectAccessService
{
    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
