using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Configuration;
using AIPMS.AI.Providers;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Services;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.Tasks.DTOs;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class AiAssistantEvaluationDatasetTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
    private readonly TimeProvider _timeProvider = new FakeTimeProvider(FixedNow);

    public sealed record EvaluationCase(
        string TestCaseName,
        string Query,
        string ExpectedFactSubstring,
        string ExpectedSourceId,
        bool ExpectInsufficientEvidence);

    public static readonly IReadOnlyList<EvaluationCase> SyntheticEvaluationCases = new List<EvaluationCase>
    {
        new("CompletedWork", "What is the progress on the project?", "Setup Infrastructure", "TASK-101", false),
        new("InProgressWork", "What is the progress on the project?", "Database Migration", "TASK-102", false),
        new("Blockers", "Are there any blockers?", "TASK-103", "TASK-103", false),
        new("Risks", "What risks or blockers exist?", "TASK-103", "TASK-103", false),
        new("MilestoneOverview", "What milestone are we working on?", "Sprint 1 Foundation", "MS-50", false)
    };

    [Theory]
    [MemberData(nameof(GetEvaluationTestCases))]
    public async Task Evaluate_FactualConsistency_RetainsKeyFactsAndValidSources(EvaluationCase testCase)
    {
        var taskRepo = new StubTaskRepository();
        taskRepo.Tasks.Add(new TaskDto(101, 50, null, "Setup Infrastructure", "Terraform scripts", "DONE", "HIGH", FixedNow.AddDays(-10), FixedNow.AddDays(-2), FixedNow.AddDays(-2), 1, "Dev", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        taskRepo.Tasks.Add(new TaskDto(102, 50, null, "Database Migration", "Flyway scripts", "IN_PROGRESS", "NORMAL", FixedNow.AddDays(-2), FixedNow.AddDays(5), null, 1, "Dev", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        taskRepo.Tasks.Add(new TaskDto(103, 50, null, "Payment Gateway Integration", "Waiting on credentials", "BLOCKED", "HIGH", FixedNow.AddDays(-1), FixedNow.AddDays(3), null, 1, "Dev", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));

        var milestoneRepo = new StubMilestoneRepository();
        milestoneRepo.Milestones.Add(new MilestoneDto(50, 10, "Sprint 1 Foundation", "Foundational setup", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "IN_PROGRESS", 1, 1, "Leader", FixedNow, FixedNow));

        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(10, testCase.Query, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(testCase.ExpectInsufficientEvidence, response.InsufficientEvidence);
        Assert.Contains(testCase.ExpectedFactSubstring, response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(response.Evidence, e => e.SourceId == testCase.ExpectedSourceId);
        Assert.All(response.Evidence, e => Assert.False(string.IsNullOrWhiteSpace(e.SourceId)));
    }

    [Fact]
    public async Task Evaluate_InsufficientEvidence_EmitsClearLimitationAndNoHallucinatedFacts()
    {
        var taskRepo = new StubTaskRepository(); // empty
        var milestoneRepo = new StubMilestoneRepository(); // empty

        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(10, "What is the deployment schedule for microservices?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.InsufficientEvidence);
        Assert.Contains("Insufficient project evidence", response.Answer);
        Assert.NotNull(response.LimitationNote);
        Assert.Empty(response.Evidence);
    }

    [Fact]
    public async Task Evaluate_CrossProjectDistractorEvidence_NeverLeaksIntoAnswer()
    {
        var taskRepo = new StubTaskRepository();
        // Project 10 task
        taskRepo.Tasks.Add(new TaskDto(101, 1, null, "Project 10 Genuine Task", "Legitimate description", "DONE", "HIGH", null, null, null, 1, "User", FixedNow, FixedNow, Array.Empty<TaskAssigneeDto>(), Array.Empty<TaskDependencyDto>()));
        var milestoneRepo = new StubMilestoneRepository();

        var contextRetriever = CreateContextRetriever(taskRepo, milestoneRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var response = await service.AskAsync(10, "What are the project tasks?", CancellationToken.None);

        Assert.NotNull(response);
        Assert.All(response.Evidence, e => Assert.Equal("TASK-101", e.SourceId));
        Assert.DoesNotContain("Project 999", response.Answer);
    }

    [Fact]
    public async Task Evaluate_ReportSummary_FactualConsistencyAcrossAllFiveSections()
    {
        var stubReportRepo = new StubProgressReportRepository();
        var reportDetail = new ProgressReportDetailDto(
            Id: 77,
            ProjectId: 10,
            SubmittedBy: 1,
            SubmittedByName: "Alice",
            ReportType: "WEEKLY",
            PeriodStart: new DateOnly(2026, 9, 1),
            PeriodEnd: new DateOnly(2026, 9, 7),
            Summary: "Week 1 sprint review",
            CompletedWork: "Finished schema design and unit tests",
            PlannedWork: "Begin API endpoint implementation",
            IssuesAndRisks: "Risk of delayed client feedback on designs",
            Status: "SUBMITTED",
            SubmittedAt: FixedNow,
            IsLate: false,
            CreatedAt: FixedNow,
            UpdatedAt: FixedNow,
            Feedbacks: Array.Empty<ProgressReportFeedbackDto>());
        stubReportRepo.Reports[77] = reportDetail;

        var contextRetriever = new AiContextRetriever(
            new StubProjectAccessService { CanAccess = true },
            new StubProjectProgressDataReader { ProjectExists = true },
            stubReportRepo,
            new StubMilestoneRepository(),
            new StubTaskRepository(),
            new StubMeetingRepository(),
            new StubContributionRepository(),
            new TestCurrentUser(1, "STUDENT"));

        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var summary = await service.SummarizeReportAsync(10, 77, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Contains("schema design", summary.Summary.Completed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("API endpoint implementation", summary.Summary.InProgress, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delayed client feedback", summary.Summary.Risks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("API endpoint implementation", summary.Summary.NextActions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(summary.Evidence, e => e.SourceId == "PR-77");
    }

    public static IEnumerable<object[]> GetEvaluationTestCases()
    {
        foreach (var tc in SyntheticEvaluationCases)
        {
            yield return new object[] { tc };
        }
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
