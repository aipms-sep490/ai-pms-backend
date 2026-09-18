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
using AIPMS.Application.Features.Topics.Abstractions;
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
        var handler = new CreateProjectDraftCommandHandler(repository, currentUser, auditTrail,
            new StubRegistrationGuard(repository), new FakeTimeProvider(FixedNow));

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
        var handler = new CreateProjectDraftCommandHandler(repository, currentUser, new RecordingAuditTrail(),
            new StubRegistrationGuard(repository), new FakeTimeProvider(FixedNow));

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
        var handler = new CreateProjectDraftCommandHandler(repository, currentUser, new RecordingAuditTrail(),
            new StubRegistrationGuard(repository), new FakeTimeProvider(FixedNow));

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
        var handler = new SubmitProjectCommandHandler(repository, currentUser, auditTrail, timeProvider,
            new StubRegistrationGuard(repository));

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
        var handler = new SubmitProjectCommandHandler(repository, currentUser, new RecordingAuditTrail(), new FakeTimeProvider(FixedNow),
            new StubRegistrationGuard(repository));

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
        var handler = new RequestProjectRevisionCommandHandler(repository, new StubAcademicStructureRepository(), currentUser, new RecordingAuditTrail(), new RecordingPublisher());

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
        var handler = new RequestProjectRevisionCommandHandler(repository, academicRepo, currentUser, new RecordingAuditTrail(), new RecordingPublisher());

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
        var publisher = new RecordingPublisher();
        var handler = new ApproveProjectCommandHandler(repository, academicRepo, currentUser, new RecordingAuditTrail(), publisher);

        // Call command with incorrect token "stale_token" instead of "token123"
        var command = new ApproveProjectCommand(50, "stale_token");

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Empty(publisher.Events);
    }

    [Fact]
    public async Task UpdateDraft_WhileEditable_Succeeds()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        var initialProject = new ProjectDto(50, 1, "Team 1", "PRJ001", "Initial Title", "Desc", "Obj", "DRAFT",
            FixedNow, null, null, null, 10, "Leader", FixedNow, FixedNow, "Prob", "Out", "token123", [], []);
        repository.Projects[50] = initialProject;

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();
        var handler = new UpdateProjectDraftCommandHandler(repository, currentUser, auditTrail);

        var command = new UpdateProjectDraftCommand(50, "token123", "Updated Title", "New Desc", "New Obj", "New Prob", "New Out", [], "Domain", [], []);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated Title", result.Title);
        Assert.Single(auditTrail.Entries);
        Assert.Equal("PROJECT_DRAFT_UPDATED", auditTrail.Entries[0].Action);
    }

    [Fact]
    public async Task UpdateDraft_AfterSubmit_Returns409()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        var submittedProject = new ProjectDto(50, 1, "Team 1", "PRJ001", "Title", "Desc", "Obj", "SUBMITTED",
            FixedNow, FixedNow, null, null, 10, "Leader", FixedNow, FixedNow, "Prob", "Out", "token123", [], []);
        repository.Projects[50] = submittedProject;

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var handler = new UpdateProjectDraftCommandHandler(repository, currentUser, new RecordingAuditTrail());

        var command = new UpdateProjectDraftCommand(50, "token123", "Attempted Update", null, null, null, null, [], "Domain", [], []);

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UnauthorizedWriter_Returns403()
    {
        // Arrange: User is not leader (e.g. standard member or outside user)
        var repository = new StubProjectRepository();
        repository.IsLeader = false;
        var project = new ProjectDto(50, 1, "Team 1", "PRJ001", "Title", "Desc", "Obj", "DRAFT",
            FixedNow, null, null, null, 10, "Leader", FixedNow, FixedNow, "Prob", "Out", "token123", [], []);
        repository.Projects[50] = project;

        var currentUser = new TestCurrentUser(99, AppRoles.Student); // Member, not leader
        var handler = new UpdateProjectDraftCommandHandler(repository, currentUser, new RecordingAuditTrail());

        var command = new UpdateProjectDraftCommand(50, "token123", "Unauthorized Edit", null, null, null, null, [], "Domain", [], []);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CrossProjectAccess_Denied()
    {
        // Arrange: Project belongs to Team 1, actor is leader of Team 2
        var repository = new StubProjectRepository();
        repository.IsLeader = false; // IsTeamLeaderAsync(teamId=1, actorUserId=20) is false
        var project = new ProjectDto(50, 1, "Team 1", "PRJ001", "Title", "Desc", "Obj", "DRAFT",
            FixedNow, null, null, null, 10, "Leader 1", FixedNow, FixedNow, "Prob", "Out", "token123", [], []);
        repository.Projects[50] = project;

        var otherTeamLeader = new TestCurrentUser(20, AppRoles.Student);
        var handler = new UpdateProjectDraftCommandHandler(repository, otherTeamLeader, new RecordingAuditTrail());

        var command = new UpdateProjectDraftCommand(50, "token123", "Cross Team Edit", null, null, null, null, [], "Domain", [], []);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Submit_WhenEligible_Succeeds()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        repository.IsTeamEligible = true;
        repository.IsRegistrationOpen = true;

        var project = new ProjectDto(50, 1, "Team 1", "PRJ001", "Title", "Desc", "Objs", "DRAFT",
            FixedNow, null, null, null, 10, "Leader", FixedNow, FixedNow, "Problem", "Output", "token123",
            [new ProjectMajorDto(1, 301, "SE", "Software Engineering")],
            [
                new ProjectTagDto(1, "Software Engineering", "DOMAIN"),
                new ProjectTagDto(2, "React", "TECHNOLOGY"),
                new ProjectTagDto(3, "AI", "KEYWORD")
            ]);
        repository.Projects[50] = project;

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var auditTrail = new RecordingAuditTrail();
        var handler = new SubmitProjectCommandHandler(repository, currentUser, auditTrail, new FakeTimeProvider(FixedNow),
            new StubRegistrationGuard(repository));

        var command = new SubmitProjectCommand(50, "token123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("SUBMITTED", result.Status);
        Assert.NotNull(result.SubmittedAt);
    }

    [Fact]
    public async Task Submit_WhenIneligible_RejectedWithReasons()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        repository.IsTeamEligible = false; // Team is ineligible
        repository.IsRegistrationOpen = true;

        var project = new ProjectDto(50, 1, "Team 1", "PRJ001", "Title", "Desc", "Objs", "DRAFT",
            FixedNow, null, null, null, 10, "Leader", FixedNow, FixedNow, "Problem", "Output", "token123",
            [new ProjectMajorDto(1, 301, "SE", "Software Engineering")],
            [
                new ProjectTagDto(1, "Software Engineering", "DOMAIN"),
                new ProjectTagDto(2, "React", "TECHNOLOGY"),
                new ProjectTagDto(3, "AI", "KEYWORD")
            ]);
        repository.Projects[50] = project;

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var handler = new SubmitProjectCommandHandler(repository, currentUser, new RecordingAuditTrail(), new FakeTimeProvider(FixedNow),
            new StubRegistrationGuard(repository));

        var command = new SubmitProjectCommand(50, "token123");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("Team is not eligible", ex.Message);
    }

    [Fact]
    public async Task Submit_WithPublishedTopic_RevalidatesTopic_WhenInvalid_FailsWithoutMutation()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        repository.IsTeamEligible = true;
        repository.IsRegistrationOpen = true;

        var project = new ProjectDto(60, 1, "Team 1", "PRJ060", "Title", "Desc", "Objs", "DRAFT",
            FixedNow, null, null, null, 10, "Leader", FixedNow, FixedNow, "Problem", "Output", "token123",
            [new ProjectMajorDto(1, 301, "SE", "Software Engineering")],
            [
                new ProjectTagDto(1, "Software Engineering", "DOMAIN"),
                new ProjectTagDto(2, "React", "TECHNOLOGY"),
                new ProjectTagDto(3, "AI", "KEYWORD")
            ],
            TopicId: 55,
            ProposalSource: "PUBLISHED_TOPIC");
        repository.Projects[60] = project;

        var guard = new StubTopicGuardForProjectTests
        {
            ValidateCallback = (topicId, projId, userId, ct) => throw new ConflictException("MAJOR_MIN_MEMBERS")
        };

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var handler = new SubmitProjectCommandHandler(repository, currentUser, new RecordingAuditTrail(), new FakeTimeProvider(FixedNow),
            new StubRegistrationGuard(repository), guard);

        var command = new SubmitProjectCommand(60, "token123");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("MAJOR_MIN_MEMBERS", ex.Message);

        // Verify project in repository remained DRAFT, not SUBMITTED (no state mutation)
        Assert.Equal("DRAFT", repository.Projects[60].Status);
        Assert.Null(repository.Projects[60].SubmittedAt);
    }

    [Fact]
    public async Task Submit_WithPublishedTopic_RevalidatesTopic_WhenValid_Succeeds()
    {
        // Arrange
        var repository = new StubProjectRepository();
        repository.IsLeader = true;
        repository.IsTeamEligible = true;
        repository.IsRegistrationOpen = true;

        var project = new ProjectDto(61, 1, "Team 1", "PRJ061", "Title", "Desc", "Objs", "DRAFT",
            FixedNow, null, null, null, 10, "Leader", FixedNow, FixedNow, "Problem", "Output", "token123",
            [new ProjectMajorDto(1, 301, "SE", "Software Engineering")],
            [
                new ProjectTagDto(1, "Software Engineering", "DOMAIN"),
                new ProjectTagDto(2, "React", "TECHNOLOGY"),
                new ProjectTagDto(3, "AI", "KEYWORD")
            ],
            TopicId: 55,
            ProposalSource: "PUBLISHED_TOPIC");
        repository.Projects[61] = project;

        var guard = new StubTopicGuardForProjectTests();

        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var handler = new SubmitProjectCommandHandler(repository, currentUser, new RecordingAuditTrail(), new FakeTimeProvider(FixedNow),
            new StubRegistrationGuard(repository), guard);

        var command = new SubmitProjectCommand(61, "token123");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("SUBMITTED", result.Status);
        Assert.Equal("PUBLISHED_TOPIC", result.ProposalSource);
        Assert.Equal(55, result.TopicId);
        Assert.True(guard.WasValidated);
    }
}

