using DocuMind.Api.Jobs;

namespace DocuMind.Api.Audits;

public interface IAuditQueue
{
    ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken = default);
}

// Durable queue of audit runs, backed by the jobs table
public class AuditQueue(IJobQueue jobs) : IAuditQueue
{
    public async ValueTask EnqueueAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await jobs.EnqueueAsync(JobTypes.RunAudit, runId, cancellationToken);
}

public class AuditJobHandler(AuditRunner runner) : IJobHandler
{
    public string Type => JobTypes.RunAudit;

    public Task HandleAsync(JobContext job, CancellationToken cancellationToken) =>
        runner.RunAsync(job.EntityId, cancellationToken, job);
}
