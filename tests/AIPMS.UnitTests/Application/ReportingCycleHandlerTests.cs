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

public sealed class ReportingCycleHandlerTests
{
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public long? UserId { get; set; } = 1;
        public string Email { get; set; } = "staff@aipms.edu";
        public string Name { get; set; } = "Staff User";
        public string FullName => Name;
        public HashSet<string> Roles { get; set; } = new(StringComparer.Ordinal) { AppRoles.DepartmentStaff };
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

    private sealed class FakeReportingCycleRepository : IReportingCycleRepository
    {
        public List<ReportingCycleDto> Cycles { get; } = new();
        public bool ValidPeriod { get; set; } = true;
        public long? DefaultPeriodId { get; set; } = 100;
        private long _idCounter = 1;

        public Task<bool> ExistsOverlapAsync(long projectId, string reportType, DateTime periodStart, DateTime periodEnd, long? excludeId = null, CancellationToken cancellationToken = default)
        {
            // Half-open interval overlap predicate: StartA < EndB && StartB < EndA
            var overlaps = Cycles.Exists(c =>
                c.ProjectId == projectId &&
                string.Equals(c.ReportType, reportType, StringComparison.OrdinalIgnoreCase) &&
                (!excludeId.HasValue || c.Id != excludeId.Value) &&
                c.PeriodStart < periodEnd && periodStart < c.PeriodEnd);

            return Task.FromResult(overlaps);
        }

        public Task<bool> IsValidProjectPeriodAsync(long projectId, long projectPeriodId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ValidPeriod);

        public Task<long?> GetDefaultProjectPeriodIdAsync(long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DefaultPeriodId);

        public Task<ReportingCycleDto> CreateAsync(long projectId, long projectPeriodId, string reportType, DateTime periodStart, DateTime periodEnd, DateTime deadline, string latePolicy, long createdBy, DateTime now, CancellationToken cancellationToken = default)
        {
            var dto = new ReportingCycleDto(
                _idCounter++,
                projectId,
                projectPeriodId,
                reportType,
                periodStart,
                periodEnd,
                deadline,
                latePolicy,
                createdBy,
                now,
                now,
                Guid.NewGuid().ToString("N"));
            Cycles.Add(dto);
            return Task.FromResult(dto);
        }

        public Task<ReportingCycleDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            var found = Cycles.Find(c => c.Id == id);
            return Task.FromResult(found);
        }