internal sealed class StubTopicGuardForProjectTests : ITopicSelectionGuard
{
    public bool WasValidated { get; private set; }
    public Action<long, long, long, CancellationToken>? ValidateCallback { get; set; }

    public Task ValidateTopicSelectionAsync(long topicId, long projectId, long actorUserId, CancellationToken cancellationToken)
    {
        WasValidated = true;
        ValidateCallback?.Invoke(topicId, projectId, actorUserId, cancellationToken);
        return Task.CompletedTask;
    }
}

internal sealed class StubProjectRepository : IProjectRepository
{
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);
    public Task<ProjectAcademicReviewDto> GetAcademicReviewAsync(long projectId, CancellationToken ct) => throw new NotSupportedException();
    public Task<ProjectAcademicReviewDto> RecordDepartmentDecisionAsync(long projectId, long actorId,
        DepartmentDecisionRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<ProjectSummaryDto>> GetVisibleProjectsAsync(long userId, string? status, long? teamId,
        long? semesterId, long? majorId, string? tag, string? search, int page, int pageSize, CancellationToken ct) =>
        GetProjectsAsync(status, teamId, semesterId, majorId, tag, search, page, pageSize, ct);

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

    public Task<ProjectDto> SelectTopicAsync(
        long projectId,
        long topicId,
        string? concurrencyToken,
        CancellationToken cancellationToken)
    {
        var existing = Projects[projectId];
        if (existing.Status != "DRAFT" && existing.Status != "REVISION_REQUIRED")
        {
            throw new ConflictException("Only an editable proposal can be updated.");
        }
        if (!string.IsNullOrWhiteSpace(concurrencyToken) && existing.ConcurrencyToken != concurrencyToken)
        {
            throw new ConflictException("Concurrency token mismatch.");
        }
        var updated = existing with
        {
            TopicId = topicId,
            ProposalSource = "PUBLISHED_TOPIC",
            SelectedTopic = new SelectedTopicDto(topicId, "TOPIC-" + topicId, "Selected topic " + topicId)
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

    public Task<ProjectProgressSummaryDto> GetProjectProgressSummaryAsync(
        long projectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ProjectProgressSummaryDto(projectId, 0, 0, 0, 0, 0, 0, 0.0));

    public Task<ProjectTimelineDataDto> GetTimelineDataAsync(
        long projectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ProjectTimelineDataDto(projectId, Array.Empty<TimelineMilestoneDto>()));
}

internal sealed class FakeTimeProvider(DateTime fixedNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(fixedNow, TimeSpan.Zero);
}
