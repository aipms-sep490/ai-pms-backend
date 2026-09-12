using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.Commands;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.Meetings.Queries;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class MeetingHandlerTests
{
    private sealed class FakeMeetingRepository : IMeetingRepository
    {
        public bool ProjectExists { get; set; } = true;
        public bool IsMemberOrSupervisor { get; set; } = true;
        public bool CanManage { get; set; } = true;
        public bool CanSchedule { get; set; } = true;
        public bool IsParticipant { get; set; } = false;
        public long? SupervisorAssignmentId { get; set; } = 100;
        public string CurrentStatus { get; set; } = "SCHEDULED";
        public MeetingDto? Meeting { get; set; }
        public MeetingDetailDto? Detail { get; set; }
        public CancellationToken LastToken { get; private set; }

        public Task<MeetingDto?> GetByIdAsync(long id, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(Meeting);
        }

        public Task<MeetingDetailDto?> GetDetailByIdAsync(long id, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(Detail);
        }

        public Task<PagedResult<MeetingDto>> GetMeetingsAsync(
            long projectId, string? status, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(new PagedResult<MeetingDto>(
                Meeting != null ? new[] { Meeting } : Array.Empty<MeetingDto>(),
                page, pageSize, Meeting != null ? 1 : 0));
        }

        public Task<MeetingDto> CreateAsync(
            long projectId, long createdBy, string title, string? agenda, DateTime startAt, DateTime? endAt,
            string? location, string? onlineUrl, IReadOnlyList<long>? participantUserIds, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            Meeting = new MeetingDto(5, projectId, title, agenda, null, startAt, endAt, location, onlineUrl,
                "SCHEDULED", createdBy, "Creator", participantUserIds?.Count ?? 1, now, now);
            return Task.FromResult(Meeting);
        }

        public Task<MeetingDto> UpdateAsync(
            long id, string title, string? agenda, DateTime startAt, DateTime? endAt, string? location, string? onlineUrl, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            Meeting = Meeting! with { Title = title, Agenda = agenda, StartAt = startAt, EndAt = endAt, Location = location, OnlineUrl = onlineUrl, UpdatedAt = now };
            return Task.FromResult(Meeting);
        }

        public Task<MeetingDto> CancelAsync(long id, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            Meeting = Meeting! with { Status = "CANCELLED", UpdatedAt = now };
            return Task.FromResult(Meeting);
        }

        public Task<MeetingDto> CompleteAsync(long id, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            if (Meeting != null && Meeting.Status == "COMPLETED")
                throw new ConflictException("Meeting is already completed.");
            if (Meeting != null && Meeting.Status == "CANCELLED")
                throw new ConflictException("Cannot complete a meeting that has been cancelled.");
            Meeting = Meeting! with { Status = "COMPLETED", UpdatedAt = now };
            return Task.FromResult(Meeting);
        }

        public Task<MeetingDto> UpdateNotesAsync(
            long id, string? meetingNotes, IReadOnlyList<ParticipantAttendanceUpdate>? attendances, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            if (Meeting != null && Meeting.Status == "CANCELLED")
                throw new ConflictException("Cancelled meetings cannot be modified.");
            Meeting = Meeting! with { MeetingNotes = meetingNotes, UpdatedAt = now };
            return Task.FromResult(Meeting);
        }

        public Task<MeetingParticipantDto> AddParticipantAsync(
            long meetingId, long userId, string? attendanceStatus, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(new MeetingParticipantDto(1, meetingId, userId, "Name", "email@test.com", attendanceStatus, now, now));
        }

        public System.Threading.Tasks.Task RemoveParticipantAsync(long meetingId, long userId, CancellationToken ct)
        {
            LastToken = ct;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task<MeetingFeedbackDto> AddFeedbackAsync(
            long meetingId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(new MeetingFeedbackDto(1, Meeting?.ProjectId ?? 1, supervisorAssignmentId, 50, "Prof", meetingId, feedbackText, now, now));
        }

        public Task<long?> GetProjectIdAsync(long meetingId, CancellationToken ct) =>
            Task.FromResult(Meeting?.ProjectId ?? (long?)1);

        public Task<string?> GetStatusAsync(long meetingId, CancellationToken ct) =>
            Task.FromResult<string?>(CurrentStatus);

        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) =>
            Task.FromResult(ProjectExists);

        public Task<bool> IsProjectMemberOrSupervisorAsync(long projectId, long userId, CancellationToken ct) =>
            Task.FromResult(IsMemberOrSupervisor);

        public Task<bool> CanManageMeetingAsync(long meetingId, long userId, CancellationToken ct) =>
            Task.FromResult(CanManage);

        public Task<bool> CanScheduleMeetingAsync(long projectId, long userId, CancellationToken ct) =>
            Task.FromResult(CanSchedule);

        public Task<bool> IsParticipantAsync(long meetingId, long userId, CancellationToken ct) =>
            Task.FromResult(IsParticipant);

        public Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken ct) =>
            Task.FromResult(SupervisorAssignmentId);
    }

    private sealed class FakeProjectAccessService : IProjectAccessService
    {
        public bool HasAccess { get; set; } = true;
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasAccess);
    }

    private sealed class FakeProjectExecutionGuard : IProjectExecutionGuard
    {
        public bool IsActive { get; set; } = true;
        public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken)
        {
            if (!IsActive) throw new ConflictException("Project is not in ACTIVE state.");
            return Task.CompletedTask;
        }
        public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => UserId.HasValue;
        public long? UserId { get; set; } = 1;
        public string? Email { get; set; } = "user@test.com";
        public string? FullName { get; set; } = "Test User";
        public IReadOnlyCollection<string> Roles { get; set; } = new[] { AppRoles.Student };
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

    private readonly FakeMeetingRepository repository = new();
    private readonly FakeProjectAccessService projectAccess = new();
    private readonly FakeProjectExecutionGuard executionGuard = new();
    private readonly FakeCurrentUser currentUser = new();
    private readonly FakeAuditTrail audit = new();
    private readonly TimeProvider clock = TimeProvider.System;

    [Fact]
    public async Task CreateMeeting_Leader_Succeeds()
    {
        var handler = new CreateMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            "Weekly Sync", "Agenda", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "Room 1", null, new[] { 2L, 3L }));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal(5, result.Id);
        Assert.Equal("SCHEDULED", result.Status);
        Assert.Single(audit.Entries);
        Assert.Equal("MEETING_SCHEDULED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task CreateMeeting_AssignedSupervisor_Succeeds()
    {
        currentUser.Roles = new[] { AppRoles.Lecturer };
        repository.CanSchedule = true;

        var handler = new CreateMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            "Advising Session", null, DateTime.UtcNow, null, null, "https://meet.google.com/abc", null));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("SCHEDULED", result.Status);
    }

    [Fact]
    public async Task CreateMeeting_OutsideProject_403()
    {
        projectAccess.HasAccess = false;
        var handler = new CreateMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            "Meeting", null, DateTime.UtcNow, null, null, null, null));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateMeeting_NonMemberParticipant_Rejects()
    {
        repository.IsMemberOrSupervisor = false;
        var handler = new CreateMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            "Meeting", null, DateTime.UtcNow, null, null, null, new[] { 999L }));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMeeting_Valid()
    {
        repository.Meeting = new MeetingDto(5, 1, "Old Title", null, null, DateTime.UtcNow, null, null, null,
            "SCHEDULED", 1, "Creator", 1, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new UpdateMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateMeetingCommand(5, new UpdateMeetingRequest("New Title", "New Agenda", DateTime.UtcNow, null, "Room 2", null));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("New Title", result.Title);
        Assert.Single(audit.Entries);
        Assert.Equal("MEETING_UPDATED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task UpdateMeeting_CompletedOrCancelled_Rejects()
    {
        repository.CurrentStatus = "COMPLETED";
        var handler = new UpdateMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateMeetingCommand(5, new UpdateMeetingRequest("Title", null, DateTime.UtcNow, null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CancelMeeting_PerLifecycle()
    {
        repository.Meeting = new MeetingDto(5, 1, "Title", null, null, DateTime.UtcNow, null, null, null,
            "SCHEDULED", 1, "Creator", 1, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new CancelMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CancelMeetingCommand(5);

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("CANCELLED", result.Status);
        Assert.Single(audit.Entries);
        Assert.Equal("MEETING_CANCELLED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task CancelMeeting_AlreadyCancelledOrCompleted_Rejects()
    {
        repository.CurrentStatus = "CANCELLED";
        var handler = new CancelMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CancelMeetingCommand(5);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task AddParticipant_ValidProjectMember()
    {
        var handler = new AddMeetingParticipantCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new AddMeetingParticipantCommand(5, new AddMeetingParticipantRequest(3, "INVITED"));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal(3, result.UserId);
        Assert.Single(audit.Entries);
        Assert.Equal("MEETING_PARTICIPANT_ADDED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task AddParticipant_CrossProject_Rejects()
    {
        repository.IsMemberOrSupervisor = false;
        var handler = new AddMeetingParticipantCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new AddMeetingParticipantCommand(5, new AddMeetingParticipantRequest(999, "INVITED"));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task AddParticipant_Duplicate_Rejects()
    {
        repository.IsParticipant = true;
        var handler = new AddMeetingParticipantCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new AddMeetingParticipantCommand(5, new AddMeetingParticipantRequest(2, "INVITED"));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveParticipant_PerLifecycle()
    {
        repository.IsParticipant = true;
        var handler = new RemoveMeetingParticipantCommandHandler(repository, projectAccess, executionGuard, currentUser, audit);
        var command = new RemoveMeetingParticipantCommand(5, 2);

        await handler.Handle(command, CancellationToken.None);
        Assert.Single(audit.Entries);
        Assert.Equal("MEETING_PARTICIPANT_REMOVED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task UpdateNotes_Valid()
    {
        repository.Meeting = new MeetingDto(5, 1, "Title", null, null, DateTime.UtcNow, null, null, null,
            "SCHEDULED", 1, "Creator", 1, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new UpdateMeetingNotesCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateMeetingNotesCommand(5, new UpdateMeetingNotesRequest(
            "Minutes: discussed architecture", new[] { new ParticipantAttendanceUpdate(2, "ATTENDED") }));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("Minutes: discussed architecture", result.MeetingNotes);
        Assert.Equal("SCHEDULED", result.Status); // Notes update does not mutate lifecycle status!
    }

    [Fact]
    public async Task AssignedSupervisor_FeedbackMeeting_Succeeds()
    {
        currentUser.Roles = new[] { AppRoles.Lecturer };
        repository.SupervisorAssignmentId = 100;

        var handler = new AddMeetingFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddMeetingFeedbackCommand(5, new AddMeetingFeedbackRequest("Solid team coordination."));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("Solid team coordination.", result.FeedbackText);
        Assert.Single(audit.Entries);
        Assert.Equal("MEETING_FEEDBACK_ADDED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task OtherSupervisor_FeedbackMeeting_403()
    {
        repository.SupervisorAssignmentId = null;
        var handler = new AddMeetingFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddMeetingFeedbackCommand(5, new AddMeetingFeedbackRequest("Feedback"));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task EndedSupervisor_FeedbackMeeting_403()
    {
        repository.SupervisorAssignmentId = null; // Ended supervisor
        var handler = new AddMeetingFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddMeetingFeedbackCommand(5, new AddMeetingFeedbackRequest("Feedback"));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledMeeting_Feedback_409()
    {
        repository.SupervisorAssignmentId = 100;
        repository.CurrentStatus = "CANCELLED";
        var handler = new AddMeetingFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddMeetingFeedbackCommand(5, new AddMeetingFeedbackRequest("Feedback"));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationToken_Forwarded_ToEndOfFlow()
    {
        using var cts = new CancellationTokenSource();
        var handler = new GetMeetingByIdQueryHandler(repository, projectAccess, currentUser);
        repository.Detail = new MeetingDetailDto(5, 1, "Title", null, null, DateTime.UtcNow, null, null, null,
            "SCHEDULED", 1, "Creator", Array.Empty<MeetingParticipantDto>(), Array.Empty<MeetingFeedbackDto>(), DateTime.UtcNow, DateTime.UtcNow);

        await handler.Handle(new GetMeetingByIdQuery(5), cts.Token);
        Assert.Equal(cts.Token, repository.LastToken);
    }

    #region Finding 3: Meeting Terminal State Tests

    [Fact]
    public async Task CancelledMeeting_NotesCannotReopen()
    {
        repository.CurrentStatus = "CANCELLED";
        repository.Meeting = new MeetingDto(5, 1, "Cancelled Title", null, null, DateTime.UtcNow, null, null, null,
            "CANCELLED", 1, "Creator", 0, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new UpdateMeetingNotesCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateMeetingNotesCommand(5, new UpdateMeetingNotesRequest("Notes", null));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("Cancelled meetings cannot be modified.", ex.Message);
    }

    [Fact]
    public async Task CompletedMeeting_NotesCannotReopen()
    {
        repository.CurrentStatus = "COMPLETED";
        repository.Meeting = new MeetingDto(5, 1, "Completed Title", null, null, DateTime.UtcNow, null, null, null,
            "COMPLETED", 1, "Creator", 0, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new UpdateMeetingNotesCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateMeetingNotesCommand(5, new UpdateMeetingNotesRequest("Finalized notes", null));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("COMPLETED", result.Status); // Persisted status remains COMPLETED!
        Assert.Equal("Finalized notes", result.MeetingNotes);
    }

    [Fact]
    public async Task NotesUpdate_DoesNotChangeStatus()
    {
        repository.CurrentStatus = "SCHEDULED";
        repository.Meeting = new MeetingDto(5, 1, "Scheduled Title", null, null, DateTime.UtcNow, null, null, null,
            "SCHEDULED", 1, "Creator", 0, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new UpdateMeetingNotesCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateMeetingNotesCommand(5, new UpdateMeetingNotesRequest("Discussion notes", null));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("SCHEDULED", result.Status); // Notes update does not mutate lifecycle status!
        Assert.Equal("Discussion notes", result.MeetingNotes);
    }

    #endregion

    #region Finding 1: Meeting Completion Handler Tests

    [Fact]
    public async Task CompleteMeeting_ValidScheduled_TransitionsToCompleted()
    {
        repository.CurrentStatus = "SCHEDULED";
        repository.Meeting = new MeetingDto(5, 1, "Sprint Review", null, null, DateTime.UtcNow, null, null, null,
            "SCHEDULED", 1, "Creator", 0, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new CompleteMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var result = await handler.Handle(new CompleteMeetingCommand(5), CancellationToken.None);

        Assert.Equal("COMPLETED", result.Status);
        Assert.Single(audit.Entries);
        Assert.Equal("MEETING_COMPLETED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task CompleteMeeting_AlreadyCompleted_ThrowsConflict()
    {
        repository.CurrentStatus = "COMPLETED";
        repository.Meeting = new MeetingDto(5, 1, "Done Meeting", null, null, DateTime.UtcNow, null, null, null,
            "COMPLETED", 1, "Creator", 0, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new CompleteMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new CompleteMeetingCommand(5), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteMeeting_Cancelled_ThrowsConflict()
    {
        repository.CurrentStatus = "CANCELLED";
        repository.Meeting = new MeetingDto(5, 1, "Cancelled Meeting", null, null, DateTime.UtcNow, null, null, null,
            "CANCELLED", 1, "Creator", 0, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new CompleteMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new CompleteMeetingCommand(5), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteMeeting_NonManager_ThrowsForbidden()
    {
        repository.CanManage = false;
        currentUser.Roles = new[] { AppRoles.Student };

        var handler = new CompleteMeetingCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new CompleteMeetingCommand(5), CancellationToken.None));
    }

    #endregion
}
