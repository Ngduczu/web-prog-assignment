using LMS.Api.domain.exceptions;

namespace LMS.Api.application.abstractions.storage;

public static class FileUploadPolicy
{
    public const long MaximumChatFileSizeBytes = 20 * 1024 * 1024;
    public const long MaximumCourseFileSizeBytes = 20 * 1024 * 1024;

    public static void Validate(FileUpload upload, long? maximumSizeBytes = null)
    {
        if (string.IsNullOrWhiteSpace(upload.FileName) || upload.FileName.Length > 255)
        {
            throw new BadRequestException("File name must contain between 1 and 255 characters.");
        }

        if (string.IsNullOrWhiteSpace(upload.ContentType) || upload.ContentType.Length > 255)
        {
            throw new BadRequestException("File content type must contain between 1 and 255 characters.");
        }

        if (upload.Length <= 0)
        {
            throw new BadRequestException("File size must be greater than zero.");
        }

        if (maximumSizeBytes is not null && upload.Length >= maximumSizeBytes)
        {
            throw new BadRequestException($"File size must be smaller than {maximumSizeBytes} bytes.");
        }

        if (!upload.Content.CanRead)
        {
            throw new BadRequestException("File content is not readable.");
        }
    }
}
