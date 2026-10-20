using DocuMind.Api.Contracts;
using DocuMind.Api.Data;
using DocuMind.Api.Entities;
using DocuMind.Api.Processing;
using DocuMind.Api.Storage;
using DocuMind.Api.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DocuMind.Api.Resilience;

namespace DocuMind.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController(
    ApplicationDbContext db,
    IFileStorage storage,
    ITenantProvider tenantProvider,
    IOptions<QuotaOptions> quotaOptions,
    IDocumentProcessingQueue processingQueue) : ControllerBase
{
    private const long MaxFileSizeBytes = 50 * 1024 * 1024;
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var documents = await db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(cancellationToken);

        return Ok(documents.Select(DocumentResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return document is null ? NotFound() : Ok(DocumentResponse.FromEntity(document));
    }

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(document.StoragePath, cancellationToken);
        return stream is null ? NotFound() : File(stream, document.ContentType, document.FileName);
    }

    private async Task<string?> QuotaExceededAsync(CancellationToken cancellationToken)
    {
        var quotas = quotaOptions.Value;
        if (quotas.MaxDocumentsPerTenant > 0
            && await db.Documents.CountAsync(cancellationToken) >= quotas.MaxDocumentsPerTenant)
        {
            return $"Your company has reached its limit of {quotas.MaxDocumentsPerTenant} documents. Delete some to upload more.";
        }

        if (quotas.MaxUploadsPerTenantPerDay > 0)
        {
            var since = DateTimeOffset.UtcNow.AddDays(-1);
            if (await db.Documents.CountAsync(d => d.UploadedAt >= since, cancellationToken) >= quotas.MaxUploadsPerTenantPerDay)
            {
                return $"Your company has reached its daily limit of {quotas.MaxUploadsPerTenantPerDay} uploads. Try again tomorrow.";
            }
        }

        return null;
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Expensive)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxFileSizeBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSizeBytes + 1024 * 1024)]
    public async Task<ActionResult<DocumentResponse>> Upload(
        IFormFile file,
        [FromForm] string? title,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Problem("The uploaded file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return Problem($"Files larger than {MaxFileSizeBytes / (1024 * 1024)} MB are not allowed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Check the file's magic bytes rather than trusting the client-supplied content type
        if (!await IsPdfAsync(file, cancellationToken))
        {
            return Problem("Only PDF files are supported.", statusCode: StatusCodes.Status400BadRequest);
        }

        var tenantId = tenantProvider.TenantId;
        if (await QuotaExceededAsync(cancellationToken) is { } quotaMessage)
        {
            return Problem(quotaMessage, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var fileName = Path.GetFileName(file.FileName);
        var document = new Document
        {
            TenantId = tenantId,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim(),
            FileName = fileName,
            FileSizeBytes = file.Length,
            ContentType = "application/pdf",
        };
        document.StoragePath = $"{tenantId}/{document.Id}.pdf";

        await using (var content = file.OpenReadStream())
        {
            await storage.SaveAsync(document.StoragePath, content, cancellationToken);
        }

        db.Documents.Add(document);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Don't leave orphaned files behind when the database write fails
            await storage.DeleteAsync(document.StoragePath, CancellationToken.None);
            throw;
        }

        await processingQueue.EnqueueAsync(document.Id, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = document.Id }, DocumentResponse.FromEntity(document));
    }

    // Queues a document for processing again, e.g. after a failure or a change of embedding model
    [HttpPost("{id:guid}/reprocess")]
    public async Task<ActionResult<DocumentResponse>> Reprocess(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (document.Status is DocumentStatus.Pending or DocumentStatus.Processing)
        {
            return Problem("The document is already queued for processing.", statusCode: StatusCodes.Status409Conflict);
        }

        document.Status = DocumentStatus.Pending;
        document.ProcessingError = null;
        await db.SaveChangesAsync(cancellationToken);
        await processingQueue.EnqueueAsync(document.Id, cancellationToken);

        return Accepted(DocumentResponse.FromEntity(document));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(document.StoragePath, cancellationToken);

        return NoContent();
    }

    private static async Task<bool> IsPdfAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var header = new byte[PdfSignature.Length];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        return read == header.Length && header.AsSpan().SequenceEqual(PdfSignature);
    }
}
