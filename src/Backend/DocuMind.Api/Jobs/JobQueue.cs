using DocuMind.Api.Data;
using DocuMind.Api.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DocuMind.Api.Jobs;

public interface IJobQueue
{
    // Adds a job unless an identical one (same type + entity) is already pending or running
    Task EnqueueAsync(string type, Guid entityId, CancellationToken cancellationToken = default);
}

public class JobQueue(ApplicationDbContext db, ICorrelationIdAccessor correlation, IOptions<JobOptions> options) : IJobQueue
{
    public async Task EnqueueAsync(string type, Guid entityId, CancellationToken cancellationToken = default)
    {
        db.Jobs.Add(new Job
        {
            Type = type,
            EntityId = entityId,
            MaxAttempts = options.Value.MaxAttempts,
            CorrelationId = correlation.CorrelationId,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The partial unique index says an active job already exists, which is exactly what we want
            db.ChangeTracker.Entries<Job>().ToList().ForEach(e => e.State = EntityState.Detached);
        }
    }
}
