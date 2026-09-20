using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Configuration;
using AIPMS.AI.Providers;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Queries;
using AIPMS.Application.Features.AiAssistant.Services;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.Tasks.DTOs;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class AiAssistantProjectQueryTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
    private readonly TimeProvider _timeProvider = new FakeTimeProvider(FixedNow);

    [Fact]
    public async Task ProjectAssistant_WhenValidOwnProjectQuery_ReturnsGroundedAnswer()
    {
        var taskRepo = new StubTaskRepository();
        taskRepo.Tasks.Add(new TaskDto(
            Id: 10,
            MilestoneId: 1,
            ParentTaskId: null,
            Title: "Setup Azure Pipeline",
            Description: "CI/CD setup",
            Status: "DONE",
            Priority: "HIGH",
            StartAt: FixedNow.AddDays(-5),
            DueAt: FixedNow.AddDays(-1),
            CompletedAt: FixedNow.AddDays(-1),
            CreatedBy: 1,
            CreatedByFullName: "Leader",
            CreatedAt: FixedNow.AddDays(-5),
            UpdatedAt: FixedNow.AddDays(-1),
            Assignees: Array.Empty<TaskAssigneeDto>(),
            Dependencies: Array.Empty<TaskDependencyDto>()));

        var milestoneRepo = new StubMilestoneRepository();
        milestoneRepo.Milestones.Add(new MilestoneDto(
            Id: 1,
            ProjectId: 101,
            Title: "Infrastructure Setup",
            Description: "Setup base infra",
            StartDate: new DateOnly(2026, 9, 1),
            DueDate: new DateOnly(2026, 9, 20),
            Status: "IN_PROGRESS",
            SortOrder: 1,
            CreatedBy: 1,
            CreatedByFullName: "Leader",
            CreatedAt: FixedNow,
            UpdatedAt: FixedNow));

        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(101, "What is the progress on the project?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(101, response.ProjectId);
        Assert.False(response.InsufficientEvidence);
        Assert.NotEmpty(response.Evidence);
        Assert.Contains(response.Evidence, e => e.SourceId == "MS-1");
        Assert.Contains(response.Evidence, e => e.SourceId == "TASK-10");
        Assert.Contains("Infrastructure Setup", response.Answer);
    }

    [Fact]
    public async Task ProjectAssistant_WhenOutsiderUser_ThrowsForbiddenException()
    {
        var taskRepo = new StubTaskRepository();
        var milestoneRepo = new StubMilestoneRepository();
        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: false, actorId: 999);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.AskAsync(101, "Tell me about tasks", CancellationToken.None));
    }

    [Fact]
    public async Task ProjectAssistant_WhenCrossProjectAccessAttempted_ThrowsForbiddenException()
    {
        // User is member of Project 101, but queries Project 202
        var taskRepo = new StubTaskRepository();
        var milestoneRepo = new StubMilestoneRepository();
        var dataReader = new StubProjectProgressDataReader { ProjectExists = true };
        var accessService = new StubCrossProjectAccessService(allowedProjectId: 101);
        var currentUser = new TestCurrentUser(10, "STUDENT");
        var reportRepo = new StubProgressReportRepository();
        var meetingRepo = new StubMeetingRepository();
        var contribRepo = new StubContributionRepository();

        var contextRetriever = new AiContextRetriever(
            accessService, dataReader, reportRepo, milestoneRepo, taskRepo, meetingRepo, contribRepo, currentUser);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.AskAsync(202, "Show project info", CancellationToken.None));
    }

    [Fact]
    public async Task ProjectAssistant_WhenProjectNotFound_ThrowsNotFoundException()
    {
        var taskRepo = new StubTaskRepository();
        var milestoneRepo = new StubMilestoneRepository();
        var dataReader = new StubProjectProgressDataReader { ProjectExists = false };
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new TestCurrentUser(10, "STUDENT");
        var reportRepo = new StubProgressReportRepository();
        var meetingRepo = new StubMeetingRepository();
        var contribRepo = new StubContributionRepository();

        var contextRetriever = new AiContextRetriever(
            accessService, dataReader, reportRepo, milestoneRepo, taskRepo, meetingRepo, contribRepo, currentUser);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AskAsync(999, "What is status?", CancellationToken.None));
    }

    [Fact]
    public async Task ProjectAssistant_WhenUnauthenticated_ThrowsUnauthorizedException()
    {
        var taskRepo = new StubTaskRepository();
        var milestoneRepo = new StubMilestoneRepository();
        var dataReader = new StubProjectProgressDataReader { ProjectExists = true };
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new UnauthenticatedTestCurrentUser();
        var reportRepo = new StubProgressReportRepository();
        var meetingRepo = new StubMeetingRepository();
        var contribRepo = new StubContributionRepository();

        var contextRetriever = new AiContextRetriever(
            accessService, dataReader, reportRepo, milestoneRepo, taskRepo, meetingRepo, contribRepo, currentUser);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.AskAsync(101, "What is status?", CancellationToken.None));
    }

    [Fact]
    public async Task ProjectAssistant_WhenContextHasNoEvidence_ReturnsInsufficientEvidenceSignal()
    {
        var taskRepo = new StubTaskRepository(); // empty
        var milestoneRepo = new StubMilestoneRepository(); // empty
        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(101, "What is the blocker?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.InsufficientEvidence);
        Assert.Contains("Insufficient project evidence", response.Answer);
        Assert.NotNull(response.LimitationNote);
    }

    [Fact]
    public async Task ProjectAssistant_WhenProviderThrowsException_ReturnsDeterministicFallback()
    {
        var taskRepo = new StubTaskRepository();
        taskRepo.Tasks.Add(new TaskDto(1, 1, null, "Task 1", "Desc", "TODO", "LOW", null, null, null, 1, "User", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        var milestoneRepo = new StubMilestoneRepository();

        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var failingProvider = new GroundedAiTextGenerationProvider(new AiAssistantOptions { SimulateFailure = true });
        var service = new AiAssistantService(contextRetriever, failingProvider, _timeProvider);

        var response = await service.AskAsync(101, "Overview?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("temporarily unavailable", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deterministic fallback", response.LimitationNote, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(response.Evidence);
    }

    [Fact]
    public async Task ProjectAssistant_WhenProviderTimesOut_ReturnsDeterministicFallback()
    {
        var taskRepo = new StubTaskRepository();
        taskRepo.Tasks.Add(new TaskDto(1, 1, null, "Task 1", "Desc", "TODO", "LOW", null, null, null, 1, "User", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        var milestoneRepo = new StubMilestoneRepository();

        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var timeoutProvider = new GroundedAiTextGenerationProvider(new AiAssistantOptions { SimulateTimeout = true, TimeoutSeconds = 0 });
        var service = new AiAssistantService(contextRetriever, timeoutProvider, _timeProvider);

        var response = await service.AskAsync(101, "Overview?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("temporarily unavailable", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deterministic fallback", response.LimitationNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectAssistant_PreservesAuthoritativeEvidenceReferences()
    {
        var taskRepo = new StubTaskRepository();
        taskRepo.Tasks.Add(new TaskDto(42, 1, null, "Implement OAuth", "JWT authentication", "DONE", "HIGH", null, FixedNow, FixedNow, 1, "AuthLead", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        var milestoneRepo = new StubMilestoneRepository();

        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(101, "Tell me about OAuth task", CancellationToken.None);

        Assert.NotNull(response);
        var evidence = Assert.Single(response.Evidence);
        Assert.Equal("TASK-42", evidence.SourceId);
        Assert.Equal("TASK", evidence.SourceType);
        Assert.Equal("/api/v1/projects/101/tasks/42", evidence.ReferenceUrl);
    }

    private static AiContextRetriever CreateContextRetriever(
        StubTaskRepository taskRepo, StubMilestoneRepository milestoneRepo, bool canAccess, long? actorId)
    {
        var dataReader = new StubProjectProgressDataReader { ProjectExists = true };
        var accessService = new StubProjectAccessService { CanAccess = canAccess };
        var currentUser = actorId.HasValue ? new TestCurrentUser(actorId.Value, "STUDENT") : (ICurrentUser)new UnauthenticatedTestCurrentUser();
        var reportRepo = new StubProgressReportRepository();
        var meetingRepo = new StubMeetingRepository();
        var contribRepo = new StubContributionRepository();

        return new AiContextRetriever(
            accessService, dataReader, reportRepo, milestoneRepo, taskRepo, meetingRepo, contribRepo, currentUser);
    }
}
