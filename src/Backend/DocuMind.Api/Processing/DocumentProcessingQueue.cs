using DocuMind.Api.Jobs;

namespace DocuMind.Api.Processing;

public interface IDocumentProcessingQueue
{
    ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default);
}

// Durable: documents are queued as rows in the jobs table, so they survive restarts and can be
// picked up by any worker instance.
public class DocumentProcessingQueue(IJobQueue jobs) : IDocumentProcessingQueue
{
    public async ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await jobs.EnqueueAsync(JobTypes.ProcessDocument, documentId, cancellationToken);
}

public class DocumentProcessingJobHandler(DocumentProcessor processor) : IJobHandler
{
    public string Type => JobTypes.ProcessDocument;

    public Task HandleAsync(JobContext job, CancellationToken cancellationToken) =>
        processor.ProcessAsync(job.EntityId, cancellationToken, job);
}
