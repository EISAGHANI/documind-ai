using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.Processing;

// Processes queued documents one at a time in the background
public class DocumentProcessingWorker(
    IDocumentProcessingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var documentId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                // A new scope per document gives each run its own DbContext
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<DocumentProcessor>();
                await processor.ProcessAsync(documentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one document stop the worker
                logger.LogError(ex, "Unexpected error while processing document {DocumentId}", documentId);
            }
        }
    }

    // The queue is in memory, so documents still pending (or interrupted mid-run) are queued again on startup
    private async Task RequeueUnfinishedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var unfinished = await db.Documents
                .IgnoreQueryFilters()
                .Where(d => d.Status == DocumentStatus.Pending || d.Status == DocumentStatus.Processing)
                .OrderBy(d => d.UploadedAt)
                .Select(d => d.Id)
                .ToListAsync(stoppingToken);

            foreach (var id in unfinished)
            {
                await queue.EnqueueAsync(id, stoppingToken);
            }

            if (unfinished.Count > 0)
            {
                logger.LogInformation("Re-queued {Count} unfinished documents", unfinished.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database may not be up yet; new uploads are still processed
            logger.LogWarning(ex, "Could not re-queue unfinished documents at startup");
        }
    }
}
