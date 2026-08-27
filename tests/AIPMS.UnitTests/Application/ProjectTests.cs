using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Models;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Queries;
using MediatR;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateProjectDraft_ValidRequest_CreatesDraftAndRecordsAudit()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.UserActiveTeamId = 1;
        repository.IsLeader = true;
        repository.HasActiveProject = false;
        repository.MajorsExist = true;

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();
        var handler = new CreateProjectDraftCommandHandler(repository, currentUser, auditTrail);

        var command = new CreateProjectDraftCommand(
            "AI-PMS Proposal",
            "Capstone management app",
            "Solve management issues",
            "No central tracking tool",
            "Web application",
            [301, 302],
            "Software Engineering",
            ["React", ".NET 8"],
            ["AI", "Management"]
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal("AI-PMS Proposal", result.Title);
        Assert.Equal(10, result.CreatedBy);
        Assert.Single(auditTrail.Entries);
        Assert.Equal("PROJECT_DRAFT_CREATED", auditTrail.Entries[0].Action);
    }

    [Fact]
    public async Task CreateProjectDraft_TeamHasUnfinishedProject_ThrowsConflict()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.UserActiveTeamId = 1;
        repository.IsLeader = true;
        repository.HasActiveProject = true; // Blocked

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var handler = new CreateProjectDraftCommandHandler(repository, currentUser, new RecordingAuditTrail());

        var command = new CreateProjectDraftCommand("Title", null, null, null, null, [], "Domain", [], []);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CreateProjectDraft_UserNotLeader_ThrowsForbidden()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.UserActiveTeamId = 1;
        repository.IsLeader = false; // Not a leader

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var handler = new CreateProjectDraftCommandHandler(repository, currentUser, new RecordingAuditTrail());

        var command = new CreateProjectDraftCommand("Title", null, null, null, null, [], "Domain", [], []);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SubmitProject_ValidRequest_TransitionsToSubmitted()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        repository.IsTeamEligible = true;
        repository.IsRegistrationOpen = true;

        var initialProject = new ProjectDto(
            Id: 50,
            TeamId: 1,
            TeamName: "Team 1",
            Code: "PRJ001",
            Title: "Title",
            Description: "Desc",
            Objectives: "Objs",
            Status: "DRAFT",
            RegisteredAt: FixedNow,
            SubmittedAt: null,
            ApprovedAt: null,
            CompletedAt: null,
            CreatedBy: 10,
            CreatedByName: "Student Leader",
            CreatedAt: FixedNow,
            UpdatedAt: FixedNow,
            ProblemStatement: "Problem",
            ExpectedOutput: "Output",
            ConcurrencyToken: "token123",
            Majors: [new ProjectMajorDto(1, 301, "SE", "Software Engineering")],
            Tags: [
                new ProjectTagDto(1, "Software Engineering", "DOMAIN"),
                new ProjectTagDto(2, "React", "TECHNOLOGY"),
                new ProjectTagDto(3, "AI", "KEYWORD")
            ]
        );
        repository.Projects[50] = initialProject;

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();
        var timeProvider = new FakeTimeProvider(FixedNow);
        var handler = new SubmitProjectCommandHandler(repository, currentUser, auditTrail, timeProvider);

        var command = new SubmitProjectCommand(50, "token123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("SUBMITTED", result.Status);
        Assert.NotNull(result.SubmittedAt);
        Assert.Single(repository.StatusHistories[50]);
        Assert.Equal("DRAFT", repository.StatusHistories[50][0].OldStatus);
        Assert.Equal("SUBMITTED", repository.StatusHistories[50][0].NewStatus);
    }

    [Fact]
    public async Task SubmitProject_MissingRequiredFields_ThrowsConflict()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        repository.IsTeamEligible = true;
        repository.IsRegistrationOpen = true;

        var initialProject = new ProjectDto(
            Id: 50,
            TeamId: 1,
            TeamName: "Team 1",
            Code: "PRJ001",
            Title: "Title",
            Description: "Desc",
            Objectives: "Objs",
            Status: "DRAFT",
            RegisteredAt: FixedNow,
            SubmittedAt: null,
            ApprovedAt: null,
            CompletedAt: null,
            CreatedBy: 10,
            CreatedByName: "Student Leader",
            CreatedAt: FixedNow,
            UpdatedAt: FixedNow,
            ProblemStatement: null, // Missing Problem Statement!
            ExpectedOutput: "Output",
            ConcurrencyToken: "token123",
            Majors: [new ProjectMajorDto(1, 301, "SE", "Software Engineering")],
            Tags: [
                new ProjectTagDto(1, "Software Engineering", "DOMAIN"),
                new ProjectTagDto(2, "React", "TECHNOLOGY"),
                new ProjectTagDto(3, "AI", "KEYWORD")
            ]
        );
        repository.Projects[50] = initialProject;

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var handler = new SubmitProjectCommandHandler(repository, currentUser, new RecordingAuditTrail(), new FakeTimeProvider(FixedNow));

        var command = new SubmitProjectCommand(50, "token123");

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task RequestRevision_NoReason_ThrowsConflict()
    {
        // Arrange
        var repository = new StubProjectRepository();
        var currentUser = new TestCurrentUser(11, AppRoles.DepartmentStaff);
        var handler = new RequestProjectRevisionCommandHandler(repository, new StubAcademicStructureRepository(), currentUser, new RecordingAuditTrail());

        var command = new RequestProjectRevisionCommand(50, "token123", "   ");

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task RequestRevision_StaffOutsideScope_ThrowsForbidden()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.Projects[50] = new ProjectDto(50, 1, "Team 1", "PRJ001", "Title", null, null, "UNDER_REVIEW", FixedNow, null, null, null, 10, "L", FixedNow, FixedNow, "P", "O", "token123", [], []);
        repository.ProjectDeptIds.Add(200); // Project belongs to department 200

        var academicRepo = new StubAcademicStructureRepository();
        academicRepo.UserScopes[11] = new AcademicUserScope(1, 100); // Staff belongs to department 100 (Out of scope!)

        var currentUser = new TestCurrentUser(11, AppRoles.DepartmentStaff);
        var handler = new RequestProjectRevisionCommandHandler(repository, academicRepo, currentUser, new RecordingAuditTrail());

        var command = new RequestProjectRevisionCommand(50, "token123", "Need more details.");

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task ApproveProject_ConcurrencyConflict_ThrowsConflict()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.Projects[50] = new ProjectDto(50, 1, "Team 1", "PRJ001", "Title", null, null, "UNDER_REVIEW", FixedNow, null, null, null, 10, "L", FixedNow, FixedNow, "P", "O", "token123", [], []);
        repository.ProjectDeptIds.Add(100);

        var academicRepo = new StubAcademicStructureRepository();
        academicRepo.UserScopes[11] = new AcademicUserScope(1, 100);

        var currentUser = new TestCurrentUser(11, AppRoles.DepartmentStaff);
        var handler = new ApproveProjectCommandHandler(repository, academicRepo, currentUser, new RecordingAuditTrail());

        // Call command with incorrect token "stale_token" instead of "token123"
        var command = new ApproveProjectCommand(50, "stale_token");

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }
}

