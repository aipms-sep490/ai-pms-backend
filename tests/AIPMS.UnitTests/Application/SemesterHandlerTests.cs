using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.Commands;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Models;
using AIPMS.Application.Features.Semesters.Queries;
using AIPMS.Application.Features.Semesters.Services;
using AIPMS.Application.Features.Semesters.Validators;

namespace AIPMS.UnitTests.Application;

// ── Stub repository ────────────────────────────────────────────────────────────

internal sealed class StubSemesterRepository : ISemesterRepository
{
    private long _nextSemesterId = 100;
    private long _nextPeriodId = 200;

    public Dictionary<long, AcademicSemesterModel> Semesters { get; } = [];
    public Dictionary<long, ProjectPeriodModel> Periods { get; } = [];
    public bool SemesterCodeDuplicate { get; set; }
    public bool PeriodCodeDuplicate { get; set; }
    public bool HasActiveProjects { get; set; }

    private static readonly DateTime Ts = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    public Task<PagedResult<AcademicSemesterModel>> GetSemestersAsync(
        long? organizationId, string? search, string? status, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<AcademicSemesterModel>(
            Semesters.Values.ToArray(), page, pageSize, Semesters.Count));

    public Task<AcademicSemesterModel?> GetSemesterAsync(
        long semesterId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Semesters.GetValueOrDefault(semesterId));

    public Task<bool> SemesterCodeExistsAsync(
        long organizationId, string code, long? excludedSemesterId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SemesterCodeDuplicate);

    public Task<AcademicSemesterModel> CreateSemesterAsync(
        long organizationId, string code, string name,
        DateOnly startDate, DateOnly endDate, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var result = new AcademicSemesterModel(
            _nextSemesterId++, organizationId, "ORG", "Organization",
            code, name, startDate, endDate, "DRAFT", utcNow, utcNow);
        Semesters[result.Id] = result;
        return Task.FromResult(result);
    }

    public Task<AcademicSemesterModel> UpdateSemesterAsync(
        long semesterId, string code, string name,
        DateOnly startDate, DateOnly endDate, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var existing = Semesters[semesterId];
        var updated = existing with
        {
            Code = code, Name = name, StartDate = startDate,
            EndDate = endDate, UpdatedAt = utcNow
        };
        Semesters[semesterId] = updated;
        return Task.FromResult(updated);
    }

    public Task<AcademicSemesterModel> SetSemesterStatusAsync(
        long semesterId, string status, string? expectedStatus, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var existing = Semesters[semesterId];
        if (!string.IsNullOrEmpty(expectedStatus) && existing.Status != expectedStatus)
        {
            throw new ConflictException("Concurrent modification");
        }
        var updated = existing with { Status = status, UpdatedAt = utcNow };
        Semesters[semesterId] = updated;
        return Task.FromResult(updated);
    }

    public Task<PagedResult<ProjectPeriodModel>> GetProjectPeriodsAsync(
        long? semesterId, string? search, string? status, string? periodType,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var items = Periods.Values.AsEnumerable();
        if (semesterId.HasValue)
        {
            items = items.Where(p => p.AcademicSemesterId == semesterId.Value);
        }
        var arr = items.ToArray();
        return Task.FromResult(new PagedResult<ProjectPeriodModel>(arr, page, pageSize, arr.Length));
    }

    public Task<ProjectPeriodModel?> GetProjectPeriodAsync(
        long periodId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Periods.GetValueOrDefault(periodId));

    public Task<bool> PeriodCodeExistsAsync(
        long semesterId, string code, long? excludedPeriodId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PeriodCodeDuplicate);

    public Task<ProjectPeriodModel> CreateProjectPeriodAsync(
        long semesterId, string code, string name, string periodType,
        DateTime startAt, DateTime endAt,
        int? minTeamSize, int? maxTeamSize, int? minDistinctMajors, int? maxProjectsPerSupervisor,
        long? milestoneTemplateId, long? rubricId,
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var semester = Semesters[semesterId];
        var result = new ProjectPeriodModel(
            _nextPeriodId++, semesterId, semester.Code, semester.Name,
            code, name, periodType, startAt, endAt, "DRAFT",
            minTeamSize, maxTeamSize, minDistinctMajors, maxProjectsPerSupervisor, milestoneTemplateId, rubricId, utcNow, utcNow);
        Periods[result.Id] = result;
        return Task.FromResult(result);
    }

