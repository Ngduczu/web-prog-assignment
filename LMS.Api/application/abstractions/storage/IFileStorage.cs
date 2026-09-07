namespace LMS.Api.application.abstractions.storage;

public interface IFileStorage
{
    Task<Stream> OpenReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
    Task<StoredFile> UploadAsync(
        FileUpload upload,
        string directory,
        long? maximumSizeBytes = null,
        CancellationToken cancellationToken = default);
    Task DeleteIfExistsAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}

public sealed record FileUpload(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record StoredFile(
    string FileName,
    string ObjectKey,
    string ContentType,
    long FileSize);

public sealed record FileContent(
    Stream Content,
    string FileName,
    string ContentType);
