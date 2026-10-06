using LMS.Api.application.abstractions.storage;
using LMS.Api.application.common;
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
    ExerciseStatus Status,
    FileAttachmentDto? Attachment,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record SubmissionDto(
    Guid Id,
    Guid ExerciseId,
    Guid StudentId,
    SubmissionStatus Status,
    DateTime? SubmittedAt,
    decimal? Grade,
    string? Feedback,
    DateTime? GradedAt,
    Guid? GradedById,
    FileAttachmentDto? Attachment,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record SubmissionQuery(int Page = 1, int PageSize = 20);
public sealed record GradeSubmissionRequest(decimal Grade, string? Feedback);

public interface IExerciseService
{
    Task<ExerciseDto> CreateAsync(Guid courseId, CreateExerciseRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExerciseDto>> ListAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<ExerciseDto> GetAsync(Guid exerciseId, CancellationToken cancellationToken = default);
    Task<FileContent> GetAttachmentContentAsync(Guid exerciseId, CancellationToken cancellationToken = default);
    Task<ExerciseDto> UpdateAsync(Guid exerciseId, UpdateExerciseRequest request, CancellationToken cancellationToken = default);
    Task<ExerciseDto> UploadAttachmentAsync(Guid exerciseId, FileUpload upload, CancellationToken cancellationToken = default);
    Task<ExerciseDto> PublishAsync(Guid exerciseId, CancellationToken cancellationToken = default);
    Task<ExerciseDto> CloseAsync(Guid exerciseId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid exerciseId, CancellationToken cancellationToken = default);
}

public interface ISubmissionService
{
    Task<SubmissionDto> UploadAsync(Guid exerciseId, FileUpload upload, CancellationToken cancellationToken = default);
    Task<SubmissionDto> GetMineAsync(Guid exerciseId, CancellationToken cancellationToken = default);
    Task<PagedResult<SubmissionDto>> ListByExerciseAsync(Guid exerciseId, SubmissionQuery query, CancellationToken cancellationToken = default);
    Task<FileContent> GetContentAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<SubmissionDto> GradeAsync(Guid submissionId, GradeSubmissionRequest request, CancellationToken cancellationToken = default);
    Task<SubmissionDto> SubmitAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<SubmissionDto> UnsubmitAsync(Guid submissionId, CancellationToken cancellationToken = default);
}
