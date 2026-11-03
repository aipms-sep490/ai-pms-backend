using System.Text.Json;
using AIPMS.Infrastructure.Services;

namespace AIPMS.UnitTests.AI;

public sealed class AssistantMeetingDraftParserTests
{
    [Fact]
    public void Converts_explicit_Vietnam_time_and_keeps_invitation_intent_without_ids()
    {
        using var json = JsonDocument.Parse("""{"meeting":{"startAt":"2026-11-04T12:00:00+07:00","meetingDeliveryMode":"REMOTE","videoChannel":"IN_APP_VIDEO","inviteAll":true,"participantUserIds":[999]}}""");
        var result = AssistantMeetingDraftParser.Parse(json.RootElement)!;
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 5, 0, 0, TimeSpan.Zero), result.StartAt);
        Assert.Null(result.EndAt);
        Assert.Equal("REMOTE", result.MeetingDeliveryMode);
        Assert.Equal("IN_APP_VIDEO", result.VideoChannel);
        Assert.True(result.InviteAll);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"meeting\":null}")]
    [InlineData("{\"meeting\":[]}")]
    public void Old_or_non_object_drafts_do_not_invent_fields(string input)
    {
        using var json = JsonDocument.Parse(input);
        Assert.Null(AssistantMeetingDraftParser.Parse(json.RootElement));
    }

    [Fact]
    public void Invalid_types_dates_modes_and_unsafe_links_are_ignored()
    {
        using var json = JsonDocument.Parse("""{"meeting":{"startAt":"2026-02-30T12:00:00+07:00","endAt":"2026-11-04T12:00:00","meetingDeliveryMode":"MYSTERY","videoChannel":"EXTERNAL_LINK","onlineUrl":"https://user:secret@example.com","inviteAll":"true","location":123}}""");
        var result = AssistantMeetingDraftParser.Parse(json.RootElement)!;
        Assert.Null(result.StartAt); Assert.Null(result.EndAt); Assert.Null(result.MeetingDeliveryMode);
        Assert.Null(result.OnlineUrl); Assert.Null(result.Location); Assert.False(result.InviteAll);
    }

    [Fact]
    public void Drops_end_before_start_and_incompatible_onsite_channel()
    {
        using var json = JsonDocument.Parse("""{"meeting":{"startAt":"2026-11-04T12:00:00+07:00","endAt":"2026-11-04T11:00:00+07:00","meetingDeliveryMode":"ONSITE","videoChannel":"IN_APP_VIDEO","location":"Room 1"}}""");
        var result = AssistantMeetingDraftParser.Parse(json.RootElement)!;
        Assert.Null(result.EndAt); Assert.Equal("NONE", result.VideoChannel); Assert.Equal("Room 1", result.Location);
    }
}
