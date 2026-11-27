using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIPMS.Infrastructure.Storage;

internal sealed class ProjectFileUploadProcessor(
    DeliverableWorkflow workflow,
    IFileSecurityScanner scanner,
    IAuditTrail audit)
{
    public async Task<ProjectFileDto> ProcessAsync(ProjectFileUploadSessionStore.Entry entry, Stream content, CancellationToken ct)
    {
        if (content.CanSeek) content.Position = 0;
        var scan = await scanner.ScanAsync(content, entry.FileName, entry.ContentType, ct);
        if (!scan.IsSafe)
        {
            await audit.RecordAsync(new AuditEntry(entry.ActorId, "FILE_SCAN_QUARANTINED", "PROJECT_FILE_UPLOAD_SESSION", null,
                new Dictionary<string, object?>
                {
                    ["sessionId"] = entry.SessionId,
                    ["flow"] = "PROJECT_FILE_CHUNKED",
                    ["provider"] = scan.Provider,
                    ["reason"] = scan.Details,
                    ["fileName"] = entry.FileName,
                    ["verdict"] = "QUARANTINED"
                }, Outcome: "DENIED"), ct);
            throw new DomainException(scan.Details ?? "File failed security scan and was quarantined.", WorkflowErrorCodes.FileQuarantined);
        }

        if (content.CanSeek) content.Position = 0;
        var metadata = await ProjectFileUploadValidator.ValidateAsync(content, entry.FileName, entry.ContentType, entry.TotalBytes, ct);
        content.Position = 0;
        return await workflow.AttachPreparedForActorAsync(entry.ActorId, entry.ParentType, entry.ParentId,
            entry.SessionId, metadata, content, ct);
    }
}

internal sealed class ProjectFileUploadWorker(ProjectFileUploadSessionStore store, IServiceScopeFactory scopes,
    ILogger<ProjectFileUploadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var entry in await store.EntriesAsync(stoppingToken))
                {
                    if (entry.State != "PROCESSING") continue;
                    using var scope = scopes.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ProjectFileUploadProcessor>();
                    await store.ProcessAsync(entry.SessionId, processor.ProcessAsync, stoppingToken);
                }
                await store.CleanupAsync(stoppingToken, async (entry, ct) =>
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<DeliverableWorkflow>()
                        .CleanupPreparedForActorAsync(entry.ActorId, entry.ParentType, entry.ParentId, entry.SessionId, ct);
                });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Could not process durable project file upload sessions"); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
