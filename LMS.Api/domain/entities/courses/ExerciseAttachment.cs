using LMS.Api.domain.Entities.Users;

namespace LMS.Api.domain.Entities.Courses;

public class ExerciseAttachment
{
    public Guid Id { get; private set; }

    public Guid ExerciseId { get; private set; }
    public Exercise? Exercise { get; private set; }

    public string FileName { get; private set; } = null!;
    public string ObjectKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long FileSize { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public User? CreatedBy { get; private set; }

    private ExerciseAttachment() { }

    public ExerciseAttachment(
        Guid exerciseId,
        string fileName,
        string objectKey,
        string contentType,
        long fileSize,
        Guid createdById)
    {
        Id = Guid.NewGuid();

        ExerciseId = exerciseId;
        FileName = fileName;
        ObjectKey = objectKey;
        ContentType = contentType;
        FileSize = fileSize;

        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;

        CreatedById = createdById;
    }

    public void SetFileMetadata(string fileName, string objectKey, string contentType, long fileSize)
    {
        FileName = fileName;
        ObjectKey = objectKey;
        ContentType = contentType;
        FileSize = fileSize;
        ModifiedAt = DateTime.UtcNow;
    }
}
