namespace LMS.Api.domain.Entities;

public class Material
{
    public Guid Id { get; private set; }

    public Guid LessonId { get; private set; }
    public Lesson? Lesson { get; private set; }

    public string FileName { get; private set; } = null!;
    public string ObjectKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long FileSize { get; private set; }

    public DateTime CreatedOn { get; private set; }
    public DateTime ModifiedOn { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }

    public Users? CreatedBy { get; private set; }
    public Users? ModifiedBy { get; private set; }

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

        CreatedOn = DateTime.UtcNow;
        ModifiedOn = DateTime.UtcNow;

        CreatedById = createdById;
        ModifiedById = createdById;
    }

    private void Touch()
    {
        ModifiedOn = DateTime.UtcNow;
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