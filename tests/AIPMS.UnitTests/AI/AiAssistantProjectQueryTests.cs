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
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Meetings.DTOs;
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
        Assert.Equal("/api/v1/tasks/42", evidence.ReferenceUrl);
    }

    [Fact]
    public async Task ProjectAssistant_EmitsCanonicalRoutes_ForAllEvidenceTypes()
    {
        var taskRepo = new StubTaskRepository();
        taskRepo.Tasks.Add(new TaskDto(10, 1, null, "Task 10", "Desc", "DONE", "HIGH", null, FixedNow, FixedNow, 1, "Dev", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));

        var milestoneRepo = new StubMilestoneRepository();
        milestoneRepo.Milestones.Add(new MilestoneDto(5, 101, "Milestone 5", "Desc", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "IN_PROGRESS", 1, 1, "Leader", FixedNow, FixedNow));

        var reportRepo = new StubProgressReportRepository();
        reportRepo.Reports[15] = new ProgressReportDetailDto(15, 101, 1, "Leader", "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary 15", "Completed", "Planned", "Issues", "SUBMITTED", FixedNow, false, FixedNow, FixedNow, Array.Empty<ProgressReportFeedbackDto>());

        var meetingRepo = new StubMeetingRepository();
        meetingRepo.Meetings.Add(new MeetingDto(20, 101, "Meeting 20", "Agenda", null, FixedNow, FixedNow.AddHours(1), "Room A", null, "COMPLETED", 1, "Leader", 1, FixedNow, FixedNow));

        var contribRepo = new StubContributionRepository
        {
            Summary = new ContributionSummaryDto("ACTIVE", 0.5, new[] { new ContributionMemberDto(1, "Leader", 10, 5, 2, 3, 1, 0.5, 20) }, 1, 20, 0, "v1", null, FixedNow)
        };
        var dataReader = new StubProjectProgressDataReader { ProjectExists = true };
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new TestCurrentUser(10, "STUDENT");

        var contextRetriever = new AiContextRetriever(
            accessService, dataReader, reportRepo, milestoneRepo, taskRepo, meetingRepo, contribRepo, currentUser);

        var context = await contextRetriever.RetrieveProjectContextAsync(101, "overview", CancellationToken.None);

        Assert.NotNull(context);
        var projectEv = Assert.Single(context.EvidenceList, e => e.SourceType == "PROJECT");
        Assert.Equal("/api/v1/projects/101", projectEv.ReferenceUrl);

        var milestoneEv = Assert.Single(context.EvidenceList, e => e.SourceType == "MILESTONE");
        Assert.Equal("/api/v1/milestones/5", milestoneEv.ReferenceUrl);

        var taskEv = Assert.Single(context.EvidenceList, e => e.SourceType == "TASK");
        Assert.Equal("/api/v1/tasks/10", taskEv.ReferenceUrl);

        var reportEv = Assert.Single(context.EvidenceList, e => e.SourceType == "PROGRESS_REPORT");
        Assert.Equal("/api/v1/progress-reports/15", reportEv.ReferenceUrl);

        var meetingEv = Assert.Single(context.EvidenceList, e => e.SourceType == "MEETING");
        Assert.Equal("/api/v1/meetings/20", meetingEv.ReferenceUrl);

        var contribEv = Assert.Single(context.EvidenceList, e => e.SourceType == "CONTRIBUTION");
        Assert.Equal("/api/v1/projects/101/contributions", contribEv.ReferenceUrl);
    }

    [Fact]
    public async Task Assistant_WhenTaskContextTruncated_ReportsLimitation()
    {
        var taskRepo = new StubTaskRepository();
        for (int i = 1; i <= 20; i++)
        {
            taskRepo.Tasks.Add(new TaskDto(i, 1, null, $"Task {i}", "Desc", "DONE", "NORMAL", null, null, null, 1, "Dev", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        }

        var milestoneRepo = new StubMilestoneRepository();
        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(101, "Are there any blockers?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.LimitationNote);
        Assert.Contains("Based on 15 retrieved tasks out of 20 total tasks; non-retrieved tasks may contain additional items.", response.LimitationNote);
        Assert.Contains("No blocked tasks found in the 15 retrieved tasks (out of 20 total). Non-retrieved tasks were not inspected.", response.Answer);
        Assert.DoesNotContain("No blocked tasks were found in the current project records.", response.Answer);
    }

    [Fact]
    public async Task Assistant_BlockerQuery_DoesNotMissBlockedTaskOutsideDefaultFirst15()
    {
        var taskRepo = new StubTaskRepository();
        for (int i = 1; i <= 15; i++)
        {
            taskRepo.Tasks.Add(new TaskDto(i, 1, null, $"Task {i}", "Desc", "DONE", "NORMAL", null, null, null, 1, "Dev", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        }
        taskRepo.Tasks.Add(new TaskDto(16, 1, null, "Critical Blocked Task", "Blocked desc", "BLOCKED", "HIGH", null, null, null, 1, "Dev", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));

        var milestoneRepo = new StubMilestoneRepository();
        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(101, "Are there any blockers?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains(response.Evidence, e => e.SourceId == "TASK-16");
        Assert.Contains("TASK-16", response.Answer);
        Assert.Contains("Critical Blocked Task", response.Answer);
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
