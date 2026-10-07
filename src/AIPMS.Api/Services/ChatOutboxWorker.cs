using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Chat.Abstractions;
using AIPMS.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class ChatOutboxWorker(IServiceScopeFactory scopes, IOptions<ChatSettings> settings,
    IChatWakeSignal wake, ILogger<ChatOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        if (!options.Enabled || !options.RealtimeEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delivered = false;
                for (var i = 0; i < 100; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var outbox = scope.ServiceProvider.GetRequiredService<IChatOutbox>();
                    var item = await outbox.ClaimAsync(stoppingToken);
                    if (item is null) break;
                    delivered = true;
                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<ChatDelivery>().SendAsync(
                            long.Parse(item.Event.ConversationId,System.Globalization.CultureInfo.InvariantCulture),item.EventType,item.Event,stoppingToken);
                        await outbox.CompleteAsync(item, true, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning("Chat realtime dispatch failed for outbox {OutboxId}: {ErrorType}", item.Id, ex.GetType().Name);
                        await outbox.CompleteAsync(item, false, stoppingToken);
                    }
                }
                if (!delivered) await wake.WaitAsync(TimeSpan.FromSeconds(options.OutboxPollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Chat outbox sweep failed: {ErrorType}",ex.GetType().Name); await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
        }
    }
}
