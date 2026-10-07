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
    string Sequence, string Version, DateTime UpdatedAt, long UnreadCount, ChatMessageDto? LastMessage, bool CanSend);
public sealed record ChatReply(string Id, string? Body, bool Unavailable);
public sealed record ChatMessageDto(string Id, string ConversationId, string Sequence, string SenderId, string SenderName,
    Guid ClientMessageId, string? Body, DateTime CreatedAt, DateTime? EditedAt, DateTime? RecalledAt,
    Guid ConcurrencyToken, ChatReply? Reply, bool CanEdit, bool CanRecall);
public sealed record ChatSendRequest(Guid ClientMessageId, string Body, string? ReplyToMessageId = null);
public sealed record ChatEditRequest(string Body, Guid ConcurrencyToken);
public sealed record ChatRecallRequest(Guid ConcurrencyToken);
public sealed record ChatDirectRequest(string RecipientUserId);
public sealed record ChatReadRequest(string MessageId);
public sealed record ChatEvent(Guid EventId, string ConversationId, string Version, int SchemaVersion = 1);
public interface IChatAccessService
{
    Task<bool> CanAccessAsync(long userId, long conversationId, CancellationToken ct);
    Task<IReadOnlyList<long>> RecipientsAsync(long conversationId, CancellationToken ct);
}
public interface IChatService
{
    Task<ChatPage<ChatPerson>> ContactsAsync(long actor, string? search, string? cursor, int size, CancellationToken ct);
    Task<ChatConversationDto> OpenAsync(long actor, string kind, long target, CancellationToken ct);
    Task<ChatPage<ChatConversationDto>> InboxAsync(long actor, string? cursor, int size, CancellationToken ct);
    Task<ChatConversationDto> DetailAsync(long actor, long id, CancellationToken ct);
    Task<ChatPage<ChatPerson>> MembersAsync(long actor, long id, string? cursor, int size, CancellationToken ct);
    Task<ChatPage<ChatMessageDto>> MessagesAsync(long actor, long id, string? before, string? after, int size, CancellationToken ct);
    Task<ChatMessageDto> SendAsync(long actor, long id, ChatSendRequest request, CancellationToken ct);
    Task<ChatMessageDto> EditAsync(long actor, long id, long messageId, ChatEditRequest request, CancellationToken ct);
    Task<ChatMessageDto> RecallAsync(long actor, long id, long messageId, ChatRecallRequest request, CancellationToken ct);
    Task ReadAsync(long actor, long id, long messageId, CancellationToken ct);
}
public interface IChatWakeSignal { void Pulse(); Task WaitAsync(TimeSpan timeout, CancellationToken ct); }
public interface IChatCredentialGuard { Task CheckAsync(long actor, CancellationToken ct); }
public sealed record ChatDispatch(long Id, Guid Lease, ChatEvent Event, string EventType);
public interface IChatOutbox
{
    Task<ChatDispatch?> ClaimAsync(CancellationToken ct);
    Task CompleteAsync(ChatDispatch dispatch, bool success, CancellationToken ct);
}
