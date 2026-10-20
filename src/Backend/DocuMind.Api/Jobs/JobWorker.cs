using DocuMind.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Jobs;

// Polls the jobs table. Several workers (in this process or others) can run at once:
// FOR UPDATE SKIP LOCKED hands each job to exactly one of them.
public class JobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<JobOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    private readonly JobOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job worker {WorkerId} starting with concurrency {Concurrency}", _workerId, _options.Concurrency);

        var loops = Enumerable.Range(0, Math.Max(1, _options.Concurrency)).Select(i => RunLoopAsync(i, stoppingToken)).ToList();
        loops.Add(CleanupLoopAsync(stoppingToken));
        await Task.WhenAll(loops);
    }

    private async Task RunLoopAsync(int slot, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await TryRunOneAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Usually the database being unavailable; back off and keep polling
                logger.LogWarning(ex, "Job worker loop {Slot} failed, retrying", slot);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<bool> TryRunOneAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var job = await ClaimAsync(db, stoppingToken);
        if (job is null)
        {
            return false;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobId"] = job.Id,
            ["JobType"] = job.Type,
            ["CorrelationId"] = job.CorrelationId,
        });
        using var activity = JobTelemetry.Source.StartActivity($"job {job.Type}");
        activity?.SetTag("correlation_id", job.CorrelationId);
        activity?.SetTag("job.attempt", job.Attempts);

        if (job.Attempts > job.MaxAttempts)
        {
            // Reclaimed after crashing on its last attempt
            await CompleteAsync(db, job, JobStatus.Dead, job.LastError ?? "Worker stopped before finishing", null);
            return true;
        }

        var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => h.Type == job.Type);
        if (handler is null)
        {
            await CompleteAsync(db, job, JobStatus.Dead, $"No handler for job type '{job.Type}'", null);
            return true;
        }

        try
        {
            await handler.HandleAsync(new JobContext(job.EntityId, job.Attempts, job.MaxAttempts), stoppingToken);
            await CompleteAsync(db, job, JobStatus.Succeeded, null, null);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: release the job straight away so another worker can pick it up
            await db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Pending)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.Attempts, j => j.Attempts - 1), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            if (job.Attempts >= job.MaxAttempts)
            {
                logger.LogError(ex, "Job {JobId} ({Type}) dead-lettered after {Attempts} attempts", job.Id, job.Type, job.Attempts);
                await CompleteAsync(db, job, JobStatus.Dead, error, null);
            }
            else
            {
                var delay = Backoff(job.Attempts);
                logger.LogWarning(ex, "Job {JobId} ({Type}) failed on attempt {Attempt}, retrying in {Delay}", job.Id, job.Type, job.Attempts, delay);
                await CompleteAsync(db, job, JobStatus.Pending, error, DateTime.UtcNow + delay);
            }
        }

        return true;
    }

    internal static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Max(0, attempt - 1))));

    private async Task<Job?> ClaimAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var lockedUntil = DateTime.UtcNow.AddMinutes(_options.VisibilityTimeoutMinutes);
        var claimed = await db.Jobs
            .FromSql($"""
                UPDATE "Jobs" SET "Status" = 'Running', "Attempts" = "Attempts" + 1,
                    "LockedUntil" = {lockedUntil}, "LockedBy" = {_workerId}
                WHERE "Id" = (
                    SELECT "Id" FROM "Jobs"
                    WHERE ("Status" = 'Pending' AND "RunAfter" <= now())
                       OR ("Status" = 'Running' AND "LockedUntil" < now())
                    ORDER BY "RunAfter"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return claimed.SingleOrDefault();
    }

    private static Task CompleteAsync(ApplicationDbContext db, Job job, JobStatus status, string? error, DateTime? runAfter)
    {
        var completedAt = status == JobStatus.Pending ? (DateTime?)null : DateTime.UtcNow;
        if (runAfter is { } retryAt)
        {
            return db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.LastError, error)
                .SetProperty(j => j.LockedUntil, (DateTime?)null)
                .SetProperty(j => j.RunAfter, retryAt)
                .SetProperty(j => j.CompletedAt, completedAt), CancellationToken.None);
        }

        return db.Jobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, status)
            .SetProperty(j => j.LastError, error)
            .SetProperty(j => j.LockedUntil, (DateTime?)null)
            .SetProperty(j => j.CompletedAt, completedAt), CancellationToken.None);
    }

    private async Task CleanupLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
                await db.Jobs.Where(j => j.Status == JobStatus.Succeeded && j.CompletedAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Job cleanup failed");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}

public static class JobTelemetry
{
    public const string SourceName = "DocuMind.Jobs";
    public static readonly System.Diagnostics.ActivitySource Source = new(SourceName);
}