internal sealed class StubProjectRepository : IProjectRepository
{
    private long _nextProjectId = 100;
    private long _nextHistoryId = 200;

    public Dictionary<long, ProjectDto> Projects { get; } = new();
    public Dictionary<long, List<ProjectStatusHistoryDto>> StatusHistories { get; } = new();
    
    public long? UserActiveTeamId { get; set; }
    public bool IsLeader { get; set; }
    public bool HasActiveProject { get; set; }
    public bool IsTeamEligible { get; set; } = true;
    public bool IsRegistrationOpen { get; set; } = true;
    public bool MajorsExist { get; set; } = true;
    public List<long> ProjectDeptIds { get; } = new();
    public bool CanView { get; set; } = true;

    public Task<ProjectDto?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(Projects.GetValueOrDefault(id));

    public Task<PagedResult<ProjectSummaryDto>> GetProjectsAsync(
        string? status,
        long? teamId,
        long? semesterId,
        long? majorId,
        string? tag,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var list = Projects.Values
            .Select(p => new ProjectSummaryDto(p.Id, p.TeamId, p.TeamName, p.Code, p.Title, p.Status, p.CreatedAt, p.SubmittedAt, p.Majors, p.Tags))
            .ToArray();
        return Task.FromResult(new PagedResult<ProjectSummaryDto>(list, page, pageSize, list.Length));
    }

    public Task<PagedResult<ProjectSummaryDto>> GetReviewQueueAsync(
        long? departmentId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var list = Projects.Values
            .Select(p => new ProjectSummaryDto(p.Id, p.TeamId, p.TeamName, p.Code, p.Title, p.Status, p.CreatedAt, p.SubmittedAt, p.Majors, p.Tags))
            .ToArray();
        return Task.FromResult(new PagedResult<ProjectSummaryDto>(list, page, pageSize, list.Length));
    }

    public Task<bool> HasActiveProjectAsync(long teamId, CancellationToken cancellationToken) =>
        Task.FromResult(HasActiveProject);

    public Task<long?> GetActiveRegistrationSemesterIdAsync(long userId, DateTime currentUtc, CancellationToken cancellationToken) =>
        Task.FromResult(IsRegistrationOpen ? (long?)1 : null);

    public Task<long?> GetUserActiveTeamIdAsync(long userId, long semesterId, CancellationToken cancellationToken) =>
        Task.FromResult(UserActiveTeamId);

    public Task<bool> IsTeamLeaderAsync(long teamId, long userId, CancellationToken cancellationToken) =>
        Task.FromResult(IsLeader);

