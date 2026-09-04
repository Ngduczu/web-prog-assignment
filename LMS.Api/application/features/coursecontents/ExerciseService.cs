using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.features.courses;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.coursecontents;

public sealed class ExerciseService : IExerciseService
{
    private readonly IExerciseRepository _exercises;
    private readonly ICourseRepository _courses;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public ExerciseService(
        IExerciseRepository exercises,
        ICourseRepository courses,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _exercises = exercises;
        _courses = courses;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<ExerciseDto> CreateAsync(
        Guid courseId,
        CreateExerciseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        ValidateExercise(request.Title);
        var course = await GetCourseAsync(courseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);
        var exercise = new Exercise(
            course.Id,
            request.Title.Trim(),
            NormalizeOptional(request.Description),
            request.DueAt,
            actorId);
        _exercises.Add(exercise);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(exercise);
    }

    public async Task<IReadOnlyList<ExerciseDto>> ListAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        var course = await GetCourseAsync(courseId, cancellationToken);
        CourseAuthorization.EnsureCanManage(_currentUser, course);
        var exercises = await _exercises.ListByCourseAsync(courseId, cancellationToken);
        return exercises.Select(Map).ToArray();
    }

    public async Task<ExerciseDto> UpdateAsync(
        Guid exerciseId,
        UpdateExerciseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        ValidateExercise(request.Title);
        var exercise = await GetExerciseAsync(exerciseId, false, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);
        exercise.SetTitle(request.Title.Trim());
        exercise.SetDescription(NormalizeOptional(request.Description));
        exercise.SetDueAt(request.DueAt);
        exercise.SetModifiedBy(actorId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(exercise);
    }

    public async Task<ExerciseDto> UploadAttachmentAsync(
        Guid exerciseId,
        FileUpload upload,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        FileUploadPolicy.Validate(upload, FileUploadPolicy.MaximumCourseFileSizeBytes);
        var exercise = await GetExerciseAsync(exerciseId, true, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);
        var storedFile = await _fileStorage.UploadAsync(
            upload,
            $"courses/{course.Id:N}/exercises/{exercise.Id:N}",
            cancellationToken: cancellationToken);
        var oldObjectKey = exercise.Attachment?.ObjectKey;

        try
        {
            if (exercise.Attachment is null)
            {
                _exercises.AddAttachment(new ExerciseAttachment(
                    exercise.Id,
                    storedFile.FileName,
                    storedFile.ObjectKey,
                    storedFile.ContentType,
                    storedFile.FileSize,
                    actorId));
            }
            else
            {
                exercise.Attachment.SetFileMetadata(
                    storedFile.FileName,
                    storedFile.ObjectKey,
                    storedFile.ContentType,
                    storedFile.FileSize);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _fileStorage.DeleteIfExistsAsync(storedFile.ObjectKey, CancellationToken.None);
            throw;
        }

        if (oldObjectKey is not null)
        {
            await _fileStorage.DeleteIfExistsAsync(oldObjectKey, CancellationToken.None);
        }

        var refreshed = await GetExerciseAsync(exerciseId, true, cancellationToken);
        return Map(refreshed);
    }

    private async Task<Course> GetCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        await _courses.GetByIdAsync(courseId, false, cancellationToken)
        ?? throw new NotFoundException("Course was not found.");

    private async Task<Exercise> GetExerciseAsync(
        Guid exerciseId,
        bool includeAttachment,
        CancellationToken cancellationToken) =>
        await _exercises.GetByIdAsync(exerciseId, includeAttachment, cancellationToken)
        ?? throw new NotFoundException("Exercise was not found.");

    private static void ValidateExercise(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new BadRequestException("Exercise title must contain between 1 and 200 characters.");
        }
    }

    private static void ValidateId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new BadRequestException($"{fieldName} is required.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExerciseDto Map(Exercise exercise) => new(
        exercise.Id,
        exercise.CourseId,
        exercise.Title,
        exercise.Description,
        exercise.DueAt,
        exercise.Attachment is null
            ? null
            : new FileAttachmentDto(
                exercise.Attachment.Id,
                exercise.Attachment.FileName,
                exercise.Attachment.ObjectKey,
                exercise.Attachment.ContentType,
                exercise.Attachment.FileSize),
        exercise.CreatedAt,
        exercise.ModifiedAt);
}
