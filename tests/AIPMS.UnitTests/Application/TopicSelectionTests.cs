using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Topics.Abstractions;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Models;
using AIPMS.Application.Features.Topics.Services;
using AIPMS.Domain.Teams;
using AIPMS.Domain.Topics;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class TopicSelectionTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SelectValidTopic_Succeeds()
    {
        // Arrange
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicMajorIds: [100],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 100,
            teamMajorIds: [100]);

        // Assert
        Assert.Empty(issues);
    }

    [Fact]
    public void SelectValidTopic_Interdisciplinary_Succeeds()
    {
        // Arrange
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicMajorIds: [100, 200],
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            teamMajorIds: [100, 200]);

        // Assert
        Assert.Empty(issues);
    }

    [Fact]
    public void SelectTopic_WhenTopicNotPublished_ReturnsIssue()
    {
        // Draft topic cannot be selected
        var issuesDraft = TopicSelectionRules.ValidateSelection(
            topicStatus: "DRAFT",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicMajorIds: [100],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 100,
            teamMajorIds: [100]);

        Assert.Contains("TOPIC_NOT_PUBLISHED", issuesDraft);

        // Closed topic cannot be selected
        var issuesClosed = TopicSelectionRules.ValidateSelection(
            topicStatus: "CLOSED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicMajorIds: [100],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 100,
            teamMajorIds: [100]);

        Assert.Contains("TOPIC_NOT_PUBLISHED", issuesClosed);
    }

    [Fact]
    public void SelectTopicOnSubmittedDraft_Returns409()
    {
        // Project in SUBMITTED status cannot select/modify topic
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "SUBMITTED",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicMajorIds: [100],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 100,
            teamMajorIds: [100]);

        Assert.Contains("PROJECT_NOT_EDITABLE", issues);
    }

    [Fact]
    public void CrossScopeTopic_Rejected()
    {
        // 1. Single major mismatch
        var issuesPrimaryMajor = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicMajorIds: [100],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 200, // Team has different primary major
            teamMajorIds: [200]);

        Assert.Contains("PRIMARY_MAJOR_MISMATCH", issuesPrimaryMajor);

        // 2. Project mode mismatch
        var issuesModeMismatch = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicMajorIds: [100, 200],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 100,
            teamMajorIds: [100]);

        Assert.Contains("PROJECT_MODE_MISMATCH", issuesModeMismatch);

        // 3. Period mismatch
        var issuesPeriodMismatch = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 20, // Different period
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicMajorIds: [100],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 100,
            teamMajorIds: [100]);

        Assert.Contains("TOPIC_PERIOD_MISMATCH", issuesPeriodMismatch);

        // 4. Missing required interdisciplinary major
        var issuesInterdisciplinaryMissing = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicMajorIds: [100, 200, 300], // Requires 300
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            teamMajorIds: [100, 200]);

        Assert.Contains("TOPIC_MAJOR_REQUIREMENTS_UNSATISFIED", issuesInterdisciplinaryMissing);
    }

    // P1 #1 Tests
    [Fact]
    public void TopicSelectionRules_MissingTeamPeriod_ReturnsRegistrationWindowUnavailable()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 0, // Missing team period window
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicRequirements: [new TopicMajorRequirementRule(100, 1, int.MaxValue)],
            teamProjectMode: "SINGLE_MAJOR",
            teamPrimaryMajorId: 100,
            memberEvidences: [new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false)]);

        Assert.Contains("REGISTRATION_WINDOW_UNAVAILABLE", issues);
    }

    [Fact]
    public async Task SelectTopic_WhenRegistrationWindowUnavailable_ReturnsConflict()
    {
        var projectRepo = new StubProjectRepository();
        projectRepo.IsLeader = true;
        var project = new ProjectDto(1, 10, "Team", "PRJ", "Title", null, null, "DRAFT",
            FixedNow, null, null, null, 10, "Leader", FixedNow, FixedNow, "Prob", "Out", "tok", [], []);
        projectRepo.Projects[1] = project;

        var teamRepo = new StubTeamRepoForGuard { Window = null }; // registration window unavailable
        var topicRepo = new StubTopicRepoForGuard();
        var guard = new TopicSelectionGuard(projectRepo, teamRepo, topicRepo.Workflow, new FakeTimeProvider(FixedNow));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            guard.ValidateTopicSelectionAsync(100, 1, 10, CancellationToken.None));

        Assert.Equal("REGISTRATION_WINDOW_UNAVAILABLE", ex.Message);
    }

    // P1 #2 Tests: Legacy team without AcademicScope
    [Fact]
    public void LegacyTeam_NoAcademicScope_SingleMajorTopic_MismatchedVerifiedMemberMajor_Rejected()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicRequirements: [new TopicMajorRequirementRule(100, 1, int.MaxValue)],
            teamProjectMode: null, // Legacy team: no AcademicScope
            teamPrimaryMajorId: null,
            memberEvidences: [new TeamMemberEvidence(200, IsVerifiedActive: true, IsFormer: false)]);

        Assert.Contains("PRIMARY_MAJOR_MISMATCH", issues);
    }

    [Fact]
    public void LegacyTeam_NoAcademicScope_SingleMajorTopic_AllVerifiedMembersMatch_Passes()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicRequirements: [new TopicMajorRequirementRule(100, 1, int.MaxValue)],
            teamProjectMode: null, // Legacy team: no AcademicScope
            teamPrimaryMajorId: null,
            memberEvidences: [new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false)]);

        Assert.Empty(issues);
    }

    [Fact]
    public void LegacyTeam_NoAcademicScope_MixedVerifiedMajors_SingleMajorTopic_Rejected()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicRequirements: [new TopicMajorRequirementRule(100, 1, int.MaxValue)],
            teamProjectMode: null, // Legacy team: no AcademicScope
            teamPrimaryMajorId: null,
            memberEvidences: [
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false),
                new TeamMemberEvidence(200, IsVerifiedActive: true, IsFormer: false)
            ]);

        Assert.Contains("PRIMARY_MAJOR_MISMATCH", issues);
    }

    [Fact]
    public void LegacyTeam_NoAcademicScope_SingleMajorTopic_MissingAuthoritativeMajorEvidence_FailsClosed()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicRequirements: [new TopicMajorRequirementRule(100, 1, int.MaxValue)],
            teamProjectMode: null, // Legacy team: no AcademicScope
            teamPrimaryMajorId: null,
            memberEvidences: []); // No member evidence

        Assert.Contains("MAJOR_EVIDENCE_UNAVAILABLE", issues);
    }

    [Fact]
    public void LegacyTeam_NoAcademicScope_SingleMajorTopic_FormerMemberMismatch_ActiveMemberMatches_Passes()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "SINGLE_MAJOR",
            topicPrimaryMajorId: 100,
            topicRequirements: [new TopicMajorRequirementRule(100, 1, int.MaxValue)],
            teamProjectMode: null, // Legacy team: no AcademicScope
            teamPrimaryMajorId: null,
            memberEvidences: [
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false), // Active verified member
                new TeamMemberEvidence(200, IsVerifiedActive: true, IsFormer: true)  // Former member with different major
            ]);

        Assert.Empty(issues);
    }

    // P1 #3 Tests: Interdisciplinary evidence + per-major quotas
    [Fact]
    public void InterdisciplinaryTopic_NoMajorEvidence_Rejected()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicRequirements: [
                new TopicMajorRequirementRule(100, 1, 2),
                new TopicMajorRequirementRule(200, 1, 2)
            ],
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            memberEvidences: []);

        Assert.Contains("MAJOR_EVIDENCE_UNAVAILABLE", issues);
    }

    [Fact]
    public void InterdisciplinaryTopic_MajorBelowMin_Rejected()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicRequirements: [
                new TopicMajorRequirementRule(100, 2, 3),
                new TopicMajorRequirementRule(200, 1, 2)
            ],
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            memberEvidences: [
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false), // only 1 member, but min is 2
                new TeamMemberEvidence(200, IsVerifiedActive: true, IsFormer: false)
            ]);

        Assert.Contains("MAJOR_MIN_MEMBERS", issues);
    }

    [Fact]
    public void InterdisciplinaryTopic_MajorAboveMax_Rejected()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicRequirements: [
                new TopicMajorRequirementRule(100, 1, 2),
                new TopicMajorRequirementRule(200, 1, 2)
            ],
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            memberEvidences: [
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false),
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false),
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false), // 3 members for 100, max is 2
                new TeamMemberEvidence(200, IsVerifiedActive: true, IsFormer: false)
            ]);

        Assert.Contains("MAJOR_MAX_MEMBERS", issues);
    }

    [Fact]
    public void InterdisciplinaryTopic_AllPerMajorQuotasSatisfied_Passes()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicRequirements: [
                new TopicMajorRequirementRule(100, 1, 2),
                new TopicMajorRequirementRule(200, 1, 2)
            ],
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            memberEvidences: [
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false),
                new TeamMemberEvidence(200, IsVerifiedActive: true, IsFormer: false)
            ]);

        Assert.Empty(issues);
    }

    [Fact]
    public void FormerMember_DoesNotCountTowardMajorQuota()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicRequirements: [
                new TopicMajorRequirementRule(100, 1, 2),
                new TopicMajorRequirementRule(200, 1, 2)
            ],
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            memberEvidences: [
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false),
                new TeamMemberEvidence(200, IsVerifiedActive: true, IsFormer: true) // former member!
            ]);

        Assert.Contains("MAJOR_MIN_MEMBERS", issues);
        Assert.Contains("MAJOR_REQUIREMENT_MISSING", issues);
    }

    [Fact]
    public void UnverifiedMajor_DoesNotCountTowardMajorQuota()
    {
        var issues = TopicSelectionRules.ValidateSelection(
            topicStatus: "PUBLISHED",
            projectStatus: "DRAFT",
            topicPeriodId: 10,
            teamPeriodId: 10,
            topicProjectMode: "INTERDISCIPLINARY",
            topicPrimaryMajorId: null,
            topicRequirements: [
                new TopicMajorRequirementRule(100, 1, 2),
                new TopicMajorRequirementRule(200, 1, 2)
            ],
            teamProjectMode: "INTERDISCIPLINARY",
            teamPrimaryMajorId: null,
            memberEvidences: [
                new TeamMemberEvidence(100, IsVerifiedActive: true, IsFormer: false),
                new TeamMemberEvidence(null, IsVerifiedActive: true, IsFormer: false), // unverified major (null)!
                new TeamMemberEvidence(200, IsVerifiedActive: false, IsFormer: false) // unverified member!
            ]);

        Assert.Contains("MAJOR_MIN_MEMBERS", issues);
        Assert.Contains("MAJOR_REQUIREMENT_MISSING", issues);
    }
}

