namespace LMS.Api.application.abstractions.storage;

public interface IFileCleanupQueue
{
    void Enqueue(string objectKey);
    Task EnqueueImmediatelyAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}
