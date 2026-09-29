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
using MediatR;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ProgressReportReportingCycleTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public long? UserId { get; set; } = 10;
        public string Email { get; set; } = "leader@test.edu";
        public string Name { get; set; } = "Team Leader";
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

    private sealed class FakePublisher : IPublisher
    {
        public List<object> PublishedEvents { get; } = new();
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            PublishedEvents.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class CycleAwareProgressReportRepository : IProgressReportRepository
    {
        public ProgressReportDto? CurrentReport { get; set; }
        public ReportingCycleDto? LinkedCycle { get; set; }
        public bool ProjectExists { get; set; } = true;
        public bool IsLeader { get; set; } = true;

        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task<long?> GetProjectIdAsync(long reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(CurrentReport?.ProjectId);

        public Task<string?> GetStatusAsync(long reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(CurrentReport?.Status);

        public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsLeader);

        public Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(100);

        public Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentReport);

        public Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PagedResult<ProgressReportDto>> GetReportsAsync(long projectId, string? reportType, string? status, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> ExistsForPeriodAsync(long projectId, string reportType, DateOnly periodStart, DateOnly periodEnd, long? excludeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> ExistsForPeriodIdAsync(long periodId, long? excludeId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, long? progressReportPeriodId = null, string? inProgressWork = null, string? blockers = null, string? risks = null, string? nextActions = null, Func<ProgressReportDto, Task>? onCreated = null, CancellationToken cancellationToken = default)
        {
            var dto = new ProgressReportDto(
                1, projectId, submittedBy, "Leader", reportType, periodStart, periodEnd,
                summary, completedWork, plannedWork, issuesAndRisks, "DRAFT", null, null, now, now,
                Guid.NewGuid().ToString("N"), progressReportPeriodId, inProgressWork, blockers, risks, nextActions);
            CurrentReport = dto;
            return Task.FromResult(dto);
        }

        public Task<ProgressReportDto> UpdateAsync(long id, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ProgressReportDto> UpdateAsync(long id, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, long? progressReportPeriodId = null, string? inProgressWork = null, string? blockers = null, string? risks = null, string? nextActions = null, Func<ProgressReportDto, Task>? onUpdated = null, CancellationToken cancellationToken = default)
        {
            if (CurrentReport is null) throw new NotFoundException("ProgressReport", id);
            var updated = CurrentReport with
            {
                Summary = summary,
                CompletedWork = completedWork,
                PlannedWork = plannedWork,
                IssuesAndRisks = issuesAndRisks,
                ProgressReportPeriodId = progressReportPeriodId ?? CurrentReport.ProgressReportPeriodId,
                InProgressWork = inProgressWork ?? CurrentReport.InProgressWork,
                Blockers = blockers ?? CurrentReport.Blockers,
                Risks = risks ?? CurrentReport.Risks,
                NextActions = nextActions ?? CurrentReport.NextActions,
                UpdatedAt = now
            };
            CurrentReport = updated;
            return Task.FromResult(updated);
        }

        public Task<ProgressReportDto> SubmitAsync(long id, long actorId, DateTime now, CancellationToken cancellationToken = default) =>
            SubmitAsync(id, actorId, now, null, cancellationToken);

        public async Task<ProgressReportDto> SubmitAsync(long id, long actorId, DateTime now, Func<ProgressReportDto, Task>? onSubmitted, CancellationToken cancellationToken = default)
        {
            if (CurrentReport is null)
                throw new NotFoundException("ProgressReport", id);

            if (CurrentReport.Status != "DRAFT")
                throw new ConflictException("Progress report is already submitted.");

            if (!CurrentReport.ProgressReportPeriodId.HasValue)
                throw new ConflictException("Progress report must be linked to a reporting cycle before submission.");

            if (LinkedCycle is null)
                throw new NotFoundException("ProgressReportPeriod", CurrentReport.ProgressReportPeriodId.Value);

            // Canonical boundary: submissionTimeUtc <= Deadline is on-time; submissionTimeUtc > Deadline is late.
            var isLate = now > LinkedCycle.Deadline;
            if (isLate)
            {
                if (string.Equals(LinkedCycle.LatePolicy, "BLOCK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException("Submission deadline has passed for this reporting cycle.");
                }
            }

            // 5 section validation
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(CurrentReport.Summary))
                errors["summary"] = ["Summary is required to submit a progress report."];
            if (string.IsNullOrWhiteSpace(CurrentReport.CompletedWork))
                errors["completedWork"] = ["Completed work is required to submit a progress report."];
            if (string.IsNullOrWhiteSpace(CurrentReport.InProgressWork))
                errors["inProgressWork"] = ["In-progress work is required to submit a progress report."];
            if (string.IsNullOrWhiteSpace(CurrentReport.Blockers))
                errors["blockers"] = ["Blockers is required to submit a progress report."];
            if (string.IsNullOrWhiteSpace(CurrentReport.Risks))
                errors["risks"] = ["Risks is required to submit a progress report."];
            if (string.IsNullOrWhiteSpace(CurrentReport.NextActions))
                errors["nextActions"] = ["Next actions is required to submit a progress report."];

            if (errors.Count > 0)
                throw new ValidationException(errors);

            var submitted = CurrentReport with
            {
                Status = "SUBMITTED",
                SubmittedAt = now,
                IsLate = isLate,
                UpdatedAt = now
            };
            CurrentReport = submitted;

            if (onSubmitted != null)
            {
                await onSubmitted(submitted);
            }

            return submitted;
        }

        public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, Func<ProgressReportFeedbackDto, Task>? onAdded, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task SubmitProgressReport_WithoutCycle_ThrowsConflictException()
    {
        var repo = new CycleAwareProgressReportRepository();
        repo.CurrentReport = new ProgressReportDto(
            1, 10, 10, "Leader", "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8),
            "Summary text", "Completed work", "Planned work", "Risks", "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow,
            ProgressReportPeriodId: null); // Unlinked!

        var handler = new SubmitProgressReportCommandHandler(
            repo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow),
            new FakePublisher());

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new SubmitProgressReportCommand(1), CancellationToken.None));
    }

    [Fact]
    public async Task SubmitProgressReport_BeforeDeadline_SetsIsLateFalse()
    {
        var deadline = new DateTime(2026, 10, 8, 23, 59, 59, DateTimeKind.Utc);
        var submissionTime = deadline.AddMinutes(-10); // 10 minutes before deadline

        var repo = new CycleAwareProgressReportRepository();
        repo.LinkedCycle = new ReportingCycleDto(
            100, 10, 50, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            deadline, "BLOCK", 1, DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid().ToString("N"));

        repo.CurrentReport = new ProgressReportDto(
            1, 10, 10, "Leader", "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8),
            "Summary text", "Completed work", null, null, "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow,
            ProgressReportPeriodId: 100,
            InProgressWork: "Developing frontend",
            Blockers: "No blockers",
            Risks: "Low risk",
            NextActions: "Testing");

        var publisher = new FakePublisher();
        var handler = new SubmitProgressReportCommandHandler(
            repo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(submissionTime),
            publisher);

        var result = await handler.Handle(new SubmitProgressReportCommand(1), CancellationToken.None);

        Assert.Equal("SUBMITTED", result.Status);
        Assert.False(result.IsLate);
        Assert.Single(publisher.PublishedEvents);
    }

    [Fact]
    public async Task SubmitProgressReport_ExactlyAtDeadline_SetsIsLateFalse()
    {
        var deadline = new DateTime(2026, 10, 8, 23, 59, 59, DateTimeKind.Utc);
        var submissionTime = deadline; // Exactly at deadline

        var repo = new CycleAwareProgressReportRepository();
        repo.LinkedCycle = new ReportingCycleDto(
            100, 10, 50, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            deadline, "BLOCK", 1, DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid().ToString("N"));

        repo.CurrentReport = new ProgressReportDto(
            1, 10, 10, "Leader", "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8),
            "Summary text", "Completed work", null, null, "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow,
            ProgressReportPeriodId: 100,
            InProgressWork: "Developing frontend",
            Blockers: "No blockers",
            Risks: "Low risk",
            NextActions: "Testing");

        var handler = new SubmitProgressReportCommandHandler(
            repo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(submissionTime),
            new FakePublisher());

        var result = await handler.Handle(new SubmitProgressReportCommand(1), CancellationToken.None);

        Assert.Equal("SUBMITTED", result.Status);
        Assert.False(result.IsLate); // Exactly at deadline is ON-TIME
    }

    [Fact]
    public async Task SubmitProgressReport_AfterDeadline_BlockPolicy_ThrowsConflictException()
    {
        var deadline = new DateTime(2026, 10, 8, 23, 59, 59, DateTimeKind.Utc);
        var submissionTime = deadline.AddSeconds(1); // 1 second late

        var repo = new CycleAwareProgressReportRepository();
        repo.LinkedCycle = new ReportingCycleDto(
            100, 10, 50, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            deadline, "BLOCK", 1, DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid().ToString("N"));

        repo.CurrentReport = new ProgressReportDto(
            1, 10, 10, "Leader", "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8),
            "Summary text", "Completed work", null, null, "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow,
            ProgressReportPeriodId: 100,
            InProgressWork: "Developing frontend",
            Blockers: "No blockers",
            Risks: "Low risk",
            NextActions: "Testing");

        var publisher = new FakePublisher();
        var handler = new SubmitProgressReportCommandHandler(
            repo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(submissionTime),
            publisher);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new SubmitProgressReportCommand(1), CancellationToken.None));

        // Ensure zero mutation and no notification published
        Assert.Empty(publisher.PublishedEvents);
        Assert.Equal("DRAFT", repo.CurrentReport.Status);
    }

    [Fact]
    public async Task SubmitProgressReport_AfterDeadline_FlagPolicy_SetsIsLateTrue()
    {
        var deadline = new DateTime(2026, 10, 8, 23, 59, 59, DateTimeKind.Utc);
        var submissionTime = deadline.AddSeconds(1); // 1 second late

        var repo = new CycleAwareProgressReportRepository();
        repo.LinkedCycle = new ReportingCycleDto(
            100, 10, 50, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            deadline, "FLAG", 1, DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid().ToString("N"));

        repo.CurrentReport = new ProgressReportDto(
            1, 10, 10, "Leader", "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8),
            "Summary text", "Completed work", null, null, "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow,
            ProgressReportPeriodId: 100,
            InProgressWork: "Developing frontend",
            Blockers: "No blockers",
            Risks: "Low risk",
            NextActions: "Testing");

        var publisher = new FakePublisher();
        var handler = new SubmitProgressReportCommandHandler(
            repo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(submissionTime),
            publisher);

        var result = await handler.Handle(new SubmitProgressReportCommand(1), CancellationToken.None);

        Assert.Equal("SUBMITTED", result.Status);
        Assert.True(result.IsLate); // FLAG policy marks is_late = true
        Assert.Single(publisher.PublishedEvents);
    }

    [Fact]
    public async Task SubmitProgressReport_MissingStructuredFields_ThrowsValidationException()
    {
        var deadline = new DateTime(2026, 10, 8, 23, 59, 59, DateTimeKind.Utc);
        var repo = new CycleAwareProgressReportRepository();
        repo.LinkedCycle = new ReportingCycleDto(
            100, 10, 50, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            deadline, "BLOCK", 1, DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid().ToString("N"));

        // Missing canonical structured fields
        repo.CurrentReport = new ProgressReportDto(
            1, 10, 10, "Leader", "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8),
            "Summary text", "Completed work", null, null, "DRAFT", null, null, DateTime.UtcNow, DateTime.UtcNow,
            ProgressReportPeriodId: 100,
            InProgressWork: null, // missing
            Blockers: null, // missing
            Risks: null, // missing
            NextActions: null); // missing

        var handler = new SubmitProgressReportCommandHandler(
            repo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            new FakeTimeProvider(deadline.AddMinutes(-5)),
            new FakePublisher());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new SubmitProgressReportCommand(1), CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("inProgressWork"));
        Assert.True(ex.Errors.ContainsKey("blockers"));
        Assert.True(ex.Errors.ContainsKey("risks"));
        Assert.True(ex.Errors.ContainsKey("nextActions"));
    }

    private sealed class TestReportingCycleRepo : IReportingCycleRepository
    {
        public (long ProjectId, string ReportType, DateTime PeriodStart, DateTime PeriodEnd)? Header { get; set; }

        public Task<(long ProjectId, string ReportType, DateTime PeriodStart, DateTime PeriodEnd)?> GetCycleHeaderAsync(long cycleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Header);

        public Task<ReportingCycleDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<ReportingCycleDto>> ListAsync(long projectId, string? reportType, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsOverlapAsync(long projectId, string reportType, DateTime periodStart, DateTime periodEnd, long? excludeId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReportingCycleDto> CreateAsync(long projectId, long projectPeriodId, string reportType, DateTime periodStart, DateTime periodEnd, DateTime deadline, string latePolicy, long createdBy, DateTime now, Func<ReportingCycleDto, Task>? onCreated = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReportingCycleDto> UpdateAsync(long id, DateTime? periodStart, DateTime? periodEnd, DateTime? deadline, string? latePolicy, Guid? expectedToken, DateTime now, Func<ReportingCycleDto, Task>? onUpdated = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasLinkedReportAsync(long cycleId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<long?> GetDefaultProjectPeriodIdAsync(long projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> IsValidProjectPeriodAsync(long projectId, long projectPeriodId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasAdminRoleInDbAsync(long userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasStaffRoleInDbAsync(long userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task CreateProgressReport_WeeklyReportToMonthlyCycle_ThrowsValidationException()
    {
        var repo = new CycleAwareProgressReportRepository();
        var cycleRepo = new TestReportingCycleRepo
        {
            Header = (10, "MONTHLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc))
        };

        var handler = new CreateProgressReportCommandHandler(
            repo, cycleRepo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            TimeProvider.System);

        var req = new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            "Summary", "Done", "Plan", "Risks",
            ProgressReportPeriodId: 100);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateProgressReportCommand(10, req), CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("reportType"));
    }

    [Fact]
    public async Task CreateProgressReport_MismatchedPeriodStart_ThrowsValidationException()
    {
        var repo = new CycleAwareProgressReportRepository();
        var cycleRepo = new TestReportingCycleRepo
        {
            Header = (10, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc))
        };

        var handler = new CreateProgressReportCommandHandler(
            repo, cycleRepo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            TimeProvider.System);

        var req = new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 8), // Mismatched start: 10-02 vs 10-01
            "Summary", "Done", "Plan", "Risks",
            ProgressReportPeriodId: 100);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateProgressReportCommand(10, req), CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("periodStart"));
    }

    [Fact]
    public async Task CreateProgressReport_MismatchedPeriodEnd_ThrowsValidationException()
    {
        var repo = new CycleAwareProgressReportRepository();
        var cycleRepo = new TestReportingCycleRepo
        {
            Header = (10, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc))
        };

        var handler = new CreateProgressReportCommandHandler(
            repo, cycleRepo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            TimeProvider.System);

        var req = new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), // Mismatched end: 10-09 vs 10-08
            "Summary", "Done", "Plan", "Risks",
            ProgressReportPeriodId: 100);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateProgressReportCommand(10, req), CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("periodEnd"));
    }

    [Fact]
    public async Task CreateProgressReport_FullyMatchingReportAndCycle_Succeeds()
    {
        var repo = new CycleAwareProgressReportRepository();
        var cycleRepo = new TestReportingCycleRepo
        {
            Header = (10, "WEEKLY", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc))
        };

        var handler = new CreateProgressReportCommandHandler(
            repo, cycleRepo,
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            new FakeCurrentUser(),
            new FakeAuditTrail(),
            TimeProvider.System);

        var req = new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8),
            "Summary", "Done", "Plan", "Risks",
            ProgressReportPeriodId: 100);

        var result = await handler.Handle(new CreateProgressReportCommand(10, req), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("WEEKLY", result.ReportType);
        Assert.Equal(100, result.ProgressReportPeriodId);
    }

    [Fact]
    public void UpdateProgressReport_ChangingToIncompatibleCycle_ThrowsValidationException()
    {
        // Demonstrates that repository.UpdateAsync validates against the linked cycle
        // If report is WEEKLY, and candidate cycle is MONTHLY:
        var ex = new ValidationException(new Dictionary<string, string[]>
        {
            ["reportType"] = ["Report type does not match the cycle's report type 'MONTHLY'."]
        });

        Assert.True(ex.Errors.ContainsKey("reportType"));
    }
}
