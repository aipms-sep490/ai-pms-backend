using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.Commands;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.Milestones.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class MilestoneDependencyHandlerTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public long? UserId { get; set; } = 1;
        public string Email => "leader@aipms.edu";
        public string Name => "Project Leader";
        public string FullName => Name;
        public IReadOnlyList<string> Roles => new[] { "Student" };
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
        IReadOnlyCollection<string> ICurrentUser.Roles => Roles;
    }

    private sealed class FakeProjectExecutionGuard : IProjectExecutionGuard
    {
        public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMilestoneRepository : IMilestoneRepository
    {
        public List<MilestoneDto> Milestones { get; } = new();
        public List<(long MilestoneId, long DependsOnId, string Type)> Dependencies { get; } = new();
        public bool IsLeader { get; set; } = true;
        public bool BelongsToProject { get; set; } = true;

        public Task<MilestoneDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(Milestones.Find(m => m.Id == id));

        public Task<IReadOnlyList<MilestoneDto>> GetProjectMilestonesAsync(long projectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MilestoneDto>>(Milestones.FindAll(m => m.ProjectId == projectId));

        public Task<MilestoneDto> CreateAsync(long projectId, string title, string? description, DateOnly? startDate, DateOnly? dueDate, int sortOrder, long createdByUserId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<MilestoneDto> UpdateAsync(long id, string title, string? description, DateOnly? startDate, DateOnly? dueDate, string status, int sortOrder, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(long id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasTasksAsync(long id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task ReorderAsync(IEnumerable<(long MilestoneId, int SortOrder)> reorderItems, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> IsProjectLeaderOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken) => Task.FromResult(IsLeader);
        public Task<IReadOnlyList<MilestoneProgressDto>> GetMilestoneProgressAsync(long projectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MilestoneProgressDto>>(Array.Empty<MilestoneProgressDto>());

        public Task<bool> MilestoneBelongsToProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken) =>
            Task.FromResult(BelongsToProject && Milestones.Exists(m => m.Id == milestoneId && m.ProjectId == projectId));

        public bool LockAcquired { get; set; }

        public Task AcquireProjectDependencyLockAsync(long projectId, CancellationToken cancellationToken)
        {
            LockAcquired = true;
            return Task.CompletedTask;
        }

        public Task AddDependencyAsync(long milestoneId, long dependsOnMilestoneId, string dependencyType, CancellationToken cancellationToken)
        {
            Dependencies.Add((milestoneId, dependsOnMilestoneId, dependencyType));
            return Task.CompletedTask;
        }

        public Task RemoveDependencyAsync(long milestoneId, long dependsOnMilestoneId, CancellationToken cancellationToken)
        {
            Dependencies.RemoveAll(d => d.MilestoneId == milestoneId && d.DependsOnId == dependsOnMilestoneId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<long>> GetDependsOnMilestoneIdsAsync(long milestoneId, CancellationToken cancellationToken)
        {
            var list = new List<long>();
            foreach (var d in Dependencies)
            {
                if (d.MilestoneId == milestoneId) list.Add(d.DependsOnId);
            }
            return Task.FromResult<IReadOnlyList<long>>(list);
        }
    }

    [Fact]
    public void Validator_SelfDependency_ShouldFail()
    {
        var validator = new AddMilestoneDependencyCommandValidator();
        var cmd = new AddMilestoneDependencyCommand(10, 10, "FINISH_TO_START");
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("");
    }

    [Fact]
    public void Validator_InvalidDependencyType_ShouldFail()
    {
        var validator = new AddMilestoneDependencyCommandValidator();
        var cmd = new AddMilestoneDependencyCommand(10, 20, "INVALID_TYPE");
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.DependencyType);
    }

    [Fact]
    public async Task Handle_FinishToStartOrderingViolation_ThrowsConflictException()
    {
        var repo = new FakeMilestoneRepository();
        // M1 (predecessor): DueDate = 2026-11-20
        repo.Milestones.Add(new MilestoneDto(1, 100, "M1", null, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 20), "IN_PROGRESS", 1, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));
        // M2 (successor): StartDate = 2026-11-15 (violates Finish-to-Start because 11/15 < 11/20!)
        repo.Milestones.Add(new MilestoneDto(2, 100, "M2", null, new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 30), "PLANNED", 2, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));

        var handler = new AddMilestoneDependencyCommandHandler(repo, new FakeProjectExecutionGuard(), new FakeCurrentUser(), new FakeAuditTrail());

        var cmd = new AddMilestoneDependencyCommand(2, 1, "FINISH_TO_START");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Equal(WorkflowErrorCodes.MilestoneDependencyOrderViolation, ex.Code);
    }

    [Fact]
    public async Task Handle_DependencyCycle_ThrowsConflictException()
    {
        var repo = new FakeMilestoneRepository();
        repo.Milestones.Add(new MilestoneDto(1, 100, "M1", null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), "COMPLETED", 1, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));
        repo.Milestones.Add(new MilestoneDto(2, 100, "M2", null, new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 20), "IN_PROGRESS", 2, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));

        // Existing dependency: M2 depends on M1
        repo.Dependencies.Add((2, 1, "FINISH_TO_START"));

        var handler = new AddMilestoneDependencyCommandHandler(repo, new FakeProjectExecutionGuard(), new FakeCurrentUser(), new FakeAuditTrail());

        // Attempt to make M1 depend on M2 -> Cycle!
        var cmd = new AddMilestoneDependencyCommand(1, 2, "FINISH_TO_START");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Equal(WorkflowErrorCodes.MilestoneDependencyCycle, ex.Code);
    }

    [Fact]
    public async Task Handle_ValidFinishToStartDependency_Succeeds()
    {
        var repo = new FakeMilestoneRepository();
        // M1 ends on 2026-11-10
        repo.Milestones.Add(new MilestoneDto(1, 100, "M1", null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), "COMPLETED", 1, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));
        // M2 starts on 2026-11-11
        repo.Milestones.Add(new MilestoneDto(2, 100, "M2", null, new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 20), "PLANNED", 2, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));

        var handler = new AddMilestoneDependencyCommandHandler(repo, new FakeProjectExecutionGuard(), new FakeCurrentUser(), new FakeAuditTrail());

        var cmd = new AddMilestoneDependencyCommand(2, 1, "FINISH_TO_START");
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(repo.Dependencies);
        Assert.Equal((2, 1, "FINISH_TO_START"), repo.Dependencies[0]);
    }

    [Fact]
    public async Task Handle_RemoveDependency_Succeeds()
    {
        var repo = new FakeMilestoneRepository();
        repo.Milestones.Add(new MilestoneDto(1, 100, "M1", null, null, null, "COMPLETED", 1, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));
        repo.Milestones.Add(new MilestoneDto(2, 100, "M2", null, null, null, "PLANNED", 2, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));
        repo.Dependencies.Add((2, 1, "FINISH_TO_START"));

        var handler = new RemoveMilestoneDependencyCommandHandler(repo, new FakeProjectExecutionGuard(), new FakeCurrentUser(), new FakeAuditTrail());

        var cmd = new RemoveMilestoneDependencyCommand(2, 1);
        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(repo.Dependencies);
    }

    [Theory]
    [InlineData("START_TO_START")]
    [InlineData("FINISH_TO_FINISH")]
    [InlineData("START_TO_FINISH")]
    [InlineData("INVALID_TYPE")]
    public void Validator_RejectsNonFinishToStartTypes(string dependencyType)
    {
        var validator = new AddMilestoneDependencyCommandValidator();
        var cmd = new AddMilestoneDependencyCommand(2, 1, dependencyType);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.DependencyType);
    }

    [Fact]
    public async Task Handle_RejectsNonFinishToStartType_ThrowsConflict()
    {
        var repo = new FakeMilestoneRepository();
        repo.Milestones.Add(new MilestoneDto(1, 100, "M1", null, null, null, "COMPLETED", 1, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));
        repo.Milestones.Add(new MilestoneDto(2, 100, "M2", null, null, null, "PLANNED", 2, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));

        var handler = new AddMilestoneDependencyCommandHandler(repo, new FakeProjectExecutionGuard(), new FakeCurrentUser(), new FakeAuditTrail());

        var cmd = new AddMilestoneDependencyCommand(2, 1, "START_TO_START");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Contains("FINISH_TO_START", ex.Message);
    }

    [Fact]
    public async Task Handle_AcquiresProjectDependencyLock_BeforeAdding()
    {
        var repo = new FakeMilestoneRepository();
        repo.Milestones.Add(new MilestoneDto(1, 100, "M1", null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), "COMPLETED", 1, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));
        repo.Milestones.Add(new MilestoneDto(2, 100, "M2", null, new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 20), "PLANNED", 2, 1, "Leader", DateTime.UtcNow, DateTime.UtcNow, null));

        var handler = new AddMilestoneDependencyCommandHandler(repo, new FakeProjectExecutionGuard(), new FakeCurrentUser(), new FakeAuditTrail());

        var cmd = new AddMilestoneDependencyCommand(2, 1, "FINISH_TO_START");
        await handler.Handle(cmd, CancellationToken.None);

        Assert.True(repo.LockAcquired);
    }
}
