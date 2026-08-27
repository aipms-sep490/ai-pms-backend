using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressMeetings.Abstractions;
using AIPMS.Application.Features.ProgressMeetings.Commands;
using AIPMS.Application.Features.ProgressMeetings.DTOs;

namespace AIPMS.UnitTests.Application;

public sealed class ProgressMeetingHandlerTests
{
    [Fact]
    public async Task Member_cannot_update_submitted_report()
    {
        var repository = new StubRepository { Report = Report("SUBMITTED"), IsMember = true };
        var handler = new UpdateProgressReportHandler(new CurrentUser(7), repository);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateProgressReportCommand(10, "summary", "done", "doing", "none", "none", "next"), default));
        Assert.False(repository.UpdateReportCalled);
    }

    [Fact]
    public async Task Only_leader_can_submit_report()
    {
        var repository = new StubRepository { Report = Report("DRAFT"), IsLeader = false };
        var handler = new SubmitProgressReportHandler(new CurrentUser(7), repository, TimeProvider.System);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new SubmitProgressReportCommand(10), default));
        Assert.False(repository.SubmitReportCalled);
    }

    [Fact]
    public async Task Late_block_policy_prevents_submit()
    {
        var repository = new StubRepository
        {
            Report = Report("DRAFT") with { DeadlineAt = DateTime.UtcNow.AddMinutes(-5), LatePolicy = "BLOCK" },
            IsLeader = true
        };
        var handler = new SubmitProgressReportHandler(new CurrentUser(7), repository, TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new SubmitProgressReportCommand(10), default));
        Assert.False(repository.SubmitReportCalled);
    }

    [Fact]
    public async Task Meeting_rejects_participant_outside_project_scope()
    {
        var repository = new StubRepository
        {
            ProjectExists = true,
            IsLeader = true,
            ParticipantsValid = false
        };
        var handler = new CreateMeetingHandler(new CurrentUser(7), repository);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new CreateMeetingCommand(1, "Planning", null, DateTime.UtcNow.AddDays(1), null, null, null, [99]), default));
        Assert.False(repository.CreateMeetingCalled);
    }

    [Fact]
    public async Task Assigned_supervisor_can_review_submitted_report()
    {
        var repository = new StubRepository { Report = Report("SUBMITTED"), AssignmentId = 42 };
        var handler = new AddProgressReportFeedbackHandler(new CurrentUser(8), repository);

        await handler.Handle(new AddProgressReportFeedbackCommand(10, " Looks good "), default);

        Assert.Equal((10L, 1L, 42L, "Looks good"), repository.ReportFeedback);
    }

    private static ProgressReportDto Report(string status) => new(
        10, 1, 7, "WEEKLY", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7),
        "summary", null, null, null, status, null, DateTime.UtcNow, DateTime.UtcNow,
        3, DateTime.UtcNow.AddDays(1), "FLAG", false, [], [], []);

    private sealed class CurrentUser(long id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class StubRepository : IProgressMeetingRepository
    {
        public bool ProjectExists { get; init; } = true;
        public bool IsMember { get; init; }
        public bool IsLeader { get; init; }
        public bool ParticipantsValid { get; init; } = true;
        public long? AssignmentId { get; init; }
        public ProgressReportDto? Report { get; init; }
        public bool UpdateReportCalled { get; private set; }
        public bool SubmitReportCalled { get; private set; }
        public bool CreateMeetingCalled { get; private set; }
        public (long Id, long ProjectId, long AssignmentId, string Text)? ReportFeedback { get; private set; }

        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) => Task.FromResult(ProjectExists);
        public Task<bool> IsProjectMemberAsync(long userId, long projectId, CancellationToken ct) => Task.FromResult(IsMember);
        public Task<bool> IsProjectLeaderAsync(long userId, long projectId, CancellationToken ct) => Task.FromResult(IsLeader);
        public Task<long?> GetActiveSupervisorAssignmentAsync(long userId, long projectId, CancellationToken ct) => Task.FromResult(AssignmentId);
        public Task<bool> AreValidParticipantsAsync(long projectId, IReadOnlyCollection<long> userIds, CancellationToken ct) => Task.FromResult(ParticipantsValid);
        public Task<ReportPeriodDto?> GetReportPeriodAsync(long id, CancellationToken ct) => Task.FromResult<ReportPeriodDto?>(null);
        public Task<ProgressReportDto?> GetReportAsync(long id, CancellationToken ct) => Task.FromResult(Report);
        public Task<PagedResult<ProgressReportDto>> ListReportsAsync(long projectId, ReportListFilter filter, CancellationToken ct) => Task.FromResult(new PagedResult<ProgressReportDto>([], 1, 20, 0));
        public Task<long> CreateReportAsync(CreateReportData data, CancellationToken ct) => Task.FromResult(10L);
        public Task<bool> UpdateDraftReportAsync(long id, UpdateReportData data, CancellationToken ct) { UpdateReportCalled = true; return Task.FromResult(true); }
        public Task<bool> SubmitDraftReportAsync(long id, DateTime submittedAt, CancellationToken ct) { SubmitReportCalled = true; return Task.FromResult(true); }
        public Task AddReportFeedbackAsync(long id, long projectId, long assignmentId, string text, CancellationToken ct) { ReportFeedback = (id, projectId, assignmentId, text); return Task.CompletedTask; }
        public Task AddContributionAsync(long reportId, long contributorId, string sectionType, string content, CancellationToken ct) => Task.CompletedTask;
        public Task<MeetingDto?> GetMeetingAsync(long id, CancellationToken ct) => Task.FromResult<MeetingDto?>(null);
        public Task<PagedResult<MeetingDto>> ListMeetingsAsync(long projectId, MeetingListFilter filter, CancellationToken ct) => Task.FromResult(new PagedResult<MeetingDto>([], 1, 20, 0));
        public Task<long> CreateMeetingAsync(CreateMeetingData data, CancellationToken ct) { CreateMeetingCalled = true; return Task.FromResult(1L); }
        public Task<bool> UpdateScheduledMeetingAsync(long id, UpdateMeetingData data, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> CancelMeetingAsync(long id, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> UpdateMinutesAsync(long id, string? notes, bool complete, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetAttendanceAsync(long meetingId, long userId, string status, CancellationToken ct) => Task.FromResult(true);
        public Task ReplaceParticipantsAsync(long meetingId, long actorId, string title, IReadOnlyCollection<long> userIds, CancellationToken ct) => Task.CompletedTask;
        public Task AddDecisionAsync(long meetingId, long actorId, string content, CancellationToken ct) => Task.CompletedTask;
        public Task AddBlockerAsync(long meetingId, long actorId, string content, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsTaskInProjectAsync(long taskId, long projectId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsMilestoneInProjectAsync(long milestoneId, long projectId, CancellationToken ct) => Task.FromResult(true);
        public Task AddActionItemAsync(CreateActionItemData data, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> UpdateActionItemStatusAsync(long meetingId, long actionItemId, string status, CancellationToken ct) => Task.FromResult(true);
        public Task AddMeetingFeedbackAsync(long id, long projectId, long assignmentId, string text, CancellationToken ct) => Task.CompletedTask;
    }
}
