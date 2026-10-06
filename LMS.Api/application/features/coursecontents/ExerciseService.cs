using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.courses;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.coursecontents;

public sealed class ExerciseService : IExerciseService
{
    private readonly IExerciseRepository _exercises;
    private readonly ICourseRepository _courses;
    private readonly IEnrollmentRepository _enrollments;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IClock? _clock;
    private readonly IFileCleanupQueue? _fileCleanup;

    public ExerciseService(
        IExerciseRepository exercises,
        ICourseRepository courses,
        IEnrollmentRepository enrollments,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IClock? clock = null,
        IFileCleanupQueue? fileCleanup = null)
    {
        _exercises = exercises;
        _courses = courses;
        _enrollments = enrollments;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _fileCleanup = fileCleanup;
    }

    public async Task<ExerciseDto> CreateAsync(
        Guid courseId,
        CreateExerciseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        ValidateExercise(request.Title, request.Description, request.DueAt);
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
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        var exercises = await _exercises.ListByCourseAsync(courseId, cancellationToken);
        if (!CourseAuthorization.IsCourseManager(_currentUser, course))
        {
            exercises = exercises
                .Where(exercise => exercise.Status is ExerciseStatus.Published or ExerciseStatus.Closed)
                .ToArray();
        }
        return exercises.Select(Map).ToArray();
    }

    public async Task<ExerciseDto> GetAsync(
        Guid exerciseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        var exercise = await GetExerciseAsync(exerciseId, true, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        EnsureStudentCanAccessReleasedExercise(course, exercise);
        return Map(exercise);
    }

    public async Task<ExerciseDto> UpdateAsync(
        Guid exerciseId,
        UpdateExerciseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        ValidateExercise(request.Title, request.Description, request.DueAt);
        var exercise = await GetExerciseAsync(exerciseId, false, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);
        if (exercise.Status != ExerciseStatus.Draft)
        {
            throw new ConflictException("Only draft exercises can be updated.");
        }
        exercise.SetTitle(request.Title.Trim());
        exercise.SetDescription(NormalizeOptional(request.Description));
        exercise.SetDueAt(request.DueAt);
        exercise.SetModifiedBy(actorId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(exercise);
    }

    public async Task<FileContent> GetAttachmentContentAsync(
        Guid exerciseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        var exercise = await GetExerciseAsync(exerciseId, true, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        EnsureStudentCanAccessReleasedExercise(course, exercise);
        var attachment = exercise.Attachment
            ?? throw new NotFoundException("Exercise attachment was not found.");
        var content = await _fileStorage.OpenReadAsync(attachment.ObjectKey, cancellationToken);
        return new FileContent(content, attachment.FileName, attachment.ContentType);
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
        if (exercise.Status != ExerciseStatus.Draft)
        {
            throw new ConflictException("Only draft exercises can have their attachment changed.");
        }

        var storedFile = await _fileStorage.UploadAsync(
            upload,
            $"courses/{course.Id:N}/exercises/{exercise.Id:N}",
            FileUploadPolicy.MaximumCourseFileSizeBytes,
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
            await CleanupFileAsync(storedFile.ObjectKey);
            throw;
        }

        if (oldObjectKey is not null)
        {
            await CleanupFileAsync(oldObjectKey);
        }

        var refreshed = await GetExerciseAsync(exerciseId, true, cancellationToken);
        return Map(refreshed);
    }

    public Task<ExerciseDto> PublishAsync(Guid exerciseId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(exerciseId, ExerciseStatus.Published, cancellationToken);

    public Task<ExerciseDto> CloseAsync(Guid exerciseId, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(exerciseId, ExerciseStatus.Closed, cancellationToken);

    public async Task DeleteAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        var exercise = await GetExerciseAsync(exerciseId, true, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        CourseAuthorization.EnsureCanManage(_currentUser, course);
        if (exercise.Status != ExerciseStatus.Draft)
        {
            throw new ConflictException("Only draft exercises can be deleted.");
        }

        var objectKey = exercise.Attachment?.ObjectKey;
        _exercises.Remove(exercise);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (objectKey is not null)
        {
            await CleanupFileAsync(objectKey);
        }
    }

    private Task CleanupFileAsync(string objectKey)
    {
        if (_fileCleanup is null)
        {
            return _fileStorage.DeleteIfExistsAsync(objectKey, CancellationToken.None);
        }

        _fileCleanup.Enqueue(objectKey);
        return Task.CompletedTask;
    }

    private async Task<ExerciseDto> ChangeStatusAsync(
        Guid exerciseId,
        ExerciseStatus targetStatus,
        CancellationToken cancellationToken)
    {
        ValidateId(exerciseId, "Exercise id");
        var exercise = await GetExerciseAsync(exerciseId, true, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);
        if (targetStatus == ExerciseStatus.Published)
        {
            if (exercise.Status != ExerciseStatus.Draft)
            {
                throw new ConflictException("Only draft exercises can be published.");
            }

            if (exercise.DueAt is not null && exercise.DueAt <= UtcNow)
            {
                throw new ConflictException("An exercise cannot be published after its deadline.");
            }
        }
        else if (exercise.Status != ExerciseStatus.Published)
        {
            throw new ConflictException("Only published exercises can be closed.");
        }

        exercise.SetStatus(targetStatus);
        exercise.SetModifiedBy(actorId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(exercise);
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

    private static void ValidateExercise(string title, string? description, DateTime? dueAt)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new BadRequestException("Exercise title must contain between 1 and 200 characters.");
        }

        if (description?.Trim().Length > 4000)
        {
            throw new BadRequestException("Exercise description cannot exceed 4000 characters.");
        }

        if (dueAt is not null && dueAt.Value.Kind != DateTimeKind.Utc)
        {
            throw new BadRequestException("Exercise deadline must use UTC.");
        }
    }

    private void EnsureStudentCanAccessReleasedExercise(Course course, Exercise exercise)
    {
        if (!CourseAuthorization.IsCourseManager(_currentUser, course) &&
            exercise.Status is not ExerciseStatus.Published and not ExerciseStatus.Closed)
        {
            throw new NotFoundException("Exercise was not found.");
        }
    }

    private DateTime UtcNow => _clock?.UtcNow ?? DateTime.UtcNow;

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
        exercise.Status,
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
