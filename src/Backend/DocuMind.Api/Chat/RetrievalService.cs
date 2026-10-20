using DocuMind.Api.ApiKeys;
using DocuMind.Api.Data;
using DocuMind.Api.Embeddings;
using DocuMind.Api.Entities;
using DocuMind.Api.Tenancy;
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

// Reciprocal Rank Fusion: merges ranked lists without needing comparable scores
public static class HybridRanker
{
    public const int RrfK = 60;

    public static IReadOnlyList<T> Fuse<T>(IEnumerable<IReadOnlyList<T>> rankings, Func<T, Guid> key, int limit)
    {
        var scores = new Dictionary<Guid, double>();
        var items = new Dictionary<Guid, T>();
        foreach (var ranking in rankings)
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                var id = key(ranking[rank]);
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (RrfK + rank + 1);
                items.TryAdd(id, ranking[rank]);
            }
        }

        return scores.OrderByDescending(s => s.Value).Take(limit).Select(s => items[s.Key]).ToList();
    }
}

// Finds the chunks most relevant to a question: vector similarity plus Postgres full-text search,
// fused with RRF so exact terms (clause numbers, names) still match. Runs inside the request, so
// the tenant query filter limits results to the caller's own documents.
public class RetrievalService(
    ApplicationDbContext db,
    IEmbeddingService embeddings,
    ApiKeyStore keys,
    ITenantProvider tenantProvider,
    ILogger<RetrievalService> logger)
{
    // Candidates fetched per ranking before fusion
    private const int CandidateMultiplier = 3;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        string question,
        IReadOnlyCollection<Guid>? documentIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var apiKey = await keys.GetKeyAsync(tenantProvider.TenantId, AiProvider.Voyage, cancellationToken)
            ?? throw new MissingApiKeyException("Add a Voyage AI API key in Settings to search your documents.");

        var queryEmbedding = (await embeddings.EmbedAsync(apiKey, [question], EmbeddingInputType.Query, cancellationToken))[0];
        var queryVector = new Vector(queryEmbedding);
        var candidates = limit * CandidateMultiplier;

        var query = db.DocumentChunks
            .AsNoTracking()
            .Where(c => c.Embedding != null && c.Document.Status == DocumentStatus.Processed);

        if (documentIds is { Count: > 0 })
        {
            query = query.Where(c => documentIds.Contains(c.DocumentId));
        }

        // Hold one connection so the session setting below applies to the vector query
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await EnableIterativeIndexScanAsync(cancellationToken);

            var byVector = await query
                .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
                .Take(candidates)
                .Select(c => new RetrievedChunk(
                    c.Id, c.DocumentId, c.Document.Title, c.PageNumber, c.Content,
                    c.Embedding!.CosineDistance(queryVector)))
                .ToListAsync(cancellationToken);

            var byText = await query
                .Where(c => c.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(DocumentChunk.TextSearchConfig, question)))
                .OrderByDescending(c => c.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(DocumentChunk.TextSearchConfig, question)))
                .Take(candidates)
                .Select(c => new RetrievedChunk(
                    c.Id, c.DocumentId, c.Document.Title, c.PageNumber, c.Content,
                    c.Embedding!.CosineDistance(queryVector)))
                .ToListAsync(cancellationToken);

            return HybridRanker.Fuse<RetrievedChunk>([byVector, byText], c => c.ChunkId, limit);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // pgvector 0.8+: keep scanning the HNSW index until enough rows pass the tenant/document filters.
    // Older versions don't know the setting; retrieval still works, just with less recall on filtered queries.
    private async Task EnableIterativeIndexScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET hnsw.iterative_scan = relaxed_order", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "hnsw.iterative_scan is not supported by this pgvector version");
        }
    }
}
