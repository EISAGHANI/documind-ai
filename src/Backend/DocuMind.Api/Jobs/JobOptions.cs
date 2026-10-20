namespace DocuMind.Api.Jobs;

public class JobOptions
{
    public const string SectionName = "Jobs";

    // Api = enqueue only, Worker = process jobs (and serve health endpoints only), All = both (default, single instance / dev)
    public string Mode { get; set; } = "All";

    // Jobs processed in parallel by this instance. Keep low: each one makes paid AI calls.
    public int Concurrency { get; set; } = 2;
    public int PollIntervalSeconds { get; set; } = 2;

    // How long a claimed job is invisible to other workers before it's considered abandoned
    public int VisibilityTimeoutMinutes { get; set; } = 15;
    public int MaxAttempts { get; set; } = 5;
    public int RetentionDays { get; set; } = 7;

    public bool RunsApi => !Mode.Equals("Worker", StringComparison.OrdinalIgnoreCase);
    public bool RunsWorker => !Mode.Equals("Api", StringComparison.OrdinalIgnoreCase);
}

public interface IJobHandler
{
    string Type { get; }

    // Must be idempotent: a job can run more than once (retry, or a crashed worker's job being reclaimed).
    // Throw to ask for a retry; the worker applies backoff and eventually dead-letters the job.
    Task HandleAsync(JobContext job, CancellationToken cancellationToken);
}

public record JobContext(Guid EntityId, int Attempt, int MaxAttempts)
{
    public bool IsFinalAttempt => Attempt >= MaxAttempts;
}
