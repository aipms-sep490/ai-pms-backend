using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Chat;
using AIPMS.Infrastructure.Identity;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests.Chat;

public sealed class ChatRealtimeTests(FinalSubmissionDraftDatabaseFixture fixture):IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    [Fact]
    public async Task Two_clients_receive_committed_hints_typing_presence_and_revocation()
    {
        var s=await fixture.Seed();await using var app=new Factory(fixture.ConnectionString);
        using var leader=app.CreateAuthenticatedClient(s.Users.Student);using var member=app.CreateAuthenticatedClient(s.MemberId);
        var opened=await leader.PostAsync($"/api/v1/chat/projects/{s.ProjectId}/conversation",null);opened.EnsureSuccessStatusCode();
        var conversation=(await opened.Content.ReadFromJsonAsync<ChatConversationDto>())!;
        using var a=await Connect(app,leader);using var b=await Connect(app,member);
        await Invoke(a,"WatchConversation",conversation.Id);await Event(a,"PresenceChanged");
        await Invoke(b,"WatchConversation",conversation.Id);await Event(b,"PresenceChanged");
        var sent=await leader.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages",new ChatSendRequest(Guid.NewGuid(),"Private text must not be in hub payload"));sent.EnsureSuccessStatusCode();
        var hint=await Event(b,"MessagesChanged");Assert.Equal(conversation.Id,hint.GetProperty("arguments")[0].GetProperty("conversationId").GetString());
        Assert.DoesNotContain("Private text",hint.GetRawText());
        await Invoke(a,"SetTyping",conversation.Id,true);
        var typing=await Event(b,"TypingChanged");Assert.True(typing.GetProperty("arguments")[0].GetProperty("isTyping").GetBoolean());
        await using(var db=fixture.CreateContext())
        {
            var row=db.TeamMembers.Single(m=>m.TeamId==s.TeamId&&m.UserId==s.MemberId);row.LeftAt=DateTime.UtcNow;await db.SaveChangesAsync();
        }
        await Invoke(b,"Ping");await Event(b,"AccessRevoked");
        Assert.Equal(HttpStatusCode.NotFound,(await member.GetAsync($"/api/v1/chat/conversations/{conversation.Id}/messages")).StatusCode);
        a.Abort();b.Abort();
    }

    [Fact]
    public async Task Hub_rejects_foreign_origin_and_does_not_accept_query_token_on_REST()
    {
        var s=await fixture.Seed();await using var app=new Factory(fixture.ConnectionString);using var client=app.CreateAuthenticatedClient(s.Users.Student);
        client.DefaultRequestHeaders.Add("Origin","https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync("/hubs/chat/negotiate?negotiateVersion=1",null)).StatusCode);
        using var anonymous=app.CreateClient();
        var token=client.DefaultRequestHeaders.Authorization!.Parameter;
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/chat/conversations?access_token="+token)).StatusCode);
    }

    [Fact]
    public async Task Password_change_invalidates_an_already_connected_socket()
    {
        var s=await fixture.Seed();await using var app=new Factory(fixture.ConnectionString);using var client=app.CreateAuthenticatedClient(s.Users.Student);
        using var socket=await Connect(app,client);
        await using(var db=fixture.CreateContext()) { (await db.Users.FindAsync(s.Users.Student))!.PasswordChangedAt=DateTime.UtcNow;await db.SaveChangesAsync(); }
        await Invoke(socket,"Ping");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer=new byte[4096];
        try
        {
            var result=await socket.ReceiveAsync(buffer,timeout.Token);
            Assert.True(result.MessageType==WebSocketMessageType.Close || Encoding.UTF8.GetString(buffer,0,result.Count).Contains("\"type\":7",StringComparison.Ordinal));
        }
        catch(WebSocketException) { }
    }

    private static async Task<WebSocket> Connect(Factory app,HttpClient client)
    {
        var ws=app.Server.CreateWebSocketClient();
        ws.ConfigureRequest=r=> {r.Headers["Origin"]="http://localhost:5173";r.Headers["Authorization"]=client.DefaultRequestHeaders.Authorization!.ToString();};
        var socket=await ws.ConnectAsync(new Uri("ws://localhost/hubs/chat"),default);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),WebSocketMessageType.Text,true,default);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer=new byte[4096];var result=await socket.ReceiveAsync(buffer,timeout.Token);
        Assert.StartsWith("{}",Encoding.UTF8.GetString(buffer,0,result.Count));return socket;
    }
    private static Task Invoke(WebSocket socket,string target,params object[] args)=>socket.SendAsync(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{type=1,target,arguments=args})+"\u001e"),WebSocketMessageType.Text,true,default);
    private static async Task<JsonElement> Event(WebSocket socket,string target)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));var buffer=new byte[16384];var text=new StringBuilder();
        while(true)
        {
            var result=await socket.ReceiveAsync(buffer,timeout.Token);
            if(result.MessageType==WebSocketMessageType.Close) throw new InvalidOperationException("Socket closed before "+target);
            text.Append(Encoding.UTF8.GetString(buffer,0,result.Count));if(!result.EndOfMessage) continue;
            var frames=text.ToString().Split('\u001e');text.Clear();text.Append(frames[^1]);
            foreach(var frame in frames[..^1])
            {
                using var json=JsonDocument.Parse(frame);
                if(json.RootElement.TryGetProperty("target",out var name)&&name.GetString()==target) return json.RootElement.Clone();
            }
        }
    }
    private sealed class Factory(string connection):AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                ["ConnectionStrings:DefaultConnection"]=connection,["Chat:Enabled"]="true",["Chat:RealtimeEnabled"]="true",["Chat:OutboxPollSeconds"]="1"}));
            builder.ConfigureServices(s=>{s.RemoveAll<IAccessTokenAccountValidator>();s.AddScoped<IAccessTokenAccountValidator,AccessTokenAccountValidator>();});
        }
    }
}
