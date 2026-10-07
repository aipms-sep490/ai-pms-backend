namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public sealed class ChatConversation
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public long? TeamId { get; set; }
    public long? ProjectId { get; set; }
    public long? FirstUserId { get; set; }
    public long? SecondUserId { get; set; }
    public string Status { get; set; } = "OPEN";
    public long Sequence { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
public sealed class ChatMembershipInterval
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public long UserId { get; set; }
    public long FromSequence { get; set; }
    public long? LeftSequence { get; set; }
    public string SourceKey { get; set; } = "";
    public bool Retained { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
}
public sealed class ChatMessage
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public long Sequence { get; set; }
    public long SenderId { get; set; }
    public Guid ClientMessageId { get; set; }
    public string RequestHash { get; set; } = "";
    public long? ReplyToMessageId { get; set; }
    public string? Body { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? RecalledAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
public sealed class ChatMemberState
{
    public long ConversationId { get; set; }
    public long UserId { get; set; }
    public long LastReadSequence { get; set; }
    public DateTime UpdatedAt { get; set; }
}
public sealed class ChatOutbox
{
    public long Id { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public long ConversationId { get; set; }
    public long Version { get; set; }
    public string EventType { get; set; } = "MessagesChanged";
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
}
public sealed class ChatScopeMember
{
    public string Kind { get; set; } = "";
    public long ScopeId { get; set; }
    public long UserId { get; set; }
    public string SourceKey { get; set; } = "";
}