    public Task<ProjectPeriodModel> UpdateProjectPeriodAsync(
        long periodId, string code, string name, string periodType,
        DateTime startAt, DateTime endAt,
        int? minTeamSize, int? maxTeamSize, int? minDistinctMajors, int? maxProjectsPerSupervisor,
        long? milestoneTemplateId, long? rubricId,
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var existing = Periods[periodId];
        var updated = existing with
        {
            Code = code, Name = name, PeriodType = periodType,
            StartAt = startAt, EndAt = endAt,
            MinTeamSize = minTeamSize, MaxTeamSize = maxTeamSize,
            MinDistinctMajors = minDistinctMajors, MaxProjectsPerSupervisor = maxProjectsPerSupervisor,
            MilestoneTemplateId = milestoneTemplateId, RubricId = rubricId,
            UpdatedAt = utcNow
        };
        Periods[periodId] = updated;
        return Task.FromResult(updated);
    }

    public Task<ProjectPeriodModel> SetProjectPeriodStatusAsync(
        long periodId, string status, string? expectedStatus, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var existing = Periods[periodId];
        if (!string.IsNullOrEmpty(expectedStatus) && existing.Status != expectedStatus)
        {
            throw new ConflictException("Concurrent modification");
        }
        var updated = existing with { Status = status, UpdatedAt = utcNow };
        Periods[periodId] = updated;
        return Task.FromResult(updated);
    }

    public Task<bool> HasActiveProjectsAsync(
        long semesterId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasActiveProjects);

    public HashSet<long> UsableRubricIds { get; } = [];

    public Task<bool> ValidateRubricUsableAsync(
        long rubricId, long semesterId, CancellationToken cancellationToken = default) =>
        Task.FromResult(UsableRubricIds.Contains(rubricId));

    public Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action, CancellationToken cancellationToken = default) =>
        action();

    public AcademicSemesterModel AddDraftSemester(long id = 1, string status = "DRAFT")
    {
        var s = new AcademicSemesterModel(
            id, 10, "ORG", "Organization", "SEM-01", "Semester One",
            new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            status, Ts, Ts);
        Semesters[id] = s;
        return s;
    }

    public ProjectPeriodModel AddDraftPeriod(long semesterId, long id = 1, string status = "DRAFT")
    {
        var p = new ProjectPeriodModel(
            id, semesterId, "SEM-01", "Semester One", "REG-01", "Registration Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            status, 3, 5, 1, 5, null, null, Ts, Ts);
        Periods[id] = p;
        return p;
    }
}

// ── Semester Handler Tests ─────────────────────────────────────────────────────

public sealed class SemesterHandlerTests
{
    // ── CreateSemester ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSemester_Admin_NormalizesCodeAndCreatesEntry()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(); // populate org so id 10 is known
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var audit = new RecordingAuditTrail();
        var handler = new CreateSemesterCommandHandler(repo, access, audit, TimeProvider.System);

        var result = await handler.Handle(
            new CreateSemesterCommand(
                10, "  sem-2027  ", "  Spring 2027  ",
                new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)),
            CancellationToken.None);

        Assert.Equal("SEM-2027", result.Code);
        Assert.Equal("Spring 2027", result.Name);
        Assert.Equal("DRAFT", result.Status);
        Assert.Single(audit.Entries);
        Assert.Equal("SEMESTER_CREATED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task CreateSemester_DepartmentStaff_ThrowsForbidden()
    {
        var repo = new StubSemesterRepository();
        var currentUser = new TestCurrentUser(2, AppRoles.DepartmentStaff);
        var access = new SemesterAccessService(currentUser);
        var handler = new CreateSemesterCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new CreateSemesterCommand(
                    10, "SEM-X", "Some Semester",
                    new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)),
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateSemester_DuplicateCode_ThrowsConflict()
    {
        var repo = new StubSemesterRepository();
        repo.SemesterCodeDuplicate = true;
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new CreateSemesterCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new CreateSemesterCommand(
                    10, "SEM-01", "Dup",
                    new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)),
                CancellationToken.None));
    }

    // ── UpdateSemester ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSemester_DraftSemester_UpdatesFields()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 5);
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new UpdateSemesterCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        var result = await handler.Handle(
            new UpdateSemesterCommand(
                5, "SEM-NEW", "New Name",
                new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)),
            CancellationToken.None);

        Assert.Equal("SEM-NEW", result.Code);
        Assert.Equal("New Name", result.Name);
    }

    [Fact]
    public async Task UpdateSemester_ArchivedSemester_ThrowsConflict()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 6, status: "ARCHIVED");
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new UpdateSemesterCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new UpdateSemesterCommand(
                    6, "SEM-X", "Should Fail",
                    new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateSemester_NotFound_ThrowsNotFoundException()
    {
        var repo = new StubSemesterRepository();
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new UpdateSemesterCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(
                new UpdateSemesterCommand(
                    999, "X", "Y",
                    new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)),
                CancellationToken.None));
    }

    // ── SetSemesterStatus ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("DRAFT", "UPCOMING")]
    [InlineData("UPCOMING", "ACTIVE")]
    [InlineData("ACTIVE", "CLOSED")]
    [InlineData("CLOSED", "ARCHIVED")]
    public async Task SetSemesterStatus_ValidTransition_Succeeds(
        string from, string to)
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 10, status: from);
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetSemesterStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        var result = await handler.Handle(
            new SetSemesterStatusCommand(10, to), CancellationToken.None);

        Assert.Equal(to, result.Status);
    }

    [Theory]
    [InlineData("ACTIVE", "DRAFT")]
    [InlineData("CLOSED", "DRAFT")]
    [InlineData("ARCHIVED", "DRAFT")]
    [InlineData("ARCHIVED", "ACTIVE")]
    public async Task SetSemesterStatus_InvalidTransition_ThrowsConflict(
        string from, string to)
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 11, status: from);
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetSemesterStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new SetSemesterStatusCommand(11, to), CancellationToken.None));
    }

    [Fact]
    public async Task SetSemesterStatus_ArchiveWithClosedChild_ThrowsConflict()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 12, status: "CLOSED");
        repo.AddDraftPeriod(semesterId: 12, id: 101, status: "CLOSED");
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetSemesterStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new SetSemesterStatusCommand(12, "ARCHIVED"), CancellationToken.None));
        Assert.Contains("All child project periods must be archived first", ex.Message);
    }

    [Fact]
    public async Task SetSemesterStatus_ArchiveWithArchivedChild_Succeeds()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 13, status: "CLOSED");
        repo.AddDraftPeriod(semesterId: 13, id: 102, status: "ARCHIVED");
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetSemesterStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        var result = await handler.Handle(
            new SetSemesterStatusCommand(13, "ARCHIVED"), CancellationToken.None);
        Assert.Equal("ARCHIVED", result.Status);
    }
}

