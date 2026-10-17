using Pgvector;

namespace DocuMind.Api.Entities;

// A slice of a document's extracted text plus its embedding, used for vector search
public class DocumentChunk
{
    // Must match the embedding model chosen for the processing pipeline
    public const int EmbeddingDimensions = 1024;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int? PageNumber { get; set; }

    // Null until the processing pipeline has generated the embedding
    public Vector? Embedding { get; set; }
}
