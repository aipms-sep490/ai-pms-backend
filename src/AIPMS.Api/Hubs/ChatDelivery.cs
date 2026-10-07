using System.Globalization;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Chat.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace AIPMS.Api.Hubs;

public sealed class ChatDelivery(ChatConnections connections,IHubContext<ChatHub> hub,IChatAccessService access,IAccessTokenAccountValidator credentials)
{
    private async Task<ChatConnection[]> Authorized(long conversation,CancellationToken ct)
    {
        var users=await access.RecipientsAsync(conversation,ct);
        var valid=new List<ChatConnection>();var checkedCredentials=new Dictionary<(long,long),bool>();
        foreach(var c in connections.Snapshot().Where(c=>users.Contains(c.UserId)))
        {
            var key=(c.UserId,c.PasswordTicks);
            if(!checkedCredentials.TryGetValue(key,out var ok))
                checkedCredentials[key]=ok=await credentials.IsValidAsync(c.UserId,c.PasswordTicks==0?null:new DateTime(c.PasswordTicks,DateTimeKind.Utc),ct);
            if(ok) valid.Add(c);else { connections.Remove(c.Id);c.Abort(); }
        }
        return valid.ToArray();
    }
    public async Task SendAsync(long conversation,string type,object payload,CancellationToken ct)
    {
        var recipients=await Authorized(conversation,ct);
        if(recipients.Length>0) await hub.Clients.Clients(recipients.Select(c=>c.Id)).SendAsync(type,payload,ct);
    }
    public async Task PresenceAsync(long conversation,CancellationToken ct)
    {
        var recipients=await Authorized(conversation,ct);
        if(recipients.Length>0) await hub.Clients.Clients(recipients.Where(c=>c.Watching==conversation).Select(c=>c.Id))
            .SendAsync("PresenceChanged",new {schemaVersion=1,conversationId=conversation.ToString(CultureInfo.InvariantCulture),
                onlineUserIds=recipients.Select(c=>c.UserId.ToString(CultureInfo.InvariantCulture)).Distinct().ToArray()},ct);
    }
}
