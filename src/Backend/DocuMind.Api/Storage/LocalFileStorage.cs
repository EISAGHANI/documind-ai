namespace DocuMind.Api.Storage;

// Stores files on local disk. Swap for a blob storage implementation (S3/Azure) in production.
public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredRoot = configuration["Storage:RootPath"] ?? "uploads";
        _rootPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredRoot));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        Stream? stream = File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    // Prevents path traversal: the resolved path must stay inside the storage root
    private string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, path));
        if (!fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage path escapes the storage root.");
        }

        return fullPath;
    }
}
