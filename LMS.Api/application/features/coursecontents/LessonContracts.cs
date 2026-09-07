using LMS.Api.application.abstractions.storage;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.application.features.coursecontents;

public sealed record CreateLessonRequest(
    string Name,
    string? Content,
    int Order,
    LessonStatus Status = LessonStatus.Draft);

public sealed record UpdateLessonRequest(
    string Name,
    string? Content,
    int Order,
    LessonStatus Status);

public sealed record LessonDto(
    Guid Id,
    Guid CourseId,
    string Name,
    string? Content,
    int Order,
    LessonStatus Status,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record MaterialDto(
    Guid Id,
    Guid LessonId,
    string FileName,
    string ObjectKey,
    string ContentType,
    long FileSize,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public interface ILessonService
{
    Task<LessonDto> CreateAsync(Guid courseId, CreateLessonRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LessonDto>> ListAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<LessonDto> GetAsync(Guid lessonId, CancellationToken cancellationToken = default);
    Task<FileContent> GetMaterialContentAsync(Guid materialId, CancellationToken cancellationToken = default);
    Task<LessonDto> UpdateAsync(Guid lessonId, UpdateLessonRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaterialDto>> ListMaterialsAsync(Guid lessonId, CancellationToken cancellationToken = default);
    Task<MaterialDto> UploadMaterialAsync(Guid lessonId, FileUpload upload, CancellationToken cancellationToken = default);
    Task DeleteMaterialAsync(Guid materialId, CancellationToken cancellationToken = default);
}
