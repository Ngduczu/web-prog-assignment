using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.application.features.coursecontents;

public sealed record CreateExerciseRequest(string Title, string? Description, DateTime? DueAt);
public sealed record UpdateExerciseRequest(string Title, string? Description, DateTime? DueAt);

public sealed record FileAttachmentDto(
    Guid Id,
    string FileName,
    string ObjectKey,
    string ContentType,
    long FileSize);

public sealed record ExerciseDto(
    Guid Id,
    Guid CourseId,
    string Title,
    string? Description,
    DateTime? DueAt,
    FileAttachmentDto? Attachment,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record SubmissionDto(
    Guid Id,
    Guid ExerciseId,
    Guid StudentId,
    SubmissionStatus Status,
    DateTime? SubmittedAt,
    FileAttachmentDto? Attachment,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public interface IExerciseService
{
    Task<ExerciseDto> CreateAsync(Guid courseId, CreateExerciseRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExerciseDto>> ListAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<ExerciseDto> UpdateAsync(Guid exerciseId, UpdateExerciseRequest request, CancellationToken cancellationToken = default);
    Task<ExerciseDto> UploadAttachmentAsync(Guid exerciseId, FileUpload upload, CancellationToken cancellationToken = default);
}

public interface ISubmissionService
{
    Task<SubmissionDto> UploadAsync(Guid exerciseId, FileUpload upload, CancellationToken cancellationToken = default);
    Task<SubmissionDto> SubmitAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<SubmissionDto> UnsubmitAsync(Guid submissionId, CancellationToken cancellationToken = default);
}
