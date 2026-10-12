using System.ComponentModel.DataAnnotations;

namespace AIPMS.Application.Features.Chat;

public sealed class ChatSettings
{
    public bool Enabled { get; set; }
    public bool RealtimeEnabled { get; set; }
    [Range(1,4000)] public int MaxMessageLength { get; set; } = 4000;
    [Range(1,60)] public int EditWindowMinutes { get; set; } = 15;
    [Range(1,60)] public int RecallWindowMinutes { get; set; } = 15;
    [Range(1,30)] public int OutboxPollSeconds { get; set; } = 5;
    [Range(1,300)] public int SendsPerMinute { get; set; } = 30;
    [Range(1,10)] public int MaxConnectionsPerUser { get; set; } = 5;
    [Range(1,1)] public int InstanceCount { get; set; } = 1;
}
public sealed record ChatPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
public sealed record ChatPerson(string UserId, string FullName, string? LastReadSequence = null);
public sealed record ChatConversationDto(string Id, string Kind, string Title, string Status, string? TeamId, string? ProjectId,
    string Sequence, string Version, DateTime UpdatedAt, long UnreadCount, ChatMessageDto? LastMessage, bool CanSend, DateTime? MutedUntil = null);
public sealed record ChatReply(string Id, string? Body, bool Unavailable);
public sealed record ChatMessageDto(string Id, string ConversationId, string Sequence, string SenderId, string SenderName,
    Guid ClientMessageId, string? Body, DateTime CreatedAt, DateTime? EditedAt, DateTime? RecalledAt,
    Guid ConcurrencyToken, ChatReply? Reply, bool CanEdit, bool CanRecall,
    IReadOnlyList<ChatAttachmentDto>? Attachments = null, IReadOnlyList<ChatReactionDto>? Reactions = null);
public sealed record ChatSendRequest(Guid ClientMessageId, string? Body, string? ReplyToMessageId = null, IReadOnlyList<string>? AttachmentIds = null);
public sealed record ChatEditRequest(string Body, Guid ConcurrencyToken);
public sealed record ChatRecallRequest(Guid ConcurrencyToken);
public sealed record ChatDirectRequest(string RecipientUserId);
public sealed record ChatReadRequest(string MessageId);
public sealed record ChatEvent(Guid EventId, string ConversationId, string Version, int SchemaVersion = 1, bool ShouldNotify = false);
public sealed record ChatDispatch(long Id, Guid Lease, ChatEvent Event, string EventType, long? MessageId = null);
public sealed record ChatAttachmentDto(string Id, string FileName, string ContentType, long SizeBytes, string Sha256);
public sealed record ChatReactionDto(string Emoji, int Count, bool ReactedByMe);
public sealed record ChatReactionRequest(string Emoji);
public sealed record ChatMuteRequest(DateTimeOffset? MutedUntil);
