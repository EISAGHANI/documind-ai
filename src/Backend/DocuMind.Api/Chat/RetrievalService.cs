using DocuMind.Api.Data;
using DocuMind.Api.Embeddings;
using DocuMind.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DocuMind.Api.Chat;

public record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int? PageNumber,
    string Content,
    double Distance);

// Finds the chunks most similar to a question. Runs inside the request, so the tenant
// query filter limits results to the caller's own documents.
public class RetrievalService(ApplicationDbContext db, IEmbeddingService embeddings)
{
    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        string question,
        IReadOnlyCollection<Guid>? documentIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = (await embeddings.EmbedAsync([question], EmbeddingInputType.Query, cancellationToken))[0];
        var queryVector = new Vector(queryEmbedding);

        var query = db.DocumentChunks
            .AsNoTracking()
            .Where(c => c.Embedding != null && c.Document.Status == DocumentStatus.Processed);

        if (documentIds is { Count: > 0 })
        {
            query = query.Where(c => documentIds.Contains(c.DocumentId));
        }

        return await query
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(limit)
            .Select(c => new RetrievedChunk(
                c.Id,
                c.DocumentId,
                c.Document.Title,
                c.PageNumber,
                c.Content,
                c.Embedding!.CosineDistance(queryVector)))
            .ToListAsync(cancellationToken);
    }
}