// ── ProjectPeriod Handler Tests ────────────────────────────────────────────────

public sealed class ProjectPeriodHandlerTests
{
    private static DateTime StartAt => new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime EndAt => new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── CreateProjectPeriod ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateProjectPeriod_Admin_ValidRequest_CreatesAndAudits()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1);
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var audit = new RecordingAuditTrail();
        var handler = new CreateProjectPeriodCommandHandler(
            repo, access, audit, TimeProvider.System);

        var result = await handler.Handle(
            new CreateProjectPeriodCommand(
                1, "  reg-01  ", "  Registration  ", "REGISTRATION", StartAt, EndAt),
            CancellationToken.None);

        Assert.Equal("REG-01", result.Code);
        Assert.Equal("Registration", result.Name);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal("REGISTRATION", result.PeriodType);
        Assert.Single(audit.Entries);
        Assert.Equal("PROJECT_PERIOD_CREATED", audit.Entries[0].Action);
    }

    [Fact]
    public async Task CreateProjectPeriod_Student_ThrowsForbidden()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1);
        var currentUser = new TestCurrentUser(4, AppRoles.Student);
        var access = new SemesterAccessService(currentUser);
        var handler = new CreateProjectPeriodCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new CreateProjectPeriodCommand(
                    1, "REG-01", "Registration", "REGISTRATION", StartAt, EndAt),
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateProjectPeriod_SemesterNotFound_ThrowsNotFoundException()
    {
        var repo = new StubSemesterRepository();
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new CreateProjectPeriodCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(
                new CreateProjectPeriodCommand(
                    999, "REG-01", "Registration", "REGISTRATION", StartAt, EndAt),
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateProjectPeriod_ClosedSemester_ThrowsConflict()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 2, status: "CLOSED");
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new CreateProjectPeriodCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new CreateProjectPeriodCommand(
                    2, "REG-01", "Registration", "REGISTRATION", StartAt, EndAt),
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateProjectPeriod_DuplicateCode_ThrowsConflict()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 3);
        repo.PeriodCodeDuplicate = true;
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new CreateProjectPeriodCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new CreateProjectPeriodCommand(
                    3, "REG-01", "Registration", "REGISTRATION", StartAt, EndAt),
                CancellationToken.None));
    }

    // ── UpdateProjectPeriod ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProjectPeriod_DraftPeriod_UpdatesFields()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1);
        repo.AddDraftPeriod(semesterId: 1, id: 10);
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        var result = await handler.Handle(
            new UpdateProjectPeriodCommand(
                10, "EXE-01", "Execution Period", "EXECUTION", StartAt, EndAt),
            CancellationToken.None);

        Assert.Equal("EXE-01", result.Code);
        Assert.Equal("Execution Period", result.Name);
        Assert.Equal("EXECUTION", result.PeriodType);
    }

    [Fact]
    public async Task UpdateProjectPeriod_ArchivedPeriod_ThrowsConflict()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1);
        repo.AddDraftPeriod(semesterId: 1, id: 11, status: "ARCHIVED");
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new UpdateProjectPeriodCommand(
                    11, "EXE-01", "Execution Period", "EXECUTION", StartAt, EndAt),
                CancellationToken.None));
    }

    // ── SetProjectPeriodStatus ────────────────────────────────────────────────

    [Theory]
    [InlineData("DRAFT", "UPCOMING")]
    [InlineData("UPCOMING", "ACTIVE")]
    [InlineData("ACTIVE", "CLOSED")]
    [InlineData("CLOSED", "ARCHIVED")]
    public async Task SetProjectPeriodStatus_ValidTransition_Succeeds(
        string from, string to)
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1);
        repo.AddDraftPeriod(semesterId: 1, id: 20, status: from);
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetProjectPeriodStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        var result = await handler.Handle(
            new SetProjectPeriodStatusCommand(20, to), CancellationToken.None);

        Assert.Equal(to, result.Status);
    }

    [Theory]
    [InlineData("ACTIVE", "UPCOMING")]
    [InlineData("ARCHIVED", "ACTIVE")]
    public async Task SetProjectPeriodStatus_InvalidTransition_ThrowsConflict(
        string from, string to)
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1);
        repo.AddDraftPeriod(semesterId: 1, id: 21, status: from);
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetProjectPeriodStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new SetProjectPeriodStatusCommand(21, to), CancellationToken.None));
    }

    [Fact]
    public async Task SetProjectPeriodStatus_ClosedPeriodToArchived_WhenSemesterClosed_Succeeds()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1, status: "CLOSED");
        repo.AddDraftPeriod(semesterId: 1, id: 20, status: "CLOSED");
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetProjectPeriodStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        var result = await handler.Handle(
            new SetProjectPeriodStatusCommand(20, "ARCHIVED"), CancellationToken.None);

        Assert.Equal("ARCHIVED", result.Status);
    }

    [Fact]
    public async Task SetProjectPeriodStatus_AnyTransition_WhenSemesterArchived_ThrowsConflict()
    {
        var repo = new StubSemesterRepository();
        repo.AddDraftSemester(id: 1, status: "ARCHIVED");
        repo.AddDraftPeriod(semesterId: 1, id: 20, status: "CLOSED");
        var currentUser = new TestCurrentUser(1, AppRoles.Admin);
        var access = new SemesterAccessService(currentUser);
        var handler = new SetProjectPeriodStatusCommandHandler(
            repo, access, new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new SetProjectPeriodStatusCommand(20, "ARCHIVED"), CancellationToken.None));
    }
}