        public Task<PagedResult<ReportingCycleDto>> ListAsync(long projectId, string? reportType, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var list = Cycles.FindAll(c => c.ProjectId == projectId &&
                (string.IsNullOrEmpty(reportType) || string.Equals(c.ReportType, reportType, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult(new PagedResult<ReportingCycleDto>(list, page, pageSize, list.Count));
        }

        public Task<ReportingCycleDto> UpdateAsync(long id, DateTime? periodStart, DateTime? periodEnd, DateTime? deadline, string? latePolicy, Guid? expectedToken, DateTime now, CancellationToken cancellationToken = default)
        {
            var idx = Cycles.FindIndex(c => c.Id == id);
            if (idx == -1) throw new NotFoundException("ReportingCycle", id);
            var prev = Cycles[idx];
            var updated = prev with
            {
                PeriodStart = periodStart ?? prev.PeriodStart,
                PeriodEnd = periodEnd ?? prev.PeriodEnd,
                Deadline = deadline ?? prev.Deadline,
                LatePolicy = latePolicy ?? prev.LatePolicy,
                UpdatedAt = now,
                ConcurrencyToken = Guid.NewGuid().ToString("N")
            };
            Cycles[idx] = updated;
            return Task.FromResult(updated);
        }

        public Task<bool> HasLinkedReportAsync(long cycleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
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
    public async Task CreateReportingCycle_Valid_Succeeds()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)));

        var req = new CreateReportingCycleRequest(
            ReportType: "WEEKLY",
            PeriodStart: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            Deadline: new DateTimeOffset(2026, 10, 8, 23, 59, 59, TimeSpan.Zero),
            LatePolicy: "BLOCK",
            ProjectPeriodId: null);

        var result = await handler.Handle(new CreateReportingCycleCommand(1, req), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("WEEKLY", result.ReportType);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), result.PeriodStart);
        Assert.Equal(new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc), result.PeriodEnd);
        Assert.Equal("BLOCK", result.LatePolicy);
    }

    [Fact]
    public async Task CreateReportingCycle_UnauthorizedUser_ThrowsUnauthorizedException()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser { UserId = null };
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var req = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new CreateReportingCycleCommand(1, req), CancellationToken.None));
    }

    [Fact]
    public async Task CreateReportingCycle_StudentOrSupervisor_ThrowsForbiddenException()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser { Roles = new HashSet<string> { AppRoles.Student, AppRoles.Lecturer } };
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var req = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CreateReportingCycleCommand(1, req), CancellationToken.None));
    }

    [Fact]
    public async Task CreateReportingCycle_PeriodEndBeforeOrEqualStart_ThrowsValidationException()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // PeriodEnd == PeriodStart is invalid under half-open [Start, End) with Start < End
        var req = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateReportingCycleCommand(1, req), CancellationToken.None));
        Assert.True(ex.Errors.ContainsKey("periodEnd"));
    }

    [Fact]
    public async Task CreateReportingCycle_InvalidRangeAfterNormalization_ThrowsValidationException()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // Start = 10:00 UTC
        // End = 16:00 +07:00 -> 09:00 UTC
        // This is invalid because 09:00 UTC < 10:00 UTC
        var req = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.FromHours(7)),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateReportingCycleCommand(1, req), CancellationToken.None));
        Assert.True(ex.Errors.ContainsKey("periodEnd"));
    }

    [Fact]
    public async Task CreateReportingCycle_Overlap_ThrowsConflictException()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // Existing: [Oct 1, Oct 8)
        await handler.Handle(new CreateReportingCycleCommand(1, new CreateReportingCycleRequest(
            "WEEKLY",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null)), CancellationToken.None);

        // Overlapping: [Oct 4, Oct 11)
        var overlapReq = new CreateReportingCycleRequest(
            "WEEKLY",
            new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(10), "BLOCK", null);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new CreateReportingCycleCommand(1, overlapReq), CancellationToken.None));
    }

    [Fact]
    public async Task CreateReportingCycle_BoundaryAdjacent_NoOverlap_Succeeds()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // Existing: [Oct 1, Oct 8)
        await handler.Handle(new CreateReportingCycleCommand(1, new CreateReportingCycleRequest(
            "WEEKLY",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null)), CancellationToken.None);

        // Boundary-adjacent: [Oct 8, Oct 15) -> StartB == EndA -> half-open intervals do NOT overlap
        var adjacentReq = new CreateReportingCycleRequest(
            "WEEKLY",
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(14), "BLOCK", null);

        var result = await handler.Handle(new CreateReportingCycleCommand(1, adjacentReq), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc), result.PeriodStart);
        Assert.Equal(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), result.PeriodEnd);
    }

    [Fact]
    public async Task CreateReportingCycle_DifferentCadence_NoOverlap_Succeeds()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // Existing WEEKLY: [Oct 1, Oct 8)
        await handler.Handle(new CreateReportingCycleCommand(1, new CreateReportingCycleRequest(
            "WEEKLY",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null)), CancellationToken.None);

        // MONTHLY for same project over same dates -> cadence-independent, permitted
        var monthlyReq = new CreateReportingCycleRequest(
            "MONTHLY",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(30), "FLAG", null);

        var result = await handler.Handle(new CreateReportingCycleCommand(1, monthlyReq), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("MONTHLY", result.ReportType);
    }

    [Fact]
    public async Task UpdateReportingCycle_ExcludesSelfFromOverlap_Succeeds()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var createHandler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        var created = await createHandler.Handle(new CreateReportingCycleCommand(1, new CreateReportingCycleRequest(
            "WEEKLY",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow.AddDays(7), "BLOCK", null)), CancellationToken.None);

        var updateHandler = new UpdateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // Update self with shifted dates: [Oct 2, Oct 9)
        var updateReq = new UpdateReportingCycleRequest(
            PeriodStart: new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
            Deadline: DateTimeOffset.UtcNow.AddDays(8),
            LatePolicy: "FLAG");

        var updated = await updateHandler.Handle(new UpdateReportingCycleCommand(1, created.Id, updateReq), CancellationToken.None);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), updated.PeriodStart);
        Assert.Equal("FLAG", updated.LatePolicy);
    }
    [Fact]
    public async Task CreateReportingCycle_DifferentOffsetsForSameInstant_ProducesSameUtcValue()
    {
        var repo = new FakeReportingCycleRepository();
        var user = new FakeCurrentUser();
        var handler = new CreateReportingCycleCommandHandler(
            repo,
            new StubProgressRepo(),
            new FakeProjectAccessService(),
            new FakeProjectExecutionGuard(),
            user,
            new FakeAuditTrail(),
            new FakeTimeProvider(DateTime.UtcNow));

        // +07:00 vs +00:00 (UTC)
        var startUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var startLocal = new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.FromHours(7)); // Equivalent instant

        var req = new CreateReportingCycleRequest(
            "WEEKLY",
            startLocal, // Will be normalized
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 23, 59, 59, TimeSpan.Zero));

        var result = await handler.Handle(new CreateReportingCycleCommand(1, req), CancellationToken.None);

        Assert.Equal(startUtc.UtcDateTime, result.PeriodStart);
        Assert.Equal(DateTimeKind.Utc, result.PeriodStart.Kind);
    }
}
