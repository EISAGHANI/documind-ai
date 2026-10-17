using DocuMind.Api.Entities;

namespace DocuMind.Api.Contracts;

public record DocumentResponse(
    Guid Id,
    string Title,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    DateTime UploadedAt,
    string Status)
{
    public static DocumentResponse FromEntity(Document document) => new(
        document.Id,
        document.Title,
        document.FileName,
        document.FileSizeBytes,
        document.ContentType,
        document.UploadedAt,
        document.Status.ToString());
}