    public Task<ProjectDto> CreateDraftAsync(
        long teamId,
        long userId,
        string title,
        string? description,
        string? objectives,
        string? problemStatement,
        string? expectedOutput,
        IReadOnlyList<long> majorIds,
        string domain,
        IReadOnlyList<string> technologies,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        var id = _nextProjectId++;
        var project = new ProjectDto(
            id,
            teamId,
            "Team " + teamId,
            "PRJ" + id,
            title,
            description,
            objectives,
            "DRAFT",
            DateTime.UtcNow,
            null,
            null,
            null,
            userId,
            "User " + userId,
            DateTime.UtcNow,
            DateTime.UtcNow,
            problemStatement,
            expectedOutput,
            Convert.ToBase64String(BitConverter.GetBytes((long)id)),
            majorIds.Select(m => new ProjectMajorDto(m, m, "M" + m, "Major " + m)).ToArray(),
            new List<ProjectTagDto> { new(1, domain, "DOMAIN") }
                .Concat(technologies.Select(t => new ProjectTagDto(2, t, "TECHNOLOGY")))
                .Concat(keywords.Select(k => new ProjectTagDto(3, k, "KEYWORD")))
                .ToArray()
        );
        Projects[id] = project;
        return Task.FromResult(project);
    }

    public Task<ProjectDto> UpdateDraftAsync(
        long projectId,
        string concurrencyToken,
        string title,
        string? description,
        string? objectives,
        string? problemStatement,
        string? expectedOutput,
        IReadOnlyList<long> majorIds,
        string domain,
        IReadOnlyList<string> technologies,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        var existing = Projects[projectId];
        if (existing.ConcurrencyToken != concurrencyToken)
        {
            throw new ConflictException("Concurrency token mismatch.");
        }
        var updated = existing with
        {
            Title = title,
            Description = description,
            Objectives = objectives,
            ProblemStatement = problemStatement,
            ExpectedOutput = expectedOutput,
            ConcurrencyToken = Convert.ToBase64String(BitConverter.GetBytes((long)(projectId + 1))),
            Majors = majorIds.Select(m => new ProjectMajorDto(m, m, "M" + m, "Major " + m)).ToArray(),
            Tags = new List<ProjectTagDto> { new(1, domain, "DOMAIN") }
                .Concat(technologies.Select(t => new ProjectTagDto(2, t, "TECHNOLOGY")))
                .Concat(keywords.Select(k => new ProjectTagDto(3, k, "KEYWORD")))
                .ToArray()
        };
        Projects[projectId] = updated;
        return Task.FromResult(updated);
    }

    public Task<ProjectDto> UpdateStatusAsync(
        long projectId,
        string concurrencyToken,
        string oldStatus,
        string newStatus,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var existing = Projects[projectId];
        if (existing.ConcurrencyToken != concurrencyToken)
        {
            throw new ConflictException("Concurrency token mismatch.");
        }

        var updated = existing with
        {
            Status = newStatus,
            SubmittedAt = newStatus == "SUBMITTED" ? DateTime.UtcNow : existing.SubmittedAt,
            ApprovedAt = newStatus == "APPROVED" ? DateTime.UtcNow : existing.ApprovedAt,
            ConcurrencyToken = Convert.ToBase64String(BitConverter.GetBytes((long)(projectId + 2)))
        };
        Projects[projectId] = updated;

        if (!StatusHistories.ContainsKey(projectId))
        {
            StatusHistories[projectId] = [];
        }
        StatusHistories[projectId].Add(new ProjectStatusHistoryDto(
            _nextHistoryId++,
            projectId,
            oldStatus,
            newStatus,
            actorUserId,
            "Actor " + actorUserId,
            reason,
            DateTime.UtcNow
        ));

        return Task.FromResult(updated);
    }

    public Task<IReadOnlyList<ProjectStatusHistoryDto>> GetStatusHistoryAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProjectStatusHistoryDto> list = StatusHistories.GetValueOrDefault(projectId) ?? [];
        return Task.FromResult(list);
    }

    public Task<bool> IsSemesterRegistrationOpenAsync(
        long semesterId,
        DateTime currentUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(IsRegistrationOpen);

    public Task<long?> GetSemesterIdByTeamIdAsync(
        long teamId,
        CancellationToken cancellationToken) =>
        Task.FromResult((long?)1);

    public Task<bool> ValidateMajorsExistAsync(
        IEnumerable<long> majorIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(MajorsExist);

    public Task<bool> IsTeamEligibleAsync(
        long teamId,
        CancellationToken cancellationToken) =>
        Task.FromResult(IsTeamEligible);

    public Task<bool> ProjectBelongsToTeamAsync(
        long projectId,
        long teamId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Projects.ContainsKey(projectId) && Projects[projectId].TeamId == teamId);

    public Task<IReadOnlyList<long>> GetProjectMajorDepartmentIdsAsync(
        long projectId,
        CancellationToken cancellationToken) =>
        Task.FromResult((IReadOnlyList<long>)ProjectDeptIds);

    public Task<bool> CanUserViewProjectAsync(
        long projectId,
        long userId,
        bool isAdmin,
        long? staffScopeDepartmentId,
        CancellationToken cancellationToken) =>
        Task.FromResult(CanView);
}

internal sealed class FakeTimeProvider(DateTime fixedNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(fixedNow, TimeSpan.Zero);
}
