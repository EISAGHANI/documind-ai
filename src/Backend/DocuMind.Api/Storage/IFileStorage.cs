namespace DocuMind.Api.Storage;

public interface IFileStorage
{
    Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
