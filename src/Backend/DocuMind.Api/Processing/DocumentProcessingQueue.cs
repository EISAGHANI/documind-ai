using System.Threading.Channels;

namespace DocuMind.Api.Processing;

public interface IDocumentProcessingQueue
{
    ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

// In-memory queue. Lost on restart, which is why the worker re-queues unfinished documents at startup.
// Replace with a durable queue (e.g. a message broker) when running more than one API instance.
public class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(documentId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