// ── Validator Tests ────────────────────────────────────────────────────────────

public sealed class SemesterValidatorTests
{
    [Fact]
    public void CreateSemester_InvalidInputs_ReturnsErrors()
    {
        var validator = new CreateSemesterCommandValidator();

        var result = validator.Validate(
            new CreateSemesterCommand(
                0, "invalid code!", string.Empty,
                new DateOnly(2027, 6, 1), new DateOnly(2027, 1, 1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "OrganizationId");
        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
        Assert.Contains(result.Errors, e => e.PropertyName == "EndDate");
    }

    [Fact]
    public void CreateProjectPeriod_InvalidPeriodType_ReturnsError()
    {
        var validator = new CreateProjectPeriodCommandValidator();

        var now = DateTime.UtcNow;
        var result = validator.Validate(
            new CreateProjectPeriodCommand(
                1, "REG-01", "Registration",
                "INVALID_TYPE", now, now.AddDays(30)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "PeriodType");
    }

    [Fact]
    public void CreateProjectPeriod_EndBeforeStart_ReturnsError()
    {
        var validator = new CreateProjectPeriodCommandValidator();
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = validator.Validate(
            new CreateProjectPeriodCommand(1, "REG-01", "Reg", "REGISTRATION", start, end));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "EndAt");
    }

    [Fact]
    public void GetSemesters_InvalidStatus_ReturnsError()
    {
        var validator = new GetSemestersQueryValidator();

        var result = validator.Validate(
            new GetSemestersQuery(null, null, "INVALID", 1, 20));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Status");
    }

    [Fact]
    public void SetSemesterStatus_InvalidStatus_ReturnsError()
    {
        var validator = new SetSemesterStatusCommandValidator();

        var result = validator.Validate(new SetSemesterStatusCommand(1, "OPEN"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Status");
    }
}
