using System.Text.Json;
using System.Text.Json.Nodes;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.Queries;
using AIPMS.Application.Features.Teams.Services;
using AIPMS.Domain.Teams;

namespace AIPMS.UnitTests.Application.Teams;

public sealed partial class TeamHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Get_handler_preserves_member_json_contract()
    {
        var h = new Harness();
        var result = await new GetTeamQueryHandler(h.Workflow).Handle(new(1), default);
        var json = JsonSerializer.SerializeToNode(result.Members, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var expected = JsonNode.Parse("""
            [
              { "userId": 1, "fullName": "Leader", "majorId": 10, "organizationId": 1,
                "isEligibleStudent": true, "isLeader": true },
              { "userId": 2, "fullName": "SE member", "majorId": 10, "organizationId": 1,
                "isEligibleStudent": true, "isLeader": false }
            ]
            """);
        Assert.True(JsonNode.DeepEquals(expected, json), json?.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invitation_query_preserves_json_fields_nulls_and_paging(bool responded)
    {
        var h = new Harness();
        h.Repository.Invitations[1] = new(1, 1, 3, 1, "REJECTED",
            responded ? "Join us" : null, responded ? Now.AddHours(1) : null,
            responded ? Now : null, Now);
        var result = await new GetTeamInvitationsQueryHandler(h.Workflow).Handle(new(1, 1, 5), default);
        var json = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var expected = JsonNode.Parse("""
            { "items": [{ "id": 1, "teamId": 1, "invitedUserId": 3, "invitedBy": 1,
                "status": "REJECTED", "message": null, "expiresAt": null,
                "respondedAt": null, "createdAt": "2026-09-07T08:00:00Z" }],
              "page": 1, "pageSize": 5, "totalCount": 1, "totalPages": 1 }
            """)!;
        if (responded)
        {
            var invitation = expected["items"]![0]!;
            invitation["message"] = "Join us";
            invitation["expiresAt"] = "2026-09-07T09:00:00Z";
            invitation["respondedAt"] = "2026-09-07T08:00:00Z";
        }
        Assert.True(JsonNode.DeepEquals(expected, json), json?.ToJsonString());
    }

    [Fact]
    public async Task Create_handler_creates_forming_team_and_leader_in_transaction()
    {
        var h = new Harness();
        h.Repository.Team = null;
        var result = await new CreateTeamCommandHandler(h.Workflow).Handle(new(1, "team", " My Team ", null), default);
        Assert.Equal("FORMING", result.Status);
        Assert.Equal("TEAM", result.Code);
        Assert.Equal("My Team", result.Name);
        Assert.Equal(1, Assert.Single(result.Members).UserId);
        Assert.True(result.Members[0].IsLeader);
        Assert.Equal("TEAM_CREATED", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Create_handler_rejects_duplicate_membership()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateTeamCommandHandler(h.Workflow).Handle(new(1, "SECOND", "Second", null), default));
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Create_handler_fails_closed_without_policy()
    {
        var h = new Harness();
        h.Repository.Team = null;
        h.Policies.Policy = null;
        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateTeamCommandHandler(h.Workflow).Handle(new(1, "TEAM", "Team", null), default));
        Assert.Null(h.Repository.Team);
    }

    [Fact]
    public async Task Create_handler_rolls_back_on_audit_error()
    {
        var h = new Harness();
        h.Repository.Team = null;
        h.Audit.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CreateTeamCommandHandler(h.Workflow).Handle(new(1, "TEAM", "Team", null), default));
        Assert.Null(h.Repository.Team);
        Assert.Equal(1, h.Repository.Rollbacks);
    }

    [Fact]
    public async Task Invite_handler_enforces_leader_ownership()
    {
        var h = new Harness();
        h.Actor.UserId = 2;
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new InviteTeamMemberCommandHandler(h.Workflow).Handle(new(1, 3, null), default));
        Assert.Empty(h.Repository.Invitations);
    }

    [Fact]
    public async Task Invite_handler_caps_expiry_at_registration_deadline()
    {
        var h = new Harness();
        var result = await new InviteTeamMemberCommandHandler(h.Workflow).Handle(new(1, 3, null), default);
        Assert.Equal(h.Repository.Window!.EndAt, result.ExpiresAt);
    }

    [Fact]
    public async Task Invite_handler_rejects_different_major_before_creating_invitation()
    {
        var h = new Harness();
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new InviteTeamMemberCommandHandler(h.Workflow).Handle(new(1, 4, null), default));
        Assert.Equal("All team members must belong to the same major as the team leader.", error.Message);
        Assert.Empty(h.Repository.Invitations);
        Assert.Empty(h.Audit.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accept_handler_rechecks_current_major_of_recipient_and_leader(bool leaderChanged)
    {
        var h = new Harness();
        var invitation = await new InviteTeamMemberCommandHandler(h.Workflow).Handle(new(1, 3, null), default);
        h.Audit.Entries.Clear();
        if (leaderChanged)
            h.Repository.Team = h.Repository.Team! with
            {
                Members = h.Repository.Team!.Members.Select(m => m.IsLeader ? m with { MajorId = 20 } : m).ToArray()
            };
        else
            h.Repository.Students[3] = h.Repository.Students[3] with { MajorId = 20 };
        h.Actor.UserId = 3;
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new AcceptTeamInvitationCommandHandler(h.Workflow).Handle(new(invitation.Id), default));
        Assert.Equal("All team members must belong to the same major as the team leader.", error.Message);
        Assert.Equal("PENDING", h.Repository.Invitations[invitation.Id].Status);
        Assert.Equal(2, h.Repository.Team!.Members.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Accept_handler_rejects_legacy_cross_major_invitation()
    {
        var h = new Harness();
        h.Invite(4);
        h.Actor.UserId = 4;
        await Assert.ThrowsAsync<ConflictException>(() =>
            new AcceptTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default));
        Assert.Equal("PENDING", h.Repository.Invitations[1].Status);
        Assert.DoesNotContain(h.Repository.Team!.Members, m => m.UserId == 4);
    }

    [Fact]
    public async Task Transfer_handler_rejects_cross_major_member_in_legacy_roster()
    {
        var h = new Harness();
        h.Repository.Team = h.Repository.Team! with
        {
            Members = [h.Repository.Students[1] with { IsLeader = true }, h.Repository.Students[4]]
        };
        await Assert.ThrowsAsync<ConflictException>(() =>
            new TransferTeamLeaderCommandHandler(h.Workflow).Handle(new(1, 4), default));
        Assert.Equal(1, Assert.Single(h.Repository.Team!.Members, m => m.IsLeader).UserId);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Refresh_handler_marks_legacy_mixed_major_team_ineligible()
    {
        var h = new Harness();
        h.Repository.Team = h.Repository.Team! with
        {
            Members = [h.Repository.Students[1] with { IsLeader = true }, h.Repository.Students[4]]
        };
        var result = await new RefreshTeamEligibilityCommandHandler(h.Workflow).Handle(new(1), default);
        Assert.Equal("FORMING", result.Status);
        Assert.False(result.Eligibility.CanRegister);
        Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR", result.Eligibility.Reasons);
    }

    [Fact]
    public async Task Accept_handler_rejects_other_recipient_and_expired_invitation()
    {
        var h = new Harness();
        h.Invite(3);
        h.Actor.UserId = 2;
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new AcceptTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default));
        h.Actor.UserId = 3;
        h.Repository.Invitations[1] = h.Repository.Invitations[1] with { ExpiresAt = Now };
        await Assert.ThrowsAsync<ConflictException>(() =>
            new AcceptTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default));
        Assert.Equal("PENDING", h.Repository.Invitations[1].Status);
    }

    [Fact]
    public async Task Accept_handler_rechecks_capacity()
    {
        var h = new Harness();
        h.Policies.Policy = new(2, 2, 24, "v1");
        h.Invite(3);
        h.Actor.UserId = 3;
        await Assert.ThrowsAsync<ConflictException>(() =>
            new AcceptTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default));
        Assert.Equal(2, h.Repository.Team!.Members.Count);
    }

    [Fact]
    public async Task Accept_handler_rechecks_membership_in_another_team()
    {
        var h = new Harness();
        h.Invite(3);
        h.Actor.UserId = 3;
        h.Repository.OtherMemberships[3] = 99;
        await Assert.ThrowsAsync<ConflictException>(() =>
            new AcceptTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default));
        Assert.Equal("PENDING", h.Repository.Invitations[1].Status);
    }

    [Fact]
    public async Task Accept_handler_updates_membership_invitation_and_eligibility()
    {
        var h = new Harness();
        h.Repository.Team = h.Repository.Team! with { Status = "FORMING", Members = [h.Repository.Students[1] with { IsLeader = true }] };
        h.Invite(2);
        h.Actor.UserId = 2;
        var result = await new AcceptTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default);
        Assert.Equal("ELIGIBLE", result.Status);
        Assert.Equal("ACCEPTED", h.Repository.Invitations[1].Status);
        Assert.Equal(2, result.Members.Count);
    }

    [Fact]
    public async Task Transfer_handler_preserves_exactly_one_leader()
    {
        var h = new Harness();
        var result = await new TransferTeamLeaderCommandHandler(h.Workflow).Handle(new(1, 2), default);
        Assert.Equal(2, Assert.Single(result.Members, m => m.IsLeader).UserId);
        Assert.Equal("TEAM_LEADER_TRANSFERRED", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Transfer_handler_rejects_nonmember()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<ConflictException>(() =>
            new TransferTeamLeaderCommandHandler(h.Workflow).Handle(new(1, 3), default));
        Assert.Equal(1, Assert.Single(h.Repository.Team!.Members, m => m.IsLeader).UserId);
    }

    [Fact]
    public async Task Leader_cannot_leave_before_transfer()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<ConflictException>(() =>
            new RemoveTeamMemberCommandHandler(h.Workflow).Handle(new(1, null), default));
    }

    [Fact]
    public async Task Remove_handler_downgrades_eligibility_and_revokes_access()
    {
        var h = new Harness();
        await new RemoveTeamMemberCommandHandler(h.Workflow).Handle(new(1, 2), default);
        Assert.Equal("FORMING", h.Repository.Team!.Status);
        h.Actor.UserId = 2;
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetTeamQueryHandler(h.Workflow).Handle(new(1), default));
    }

    [Theory]
    [InlineData("SUBMITTED")]
    [InlineData("ACTIVE")]
    public async Task Project_state_prevents_roster_mutations(string state)
    {
        var h = new Harness();
        h.Repository.Team = h.Repository.Team! with { ProjectStatuses = [state] };
        await Assert.ThrowsAsync<ConflictException>(() =>
            new RemoveTeamMemberCommandHandler(h.Workflow).Handle(new(1, 2), default));
        Assert.Equal(2, h.Repository.Team!.Members.Count);
    }

    [Fact]
    public async Task Reject_and_cancel_handlers_persist_terminal_invitation_states()
    {
        var h = new Harness();
        h.Invite(3);
        h.Actor.UserId = 3;
        await new RejectTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default);
        Assert.Equal("REJECTED", h.Repository.Invitations[1].Status);
        h.Invite(3);
        h.Actor.UserId = 1;
        await new CancelTeamInvitationCommandHandler(h.Workflow).Handle(new(1), default);
        Assert.Equal("CANCELLED", h.Repository.Invitations[1].Status);
    }

    [Fact]
    public async Task Registration_guard_checks_fresh_members_inside_transaction()
    {
        var h = new Harness();
        h.Repository.Team = h.Repository.Team! with
        {
            Members = [h.Repository.Students[1] with { IsLeader = true }, h.Repository.Students[2] with { IsEligibleStudent = false }]
        };
        var guard = new TeamRegistrationGuard(h.Repository, h.Workflow);
        await Assert.ThrowsAsync<ConflictException>(() => guard.InTransactionAsync(async ct =>
        {
            await guard.ValidateAsync(1, ct);
            return true;
        }, default));
        Assert.Equal(1, h.Repository.Rollbacks);
    }

    private sealed class Harness
    {
        public FakeTeamRepository Repository { get; } = new();
        public FakeActor Actor { get; } = new();
        public FakePolicies Policies { get; } = new();
        public FakeAudit Audit { get; }
        public TeamWorkflow Workflow { get; }
        public Harness()
        {
            Audit = new FakeAudit(Repository);
            Workflow = new TeamWorkflow(Repository, Policies, Actor, Audit, new Clock());
        }
        public void Invite(long userId) => Repository.Invitations[1] =
            new(1, 1, userId, 1, "PENDING", null, Now.AddHours(1), null, Now);
    }

    private sealed class FakeActor : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId { get; set; } = 1;
        public string? Email => "student@test";
        public string? FullName => "Student";
        public IReadOnlyCollection<string> Roles => ["STUDENT"];
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class FakePolicies : ITeamFormationPolicyProvider
    {
        public TeamFormationPolicy? Policy { get; set; } = new(2, 3, 24, "v1");
        public Task<TeamFormationPolicy?> GetAsync(long id, CancellationToken ct) => Task.FromResult(Policy);
    }
    private sealed class FakeAudit(FakeTeamRepository repository) : IAuditTrail
    {
        public bool Fail { get; set; }
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Assert.True(repository.InTransaction);
            if (Fail) throw new InvalidOperationException("Audit unavailable");
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTeamRepository : ITeamRepository
    {
        public Dictionary<long, TeamParticipant> Students { get; } = new()
        {
            [1] = new(1, "Leader", 10, 1, true, false),
            [2] = new(2, "SE member", 10, 1, true, false),
            [3] = new(3, "SE invitee", 10, 1, true, false),
            [4] = new(4, "IS invitee", 20, 1, true, false)
        };
        public TeamSnapshot? Team { get; set; }
        public Dictionary<long, TeamInvitationData> Invitations { get; private set; } = [];
        public Dictionary<long, long> OtherMemberships { get; } = [];
        public TeamRegistrationWindow? Window { get; set; } = new(10, 1, 1, Now.AddHours(2));
        public bool InTransaction { get; private set; }
        public int Rollbacks { get; private set; }
        public FakeTeamRepository() =>
            Team = new(1, 1, "TEAM", "Team", null, "ELIGIBLE", [Students[1] with { IsLeader = true }, Students[2]], []);

        public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            var before = Team;
            var invitations = new Dictionary<long, TeamInvitationData>(Invitations);
            Assert.False(InTransaction);
            InTransaction = true;
            try { return await action(ct); }
            catch { Team = before; Invitations = invitations; Rollbacks++; throw; }
            finally { InTransaction = false; }
        }
        public Task<TeamSnapshot?> GetAsync(long id, CancellationToken ct) => Task.FromResult(Team?.Id == id ? Team : null);
        public Task<TeamParticipant?> GetStudentAsync(long id, CancellationToken ct) => Task.FromResult(Students.GetValueOrDefault(id));
        public Task<TeamRegistrationWindow?> GetOpenWindowAsync(long semester, DateTime now, CancellationToken ct) => Task.FromResult(Window);
        public Task<long?> GetCurrentTeamIdAsync(long semester, long user, CancellationToken ct) =>
            Task.FromResult(OtherMemberships.TryGetValue(user, out var other) ? (long?)other
                : Team?.SemesterId == semester && Team.Members.Any(m => m.UserId == user) ? Team.Id : (long?)null);
        public Task<long> CreateAsync(long semester, string code, string name, string? description, long actor, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Team = new(1, semester, code, name, description, "FORMING", [], []);
            return Task.FromResult(1L);
        }
        public Task UpdateAsync(long id, string name, string? description, string status, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Team = Team! with { Name = name, Description = description, Status = status };
            return Task.CompletedTask;
        }
        public Task AddMemberAsync(long id, long semester, long user, bool leader, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Team = Team! with { Members = [.. Team!.Members, Students[user] with { IsLeader = leader }] };
            return Task.CompletedTask;
        }
        public Task RemoveMemberAsync(long id, long user, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Team = Team! with { Members = Team!.Members.Where(m => m.UserId != user).ToArray() };
            return Task.CompletedTask;
        }
        public Task TransferLeaderAsync(long id, long oldLeader, long next, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Team = Team! with { Members = Team!.Members.Select(m => m with { IsLeader = m.UserId == next }).ToArray() };
            return Task.CompletedTask;
        }
        public Task<TeamInvitationData?> GetInvitationAsync(long id, CancellationToken ct) => Task.FromResult(Invitations.GetValueOrDefault(id));
        public Task<TeamInvitationData?> GetPendingInvitationAsync(long team, long user, CancellationToken ct) =>
            Task.FromResult(Invitations.Values.SingleOrDefault(i => i.TeamId == team && i.InvitedUserId == user && i.Status == "PENDING"));
        public Task<TeamInvitationData> InviteAsync(long team, long user, long actor, string? message, DateTime expiry, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            var invitation = new TeamInvitationData(Invitations.Count + 1, team, user, actor, "PENDING", message, expiry, null, now);
            Invitations[invitation.Id] = invitation;
            return Task.FromResult(invitation);
        }
        public Task RespondAsync(long id, string status, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Invitations[id] = Invitations[id] with { Status = status, RespondedAt = now };
            return Task.CompletedTask;
        }
        public Task<PagedResult<TeamInvitationData>> GetInvitationsAsync(long user, long? team, int page, int size, CancellationToken ct)
        {
            var invitations = Invitations.Values.Where(i => team.HasValue ? i.TeamId == team : i.InvitedUserId == user).ToArray();
            return Task.FromResult(new PagedResult<TeamInvitationData>(invitations.Skip((page - 1) * size).Take(size).ToArray(), page, size, invitations.Length));
        }
    }
}
