using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ActionItems.Abstractions;
using AIPMS.Application.Features.ActionItems.Commands;
using AIPMS.Application.Features.ActionItems.DTOs;
using AIPMS.Application.Features.ActionItems.Queries;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectActionItemHandlerTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public long? UserId { get; set; } = 10;
        public string Email { get; set; } = "user@test.edu";
        public string Name { get; set; } = "Test User";
        public string FullName => Name;
        public HashSet<string> Roles { get; set; } = new(StringComparer.Ordinal) { AppRoles.Student };
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
        IReadOnlyCollection<string> ICurrentUser.Roles => Roles;
    }

    private sealed class FakeProjectAccessService : IProjectAccessService
    {
        public bool CanAccess { get; set; } = true;
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CanAccess);
    }

    private sealed class FakeProjectExecutionGuard : IProjectExecutionGuard
    {
        public bool IsActive { get; set; } = true;
        public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken = default)
        {
            if (!IsActive)
                throw new ConflictException("Project is not active.");
            return Task.CompletedTask;
        }

        public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private class FakeProjectActionItemRepository : IProjectActionItemRepository
    {
        public List<ProjectActionItemDto> Items { get; } = new();
        public bool IsLeader { get; set; } = false;
        public bool IsSupervisor { get; set; } = false;
        public bool IsActiveMember { get; set; } = true;
        public bool MeetingExists { get; set; } = true;
        public bool ReportExists { get; set; } = true;
        public bool MeetingParticipant { get; set; } = true;
        public bool MeetingCreator { get; set; } = false;
        public bool EligibleOwner { get; set; } = true;
        private long _idCounter = 1;

        public async Task<ProjectActionItemDto> CreateAsync(
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
            Func<ProjectActionItemDto, Task>? onCreated = null,
            CancellationToken cancellationToken = default)
        {
            var item = new ProjectActionItemDto(
                Id: _idCounter++,
                ProjectId: projectId,
                SourceType: sourceType,
                MeetingId: meetingId,
                ProgressReportId: progressReportId,
                Title: title,
                Description: description,
                OwnerId: ownerId,
                OwnerName: ownerId.HasValue ? "Owner User" : null,
                TaskId: taskId,
                TaskTitle: taskId.HasValue ? "Task Title" : null,
                MilestoneId: milestoneId,
                MilestoneTitle: milestoneId.HasValue ? "Milestone Title" : null,
                DueAt: dueAt,
                Status: "TODO",
                CreatedBy: createdBy,
                CreatedAt: now,
                UpdatedAt: now,
                ConcurrencyToken: Guid.NewGuid().ToString("N"));
            Items.Add(item);
            if (onCreated != null) await onCreated(item);
            return item;
        }

        public Task<ProjectActionItemDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Find(i => i.Id == id));

        public Task<PagedResult<ProjectActionItemDto>> ListAsync(
            long projectId,
            string? sourceType,
            long? meetingId,
            long? progressReportId,
            string? status,
            long? ownerId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var list = Items.FindAll(i =>
                i.ProjectId == projectId &&
                (string.IsNullOrEmpty(status) || string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(sourceType) || string.Equals(i.SourceType, sourceType, StringComparison.OrdinalIgnoreCase)) &&
                (!meetingId.HasValue || i.MeetingId == meetingId.Value) &&
                (!progressReportId.HasValue || i.ProgressReportId == progressReportId.Value) &&
                (!ownerId.HasValue || i.OwnerId == ownerId.Value));
            return Task.FromResult(new PagedResult<ProjectActionItemDto>(list, page, pageSize, list.Count));
        }

        public async Task<ProjectActionItemDto> UpdateDetailsAsync(
            long id,
            string title,
            string? description,
            long? ownerId,
            long? taskId,
            long? milestoneId,
            DateTime? dueAt,
            Guid? expectedToken,
            DateTime now,
            Func<ProjectActionItemDto, Task>? onUpdated = null,
            CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(i => i.Id == id);
            if (idx == -1) throw new NotFoundException("ProjectActionItem", id);
            var prev = Items[idx];
            var updated = prev with
            {
                Title = title,
                Description = description,
                OwnerId = ownerId,
                DueAt = dueAt,
                TaskId = taskId,
                MilestoneId = milestoneId,
                UpdatedAt = now,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            Items[idx] = updated;
            if (onUpdated != null) await onUpdated(updated);
            return updated;
        }

        public async Task<ProjectActionItemDto> UpdateStatusAsync(
            long id,
            string newStatus,
            Guid? expectedToken,
            DateTime now,
            Func<ProjectActionItemDto, Task>? onUpdated = null,
            CancellationToken cancellationToken = default)
        {
            var idx = Items.FindIndex(i => i.Id == id);
            if (idx == -1) throw new NotFoundException("ProjectActionItem", id);
            var prev = Items[idx];
            var updated = prev with
            {
                Status = newStatus,
                UpdatedAt = now,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            Items[idx] = updated;
            if (onUpdated != null) await onUpdated(updated);
            return updated;
        }

        public Task<bool> IsMeetingInProjectAsync(long meetingId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MeetingExists);

        public Task<bool> IsProgressReportInProjectAsync(long reportId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReportExists);

        public Task<bool> IsTaskInProjectAsync(long taskId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> IsMilestoneInProjectAsync(long milestoneId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> IsEligibleOwnerAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EligibleOwner);

        public Task<bool> IsProjectLeaderAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsLeader);

        public Task<bool> IsAssignedSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsSupervisor);

        public virtual Task<bool> IsTaskBelongsToMilestoneAsync(long taskId, long milestoneId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true); // Default: task belongs to milestone; override in specific tests if needed

        public Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsActiveMember);

        public Task<bool> IsMeetingParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MeetingParticipant);

        public Task<bool> IsMeetingCreatorAsync(long meetingId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MeetingCreator);

        public bool MeetingCancelled { get; set; } = false;
        public Task<bool> IsMeetingCancelledAsync(long meetingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MeetingCancelled);

        public bool HasAdminRole { get; set; } = false;
        public Task<bool> HasAdminRoleInDbAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasAdminRole);
    }

    private sealed class StubProgressRepo : IProgressReportRepository
    {
        public bool ProjectExists { get; set; } = true;
        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, long? progressReportPeriodId = null, string? inProgressWork = null, string? blockers = null, string? risks = null, string? nextActions = null, Func<ProgressReportDto, Task>? onCreated = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportDto> UpdateAsync(long id, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> UpdateAsync(long id, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, long? progressReportPeriodId = null, string? inProgressWork = null, string? blockers = null, string? risks = null, string? nextActions = null, Func<ProgressReportDto, Task>? onUpdated = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportDto> SubmitAsync(long id, long actorId, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> SubmitAsync(long id, long actorId, DateTime now, Func<ProgressReportDto, Task>? onSubmitted, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, Func<ProgressReportFeedbackDto, Task>? onAdded, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsForPeriodAsync(long projectId, string reportType, DateOnly periodStart, DateOnly periodEnd, long? excludeId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsForPeriodIdAsync(long periodId, long? excludeId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PagedResult<ProgressReportDto>> GetReportsAsync(long projectId, string? reportType, string? status, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<long?> GetProjectIdAsync(long reportId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<string?> GetStatusAsync(long reportId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    [Fact]
    public async Task CreateActionItem_BothSourcesProvided_ThrowsValidationException()
    {
        var repo = new FakeProjectActionItemRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateProjectActionItemCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // Both meetingId and progressReportId set -> Invalid! Must be exactly one source.
        var req = new CreateProjectActionItemRequest(
            Title: "Fix API bug",
            Description: null,
            SourceType: "MEETING",
            MeetingId: 101,
            ProgressReportId: 202,
            OwnerId: null,
            DueAt: null,
            TaskId: null,
            MilestoneId: null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateProjectActionItemCommand(1, req), CancellationToken.None));
        Assert.True(ex.Errors.ContainsKey("source"));
    }

    [Fact]
    public async Task CreateActionItem_NeitherSourceProvided_ThrowsValidationException()
    {
        var repo = new FakeProjectActionItemRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateProjectActionItemCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // Neither meetingId nor progressReportId set
        var req = new CreateProjectActionItemRequest(
            Title: "Fix API bug",
            Description: null,
            SourceType: "MEETING",
            MeetingId: null,
            ProgressReportId: null,
            OwnerId: null,
            DueAt: null,
            TaskId: null,
            MilestoneId: null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateProjectActionItemCommand(1, req), CancellationToken.None));
        Assert.True(ex.Errors.ContainsKey("source"));
    }

    [Fact]
    public async Task CreateActionItem_IneligibleOwner_ThrowsConflictException()
    {
        var repo = new FakeProjectActionItemRepository { EligibleOwner = false };
        var user = new FakeCurrentUser();
        var handler = new CreateProjectActionItemCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var req = new CreateProjectActionItemRequest(
            Title: "Fix API bug",
            Description: null,
            SourceType: "MEETING",
            MeetingId: 101,
            ProgressReportId: null,
            OwnerId: 999, // Ineligible owner
            DueAt: null,
            TaskId: null,
            MilestoneId: null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new CreateProjectActionItemCommand(1, req), CancellationToken.None));
        Assert.Contains("Owner must be an active project member", ex.Message);
    }

    [Theory]
    [InlineData("TODO", "IN_PROGRESS", true)]
    [InlineData("TODO", "BLOCKED", true)]
    [InlineData("TODO", "CANCELLED", true)]
    [InlineData("TODO", "DONE", false)] // Invalid: must be in progress first
    [InlineData("IN_PROGRESS", "TODO", true)]
    [InlineData("IN_PROGRESS", "BLOCKED", true)]
    [InlineData("IN_PROGRESS", "DONE", true)]
    [InlineData("IN_PROGRESS", "CANCELLED", true)]
    [InlineData("BLOCKED", "TODO", true)]
    [InlineData("BLOCKED", "IN_PROGRESS", true)]
    [InlineData("BLOCKED", "CANCELLED", true)]
    [InlineData("BLOCKED", "DONE", false)] // Invalid: cannot jump from blocked to done directly
    [InlineData("DONE", "CANCELLED", false)] // Invalid
    [InlineData("CANCELLED", "DONE", false)] // Invalid
    public async Task UpdateActionItemStatus_TransitionMatrix_EnforcesRules(string initialStatus, string targetStatus, bool shouldSucceed)
    {
        var repo = new FakeProjectActionItemRepository { IsLeader = true };
        var user = new FakeCurrentUser();
        var handler = new UpdateProjectActionItemStatusCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        await repo.UpdateStatusAsync(created.Id, initialStatus, null, DateTime.UtcNow);

        var command = new UpdateProjectActionItemStatusCommand(1, created.Id, new UpdateProjectActionItemStatusRequest(targetStatus));

        if (shouldSucceed)
        {
            var result = await handler.Handle(command, CancellationToken.None);
            Assert.Equal(targetStatus, result.Status);
        }
        else
        {
            await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        }
    }

    [Fact]
    public async Task UpdateActionItemStatus_SelfTransition_IsNoOp()
    {
        var repo = new FakeProjectActionItemRepository();
        var user = new FakeCurrentUser();
        var handler = new UpdateProjectActionItemStatusCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        var command = new UpdateProjectActionItemStatusCommand(1, created.Id, new UpdateProjectActionItemStatusRequest("TODO"));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("TODO", result.Status);
    }

    [Fact]
    public async Task UpdateActionItemStatus_TerminalReopen_StandardMemberForbidden()
    {
        var repo = new FakeProjectActionItemRepository { IsLeader = false, IsSupervisor = false };
        var user = new FakeCurrentUser { Roles = new HashSet<string> { AppRoles.Student } }; // Member, not leader
        var handler = new UpdateProjectActionItemStatusCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        await repo.UpdateStatusAsync(created.Id, "DONE", null, DateTime.UtcNow);

        // Attempting to reopen DONE -> IN_PROGRESS by non-leader/supervisor/admin
        var command = new UpdateProjectActionItemStatusCommand(1, created.Id, new UpdateProjectActionItemStatusRequest("IN_PROGRESS"));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("reopen", ex.Message);
    }

    [Fact]
    public async Task UpdateActionItemStatus_TerminalReopen_LeaderAllowed()
    {
        var repo = new FakeProjectActionItemRepository { IsLeader = true };
        var user = new FakeCurrentUser { Roles = new HashSet<string> { AppRoles.Student } }; // Leader
        var handler = new UpdateProjectActionItemStatusCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        await repo.UpdateStatusAsync(created.Id, "DONE", null, DateTime.UtcNow);

        // Leader reopening DONE -> IN_PROGRESS is allowed
        var command = new UpdateProjectActionItemStatusCommand(1, created.Id, new UpdateProjectActionItemStatusRequest("IN_PROGRESS"));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("IN_PROGRESS", result.Status);
    }

    [Fact]
    public async Task UpdateActionItemStatus_TerminalReopenCancelled_SupervisorAllowed()
    {
        var repo = new FakeProjectActionItemRepository { IsSupervisor = true };
        var user = new FakeCurrentUser { Roles = new HashSet<string> { AppRoles.Lecturer } };
        var handler = new UpdateProjectActionItemStatusCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        await repo.UpdateStatusAsync(created.Id, "CANCELLED", null, DateTime.UtcNow);

        // Supervisor reopening CANCELLED -> TODO is allowed
        var command = new UpdateProjectActionItemStatusCommand(1, created.Id, new UpdateProjectActionItemStatusRequest("TODO"));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("TODO", result.Status);
    }

    [Fact]
    public async Task UpdateActionItemStatus_TerminalReopen_StaleAdminJwt_ThrowsForbiddenException()
    {
        var repo = new FakeProjectActionItemRepository { HasAdminRole = false, IsLeader = false, IsSupervisor = false };
        var user = new FakeCurrentUser { Roles = new HashSet<string> { AppRoles.Admin } }; // Stale token containing Admin
        var handler = new UpdateProjectActionItemStatusCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        await repo.UpdateStatusAsync(created.Id, "DONE", null, DateTime.UtcNow);

        var command = new UpdateProjectActionItemStatusCommand(1, created.Id, new UpdateProjectActionItemStatusRequest("IN_PROGRESS"));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("reopen", ex.Message);
    }

    [Fact]
    public async Task UpdateActionItemStatus_TerminalReopen_PersistedAdmin_Allowed()
    {
        var repo = new FakeProjectActionItemRepository { HasAdminRole = true, IsLeader = false, IsSupervisor = false };
        var user = new FakeCurrentUser { Roles = new HashSet<string> { AppRoles.Admin } };
        var handler = new UpdateProjectActionItemStatusCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        await repo.UpdateStatusAsync(created.Id, "DONE", null, DateTime.UtcNow);

        var command = new UpdateProjectActionItemStatusCommand(1, created.Id, new UpdateProjectActionItemStatusRequest("IN_PROGRESS"));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("IN_PROGRESS", result.Status);
    }

    [Fact]
    public async Task CreateActionItem_TaskMilestoneMismatch_ThrowsConflictException()
    {
        // Setup repo where IsTaskBelongsToMilestoneAsync returns false
        var repo = new MismatchedTaskActionItemRepository { IsLeader = true };
        
        var handler = new CreateProjectActionItemCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var req = new CreateProjectActionItemRequest(
            Title: "Title",
            Description: null,
            SourceType: "MEETING",
            MeetingId: 101,
            ProgressReportId: null,
            OwnerId: null,
            DueAt: null,
            TaskId: 10,
            MilestoneId: 20);

        var command = new CreateProjectActionItemCommand(1, req);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("does not belong to the selected milestone", ex.Message);
    }

    [Fact]
    public async Task UpdateActionItem_TaskMilestoneMismatch_ThrowsConflictException()
    {
        var repo = new MismatchedTaskActionItemRepository { IsLeader = true };
        var created = await repo.CreateAsync(1, "MEETING", 101, null, "Test Item", null, 10, null, null, null, 10, DateTime.UtcNow);
        
        var handler = new UpdateProjectActionItemCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var req = new UpdateProjectActionItemRequest(
            Title: "Updated",
            Description: null,
            OwnerId: null,
            DueAt: null,
            TaskId: 10,
            MilestoneId: 20,
            ConcurrencyToken: null);

        var command = new UpdateProjectActionItemCommand(1, created.Id, req);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("does not belong to the selected milestone", ex.Message);
    }

    private class MismatchedTaskActionItemRepository : FakeProjectActionItemRepository
    {
        public override Task<bool> IsTaskBelongsToMilestoneAsync(long taskId, long milestoneId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
