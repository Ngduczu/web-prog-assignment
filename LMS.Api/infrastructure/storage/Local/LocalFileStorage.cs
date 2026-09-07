using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.exceptions;
using Microsoft.Extensions.Options;

namespace LMS.Api.infrastructure.storage.Local;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredFile> UploadAsync(
        FileUpload upload,
        string directory,
        long? maximumSizeBytes = null,
        CancellationToken cancellationToken = default)
    {
        FileUploadPolicy.Validate(upload, maximumSizeBytes);
        var safeDirectory = NormalizeDirectory(directory);
        var extension = GetSafeExtension(upload.FileName);
        var objectKey = $"{safeDirectory}/{Guid.NewGuid():N}{extension}";
        var fullPath = GetFullPath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            await using var destination = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long written = 0;

            while (true)
            {
                var read = await upload.Content.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                written += read;
                if (maximumSizeBytes is not null && written >= maximumSizeBytes)
                {
                    throw new BadRequestException($"File size must be smaller than {maximumSizeBytes} bytes.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (written == 0 || written != upload.Length)
            {
                throw new BadRequestException("Uploaded file length does not match its declared length.");
            }

            return new StoredFile(
                Path.GetFileName(upload.FileName),
                objectKey,
                upload.ContentType.Trim(),
                written);
        }
        catch
        {
            File.Delete(fullPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(objectKey);
        if (!File.Exists(fullPath))
        {
            throw new NotFoundException("Stored file was not found.");
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteIfExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        File.Delete(GetFullPath(objectKey));
        return Task.CompletedTask;
    }

    private string GetFullPath(string objectKey)
    {
        var normalizedKey = objectKey.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedKey));
        var rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new BadRequestException("File object key is invalid.");
        }

        return fullPath;
    }

    private static string NormalizeDirectory(string directory)
    {
        var segments = directory
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 || segments.Any(segment =>
                segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new BadRequestException("File directory is invalid.");
        }

        return string.Join('/', segments);
    }

    private static string GetSafeExtension(string fileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        return extension.Length is > 0 and <= 16 &&
               extension.All(character => char.IsLetterOrDigit(character) || character == '.')
            ? extension.ToLowerInvariant()
            : string.Empty;
    }
}
