using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.Commands;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.ProgressReports.Queries;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ProgressReportHandlerTests
{
    private sealed class FakeProgressReportRepository : IProgressReportRepository
    {
        public bool ProjectExists { get; set; } = true;
        public bool IsLeader { get; set; } = true;
        public long? SupervisorAssignmentId { get; set; } = 100;
        public bool ExistsForPeriodResult { get; set; } = false;
        public string CurrentStatus { get; set; } = "DRAFT";
        public ProgressReportDto? Report { get; set; }
        public ProgressReportDetailDto? Detail { get; set; }
        public CancellationToken LastToken { get; private set; }

        public Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(Report);
        }

        public Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(Detail);
        }

        public Task<PagedResult<ProgressReportDto>> GetReportsAsync(
            long projectId, string? reportType, string? status, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(new PagedResult<ProgressReportDto>(
                Report != null ? new[] { Report } : Array.Empty<ProgressReportDto>(),
                page, pageSize, Report != null ? 1 : 0));
        }

        public Task<bool> ExistsForPeriodAsync(long projectId, string reportType, DateOnly periodStart, DateOnly periodEnd, long? excludeId, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(ExistsForPeriodResult);
        }

        public Task<ProgressReportDto> CreateAsync(
            long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd,
            string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            Report = new ProgressReportDto(10, projectId, submittedBy, "Author", reportType, periodStart, periodEnd,
                summary, completedWork, plannedWork, issuesAndRisks, "DRAFT", null, null, now, now);
            return Task.FromResult(Report);
        }

        public Task<ProgressReportDto> UpdateAsync(
            long id, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            Report = Report! with { Summary = summary, CompletedWork = completedWork, PlannedWork = plannedWork, IssuesAndRisks = issuesAndRisks, UpdatedAt = now };
            return Task.FromResult(Report);
        }

        public bool IsActiveMember { get; set; } = true;

        public Task<ProgressReportDto> SubmitAsync(long id, long actorId, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            if (Report == null) throw new NotFoundException("ProgressReport", id);
            if (Report.Status != "DRAFT") throw new ConflictException("Progress report is already submitted.");

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(Report.Summary))
                errors["summary"] = new[] { "Summary is required to submit a progress report." };
            if (string.IsNullOrWhiteSpace(Report.CompletedWork))
                errors["completedWork"] = new[] { "Completed work is required to submit a progress report." };
            if (string.IsNullOrWhiteSpace(Report.PlannedWork))
                errors["plannedWork"] = new[] { "Planned work is required to submit a progress report." };
            if (string.IsNullOrWhiteSpace(Report.IssuesAndRisks))
                errors["issuesAndRisks"] = new[] { "Issues and risks is required to submit a progress report." };

            if (errors.Count > 0)
                throw new ValidationException(errors);

            Report = Report! with { Status = "SUBMITTED", SubmittedBy = actorId, SubmittedAt = now, IsLate = null, UpdatedAt = now };
            return Task.FromResult(Report);
        }

        public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken ct)
        {
            LastToken = ct;
            Report = Report! with { Status = "REVIEWED", UpdatedAt = now };
            return Task.FromResult(new ProgressReportFeedbackDto(1, Report.ProjectId, supervisorAssignmentId, 50, "Prof", reportId, feedbackText, now, now));
        }

        public Task<long?> GetProjectIdAsync(long reportId, CancellationToken ct) =>
            Task.FromResult(Report?.ProjectId ?? (long?)1);

        public Task<string?> GetStatusAsync(long reportId, CancellationToken ct) =>
            Task.FromResult<string?>(CurrentStatus);

        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) =>
            Task.FromResult(ProjectExists);

        public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken ct) =>
            Task.FromResult(IsLeader);

        public Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken ct) =>
            Task.FromResult(IsActiveMember);

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

    private readonly FakeProgressReportRepository repository = new();
    private readonly FakeProjectAccessService projectAccess = new();
    private readonly FakeProjectExecutionGuard executionGuard = new();
    private readonly FakeCurrentUser currentUser = new();
    private readonly FakeAuditTrail audit = new();
    private readonly TimeProvider clock = TimeProvider.System;

    [Fact]
    public async Task CreateReport_Valid_CreatesDraft()
    {
        var handler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", "Done", "Next", "None"));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(10, result.Id);
        Assert.Equal("DRAFT", result.Status);
        Assert.Single(audit.Entries);
        Assert.Equal("PROGRESS_REPORT_CREATED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task CreateReport_InvalidProjectAccess_403()
    {
        projectAccess.HasAccess = false;
        var handler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateReport_NonExistentProject_404()
    {
        repository.ProjectExists = false;
        var handler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateProgressReportCommand(999, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateReport_InactiveProject_409()
    {
        executionGuard.IsActive = false;
        var handler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateReport_DuplicatePeriod_409()
    {
        repository.ExistsForPeriodResult = true;
        var handler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDraftReport_Valid_UpdatesContent()
    {
        repository.Report = new ProgressReportDto(10, 1, 1, "Author", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Original", null, null, null, "DRAFT", null, false, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new UpdateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateProgressReportCommand(10, new UpdateProgressReportRequest("New Summary", "Work", null, null));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("New Summary", result.Summary);
        Assert.Single(audit.Entries);
        Assert.Equal("PROGRESS_REPORT_UPDATED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task UpdateSubmittedReport_Rejects()
    {
        repository.CurrentStatus = "SUBMITTED";
        var handler = new UpdateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateProgressReportCommand(10, new UpdateProgressReportRequest("New Summary", null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SubmittedReport_ContentImmutable()
    {
        var original = new ProgressReportDto(10, 1, 1, "Author", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Original Immutable", null, null, null, "SUBMITTED", DateTime.UtcNow, false, DateTime.UtcNow, DateTime.UtcNow);
        repository.Report = original;
        repository.CurrentStatus = "SUBMITTED";

        var handler = new UpdateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateProgressReportCommand(10, new UpdateProgressReportRequest("Hacked Summary", null, null, null));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("Original Immutable", repository.Report.Summary);
    }

    [Fact]
    public async Task SubmitReport_Leader_Succeeds()
    {
        repository.Report = new ProgressReportDto(10, 1, 1, "Author", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Draft Content", "Completed task 1", "Planned task 2", "No issues", "DRAFT", null, false, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new SubmitProgressReportCommand(10);

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("SUBMITTED", result.Status);
        Assert.NotNull(result.SubmittedAt);
        Assert.Single(audit.Entries);
        Assert.Equal("PROGRESS_REPORT_SUBMITTED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task SubmitReport_MemberWithoutFinalizePermission_Rejects()
    {
        repository.IsLeader = false;
        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new SubmitProgressReportCommand(10);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SubmitReport_AlreadySubmitted_RejectsOrIdempotentPerDesign()
    {
        repository.CurrentStatus = "SUBMITTED";
        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new SubmitProgressReportCommand(10);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SubmitReport_WhenReportingPolicyAbsent_DoesNotFabricateDeadlineOrLateFromPeriodEnd()
    {
        var futureEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        repository.Report = new ProgressReportDto(10, 1, 1, "Author", "WEEKLY", futureEnd.AddDays(-7), futureEnd,
            "Summary", "Completed work", "Planned work", "No risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var result = await handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None);

        Assert.Equal("SUBMITTED", result.Status);
        Assert.Null(result.IsLate);
    }

    [Fact]
    public async Task SubmitReport_PastPeriodEnd_DoesNotFabricateLateWithoutPolicy()
    {
        var pastEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
        repository.Report = new ProgressReportDto(10, 1, 1, "Author", "WEEKLY", pastEnd.AddDays(-7), pastEnd,
            "Summary", "Completed work", "Planned work", "No risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var result = await handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None);

        Assert.Equal("SUBMITTED", result.Status);
        Assert.Null(result.IsLate);
    }

    [Fact]
    public async Task AssignedSupervisor_Feedback_Succeeds()
    {
        repository.CurrentStatus = "SUBMITTED";
        repository.Report = new ProgressReportDto(10, 1, 1, "Author", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Summary", null, null, null, "SUBMITTED", DateTime.UtcNow, false, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new AddProgressReportFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddProgressReportFeedbackCommand(10, new AddProgressReportFeedbackRequest("Great work!"));

        var result = await handler.Handle(command, CancellationToken.None);
        Assert.Equal("Great work!", result.FeedbackText);
        Assert.Equal("REVIEWED", repository.Report.Status);
    }

    [Fact]
    public async Task OtherSupervisor_Feedback_403()
    {
        repository.SupervisorAssignmentId = null;
        var handler = new AddProgressReportFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddProgressReportFeedbackCommand(10, new AddProgressReportFeedbackRequest("Feedback"));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task EndedSupervisor_Feedback_403()
    {
        repository.SupervisorAssignmentId = null; // Represents ended assignment
        var handler = new AddProgressReportFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddProgressReportFeedbackCommand(10, new AddProgressReportFeedbackRequest("Feedback"));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task DraftReport_Feedback_409()
    {
        repository.CurrentStatus = "DRAFT";
        var handler = new AddProgressReportFeedbackCommandHandler(repository, executionGuard, currentUser, audit, clock);
        var command = new AddProgressReportFeedbackCommand(10, new AddProgressReportFeedbackRequest("Feedback"));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationToken_Forwarded_ToEndOfFlow()
    {
        using var cts = new CancellationTokenSource();
        var handler = new GetProgressReportByIdQueryHandler(repository, projectAccess, currentUser);
        repository.Detail = new ProgressReportDetailDto(10, 1, 1, "Author", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Summary", null, null, null, "DRAFT", null, false, DateTime.UtcNow, DateTime.UtcNow, Array.Empty<ProgressReportFeedbackDto>());

        await handler.Handle(new GetProgressReportByIdQuery(10), cts.Token);
        Assert.Equal(cts.Token, repository.LastToken);
    }

    [Fact]
    public void ProgressReportMapper_WhenEntitySubmittedAfterPeriodEnd_DoesNotFabricateLateStatus()
    {
        var entity = new AIPMS.Infrastructure.Persistence.Generated.Models.ProgressReport
        {
            Id = 1,
            ProjectId = 10,
            SubmittedBy = 5,
            ReportType = "WEEKLY",
            PeriodStart = new DateOnly(2026, 9, 1),
            PeriodEnd = new DateOnly(2026, 9, 7),
            Summary = "Summary",
            Status = "SUBMITTED",
            SubmittedAt = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var dto = AIPMS.Infrastructure.Persistence.Mappers.ProgressReportMapper.ToDto(entity);
        var detailDto = AIPMS.Infrastructure.Persistence.Mappers.ProgressReportMapper.ToDetailDto(entity);

        Assert.Null(dto.IsLate);
        Assert.Null(detailDto.IsLate);
    }

    #region Finding 2: Authorization Tests

    [Fact]
    public async Task Staff_CreateDraft_403()
    {
        repository.IsActiveMember = false;
        var handler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("Only active team members can create progress report drafts.", ex.Message);
    }

    [Fact]
    public async Task Staff_UpdateDraft_403()
    {
        repository.IsActiveMember = false;
        var handler = new UpdateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateProgressReportCommand(10, new UpdateProgressReportRequest("Updated Summary", null, null, null));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("Only active team members can update progress report drafts.", ex.Message);
    }

    [Fact]
    public async Task Supervisor_CreateDraft_403()
    {
        repository.IsActiveMember = false;
        var handler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Supervisor_UpdateDraft_403()
    {
        repository.IsActiveMember = false;
        var handler = new UpdateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var command = new UpdateProgressReportCommand(10, new UpdateProgressReportRequest("Updated Summary", null, null, null));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveMember_CreateUpdate_OK()
    {
        repository.IsActiveMember = true;
        var createHandler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var created = await createHandler.Handle(new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Member Summary", null, null, null)), CancellationToken.None);
        Assert.Equal("DRAFT", created.Status);

        var updateHandler = new UpdateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var updated = await updateHandler.Handle(new UpdateProgressReportCommand(created.Id, new UpdateProgressReportRequest("Member New Summary", "Part 1", null, null)), CancellationToken.None);
        Assert.Equal("Member New Summary", updated.Summary);
    }

    [Fact]
    public async Task Leader_Submit_OK()
    {
        repository.IsLeader = true;
        repository.Report = new ProgressReportDto(10, 1, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Summary", "Completed work", "Planned work", "No risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var result = await handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None);

        Assert.Equal("SUBMITTED", result.Status);
    }

    [Fact]
    public async Task FormerMember_CreateUpdate_403()
    {
        repository.IsActiveMember = false; // LeftAt != null
        var createHandler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        await Assert.ThrowsAsync<ForbiddenException>(() => createHandler.Handle(new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Former Summary", null, null, null)), CancellationToken.None));

        var updateHandler = new UpdateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        await Assert.ThrowsAsync<ForbiddenException>(() => updateHandler.Handle(new UpdateProgressReportCommand(10, new UpdateProgressReportRequest("Former New Summary", null, null, null)), CancellationToken.None));
    }

    #endregion

    #region Finding 5: Completeness Tests

    [Fact]
    public async Task IncompleteDraft_CanBeSaved()
    {
        repository.IsActiveMember = true;
        var createHandler = new CreateProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var created = await createHandler.Handle(new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary Only", null, null, null)), CancellationToken.None);

        Assert.Equal("DRAFT", created.Status);
        Assert.Null(created.CompletedWork);
        Assert.Null(created.PlannedWork);
        Assert.Null(created.IssuesAndRisks);
    }

    [Fact]
    public async Task Submit_MissingCompletedWork_Rejected()
    {
        repository.IsLeader = true;
        repository.Report = new ProgressReportDto(10, 1, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Summary", null, "Planned work", "No risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None));
        Assert.Contains("completedWork", ex.Errors.Keys);
    }

    [Fact]
    public async Task Submit_MissingPlannedWork_Rejected()
    {
        repository.IsLeader = true;
        repository.Report = new ProgressReportDto(10, 1, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Summary", "Completed work", null, "No risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None));
        Assert.Contains("plannedWork", ex.Errors.Keys);
    }

    [Fact]
    public async Task Submit_MissingIssuesAndRisks_Rejected()
    {
        repository.IsLeader = true;
        repository.Report = new ProgressReportDto(10, 1, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Summary", "Completed work", "Planned work", null, "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None));
        Assert.Contains("issuesAndRisks", ex.Errors.Keys);
    }

    [Fact]
    public async Task Submit_WhitespaceRequiredField_Rejected()
    {
        repository.IsLeader = true;
        repository.Report = new ProgressReportDto(10, 1, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Summary", "   ", "Planned work", "No risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None));
        Assert.Contains("completedWork", ex.Errors.Keys);
    }

    [Fact]
    public async Task Submit_WhenSummaryMissing_Rejected()
    {
        repository.IsLeader = true;
        repository.Report = new ProgressReportDto(10, 1, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "   ", "Completed work", "Planned work", "No risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None));
        Assert.Contains("summary", ex.Errors.Keys);
    }

    [Fact]
    public async Task Submit_WhenRequiredContentComplete_Succeeds()
    {
        repository.IsLeader = true;
        repository.Report = new ProgressReportDto(10, 1, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7),
            "Complete Summary", "Completed work", "Planned work", "Risks identified", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow);

        var handler = new SubmitProgressReportCommandHandler(repository, projectAccess, executionGuard, currentUser, audit, clock);
        var result = await handler.Handle(new SubmitProgressReportCommand(10), CancellationToken.None);

        Assert.Equal("SUBMITTED", result.Status);
        Assert.NotNull(result.SubmittedAt);
    }

    #endregion
}
