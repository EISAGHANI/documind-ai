using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Storage;

public class AzureBlobStorageOptions
{
    public string Container { get; set; } = "documents";

    // Either a connection string (dev/Azurite)...
    public string? ConnectionString { get; set; }

    // ...or an account URL, authenticated with managed identity (recommended in Azure)
    public string? AccountUrl { get; set; }
}

public class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private bool _containerEnsured;

    public AzureBlobFileStorage(IOptions<AzureBlobStorageOptions> options)
    {
        var o = options.Value;
        if (!string.IsNullOrWhiteSpace(o.ConnectionString))
        {
            _container = new BlobContainerClient(o.ConnectionString, o.Container);
        }
        else if (!string.IsNullOrWhiteSpace(o.AccountUrl))
        {
            _container = new BlobContainerClient(new Uri($"{o.AccountUrl.TrimEnd('/')}/{o.Container}"), new DefaultAzureCredential());
        }
        else
        {
            throw new InvalidOperationException(
                "Storage:AzureBlob:ConnectionString or Storage:AzureBlob:AccountUrl must be set when Storage:Provider is AzureBlob.");
        }
    }

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        if (!_containerEnsured)
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerEnsured = true;
        }

        await _container.GetBlobClient(path).UploadAsync(content, overwrite: true, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _container.GetBlobClient(path).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        _container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
