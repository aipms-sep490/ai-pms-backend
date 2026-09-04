using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.Commands;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.Milestones.Queries;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class MilestoneHandlerTests
{
    [Fact]
    public async Task CreateMilestone_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var repository = new StubMilestoneRepository();
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new UnauthenticatedTestCurrentUser();
        var auditTrail = new RecordingAuditTrail();

        var handler = new CreateMilestoneCommandHandler(repository, executionGuard, currentUser, auditTrail);
        var cmd = new CreateMilestoneCommand(1, "Milestone 1", "Desc", null, null, 0);

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task CreateMilestone_UserNotLeaderOrSupervisor_ThrowsForbiddenException()
    {
        var repository = new StubMilestoneRepository { IsLeaderOrSupervisor = false };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new CreateMilestoneCommandHandler(repository, executionGuard, currentUser, auditTrail);
        var cmd = new CreateMilestoneCommand(1, "Milestone 1", "Desc", null, null, 0);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteMilestone_MilestoneWithTasks_ThrowsConflictException()
    {
        var repository = new StubMilestoneRepository
        {
            IsLeaderOrSupervisor = true,
            ExistingMilestone = new MilestoneDto(1, 1, "M1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User", DateTime.UtcNow, DateTime.UtcNow),
            HasTasks = true
        };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new DeleteMilestoneCommandHandler(repository, executionGuard, currentUser, auditTrail);
        var cmd = new DeleteMilestoneCommand(1);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task ReorderMilestones_MilestoneNotBelongingToProject_ThrowsConflictException()
    {
        var repository = new StubMilestoneRepository
        {
            IsLeaderOrSupervisor = true,
            ProjectMilestones = new List<MilestoneDto>
            {
                new(1, 1, "M1", "Desc", null, null, "IN_PROGRESS", 0, 10, "User", DateTime.UtcNow, DateTime.UtcNow)
            }
        };
        var executionGuard = new StubProjectExecutionGuard();
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();

        var handler = new ReorderMilestonesCommandHandler(repository, executionGuard, currentUser, auditTrail);
        var cmd = new ReorderMilestonesCommand(1, new[] { new MilestoneReorderItem(99, 0) });

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
    }
}

internal sealed class UnauthenticatedTestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public long? UserId => null;
    public string? Email => null;
    public string? FullName => null;
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();
}

internal sealed class StubMilestoneRepository : IMilestoneRepository
{
    public bool IsLeaderOrSupervisor { get; set; } = true;
    public MilestoneDto? ExistingMilestone { get; set; }
    public bool HasTasks { get; set; } = false;
    public IReadOnlyList<MilestoneDto> ProjectMilestones { get; set; } = Array.Empty<MilestoneDto>();

    public Task<MilestoneDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingMilestone?.Id == id ? ExistingMilestone : null);

    public Task<IReadOnlyList<MilestoneDto>> GetProjectMilestonesAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(ProjectMilestones);

    public Task<MilestoneDto> CreateAsync(long projectId, string title, string? description, DateOnly? startDate, DateOnly? dueDate, int sortOrder, long createdByUserId, CancellationToken cancellationToken) =>
        Task.FromResult(new MilestoneDto(1, projectId, title, description, startDate, dueDate, "IN_PROGRESS", sortOrder, createdByUserId, "User", DateTime.UtcNow, DateTime.UtcNow));

    public Task<MilestoneDto> UpdateAsync(long id, string title, string? description, DateOnly? startDate, DateOnly? dueDate, string status, int sortOrder, CancellationToken cancellationToken) =>
        Task.FromResult(new MilestoneDto(id, 1, title, description, startDate, dueDate, status, sortOrder, 10, "User", DateTime.UtcNow, DateTime.UtcNow));

    public Task DeleteAsync(long id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReorderAsync(IEnumerable<(long MilestoneId, int SortOrder)> items, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> HasTasksAsync(long milestoneId, CancellationToken cancellationToken) => Task.FromResult(HasTasks);

    public Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(IsLeaderOrSupervisor);

    public Task<IReadOnlyList<MilestoneProgressDto>> GetMilestoneProgressAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MilestoneProgressDto>>(Array.Empty<MilestoneProgressDto>());
}

internal sealed class StubProjectExecutionGuard : IProjectExecutionGuard
{
    public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken) => Task.CompletedTask;
}
