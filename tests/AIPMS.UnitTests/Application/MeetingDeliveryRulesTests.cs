using AIPMS.Application.Features.Meetings.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class MeetingDeliveryRulesTests
{
    [Fact]
    public void LegacyRequestsWithoutVideoMetadataRemainValid() =>
        Assert.True(MeetingDeliveryRules.IsValid(null, null, null, null));

    [Fact]
    public void ExternalLinkRejectsCredentialsInUrl() =>
        Assert.False(MeetingDeliveryRules.IsValid("REMOTE", "EXTERNAL_LINK", null, "https://user:pass@example.test/room"));

    [Theory]
    [InlineData("ONSITE", "NONE", "Room 1", null, true)]
    [InlineData("REMOTE", "EXTERNAL_LINK", null, "https://meet.example.test/room", true)]
    [InlineData("HYBRID", "IN_APP_VIDEO", "Room 1", null, true)]
    [InlineData("ONSITE", "IN_APP_VIDEO", "Room 1", null, false)]
    [InlineData("REMOTE", "EXTERNAL_LINK", null, "http://meet.example.test/room", false)]
    [InlineData("HYBRID", "NONE", "Room 1", "https://meet.example.test/room", false)]
    public void DeliveryCombinationsAreValidated(string mode, string channel, string? location, string? url, bool expected) =>
        Assert.Equal(expected, MeetingDeliveryRules.IsValid(mode, channel, location, url));
}
