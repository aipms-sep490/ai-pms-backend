using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Providers;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.AiAssistant.Services;
using AIPMS.Application.Features.ProgressReports.DTOs;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class AiEvidenceStructuredSummaryTests
{
    private sealed class StubFailingAiModelProvider : IAiTextGenerationProvider
    {
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
            throw new Exception("AI Provider simulated timeout/failure for testing fallback.");
    }

    private sealed class StubContextRetriever(ReportBoundedContext reportContext) : IAiContextRetriever
    {
        public Task<ProjectBoundedContext> RetrieveProjectContextAsync(long projectId, string userQuery, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ReportBoundedContext> RetrieveReportContextAsync(long projectId, long reportId, CancellationToken cancellationToken = default) =>
            Task.FromResult(reportContext);
    }

    [Fact]
    public async Task AiAssistantService_DeterministicFallback_PrefersNewStructuredFields()
    {
        var reportContext = new ReportBoundedContext(
            ProjectId: 1,
            ReportId: 10,
            ReportType: "WEEKLY",
            PeriodStart: new DateOnly(2026, 10, 1),
            PeriodEnd: new DateOnly(2026, 10, 8),
            Status: "SUBMITTED",
            Summary: "Overall weekly summary",
            CompletedWork: "Setup project structure and DB migration",
            PlannedWork: "Old planned work field",
            IssuesAndRisks: "Old issues and risks field",
            EvidenceList: Array.Empty<EvidenceReferenceDto>(),
            FormattedEvidenceText: "{}",
            HasSufficientEvidence: true,
            InProgressWork: "New in-progress API endpoints",
            Blockers: "Database lock contention",
            Risks: "Tight milestone deadline",
            NextActions: "Implement integration tests");

        var service = new AiAssistantService(
            contextRetriever: new StubContextRetriever(reportContext),
            provider: new StubFailingAiModelProvider(),
            timeProvider: new FakeTimeProvider(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc)));

        var summary = await service.SummarizeReportAsync(1, 10, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal("Setup project structure and DB migration", summary.Summary.Completed);
        Assert.Equal("New in-progress API endpoints", summary.Summary.InProgress);
        Assert.Equal("Database lock contention", summary.Summary.Blockers);
        Assert.Equal("Tight milestone deadline", summary.Summary.Risks);
        Assert.Equal("Implement integration tests", summary.Summary.NextActions);
    }

    [Fact]
    public async Task AiAssistantService_DeterministicFallback_FallsBackToLegacyFields()
    {
        var reportContext = new ReportBoundedContext(
            ProjectId: 1,
            ReportId: 10,
            ReportType: "WEEKLY",
            PeriodStart: new DateOnly(2026, 10, 1),
            PeriodEnd: new DateOnly(2026, 10, 8),
            Status: "SUBMITTED",
            Summary: "Overall weekly summary",
            CompletedWork: "Setup project structure",
            PlannedWork: "Legacy planned work",
            IssuesAndRisks: "Legacy issues and risks",
            EvidenceList: Array.Empty<EvidenceReferenceDto>(),
            FormattedEvidenceText: "{}",
            HasSufficientEvidence: true,
            InProgressWork: null,
            Blockers: null,
            Risks: null,
            NextActions: null);

        var service = new AiAssistantService(
            contextRetriever: new StubContextRetriever(reportContext),
            provider: new StubFailingAiModelProvider(),
            timeProvider: new FakeTimeProvider(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc)));

        var summary = await service.SummarizeReportAsync(1, 10, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal("Setup project structure", summary.Summary.Completed);
        Assert.Equal("Legacy planned work", summary.Summary.InProgress);
        Assert.Equal("Legacy issues and risks", summary.Summary.Blockers);
        Assert.Equal("Legacy issues and risks", summary.Summary.Risks);
        Assert.Equal("Legacy planned work", summary.Summary.NextActions);
    }

    [Fact]
    public async Task GroundedAiProvider_GeneratesReportSummary_UsingStructuredFields()
    {
        var provider = new GroundedAiTextGenerationProvider();
        var evidenceJson = "{\"reportId\":1,\"projectId\":1,\"reportType\":\"WEEKLY\",\"period\":\"2026-10-01 to 2026-10-08\",\"status\":\"SUBMITTED\",\"summary\":\"Weekly Summary\",\"completedWork\":\"Feature A done\",\"inProgressWork\":\"Feature B in progress\",\"blockers\":\"Blocked on network\",\"risks\":\"Performance bottleneck\",\"nextActions\":\"Review PR\"}";

        var prompt = $"<report_evidence>\n{evidenceJson}\n</report_evidence>";
        var result = await provider.GenerateTextAsync("summarizing an academic project progress report", prompt, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Feature A done", result);
        Assert.Contains("Feature B in progress", result);
        Assert.Contains("Blocked on network", result);
        Assert.Contains("Performance bottleneck", result);
        Assert.Contains("Review PR", result);
    }
}
