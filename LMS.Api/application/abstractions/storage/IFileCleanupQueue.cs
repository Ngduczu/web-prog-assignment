namespace LMS.Api.application.abstractions.storage;

public interface IFileCleanupQueue
{
    void Enqueue(string objectKey);
}
