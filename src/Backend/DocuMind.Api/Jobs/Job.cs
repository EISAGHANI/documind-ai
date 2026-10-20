namespace DocuMind.Api.Jobs;

public enum JobStatus
{
    Pending,
    Running,
    Succeeded,

    // Retries exhausted; kept for inspection and manual re-queue
    Dead
}

// A durable unit of background work. Deliberately not tenant-filtered: only workers touch it.
public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Selects the handler, e.g. "document.process"
    public string Type { get; set; } = string.Empty;

    // The entity the job operates on. Handlers load the current state, so jobs stay idempotent.
    public Guid EntityId { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime RunAfter { get; set; } = DateTime.UtcNow;

    // While Running, other workers skip the job until this passes (visibility timeout recovers crashed workers)
    public DateTime? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public static class JobTypes
{
    public const string ProcessDocument = "document.process";
    public const string RunAudit = "audit.run";
}
