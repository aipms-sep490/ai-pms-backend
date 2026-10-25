using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Contributions.Services;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ContributionAcceptanceTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 25, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Snapshot_rebuild_and_scoring_with_imbalanced_team_activity_calculates_exact_credit_and_population_variance()
    {
        // Setup: Team with 3 members having starkly imbalanced activities (UC-101 Team Imbalance)
        // Member A: ActivityScore = 10.0
        // Member B: ActivityScore = 1.0
        // Member C: ActivityScore = 0.0
        var memberA = new ContributionMemberDto(101, "Member A", AssignedTasks: 10, CompletedTasks: 10, SubmittedReports: 2, AttendedMeetings: 3, SubmittedDeliverableVersions: 1, ActivityScore: 10.0, EvidenceCount: 16);
        var memberB = new ContributionMemberDto(102, "Member B", AssignedTasks: 5, CompletedTasks: 1, SubmittedReports: 0, AttendedMeetings: 1, SubmittedDeliverableVersions: 0, ActivityScore: 1.0, EvidenceCount: 2);
        var memberC = new ContributionMemberDto(103, "Member C", AssignedTasks: 0, CompletedTasks: 0, SubmittedReports: 0, AttendedMeetings: 0, SubmittedDeliverableVersions: 0, ActivityScore: 0.0, EvidenceCount: 0);

        var members = new[] { memberA, memberB, memberC };
        var summary = ContributionScoring.Summarize(members);

        Assert.Equal("SUFFICIENT", summary.DataStatus);
        Assert.NotNull(summary.ActivityVariance);

        // Mean credit = (10 + 1 + 0) / 3 = 11.0 / 3 = 3.6666...
        // Variance = ((10 - 3.6666)^2 + (1 - 3.6666)^2 + (0 - 3.6666)^2) / 3 ~ 20.222
        var expectedVariance = ((Math.Pow(10 - (11.0 / 3.0), 2)) + Math.Pow(1 - (11.0 / 3.0), 2) + Math.Pow(0 - (11.0 / 3.0), 2)) / 3.0;
        Assert.Equal(Math.Round(expectedVariance, 4), Math.Round(summary.ActivityVariance.Value, 4));
        Assert.True(summary.ActivityVariance.Value > 20.0);
    }

    [Fact]
    public void Insufficient_data_when_team_activity_credit_under_3_returns_INSUFFICIENT_DATA_and_null_variance()
    {
        // UC-102: Handle Insufficient Contribution Data (total credit < 3.0 never guesses or hallucinates 0 variance)
        var memberA = new ContributionMemberDto(201, "Member A", 1, 1, 0, 0, 0, 1.0, 1);
        var memberB = new ContributionMemberDto(202, "Member B", 1, 1, 0, 0, 0, 1.0, 1);
        var memberC = new ContributionMemberDto(203, "Member C", 0, 0, 0, 0, 0, 0.0, 0);

        var summary = ContributionScoring.Summarize([memberA, memberB, memberC]);

        Assert.Equal("INSUFFICIENT_DATA", summary.DataStatus);
        Assert.Null(summary.ActivityVariance); // Explicit null, not 0.0
    }

    [Fact]
    public async Task Contribution_workflow_authorizes_scoped_evaluators_and_rejects_unscoped_actors_with_403()
    {
        var repository = new MockContributionRepository();
        var access = new MockProjectAccessService { AllowedUserIds = [1001, 1002] }; // 1001 and 1002 are authorized
        var audit = new RecordingAudit();

        // 1. Authorized evaluator (1001) accesses contribution summary
        var authorizedUser = new MockCurrentUser(1001);
        var workflow = new ContributionWorkflow(repository, access, authorizedUser, audit, new FixedTimeProvider(FixedNow));

        var summary = await workflow.Summary(projectId: 50, page: 1, pageSize: 20, snapshot: false, CancellationToken.None);
        Assert.NotNull(summary);

        // 2. Out-of-scope evaluator (9999) receives 403 Forbidden
        var unauthorizedUser = new MockCurrentUser(9999);
        var unauthorizedWorkflow = new ContributionWorkflow(repository, access, unauthorizedUser, audit, new FixedTimeProvider(FixedNow));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            unauthorizedWorkflow.Summary(projectId: 50, page: 1, pageSize: 20, snapshot: false, CancellationToken.None));
    }

    [Fact]
    public async Task Contribution_rebuild_requires_academic_staff_and_records_audit_trail()
    {
        var repository = new MockContributionRepository { CanRebuild = false }; // Student actor
        var access = new MockProjectAccessService { AllowedUserIds = [3001] };
        var audit = new RecordingAudit();

        var studentUser = new MockCurrentUser(3001);
        var workflow = new ContributionWorkflow(repository, access, studentUser, audit, new FixedTimeProvider(FixedNow));

        // Student cannot rebuild snapshot
        await Assert.ThrowsAsync<ForbiddenException>(() => workflow.Rebuild(projectId: 60, CancellationToken.None));
        Assert.Empty(audit.RecordedEntries);

        // Academic staff can rebuild snapshot
        repository.CanRebuild = true;
        var staffUser = new MockCurrentUser(2001);
        access.AllowedUserIds.Add(2001);
        var staffWorkflow = new ContributionWorkflow(repository, access, staffUser, audit, new FixedTimeProvider(FixedNow));

        var result = await staffWorkflow.Rebuild(projectId: 60, CancellationToken.None);
        Assert.NotNull(result);

        Assert.Contains(audit.RecordedEntries, e => e.Action == "CONTRIBUTION_SNAPSHOT_REBUILT" && e.EntityId == 60 && e.ActorUserId == 2001);
    }

    private sealed class MockContributionRepository : IContributionRepository
    {
        public bool CanRebuild { get; set; } = true;

        public Task<bool> IsActiveUserAsync(long userId, CancellationToken ct) => Task.FromResult(true);

        public Task<bool> CanRebuildAsync(long projectId, long userId, CancellationToken ct) => Task.FromResult(CanRebuild);

        public Task<ContributionSummaryDto> GetSummaryAsync(long projectId, bool storedOnly, CancellationToken ct)
        {
            var members = new[]
            {
                new ContributionMemberDto(1, "Member 1", 5, 5, 2, 1, 5, 5.0, 10),
                new ContributionMemberDto(2, "Member 2", 5, 5, 2, 1, 5, 5.0, 10)
            };
            return Task.FromResult(ContributionScoring.Summarize(members));
        }

        public Task<IReadOnlyList<ContributionEvidenceDto>> GetEvidenceAsync(long projectId, long userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContributionEvidenceDto>>([]);

        public Task<ContributionRebuildResult> RebuildSnapshotAsync(long projectId, DateTime now, CancellationToken ct) =>
            Task.FromResult(new ContributionRebuildResult(GetSummaryAsync(projectId, false, ct).Result, true));

        public async Task<T> InProjectTransactionAsync<T>(long projectId, Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            await action(ct);
    }

    private sealed class MockProjectAccessService : IProjectAccessService
    {
        public HashSet<long> AllowedUserIds { get; set; } = [];

        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken ct) =>
            Task.FromResult(AllowedUserIds.Contains(userId));
    }

    private sealed class MockCurrentUser(long? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId.HasValue;
        public long? UserId => userId;
        public string? Email => "user@fpt.edu.vn";
        public string? FullName => "User Name";
        public IReadOnlyCollection<string> Roles => ["STUDENT"];
    }

    private sealed class RecordingAudit : IAuditTrail
    {
        public List<AuditEntry> RecordedEntries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            RecordedEntries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
