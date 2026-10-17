using DocuMind.Api.Data;
using DocuMind.Api.Embeddings;
using DocuMind.Api.Entities;
using DocuMind.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace DocuMind.Api.Processing;

// Turns an uploaded PDF into embedded chunks: extract text -> split -> embed -> store.
// Runs outside any HTTP request, so it bypasses the tenant query filter and copies the
// document's TenantId onto every chunk explicitly.
public class DocumentProcessor(
    ApplicationDbContext db,
    IFileStorage storage,
    IEmbeddingService embeddings,
    ILogger<DocumentProcessor> logger)
{
    private const int MaxErrorLength = 1000;

    public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            logger.LogInformation("Document {DocumentId} was deleted before processing", documentId);
            return;
        }

        document.Status = DocumentStatus.Processing;
        document.ProcessingError = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var chunks = await BuildChunksAsync(document, cancellationToken);

            // Swap old chunks for new ones atomically so reprocessing never leaves a half-indexed document
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.DocumentChunks
                .IgnoreQueryFilters()
                .Where(c => c.DocumentId == document.Id)
                .ExecuteDeleteAsync(cancellationToken);

            db.DocumentChunks.AddRange(chunks);
            document.Status = DocumentStatus.Processed;
            document.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Processed document {DocumentId} into {ChunkCount} chunks", documentId, chunks.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Processing failed for document {DocumentId}", documentId);
            await MarkFailedAsync(documentId, ex.Message);
        }
    }

    private async Task<List<DocumentChunk>> BuildChunksAsync(Document document, CancellationToken cancellationToken)
    {
        // PdfPig needs a seekable stream; storage streams may not be
        using var pdf = new MemoryStream();
        await using (var file = await storage.OpenReadAsync(document.StoragePath, cancellationToken)
            ?? throw new FileNotFoundException("The uploaded file is missing from storage."))
        {
            await file.CopyToAsync(pdf, cancellationToken);
        }
        pdf.Position = 0;

        var pages = PdfTextExtractor.Extract(pdf);
        var textChunks = TextChunker.Split(pages);
        if (textChunks.Count == 0)
        {
            throw new InvalidOperationException(
                "No text could be extracted. The PDF may be scanned images, which need OCR (not supported yet).");
        }

        var vectors = await embeddings.EmbedAsync(
            textChunks.Select(c => c.Content).ToList(),
            EmbeddingInputType.Document,
            cancellationToken);

        return textChunks.Select((chunk, index) => new DocumentChunk
        {
            TenantId = document.TenantId,
            DocumentId = document.Id,
            ChunkIndex = index,
            PageNumber = chunk.PageNumber,
            Content = chunk.Content,
            Embedding = new Vector(vectors[index]),
        }).ToList();
    }

    // Uses a fresh query because the tracked entity may be in a broken state after the failure
    private async Task MarkFailedAsync(Guid documentId, string error)
    {
        db.ChangeTracker.Clear();
        var message = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        await db.Documents
            .IgnoreQueryFilters()
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DocumentStatus.Failed)
                .SetProperty(d => d.ProcessingError, message));
    }
}
