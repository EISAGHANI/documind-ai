namespace DocuMind.Api.Entities;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = "application/pdf";

    // Key of the file inside the storage provider (relative, never an absolute path)
    public string StoragePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public DateTime? ProcessedAt { get; set; }

    // Why processing failed, shown to the user when Status is Failed
    public string? ProcessingError { get; set; }

    public List<DocumentChunk> Chunks { get; set; } = [];
}

public enum DocumentStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}
