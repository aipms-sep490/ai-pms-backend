using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.Queries;
using AIPMS.Application.Features.Teams.Validators;

namespace AIPMS.UnitTests.Application.Teams;

public sealed partial class TeamHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Candidate_query_uses_authorized_scope_and_preserves_paging_and_pending_state(bool pending)
    {
        var h = new Harness();
        var reader = new CandidateReader { Pending = pending };
        using var cancellation = new CancellationTokenSource();
        var result = await new GetTeamInvitationCandidatesQueryHandler(h.Workflow, reader)
            .Handle(new(1, " SE001 ", 2, 5), cancellation.Token);
        Assert.Equal(new TeamInvitationCandidateScope(1, 1, 10, 1, Now), reader.Scope);
        Assert.Equal("SE001", reader.Search);
        Assert.Equal(cancellation.Token, reader.Token);
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(8, result.TotalCount);
        var candidate = Assert.Single(result.Items);
        Assert.Equal(pending ? "PENDING" : "NONE", candidate.InvitationStatus);
        Assert.Equal(!pending, candidate.CanInvite);
        Assert.Equal(pending ? 9L : (long?)null, candidate.PendingInvitationId);
        Assert.Equal(pending ? DateTimeKind.Utc : (DateTimeKind?)null, candidate.PendingInvitationExpiresAt?.Kind);
        Assert.Empty(h.Audit.Entries);
        Assert.False(h.Repository.InTransaction);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Candidate_query_does_not_read_directory_for_member_or_outsider(long userId)
    {
        var h = new Harness();
        h.Actor.UserId = userId;
        var reader = new CandidateReader();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new GetTeamInvitationCandidatesQueryHandler(h.Workflow, reader).Handle(new(1), default));
        Assert.Null(reader.Scope);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("locked")]
    [InlineData("closedWindow")]
    [InlineData("missingPolicy")]
    [InlineData("inactiveLeader")]
    [InlineData("mixedRoster")]
    public async Task Candidate_query_blocks_unusable_team_context_before_search(string reason)
    {
        var h = new Harness();
        if (reason == "full") h.Policies.Policy = h.Policies.Policy! with { MaxMembers = 2 };
        if (reason == "locked") h.Repository.Team = h.Repository.Team! with { ProjectStatuses = ["SUBMITTED"] };
        if (reason == "closedWindow") h.Repository.Window = null;
        if (reason == "missingPolicy") h.Policies.Policy = null;
        if (reason == "inactiveLeader") h.Repository.Students[1] = h.Repository.Students[1] with { IsEligibleStudent = false };
        if (reason == "mixedRoster") h.Repository.Team = h.Repository.Team! with
        {
            Members = [h.Repository.Students[1] with { IsLeader = true }, h.Repository.Students[4]]
        };
        var reader = new CandidateReader();
        await Assert.ThrowsAsync<ConflictException>(() =>
            new GetTeamInvitationCandidatesQueryHandler(h.Workflow, reader).Handle(new(1), default));
        Assert.Null(reader.Scope);
        Assert.Empty(h.Audit.Entries);
    }

    [Theory]
    [InlineData(0, 1, 20)]
    [InlineData(1, 0, 20)]
    [InlineData(1, int.MaxValue, 20)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 101)]
    public void Candidate_query_rejects_invalid_ids_and_paging(long teamId, int page, int size) =>
        Assert.False(new GetTeamInvitationCandidatesQueryValidator()
            .Validate(new GetTeamInvitationCandidatesQuery(teamId, null, page, size)).IsValid);

    [Fact]
    public void Candidate_query_limits_search_and_accepts_optional_search()
    {
        var validator = new GetTeamInvitationCandidatesQueryValidator();
        Assert.True(validator.Validate(new GetTeamInvitationCandidatesQuery(1)).IsValid);
        Assert.True(validator.Validate(new GetTeamInvitationCandidatesQuery(1, " ")).IsValid);
        Assert.False(validator.Validate(new GetTeamInvitationCandidatesQuery(1, new string('a', 256))).IsValid);
    }

    private sealed class CandidateReader : ITeamInvitationCandidateReader
    {
        public bool Pending { get; init; }
        public TeamInvitationCandidateScope? Scope { get; private set; }
        public string? Search { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<PagedResult<TeamInvitationCandidate>> SearchAsync(TeamInvitationCandidateScope scope,
            string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            Scope = scope;
            Search = search;
            Token = cancellationToken;
            return Task.FromResult(new PagedResult<TeamInvitationCandidate>(
                [new(3, "Candidate", "student@test", "SE001", 10, "SE", "Software Engineering", Pending ? 9 : null,
                    Pending ? DateTime.SpecifyKind(Now.AddHours(1), DateTimeKind.Unspecified) : null)], page, pageSize, 8));
        }
    }
}