internal sealed class StubTeamRepoForGuard : ITeamRepository
{
    public TeamRegistrationWindow? Window { get; set; } = new(10, 1, 1, DateTime.UtcNow.AddDays(10));
    public TeamSnapshot? Team { get; set; } = new(10, 1, "T10", "Team 10", null, "ELIGIBLE",
        [new TeamParticipant(10, "Leader", 100, 1, true, true)], ["DRAFT"]);

    public Task<TeamSnapshot?> GetAsync(long id, CancellationToken ct) => Task.FromResult(Team);
    public Task<TeamRegistrationWindow?> GetOpenWindowAsync(long semester, DateTime now, CancellationToken ct) => Task.FromResult(Window);
    public Task<TeamParticipant?> GetStudentAsync(long userId, CancellationToken ct) => Task.FromResult<TeamParticipant?>(null);
    public Task<long?> GetCurrentTeamIdAsync(long semesterId, long userId, CancellationToken ct) => Task.FromResult<long?>(null);
    public Task<long> CreateAsync(long semesterId, string code, string name, string? description, long actorId, DateTime now, CancellationToken ct) => Task.FromResult(1L);
    public Task UpdateAsync(long teamId, string name, string? description, string status, DateTime now, CancellationToken ct) => Task.CompletedTask;
    public Task AddMemberAsync(long teamId, long semesterId, long userId, bool isLeader, DateTime now, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveMemberAsync(long teamId, long userId, DateTime now, CancellationToken ct) => Task.CompletedTask;
    public Task TransferLeaderAsync(long teamId, long oldLeaderId, long newLeaderId, DateTime now, CancellationToken ct) => Task.CompletedTask;
    public Task<TeamInvitationData?> GetInvitationAsync(long invitationId, CancellationToken ct) => Task.FromResult<TeamInvitationData?>(null);
    public Task<TeamInvitationData?> GetPendingInvitationAsync(long teamId, long userId, CancellationToken ct) => Task.FromResult<TeamInvitationData?>(null);
    public Task<TeamInvitationData> InviteAsync(long teamId, long invitedUserId, long actorId, string? message, DateTime expiresAt, DateTime now, CancellationToken ct) => throw new NotImplementedException();
    public Task RespondAsync(long invitationId, string status, DateTime now, CancellationToken ct) => Task.CompletedTask;
    public Task<PagedResult<TeamInvitationData>> GetInvitationsAsync(long userId, long? teamId, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
    public Task ValidateAcademicScopeAsync(TeamAcademicScope scope, long organizationId, CancellationToken ct) => Task.CompletedTask;
    public Task SetAcademicScopeAsync(long teamId, TeamAcademicScope scope, Guid? expectedToken, CancellationToken ct) => Task.CompletedTask;
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);
}

internal sealed class StubTopicRepoForGuard : ITopicRepository
{
    private static readonly DateTime FixedNow = new(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);

