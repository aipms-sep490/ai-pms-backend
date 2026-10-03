namespace AIPMS.Application.Features.Meetings.Video;

public sealed record VideoCapabilities(bool CanStart, bool CanJoin, bool CanEnd, bool CanRecord);
public sealed record VideoJoinWindow(DateTime? AvailableFrom, DateTime? AvailableUntil);
public sealed record VideoSessionDto(long MeetingId, string MeetingStatus, string MeetingDeliveryMode, string VideoChannel, VideoSessionStateDto? Session, VideoCapabilities Capabilities, VideoJoinWindow? JoinWindow, IReadOnlyList<string> DenialReasons);
public sealed record VideoSessionStateDto(long Id, string Status, string Provider, DateTime? StartedAt, DateTime? EndedAt, int? ActiveParticipantCount, string ConcurrencyToken);
public sealed record VideoJoinCredentialDto(string Provider, string ServerUrl, string ParticipantIdentity, string ParticipantName, string AccessToken, DateTime ExpiresAt, VideoJoinCapabilities Capabilities);
public sealed record VideoJoinCapabilities(bool PublishAudio, bool PublishVideo, bool ScreenShare, bool Moderator);
public sealed record VideoPresenceParticipantDto(long UserId, DateTime? FirstJoinedAt, DateTime? LastLeftAt, long TotalConnectedSeconds, int ConnectionCount);
public sealed record VideoPresenceDto(long MeetingId, IReadOnlyList<VideoPresenceParticipantDto> Participants);
public sealed record VideoProviderRoomRequest(string RoomKey, long MeetingId);
public sealed record VideoProviderJoinRequest(string RoomKey, string ParticipantIdentity, string ParticipantName, bool Moderator, DateTime ExpiresAt);
public sealed record VideoProviderRoomSnapshot(string RoomKey, int? ActiveParticipantCount);


