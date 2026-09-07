using Amazon.S3;
using Amazon.S3.Model;
using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.exceptions;
using Microsoft.Extensions.Options;
using System.Net;

namespace LMS.Api.infrastructure.storage.S3;

public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly S3StorageOptions _options;
    private readonly bool _ownsClient;

    public S3FileStorage(IOptions<S3StorageOptions> options)
        : this(options, CreateClient(options.Value), true)
    {
    }

    public S3FileStorage(IOptions<S3StorageOptions> options, IAmazonS3 client)
        : this(options, client, false)
    {
    }

    private S3FileStorage(
        IOptions<S3StorageOptions> options,
        IAmazonS3 client,
        bool ownsClient)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.BucketName))
        {
            throw new InvalidOperationException("S3Storage:BucketName must be configured.");
        }

        _client = client;
        _ownsClient = ownsClient;
    }

    public async Task<StoredFile> UploadAsync(
        FileUpload upload,
        string directory,
        long? maximumSizeBytes = null,
        CancellationToken cancellationToken = default)
    {
        FileUploadPolicy.Validate(upload, maximumSizeBytes);
        var key = BuildObjectKey(directory, upload.FileName);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = upload.Content,
            ContentType = upload.ContentType.Trim(),
            AutoCloseStream = false
        }, cancellationToken);

        return new StoredFile(Path.GetFileName(upload.FileName), key, upload.ContentType.Trim(), upload.Length);
    }

    public async Task<Stream> OpenReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = ValidateObjectKey(objectKey)
            }, cancellationToken);
            return new S3ResponseStream(response);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException("Stored file was not found.");
        }
    }

    public Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken = default) =>
        _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = ValidateObjectKey(objectKey)
        }, cancellationToken);

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static IAmazonS3 CreateClient(S3StorageOptions options)
    {
        var config = new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region),
            ForcePathStyle = options.ForcePathStyle
        };
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
        }

        return string.IsNullOrWhiteSpace(options.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(options.AccessKey, options.SecretKey, config);
    }

    private static string BuildObjectKey(string directory, string fileName)
    {
        var segments = directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new BadRequestException("File directory is invalid.");
        }

        var extension = Path.GetExtension(Path.GetFileName(fileName));
        if (extension.Length > 16 || extension.Any(character => !char.IsLetterOrDigit(character) && character != '.'))
        {
            extension = string.Empty;
        }

        return $"{string.Join('/', segments)}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
    }

    private static string ValidateObjectKey(string objectKey)
    {
        var normalized = objectKey.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new BadRequestException("File object key is invalid.");
        }

        return normalized;
    }

    private sealed class S3ResponseStream : Stream
    {
        private readonly GetObjectResponse _response;
        private Stream Inner => _response.ResponseStream;

        public S3ResponseStream(GetObjectResponse response)
        {
            _response = response;
        }

        public override bool CanRead => Inner.CanRead;
        public override bool CanSeek => Inner.CanSeek;
        public override bool CanWrite => Inner.CanWrite;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override void Flush() => Inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override void SetLength(long value) => Inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Inner.ReadAsync(buffer, cancellationToken);
        public override Task FlushAsync(CancellationToken cancellationToken) => Inner.FlushAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            _response.Dispose();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