    public TopicWorkflow Workflow { get; }

    public StubTopicRepoForGuard()
    {
        Workflow = new TopicWorkflow(
            this,
            new StubPolicyProvider(),
            new TestCurrentUser(10, AppRoles.Student),
            new RecordingAuditTrail(),
            new FakeTimeProvider(FixedNow));
    }

    public Task<TopicActor?> GetActorAsync(long userId, IReadOnlyCollection<string> tokenRoles, CancellationToken ct) =>
        Task.FromResult<TopicActor?>(new TopicActor(userId, 1, 1, 1, false, false, false, true, true, true));

    public Task<TopicDto?> GetAsync(long id, TopicActor actor, bool forUpdate, CancellationToken ct) =>
        Task.FromResult<TopicDto?>(new TopicDto(
            id, "TOPIC-100", "PUBLISHED", 10, 1, 1, 1, "SE Dept", "Topic Title", "Desc", "Prob", "Objs", "Out", "Domain",
            ["C#"], ["Web"], "SINGLE_MAJOR", 100,
            [new TopicMajorRequirementDto(100, "SE", "Software", 1, "SE Dept", 1, 5, "Responsibility")],
            10, 10, FixedNow, FixedNow, 10, FixedNow, null, null, null, Guid.NewGuid(), true));

