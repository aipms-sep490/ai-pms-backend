using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.ProgressReports.Queries;
using AIPMS.Domain.Projects;

namespace AIPMS.UnitTests.Application;

public sealed class ProgressReportPrefillTests
{
    [Fact]
    public async Task Handle_WithActivity_ReturnsFormattedPrefillContent()
    {
        var clock = TimeProvider.System;
        var fakeRepo = new FakePrefillRepo(hasActivity: true);
        var cycleRepo = new FakeCycleRepo();
        var access = new FakeProjectAccess(canAccess: true);
        var guard = new FakeExecutionGuard();
        var user = new FakeCurrentUser(1001);

        var handler = new GetProgressReportPrefillQueryHandler(
            fakeRepo,
            cycleRepo,
            access,
            guard,
            user,
            clock);

        var result = await handler.Handle(new GetProgressReportPrefillQuery(50001, 40001), CancellationToken.None);

        Assert.False(result.InsufficientData);
        Assert.NotNull(result.Suggested.CompletedWork);
        Assert.Contains("Setup Database", result.Suggested.CompletedWork);
        Assert.Null(result.Suggested.Summary);
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public async Task Handle_WithNoActivity_ReturnsInsufficientDataTrueAndEmptySuggestions()
    {
        var clock = TimeProvider.System;
        var fakeRepo = new FakePrefillRepo(hasActivity: false);
        var cycleRepo = new FakeCycleRepo();
        var access = new FakeProjectAccess(canAccess: true);
        var guard = new FakeExecutionGuard();
        var user = new FakeCurrentUser(1001);

        var handler = new GetProgressReportPrefillQueryHandler(
            fakeRepo,
            cycleRepo,
            access,
            guard,
            user,
            clock);

        var result = await handler.Handle(new GetProgressReportPrefillQuery(50001, 40001), CancellationToken.None);

        Assert.True(result.InsufficientData);
        Assert.Null(result.Suggested.CompletedWork);
        Assert.Null(result.Suggested.InProgressWork);
        Assert.Null(result.Suggested.Blockers);
        Assert.Null(result.Suggested.Summary);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task Handle_ForbiddenUser_ThrowsForbiddenException()
    {
        var clock = TimeProvider.System;
        var fakeRepo = new FakePrefillRepo(hasActivity: true);
        var cycleRepo = new FakeCycleRepo();
        var access = new FakeProjectAccess(canAccess: false);
        var guard = new FakeExecutionGuard();
        var user = new FakeCurrentUser(9999);

        var handler = new GetProgressReportPrefillQueryHandler(
            fakeRepo,
            cycleRepo,
            access,
            guard,
            user,
            clock);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetProgressReportPrefillQuery(50001, 40001), CancellationToken.None));
    }

    private sealed class FakePrefillRepo(bool hasActivity) : IProgressReportRepository
    {
        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<ProgressReportPrefillDto> GetPrefillDataAsync(
            long projectId,
            long? progressReportPeriodId,
            DateOnly periodStart,
            DateOnly periodEnd,
            string reportType,
            CancellationToken cancellationToken)
        {
            if (hasActivity)
            {
                var evidence = new List<ProgressReportPrefillEvidenceDto>
                {
                    new("TASK", 101, "Setup Database", "COMPLETED"),
                    new("TASK", 102, "Build Auth API", "IN_PROGRESS"),
                    new("TASK", 103, "Payment Gateway", "BLOCKED")
                };

                return Task.FromResult(new ProgressReportPrefillDto(
                    progressReportPeriodId,
                    reportType,
                    periodStart,
                    periodEnd,
                    new ProgressReportSuggestedContentDto(
                        "- Setup Database",
                        "- Build Auth API",
                        "- Payment Gateway (BLOCKED)",
                        null,
                        "- Build Auth API",
                        null),
                    evidence,
                    false));
            }

            return Task.FromResult(new ProgressReportPrefillDto(
                progressReportPeriodId,
                reportType,
                periodStart,
                periodEnd,
                new ProgressReportSuggestedContentDto(null, null, null, null, null, null),
                [],
                true));
        }

