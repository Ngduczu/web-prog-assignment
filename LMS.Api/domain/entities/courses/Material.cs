using LMS.Api.domain.Entities.Users;

namespace LMS.Api.domain.Entities.Courses;

public class Material
{
    public Guid Id { get; private set; }

    public Guid LessonId { get; private set; }
    public Lesson? Lesson { get; private set; }

    public string FileName { get; private set; } = null!;
    public string ObjectKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long FileSize { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }

    public User? CreatedBy { get; private set; }
    public User? ModifiedBy { get; private set; }

    private Material() { }

    public Material(
        Guid lessonId,
        string fileName,
        string objectKey,
        string contentType,
        long fileSize,
        Guid createdById)
    {
        Id = Guid.NewGuid();

        LessonId = lessonId;
        FileName = fileName;
        ObjectKey = objectKey;
        ContentType = contentType;
        FileSize = fileSize;

        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;

        CreatedById = createdById;
        ModifiedById = createdById;
    }

    private void Touch()
    {
        ModifiedAt = DateTime.UtcNow;
    }

    public void SetFileName(string fileName)
    {
        FileName = fileName;
        Touch();
    }

    public void SetObjectKey(string objectKey)
    {
        ObjectKey = objectKey;
        Touch();
    }

    public void SetContentType(string contentType)
    {
        ContentType = contentType;
        Touch();
    }

    public void SetFileSize(long fileSize)
    {
        FileSize = fileSize;
        Touch();
    }

    public void SetModifiedBy(Guid modifiedById)
    {
        ModifiedById = modifiedById;
    }
}