    public Task<TopicPeriod?> GetPeriodAsync(long periodId, CancellationToken ct) =>
        Task.FromResult<TopicPeriod?>(new TopicPeriod(periodId, 1, 1, "REGISTRATION", "ACTIVE", "ACTIVE", FixedNow.AddDays(-1), FixedNow.AddDays(10), DateOnly.FromDateTime(FixedNow.AddDays(30)), true));

    public Task<IReadOnlyList<TopicMajor>> GetMajorsAsync(IReadOnlyList<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TopicMajor>>(ids.Select(id => new TopicMajor(id, 1, 1, true)).ToList());

    public Task<bool> IsActiveDepartmentAsync(long departmentId, long organizationId, CancellationToken ct) => Task.FromResult(true);
    public Task<PagedResult<TopicDto>> ListAsync(TopicActor actor, TopicFilter filter, CancellationToken ct) => throw new NotImplementedException();
    public Task<TopicDto> CreateAsync(CreateTopicRequest input, TopicActor actor, DateTime now, CancellationToken ct) => throw new NotImplementedException();
    public Task<TopicDto> UpdateAsync(long id, TopicContentRequest content, TopicActor actor, DateTime now, CancellationToken ct) => throw new NotImplementedException();
    public Task<TopicDto> SetStatusAsync(long id, bool publish, string? reason, TopicActor actor, DateTime now, CancellationToken ct) => throw new NotImplementedException();
    public Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct) => action();
}

internal sealed class StubPolicyProvider : ITeamFormationPolicyProvider
{
    public Task<TeamFormationPolicy?> GetAsync(long periodId, CancellationToken cancellationToken) =>
        Task.FromResult<TeamFormationPolicy?>(new TeamFormationPolicy(2, 5, 24, "v1", 1));
}
