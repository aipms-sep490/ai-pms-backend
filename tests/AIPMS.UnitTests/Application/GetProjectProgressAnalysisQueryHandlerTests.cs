using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Application.Features.Projects.Queries;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class GetProjectProgressAnalysisQueryHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var dataReader = new StubProjectProgressDataReader();
        var aiService = new RuleBasedProgressAnalysisService();
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new UnauthenticatedTestCurrentUser();
        var timeProvider = new FakeTimeProvider(FixedNow);

        var handler = new GetProjectProgressAnalysisQueryHandler(
            dataReader, aiService, accessService, currentUser, timeProvider);

        var query = new GetProjectProgressAnalysisQuery(101);

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(query, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UserWithoutAccess_ThrowsForbiddenException()
    {
        var dataReader = new StubProjectProgressDataReader();
        var aiService = new RuleBasedProgressAnalysisService();
        var accessService = new StubProjectAccessService { CanAccess = false };
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var timeProvider = new FakeTimeProvider(FixedNow);

        var handler = new GetProjectProgressAnalysisQueryHandler(
            dataReader, aiService, accessService, currentUser, timeProvider);

        var query = new GetProjectProgressAnalysisQuery(101);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(query, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_NonexistentProject_ThrowsNotFoundException()
    {
        var dataReader = new StubProjectProgressDataReader { ProjectExists = false, Facts = null };
        var aiService = new RuleBasedProgressAnalysisService();
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var timeProvider = new FakeTimeProvider(FixedNow);

        var handler = new GetProjectProgressAnalysisQueryHandler(
            dataReader, aiService, accessService, currentUser, timeProvider);

        var query = new GetProjectProgressAnalysisQuery(999);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(query, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AuthorizedUser_ReturnsProgressAnalysisDto()
    {
        var facts = new ProjectProgressFacts(
            ProjectId: 101,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "M1", "IN_PROGRESS", DateOnly.FromDateTime(FixedNow), DateOnly.FromDateTime(FixedNow.AddDays(14)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "TODO", "NORMAL", FixedNow, FixedNow.AddDays(5), null, 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var dataReader = new StubProjectProgressDataReader { ProjectExists = true, Facts = facts };
        var aiService = new RuleBasedProgressAnalysisService();
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var timeProvider = new FakeTimeProvider(FixedNow);

        var handler = new GetProjectProgressAnalysisQueryHandler(
            dataReader, aiService, accessService, currentUser, timeProvider);

        var query = new GetProjectProgressAnalysisQuery(101);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(101, result.ProjectId);
        Assert.Equal("SUFFICIENT", result.DataStatus);
        Assert.Equal("PROVISIONAL_RULE_BASELINE_1.0", result.RuleVersion);
    }

    [Fact]
    public async Task Handle_CancellationTokenForwarded_CancelsOperation()
    {
        var dataReader = new StubProjectProgressDataReader { ProjectExists = true };
        var aiService = new RuleBasedProgressAnalysisService();
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var timeProvider = new FakeTimeProvider(FixedNow);

        var handler = new GetProjectProgressAnalysisQueryHandler(
            dataReader, aiService, accessService, currentUser, timeProvider);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(new GetProjectProgressAnalysisQuery(101), cts.Token));
    }

    [Fact]
    public async Task Handle_ForwardsCancellationToken_ToReaderAndAiService()
    {
        var facts = new ProjectProgressFacts(101, "ACTIVE", 1, 4, Array.Empty<MilestoneFact>(), Array.Empty<TaskFact>(), Array.Empty<ProgressReportFact>(), Array.Empty<MeetingFact>());
        var dataReader = new RecordingProjectProgressDataReader { ProjectExists = true, Facts = facts };
        var aiSpy = new RecordingProgressAnalysisService();
        var accessService = new StubProjectAccessService { CanAccess = true };
        var currentUser = new TestCurrentUser(10, AppRoles.Student);
        var timeProvider = new FakeTimeProvider(FixedNow);

        var handler = new GetProjectProgressAnalysisQueryHandler(dataReader, aiSpy, accessService, currentUser, timeProvider);

        using var cts = new CancellationTokenSource();
        await handler.Handle(new GetProjectProgressAnalysisQuery(101), cts.Token);

        Assert.Equal(cts.Token, dataReader.LastToken);
        Assert.Equal(cts.Token, aiSpy.LastToken);
    }

    [Fact]
    public void Query_Contract_AcceptsOnlyProjectId()
    {
        var query = new GetProjectProgressAnalysisQuery(101);
        Assert.Equal(101, query.ProjectId);
        var propertyNames = typeof(GetProjectProgressAnalysisQuery).GetProperties().Select(p => p.Name).ToList();
        Assert.Single(propertyNames);
        Assert.Equal("ProjectId", propertyNames[0]);
    }
}

internal sealed class RecordingProgressAnalysisService : AIPMS.Application.Abstractions.AI.IProgressAnalysisService
{
    public CancellationToken LastToken { get; private set; }

    public ProjectProgressAnalysisDto Analyze(
        ProjectProgressFacts facts,
        DateTime analysisTimeUtc,
        CancellationToken cancellationToken = default)
    {
        LastToken = cancellationToken;
        return new ProjectProgressAnalysisDto(
            facts.ProjectId,
            analysisTimeUtc,
            analysisTimeUtc,
            "INSUFFICIENT_DATA",
            "INSUFFICIENT_DATA",
            null,
            0.0,
            "INSUFFICIENT_DATA",
            new ProgressSummaryDto(0, 0, 0, 0, 0, 0, 0, 0.0),
            new FeatureSnapshotDto(null, null, null, null, null, null, null, null, 0, null, null),
            Array.Empty<RiskFactorDto>(),
            Array.Empty<string>(),
            "V1",
            "V1",
            "RULE",
            null);
    }

    public AIPMS.Application.Abstractions.AI.ProgressAnalysisResult Analyze(AIPMS.Application.Abstractions.AI.ProgressAnalysisInput input) =>
        new("LOW", 0m, 0m, Array.Empty<string>());
}

internal sealed class RecordingProjectProgressDataReader : IProjectProgressDataReader
{
    public bool ProjectExists { get; set; } = true;
    public ProjectProgressFacts? Facts { get; set; }
    public CancellationToken LastToken { get; private set; }

    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        return Task.FromResult(ProjectExists);
    }

    public Task<ProjectProgressFacts?> GetProjectProgressFactsAsync(long projectId, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        return Task.FromResult(Facts);
    }
}

internal sealed class StubProjectProgressDataReader : IProjectProgressDataReader
{
    public bool ProjectExists { get; set; } = true;
    public ProjectProgressFacts? Facts { get; set; }

    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProjectExists);
    }

    public Task<ProjectProgressFacts?> GetProjectProgressFactsAsync(long projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Facts);
    }
}

internal sealed class StubProjectAccessService : AIPMS.Application.Abstractions.Security.IProjectAccessService
{
    public bool CanAccess { get; set; } = true;

    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CanAccess);
    }
}
