using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Configuration;
using AIPMS.AI.Providers;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Queries;
using AIPMS.Application.Features.AiAssistant.Services;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class AiAssistantReportSummaryTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
    private readonly TimeProvider _timeProvider = new FakeTimeProvider(FixedNow);

    [Fact]
    public async Task ReportSummary_WhenWeeklyReportValid_GeneratesStructuredSections()
    {
        var stubReportRepo = new StubProgressReportRepository();
        var reportDetail = CreateReportDetail(1, 101, "WEEKLY", "Completed user auth, in progress on dashboard, blocked by API key, risk of delay, next action write tests.");
        stubReportRepo.Reports[1] = reportDetail;

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var result = await service.SummarizeReportAsync(101, 1, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(101, result.ProjectId);
        Assert.Equal(1, result.ReportId);
        Assert.Equal("WEEKLY", result.ReportType);
        Assert.NotNull(result.Summary.Completed);
        Assert.NotNull(result.Summary.InProgress);
        Assert.NotNull(result.Summary.Blockers);
        Assert.NotNull(result.Summary.Risks);
        Assert.NotNull(result.Summary.NextActions);
        Assert.NotEmpty(result.Evidence);
        Assert.Contains(result.Evidence, e => e.SourceId == "PR-1");
    }

    [Fact]
    public async Task ReportSummary_WhenMonthlyReportValid_GeneratesStructuredSections()
    {
        var stubReportRepo = new StubProgressReportRepository();
        var reportDetail = CreateReportDetail(2, 101, "MONTHLY", "Month 1 achievements, database schema completed, next month integration.");
        stubReportRepo.Reports[2] = reportDetail;

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var result = await service.SummarizeReportAsync(101, 2, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(101, result.ProjectId);
        Assert.Equal(2, result.ReportId);
        Assert.Equal("MONTHLY", result.ReportType);
        Assert.NotNull(result.Summary.Completed);
        Assert.NotNull(result.Summary.InProgress);
        Assert.NotNull(result.Summary.NextActions);
    }

    [Fact]
    public async Task ReportSummary_WhenSectionsEmpty_ReturnsInsufficientEvidenceNote()
    {
        var stubReportRepo = new StubProgressReportRepository();
        var reportDetail = new ProgressReportDetailDto(
            Id: 3,
            ProjectId: 101,
            SubmittedBy: 10,
            SubmittedByName: "Student 1",
            ReportType: "WEEKLY",
            PeriodStart: new DateOnly(2026, 9, 1),
            PeriodEnd: new DateOnly(2026, 9, 7),
            Summary: "",
            CompletedWork: null,
            PlannedWork: null,
            IssuesAndRisks: null,
            Status: "DRAFT",
            SubmittedAt: null,
            IsLate: false,
            CreatedAt: FixedNow,
            UpdatedAt: FixedNow,
            Feedbacks: Array.Empty<ProgressReportFeedbackDto>());
        stubReportRepo.Reports[3] = reportDetail;

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        var result = await service.SummarizeReportAsync(101, 3, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(!string.IsNullOrWhiteSpace(result.LimitationNote));
    }

    [Fact]
    public async Task ReportSummary_WhenProviderThrowsException_ReturnsDeterministicFallbackFromPersistedFields()
    {
        var stubReportRepo = new StubProgressReportRepository();
        var reportDetail = CreateReportDetail(4, 101, "WEEKLY", "Sprint 3 wrap up",
            completedWork: "Implemented feature X",
            plannedWork: "Implement feature Y",
            issuesAndRisks: "Third party vendor latency");
        stubReportRepo.Reports[4] = reportDetail;

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var failingProvider = new GroundedAiTextGenerationProvider(new AiAssistantOptions { SimulateFailure = true });
        var service = new AiAssistantService(contextRetriever, failingProvider, _timeProvider);

        var result = await service.SummarizeReportAsync(101, 4, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Implemented feature X", result.Summary.Completed);
        Assert.Equal("Implement feature Y", result.Summary.InProgress);
        Assert.Equal("Third party vendor latency", result.Summary.Blockers);
        Assert.Contains("deterministic fallback", result.LimitationNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportSummary_WhenProviderTimesOut_ReturnsDeterministicFallback()
    {
        var stubReportRepo = new StubProgressReportRepository();
        var reportDetail = CreateReportDetail(5, 101, "WEEKLY", "Weekly work",
            completedWork: "Completed task A",
            plannedWork: "Plan task B",
            issuesAndRisks: "Risk C");
        stubReportRepo.Reports[5] = reportDetail;

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var timeoutProvider = new GroundedAiTextGenerationProvider(new AiAssistantOptions { SimulateTimeout = true, TimeoutSeconds = 0 });
        var service = new AiAssistantService(contextRetriever, timeoutProvider, _timeProvider);

        var result = await service.SummarizeReportAsync(101, 5, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Completed task A", result.Summary.Completed);
        Assert.Contains("deterministic fallback", result.LimitationNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportSummary_WhenProviderReturnsMalformedJson_ReturnsDeterministicFallback()
    {
        var stubReportRepo = new StubProgressReportRepository();
        var reportDetail = CreateReportDetail(6, 101, "WEEKLY", "Weekly overview",
            completedWork: "Built auth module",
            plannedWork: "Build profile module",
            issuesAndRisks: "Database migration blocked");
        stubReportRepo.Reports[6] = reportDetail;

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var malformedProvider = new StubTextGenerationProvider { ReturnText = "THIS IS NOT JSON AT ALL {{{ broken" };
        var service = new AiAssistantService(contextRetriever, malformedProvider, _timeProvider);

        var result = await service.SummarizeReportAsync(101, 6, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Built auth module", result.Summary.Completed);
        Assert.Equal("Build profile module", result.Summary.InProgress);
        Assert.Contains("deterministic fallback", result.LimitationNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportSummary_WhenActorCannotAccessProject_ThrowsForbiddenException()
    {
        var stubReportRepo = new StubProgressReportRepository();
        stubReportRepo.Reports[1] = CreateReportDetail(1, 101, "WEEKLY", "Summary");

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: false, actorId: 99);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.SummarizeReportAsync(101, 1, CancellationToken.None));
    }

    [Fact]
    public async Task ReportSummary_WhenReportBelongsToDifferentProject_ThrowsNotFoundException()
    {
        var stubReportRepo = new StubProgressReportRepository();
        // Report 1 belongs to project 202, but user asks for project 101
        stubReportRepo.Reports[1] = CreateReportDetail(1, 202, "WEEKLY", "Summary");

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        await Assert.ThrowsAsync<NotFoundException>(() => service.SummarizeReportAsync(101, 1, CancellationToken.None));
    }

    [Fact]
    public async Task ReportSummary_WhenReportTypeNotWeeklyOrMonthly_ThrowsValidationException()
    {
        var stubReportRepo = new StubProgressReportRepository();
        stubReportRepo.Reports[1] = CreateReportDetail(1, 101, "ANNUAL", "Summary");

        var contextRetriever = CreateContextRetriever(stubReportRepo, canAccess: true, actorId: 10);
        var provider = new GroundedAiTextGenerationProvider(new AiAssistantOptions());
        var service = new AiAssistantService(contextRetriever, provider, _timeProvider);

        await Assert.ThrowsAsync<ValidationException>(() => service.SummarizeReportAsync(101, 1, CancellationToken.None));
    }

    private static ProgressReportDetailDto CreateReportDetail(
        long id, long projectId, string reportType, string summary,
        string? completedWork = null, string? plannedWork = null, string? issuesAndRisks = null) =>
        new(
            Id: id,
            ProjectId: projectId,
            SubmittedBy: 10,
            SubmittedByName: "Student 1",
            ReportType: reportType,
            PeriodStart: new DateOnly(2026, 9, 1),
            PeriodEnd: new DateOnly(2026, 9, 7),
            Summary: summary,
            CompletedWork: completedWork ?? summary,
            PlannedWork: plannedWork ?? "Next tasks",
            IssuesAndRisks: issuesAndRisks ?? "None",
            Status: "SUBMITTED",
            SubmittedAt: FixedNow,
            IsLate: false,
            CreatedAt: FixedNow,
            UpdatedAt: FixedNow,
            Feedbacks: Array.Empty<ProgressReportFeedbackDto>());

    private static AiContextRetriever CreateContextRetriever(
        StubProgressReportRepository reportRepo, bool canAccess, long? actorId)
    {
        var dataReader = new StubProjectProgressDataReader { ProjectExists = true };
        var accessService = new StubProjectAccessService { CanAccess = canAccess };
        var currentUser = actorId.HasValue ? new TestCurrentUser(actorId.Value, "STUDENT") : (ICurrentUser)new UnauthenticatedTestCurrentUser();
        var milestoneRepo = new StubMilestoneRepository();
        var taskRepo = new StubTaskRepository();
        var meetingRepo = new StubMeetingRepository();
        var contribRepo = new StubContributionRepository();

        return new AiContextRetriever(
            accessService, dataReader, reportRepo, milestoneRepo, taskRepo, meetingRepo, contribRepo, currentUser);
    }
}
