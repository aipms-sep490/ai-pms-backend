using System.Globalization;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Chat.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIPMS.Api.Controllers;

[ApiController,Authorize,Route("api/v1/chat"),EnableRateLimiting("chat-read")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(429)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class ChatController(IChatService chat,ICurrentUser user) : ControllerBase
{
    private long Actor=>user.UserId??throw new UnauthorizedException("Authentication required.");
    private static long Id(string s)=>long.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0?id:throw new ArgumentException("CHAT_INVALID_ID");
    [HttpGet("recipients"), ProducesResponseType<ChatPage<ChatPerson>>(200)]
    public Task<ChatPage<ChatPerson>> Contacts(string? search,string? cursor,int pageSize=30,CancellationToken ct=default)=>chat.ContactsAsync(Actor,search,cursor,pageSize,ct);
    [HttpGet("conversations"), ProducesResponseType<ChatPage<ChatConversationDto>>(200)]
    public Task<ChatPage<ChatConversationDto>> Inbox(string? cursor,int pageSize=30,CancellationToken ct=default)=>chat.InboxAsync(Actor,cursor,pageSize,ct);
    [HttpPost("conversations/direct"),EnableRateLimiting("chat-write"), ProducesResponseType<ChatConversationDto>(200)]
    public Task<ChatConversationDto> Direct(ChatDirectRequest request,CancellationToken ct)=>chat.OpenAsync(Actor,"DIRECT",Id(request.RecipientUserId),ct);
    [HttpPost("teams/{teamId:long}/conversation"),EnableRateLimiting("chat-write"), ProducesResponseType<ChatConversationDto>(200)]
    public Task<ChatConversationDto> Team(long teamId,CancellationToken ct)=>chat.OpenAsync(Actor,"TEAM",teamId,ct);
    [HttpPost("projects/{projectId:long}/conversation"),EnableRateLimiting("chat-write"), ProducesResponseType<ChatConversationDto>(200)]
    public Task<ChatConversationDto> Project(long projectId,CancellationToken ct)=>chat.OpenAsync(Actor,"PROJECT",projectId,ct);
    [HttpGet("conversations/{id:long}"), ProducesResponseType<ChatConversationDto>(200)]
    public Task<ChatConversationDto> Detail(long id,CancellationToken ct)=>chat.DetailAsync(Actor,id,ct);
    [HttpGet("conversations/{id:long}/members"), ProducesResponseType<ChatPage<ChatPerson>>(200)]
    public Task<ChatPage<ChatPerson>> Members(long id,string? cursor,int pageSize=50,CancellationToken ct=default)=>chat.MembersAsync(Actor,id,cursor,pageSize,ct);
    [HttpGet("conversations/{id:long}/messages"), ProducesResponseType<ChatPage<ChatMessageDto>>(200)]
    public Task<ChatPage<ChatMessageDto>> Messages(long id,string? before,string? after,int pageSize=50,CancellationToken ct=default)=>chat.MessagesAsync(Actor,id,before,after,pageSize,ct);
    [HttpPost("conversations/{id:long}/messages"),EnableRateLimiting("chat-write"), ProducesResponseType<ChatMessageDto>(200)]
    public Task<ChatMessageDto> Send(long id,ChatSendRequest request,CancellationToken ct)=>chat.SendAsync(Actor,id,request,ct);
    [HttpPatch("conversations/{id:long}/messages/{messageId:long}"),EnableRateLimiting("chat-write"), ProducesResponseType<ChatMessageDto>(200)]
    public Task<ChatMessageDto> Edit(long id,long messageId,ChatEditRequest request,CancellationToken ct)=>chat.EditAsync(Actor,id,messageId,request,ct);
    [HttpPost("conversations/{id:long}/messages/{messageId:long}/recall"),EnableRateLimiting("chat-write"), ProducesResponseType<ChatMessageDto>(200)]
    public Task<ChatMessageDto> Recall(long id,long messageId,ChatRecallRequest request,CancellationToken ct)=>chat.RecallAsync(Actor,id,messageId,request,ct);
    [HttpPut("conversations/{id:long}/read"), ProducesResponseType(204)]
    public async Task<IActionResult> Read(long id,ChatReadRequest request,CancellationToken ct) { await chat.ReadAsync(Actor,id,Id(request.MessageId),ct);return NoContent(); }

    [HttpPost("conversations/{id:long}/uploads"), EnableRateLimiting("chat-write")]
    [Consumes("multipart/form-data"), RequestSizeLimit(11 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    [ProducesResponseType<ChatAttachmentDto>(200), ProducesResponseType<ProblemDetails>(413)]
    public async Task<ChatAttachmentDto> Upload(long id, [FromForm] ChatUploadForm input, CancellationToken ct)
    {
        await using var stream = input.File.OpenReadStream();
        return await chat.UploadAsync(Actor, id, new(input.File.FileName, input.File.ContentType, input.File.Length, stream), ct);
    }
    [HttpGet("conversations/{id:long}/messages/{messageId:long}/attachments/{attachmentId:long}/download")]
    [ProducesResponseType(typeof(byte[]), 200)]
    public async Task<IActionResult> Download(long id, long messageId, long attachmentId, CancellationToken ct)
    {
        var file = await chat.DownloadAsync(Actor, id, messageId, attachmentId, ct);
        Response.Headers.CacheControl = "no-store"; Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
    [HttpPut("conversations/{id:long}/messages/{messageId:long}/reactions"), EnableRateLimiting("chat-write"), ProducesResponseType<ChatMessageDto>(200)]
    public Task<ChatMessageDto> React(long id, long messageId, ChatReactionRequest input, CancellationToken ct) => chat.ReactAsync(Actor, id, messageId, input.Emoji, true, ct);
    [HttpDelete("conversations/{id:long}/messages/{messageId:long}/reactions"), EnableRateLimiting("chat-write"), ProducesResponseType<ChatMessageDto>(200)]
    public Task<ChatMessageDto> Unreact(long id, long messageId, [FromQuery] string emoji, CancellationToken ct) => chat.ReactAsync(Actor, id, messageId, emoji, false, ct);
    [HttpPut("conversations/{id:long}/mute"), EnableRateLimiting("chat-write"), ProducesResponseType<ChatConversationDto>(200)]
    public Task<ChatConversationDto> Mute(long id, ChatMuteRequest input, CancellationToken ct) => chat.MuteAsync(Actor, id, input, ct);
}

public sealed class ChatUploadForm
{
    [System.ComponentModel.DataAnnotations.Required] public IFormFile File { get; init; } = null!;
}
