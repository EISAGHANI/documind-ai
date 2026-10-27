using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace DocuMind.Api.Storage;

public class S3StorageOptions
{
    public string Bucket { get; set; } = string.Empty;
    public string? Region { get; set; }

    // Set for S3-compatible stores (MinIO, R2, ...)
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; }

    // Optional; leave empty to use the default AWS credential chain (IAM role, env vars)
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}

public class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public S3FileStorage(IOptions<S3StorageOptions> options)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Bucket))
        {
            throw new InvalidOperationException("Storage:S3:Bucket must be set when Storage:Provider is S3.");
        }

        var config = new AmazonS3Config { ForcePathStyle = o.ForcePathStyle };
        if (!string.IsNullOrWhiteSpace(o.ServiceUrl)) config.ServiceURL = o.ServiceUrl;
        else if (!string.IsNullOrWhiteSpace(o.Region)) config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(o.Region);

        _bucket = o.Bucket;
        _s3 = !string.IsNullOrWhiteSpace(o.AccessKey)
            ? new AmazonS3Client(o.AccessKey, o.SecretKey, config)
            : new AmazonS3Client(config);
    }

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        // PutObject needs a known length; buffer non-seekable request streams
        if (!content.CanSeek)
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            content = buffer;
        }

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = path,
            InputStream = content,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
        }, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, path, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        _s3.DeleteObjectAsync(_bucket, path, cancellationToken);
}
