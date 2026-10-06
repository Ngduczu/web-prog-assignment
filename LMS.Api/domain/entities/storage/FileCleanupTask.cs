namespace LMS.Api.domain.Entities.Storage;

public sealed class FileCleanupTask
{
    public Guid Id { get; private set; }
    public string ObjectKey { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private FileCleanupTask() { }

    public FileCleanupTask(string objectKey, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }

        Id = Guid.NewGuid();
        ObjectKey = objectKey;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    public void RecordFailure(string error, DateTime nextAttemptAt)
    {
        AttemptCount++;
        LastError = error.Length <= 2000 ? error : error[..2000];
        NextAttemptAt = nextAttemptAt;
    }
}
