namespace LMS.Api.domain.Entities;

public class SubmissionAttachment
{
    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Submission? Submission { get; private set; }

    public string FileName { get; private set; } = null!;
    public string ObjectKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long FileSize { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    private SubmissionAttachment() { }

    public SubmissionAttachment(Guid submissionId, string fileName, string objectKey, string contentType, long fileSize)
    {
        Id = Guid.NewGuid();
        SubmissionId = submissionId;
        FileName = fileName;
        ObjectKey = objectKey;
        ContentType = contentType;
        FileSize = fileSize;
        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;
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
