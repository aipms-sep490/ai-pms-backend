using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AIPMS.Api.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    public Task Ping() => Clients.Caller.SendAsync("NotificationPong", new { schemaVersion = 1 }, Context.ConnectionAborted);
}
