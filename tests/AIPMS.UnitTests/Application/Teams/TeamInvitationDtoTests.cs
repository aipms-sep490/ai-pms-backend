using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Models;

namespace AIPMS.UnitTests.Application.Teams;

public sealed class TeamInvitationDtoTests
{
    [Fact]
    public void ToDto_MapsTeamAndUserNamesCorrectly()
    {
        var now = DateTime.UtcNow;
        var data = new TeamInvitationData(
            Id: 1,
            TeamId: 10,
            InvitedUserId: 20,
            InvitedBy: 30,
            Status: "PENDING",
            Message: "Join our team!",
            ExpiresAt: now.AddDays(3),
            RespondedAt: null,
            CreatedAt: now,
            TeamName: "Awesome Capstone Team",
            InvitedByName: "Alice Leader",
            InvitedUserName: "Bob Member");

        var dto = data.ToDto();

        Assert.Equal(1, dto.Id);
        Assert.Equal(10, dto.TeamId);
        Assert.Equal(20, dto.InvitedUserId);
        Assert.Equal(30, dto.InvitedBy);
        Assert.Equal("PENDING", dto.Status);
        Assert.Equal("Join our team!", dto.Message);
        Assert.Equal("Awesome Capstone Team", dto.TeamName);
        Assert.Equal("Alice Leader", dto.InvitedByName);
        Assert.Equal("Bob Member", dto.InvitedUserName);
    }

    [Fact]
    public void ToDto_WithNullNames_DefaultsToNull()
    {
        var now = DateTime.UtcNow;
        var data = new TeamInvitationData(
            1, 10, 20, 30, "PENDING", null, null, null, now);

        var dto = data.ToDto();

        Assert.Null(dto.TeamName);
        Assert.Null(dto.InvitedByName);
        Assert.Null(dto.InvitedUserName);
    }
}
