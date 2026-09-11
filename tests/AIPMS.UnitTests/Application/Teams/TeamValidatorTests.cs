using AIPMS.Application.Features.Teams.Commands;
using AIPMS.Application.Features.Teams.Queries;
using AIPMS.Application.Features.Teams.Validators;
using AIPMS.Infrastructure.Services.Teams;
using Microsoft.Extensions.Configuration;

namespace AIPMS.UnitTests.Application.Teams;

public sealed class TeamValidatorTests
{
    [Theory]
    [InlineData(0, "TEAM", "Name")]
    [InlineData(1, "", "Name")]
    [InlineData(1, "Bad Code", "Name")]
    [InlineData(1, "TEAM", " ")]
    public void Create_rejects_invalid_fields(long semesterId, string code, string name) =>
        Assert.False(new CreateTeamCommandValidator().Validate(new CreateTeamCommand(semesterId, code, name, null)).IsValid);

    [Fact]
    public void Create_accepts_valid_request() =>
        Assert.True(new CreateTeamCommandValidator().Validate(new CreateTeamCommand(1, "TEAM_01", "Team One", null)).IsValid);

    [Fact]
    public void Text_limits_match_database()
    {
        Assert.False(new CreateTeamCommandValidator().Validate(new CreateTeamCommand(1, new string('a', 51), "Team", null)).IsValid);
        Assert.False(new UpdateTeamCommandValidator().Validate(new UpdateTeamCommand(1, new string('a', 256), null)).IsValid);
        Assert.False(new InviteTeamMemberCommandValidator().Validate(new InviteTeamMemberCommand(1, 2, new string('a', 1001))).IsValid);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(1000001, 20)]
    public void Pagination_is_bounded(int page, int size) =>
        Assert.False(new GetTeamInvitationsQueryValidator().Validate(new GetTeamInvitationsQuery(null, page, size)).IsValid);

    [Fact]
    public void All_identifiers_are_positive()
    {
        Assert.False(new UpdateTeamCommandValidator().Validate(new UpdateTeamCommand(0, "Name", null)).IsValid);
        Assert.False(new TransferTeamLeaderCommandValidator().Validate(new TransferTeamLeaderCommand(1, 0)).IsValid);
        Assert.False(new InviteTeamMemberCommandValidator().Validate(new InviteTeamMemberCommand(1, -1, null)).IsValid);
        Assert.False(new RemoveTeamMemberCommandValidator().Validate(new RemoveTeamMemberCommand(1, 0)).IsValid);
        Assert.False(new RefreshTeamEligibilityCommandValidator().Validate(new RefreshTeamEligibilityCommand(0)).IsValid);
        Assert.False(new AcceptTeamInvitationCommandValidator().Validate(new AcceptTeamInvitationCommand(0)).IsValid);
        Assert.False(new RejectTeamInvitationCommandValidator().Validate(new RejectTeamInvitationCommand(0)).IsValid);
        Assert.False(new CancelTeamInvitationCommandValidator().Validate(new CancelTeamInvitationCommand(0)).IsValid);
        Assert.False(new GetTeamQueryValidator().Validate(new GetTeamQuery(0)).IsValid);
        Assert.False(new GetCurrentTeamQueryValidator().Validate(new GetCurrentTeamQuery(0)).IsValid);
    }

    [Fact]
    public async Task Policy_adapter_requires_explicit_complete_period_configuration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TeamFormation:Periods:10:MinMembers"] = "2",
            ["TeamFormation:Periods:10:MaxMembers"] = "5",
            ["TeamFormation:Periods:10:InvitationHours"] = "24",
            ["TeamFormation:Periods:10:Version"] = "review-1"
        }).Build();
        var adapter = new ConfiguredTeamFormationPolicyProvider(config);
        Assert.Equal("review-1", (await adapter.GetAsync(10, default))?.Version);
        Assert.Null(await adapter.GetAsync(11, default));
        config["TeamFormation:Periods:10:MinMembers"] = "invalid";
        Assert.Null(await adapter.GetAsync(10, default));
    }
}