        public Task<ProgressReportDto?> GetByIdAsync(long id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<AIPMS.Application.Common.Models.PagedResult<ProgressReportDto>> GetReportsAsync(long projectId, string? reportType, string? status, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsForPeriodAsync(long projectId, string reportType, DateOnly periodStart, DateOnly periodEnd, long? excludeId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsForPeriodIdAsync(long periodId, long? excludeId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, long? progressReportPeriodId, string? inProgressWork, string? blockers, string? risks, string? nextActions, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> CreateAsync(long projectId, long submittedBy, string reportType, DateOnly periodStart, DateOnly periodEnd, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, long? progressReportPeriodId, string? inProgressWork, string? blockers, string? risks, string? nextActions, Func<ProgressReportDto, Task>? onCreated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportDto> UpdateAsync(long reportId, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> UpdateAsync(long reportId, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, long? progressReportPeriodId, string? inProgressWork, string? blockers, string? risks, string? nextActions, string? concurrencyToken, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> UpdateAsync(long reportId, string summary, string? completedWork, string? plannedWork, string? issuesAndRisks, DateTime now, long? progressReportPeriodId, string? inProgressWork, string? blockers, string? risks, string? nextActions, Func<ProgressReportDto, Task>? onUpdated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportDto> SubmitAsync(long reportId, DateTime submittedAt, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> SubmitAsync(long reportId, DateTime submittedAt, Func<ProgressReportDto, Task>? onSubmitted, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportDto> SubmitAsync(long reportId, long actorId, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportDto> SubmitAsync(long reportId, long actorId, DateTime now, Func<ProgressReportDto, Task>? onSubmitted, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProgressReportFeedbackDto> AddFeedbackAsync(long reportId, long supervisorAssignmentId, string feedbackText, DateTime now, Func<ProgressReportFeedbackDto, Task>? onAdded, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<long?> GetProjectIdAsync(long reportId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<string?> GetStatusAsync(long reportId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> IsActiveTeamMemberAsync(long projectId, long userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeCycleRepo : IReportingCycleRepository
    {
        public Task<(long ProjectId, string ReportType, DateTime PeriodStart, DateTime PeriodEnd)?> GetCycleHeaderAsync(long cycleId, CancellationToken cancellationToken) =>
            Task.FromResult<(long ProjectId, string ReportType, DateTime PeriodStart, DateTime PeriodEnd)?>(
                (50001, "WEEKLY", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow));

        public Task<ReportingCycleDto?> GetByIdAsync(long id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ReportingCycleDto>> GetCyclesForProjectAsync(long projectId, string? reportType, string? status, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ReportingCycleDto> CreateAsync(long projectId, int cycleNumber, DateTimeOffset periodStart, DateTimeOffset periodEnd, DateTimeOffset dueAt, string reportType, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ReportingCycleDto> CreateAsync(long projectId, long academicSemesterId, string reportType, DateTime periodStart, DateTime periodEnd, DateTime dueAt, string status, long actorId, DateTime now, Func<ReportingCycleDto, Task>? onCreated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ReportingCycleDto> UpdateAsync(long cycleId, DateTimeOffset? periodStart, DateTimeOffset? periodEnd, DateTimeOffset? dueAt, string? reportType, string? status, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ReportingCycleDto> UpdateAsync(long cycleId, DateTime? periodStart, DateTime? periodEnd, DateTime? dueAt, string? status, Guid? concurrencyToken, DateTime now, Func<ReportingCycleDto, Task>? onUpdated, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AIPMS.Application.Common.Models.PagedResult<ReportingCycleDto>> ListAsync(long projectId, string? reportType, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsOverlapAsync(long projectId, string reportType, DateTime start, DateTime end, long? excludeCycleId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasLinkedReportAsync(long cycleId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<long?> GetDefaultProjectPeriodIdAsync(long projectId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> IsValidProjectPeriodAsync(long projectId, long projectPeriodId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasAdminRoleInDbAsync(long userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasStaffRoleInDbAsync(long userId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeProjectAccess(bool canAccess) : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken) =>
            Task.FromResult(canAccess);
    }

    private sealed class FakeExecutionGuard : IProjectExecutionGuard
    {
        public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCurrentUser(long userId) : ICurrentUser
    {
        public long? UserId => userId;
        public string Email => "test@aipms.test";
        public string FullName => "Test User";
        public bool IsAuthenticated => true;
        public IReadOnlyCollection<string> Roles => ["STUDENT"];
    }
}
