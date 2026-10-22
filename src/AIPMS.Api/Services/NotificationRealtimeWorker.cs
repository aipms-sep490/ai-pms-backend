using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AIPMS.Api.Hubs;

namespace AIPMS.Api.Services;

internal sealed class NotificationRealtimeWorker(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    ILogger<NotificationRealtimeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delivered = 0;
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
                var pending = await db.NotificationRecipients.AsNoTracking()
                    .Where(r => r.RealtimeDeliveredAt == null)
                    .OrderBy(r => r.Id)
                    .Take(100)
                    .Select(r => new
                    {
                        r.Id,
                        r.UserId,
                        NotificationId = r.Notification.Id,
                        r.Notification.NotificationType,
                        r.Notification.Title,
                        r.Notification.Content,
                        r.Notification.RelatedEntityType,
                        r.Notification.RelatedEntityId,
                        CreatedAt = r.Notification.CreatedAt
                    })
                    .ToArrayAsync(stoppingToken);

                foreach (var item in pending)
                {
                    await hub.Clients.User(item.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .SendAsync("NotificationReceived", new
                        {
                            schemaVersion = 1,
                            notificationId = item.NotificationId,
                            notificationType = item.NotificationType,
                            title = item.Title,
                            content = item.Content,
                            relatedEntityType = item.RelatedEntityType,
                            relatedEntityId = item.RelatedEntityId,
                            createdAt = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc)
                        }, stoppingToken);

                    await db.NotificationRecipients
                        .Where(r => r.Id == item.Id && r.RealtimeDeliveredAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.RealtimeDeliveredAt, DateTime.UtcNow), stoppingToken);
                    delivered++;
                }

                if (delivered == 0)
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Notification realtime sweep failed: {ErrorType}", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
