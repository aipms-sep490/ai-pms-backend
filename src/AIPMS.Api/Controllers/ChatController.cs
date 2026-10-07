using System.Globalization;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Chat;
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
    [HttpGet("recipients")]
    public Task<ChatPage<ChatPerson>> Contacts(string? search,string? cursor,int pageSize=30,CancellationToken ct=default)=>chat.ContactsAsync(Actor,search,cursor,pageSize,ct);
    [HttpGet("conversations")]
    public Task<ChatPage<ChatConversationDto>> Inbox(string? cursor,int pageSize=30,CancellationToken ct=default)=>chat.InboxAsync(Actor,cursor,pageSize,ct);
    [HttpPost("conversations/direct"),EnableRateLimiting("chat-write")]
    public Task<ChatConversationDto> Direct(ChatDirectRequest request,CancellationToken ct)=>chat.OpenAsync(Actor,"DIRECT",Id(request.RecipientUserId),ct);
    [HttpPost("teams/{teamId:long}/conversation"),EnableRateLimiting("chat-write")]
    public Task<ChatConversationDto> Team(long teamId,CancellationToken ct)=>chat.OpenAsync(Actor,"TEAM",teamId,ct);
    [HttpPost("projects/{projectId:long}/conversation"),EnableRateLimiting("chat-write")]
    public Task<ChatConversationDto> Project(long projectId,CancellationToken ct)=>chat.OpenAsync(Actor,"PROJECT",projectId,ct);
    [HttpGet("conversations/{id:long}")]
    public Task<ChatConversationDto> Detail(long id,CancellationToken ct)=>chat.DetailAsync(Actor,id,ct);
    [HttpGet("conversations/{id:long}/members")]
    public Task<ChatPage<ChatPerson>> Members(long id,string? cursor,int pageSize=50,CancellationToken ct=default)=>chat.MembersAsync(Actor,id,cursor,pageSize,ct);
    [HttpGet("conversations/{id:long}/messages")]
    public Task<ChatPage<ChatMessageDto>> Messages(long id,string? before,string? after,int pageSize=50,CancellationToken ct=default)=>chat.MessagesAsync(Actor,id,before,after,pageSize,ct);
    [HttpPost("conversations/{id:long}/messages"),EnableRateLimiting("chat-write")]
    public Task<ChatMessageDto> Send(long id,ChatSendRequest request,CancellationToken ct)=>chat.SendAsync(Actor,id,request,ct);
    [HttpPatch("conversations/{id:long}/messages/{messageId:long}"),EnableRateLimiting("chat-write")]
    public Task<ChatMessageDto> Edit(long id,long messageId,ChatEditRequest request,CancellationToken ct)=>chat.EditAsync(Actor,id,messageId,request,ct);
    [HttpPost("conversations/{id:long}/messages/{messageId:long}/recall"),EnableRateLimiting("chat-write")]
    public Task<ChatMessageDto> Recall(long id,long messageId,ChatRecallRequest request,CancellationToken ct)=>chat.RecallAsync(Actor,id,messageId,request,ct);
    [HttpPut("conversations/{id:long}/read")]
    public async Task<IActionResult> Read(long id,ChatReadRequest request,CancellationToken ct) { await chat.ReadAsync(Actor,id,Id(request.MessageId),ct);return NoContent(); }
}
