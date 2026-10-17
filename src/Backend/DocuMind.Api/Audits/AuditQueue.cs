using System.Threading.Channels;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Api.Audits;

public interface IAuditQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken = default);
}

// In-memory queue of audit runs, like the document processing queue: unfinished runs are
// re-queued at startup because the queue doesn't survive a restart.
public class AuditQueue : IAuditQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(runId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public class AuditWorker(AuditQueue queue, IServiceScopeFactory scopeFactory, ILogger<AuditWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (var runId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AuditRunner>().RunAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one run stop the worker
                logger.LogError(ex, "Unexpected error in audit run {RunId}", runId);
            }
        }
    }

    private async Task RequeueUnfinishedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var unfinished = await db.AuditRuns.IgnoreQueryFilters()
                .Where(r => r.Status == AuditRunStatus.Queued || r.Status == AuditRunStatus.Running)
                .OrderBy(r => r.CreatedAt)
                .Select(r => r.Id)
                .ToListAsync(stoppingToken);

            foreach (var id in unfinished)
            {
                await queue.EnqueueAsync(id, stoppingToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not re-queue unfinished audit runs at startup");
        }
    }
}
