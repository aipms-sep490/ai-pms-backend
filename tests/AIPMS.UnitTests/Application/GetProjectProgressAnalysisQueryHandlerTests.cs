using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
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
        var dataReader = new StubProjectProgressDataReader { Facts = null };
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

        var dataReader = new StubProjectProgressDataReader { Facts = facts };
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
}

internal sealed class UnauthenticatedTestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public long? UserId => null;
    public string? Email => null;
    public string? FullName => null;
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();
}

internal sealed class StubProjectProgressDataReader : IProjectProgressDataReader
{
    public ProjectProgressFacts? Facts { get; set; }

    public Task<ProjectProgressFacts?> GetProjectProgressFactsAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(Facts);
}

internal sealed class StubProjectAccessService : AIPMS.Application.Abstractions.Security.IProjectAccessService
{
    public bool CanAccess { get; set; } = true;

    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(CanAccess);
}
