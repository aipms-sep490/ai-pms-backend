using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Chat.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Services;

internal sealed class ChatUploadCleanupWorker(IServiceScopeFactory scopes, IOptions<ChatSettings> options,
    ILogger<ChatUploadCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                bounded.CancelAfter(TimeSpan.FromSeconds(30));
                await scope.ServiceProvider.GetRequiredService<IChatUploadCleanup>().SweepAsync(bounded.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Chat upload cleanup will retry: {ErrorType}", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
