namespace AIPMS.Application.Features.Chat.Abstractions;

using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.DTOs;

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
    Task<ChatAttachmentDto> UploadAsync(long actor, long id, UploadContent upload, CancellationToken ct);
    Task<FileDownload> DownloadAsync(long actor, long id, long messageId, long attachmentId, CancellationToken ct);
    Task<ChatMessageDto> ReactAsync(long actor, long id, long messageId, string emoji, bool add, CancellationToken ct);
    Task<ChatConversationDto> MuteAsync(long actor, long id, ChatMuteRequest request, CancellationToken ct);
}
public interface IChatUploadCleanup { Task<int> SweepAsync(CancellationToken ct); }
public interface IChatNotificationPolicy { Task<IReadOnlySet<long>> NotifyAsync(long conversation, long messageId, CancellationToken ct); }
public interface IChatWakeSignal { void Pulse(); Task WaitAsync(TimeSpan timeout, CancellationToken ct); }
public interface IChatCredentialGuard { Task CheckAsync(long actor, CancellationToken ct); }

public interface IChatOutbox
{
    Task<ChatDispatch?> ClaimAsync(CancellationToken ct);
    Task CompleteAsync(ChatDispatch dispatch, bool success, CancellationToken ct);
}
