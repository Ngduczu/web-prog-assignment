using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.common;
using LMS.Api.application.features.courses;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.coursecontents;

public sealed class SubmissionService : ISubmissionService
{
    private readonly ISubmissionRepository _submissions;
    private readonly IExerciseRepository _exercises;
    private readonly ICourseRepository _courses;
    private readonly IEnrollmentRepository _enrollments;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ITransactionManager _transactionManager;
    private readonly IFileCleanupQueue? _fileCleanup;
    private readonly ILogger<SubmissionService>? _logger;

    public SubmissionService(
        ISubmissionRepository submissions,
        IExerciseRepository exercises,
        ICourseRepository courses,
        IEnrollmentRepository enrollments,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IClock clock,
        ITransactionManager transactionManager,
        IFileCleanupQueue? fileCleanup = null,
        ILogger<SubmissionService>? logger = null)
    {
        _submissions = submissions;
        _exercises = exercises;
        _courses = courses;
        _enrollments = enrollments;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _transactionManager = transactionManager;
        _fileCleanup = fileCleanup;
        _logger = logger;
    }

    public async Task<SubmissionDto> UploadAsync(
        Guid exerciseId,
        FileUpload upload,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        FileUploadPolicy.Validate(upload, FileUploadPolicy.MaximumCourseFileSizeBytes);
        var studentId = EnsureStudent();
        var exercise = await GetExerciseAsync(exerciseId, cancellationToken);
        await EnsureAcceptedEnrollmentAsync(studentId, exercise.CourseId, cancellationToken);

        if (exercise.Status != ExerciseStatus.Published)
        {
            throw new ConflictException("Submissions are only accepted for published exercises.");
        }

        if (exercise.DueAt is not null && exercise.DueAt <= _clock.UtcNow)
        {
            throw new ConflictException("The exercise deadline has passed.");
        }

        var storedFile = await _fileStorage.UploadAsync(
            upload,
            $"courses/{exercise.CourseId:N}/submissions/{studentId:N}",
            FileUploadPolicy.MaximumCourseFileSizeBytes,
            cancellationToken: cancellationToken);
        string? oldObjectKey = null;
        SubmissionDto? result = null;

        try
        {
            await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
            {
                var submission = await _submissions.GetByExerciseAndStudentAsync(
                    exerciseId,
                    studentId,
                    true,
                    transactionCancellationToken);
                var isNewSubmission = submission is null;
                submission ??= new Submission(exerciseId, studentId);

                oldObjectKey = submission.Attachment?.ObjectKey;
                try
                {
                    if (submission.Attachment is null)
                    {
                        var attachment = new SubmissionAttachment(
                            submission.Id,
                            storedFile.FileName,
                            storedFile.ObjectKey,
                            storedFile.ContentType,
                            storedFile.FileSize);
                        submission.AddAttachment(attachment, _clock.UtcNow);
                        if (isNewSubmission)
                        {
                            _submissions.Add(submission);
                        }

                        _submissions.AddAttachment(attachment);
                    }
                    else
                    {
                        submission.RecordAttachmentChange(_clock.UtcNow);
                        submission.Attachment.SetFileMetadata(
                            storedFile.FileName,
                            storedFile.ObjectKey,
                            storedFile.ContentType,
                            storedFile.FileSize);
                    }
                }
                catch (InvalidOperationException exception)
                {
                    throw new ConflictException(exception.Message);
                }

                if (oldObjectKey is not null && _fileCleanup is not null)
                {
                    _fileCleanup.Enqueue(oldObjectKey);
                }

                await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
                result = Map(submission);
            }, cancellationToken);
        }
        catch
        {
            await CleanupFileBestEffortAsync(storedFile.ObjectKey);
            throw;
        }

        if (oldObjectKey is not null && _fileCleanup is null)
        {
            await CleanupFileBestEffortAsync(oldObjectKey);
        }

        return result!;
    }

    private async Task CleanupFileBestEffortAsync(string objectKey)
    {
        if (_fileCleanup is not null)
        {
            try
            {
                await _fileCleanup.EnqueueImmediatelyAsync(objectKey, CancellationToken.None);
                return;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(
                    exception,
                    "Could not enqueue file cleanup for {ObjectKey}; falling back to direct deletion.",
                    objectKey);
            }
        }

        try
        {
            await _fileStorage.DeleteIfExistsAsync(objectKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Best-effort file cleanup failed for {ObjectKey}.", objectKey);
        }
    }

    public Task<SubmissionDto> SubmitAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(submissionId, true, cancellationToken);

    public Task<SubmissionDto> UnsubmitAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(submissionId, false, cancellationToken);

    public async Task<SubmissionDto> GetMineAsync(
        Guid exerciseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        var studentId = EnsureStudent();
        var exercise = await GetExerciseAsync(exerciseId, cancellationToken);
        await EnsureAcceptedEnrollmentAsync(studentId, exercise.CourseId, cancellationToken);
        var submission = await _submissions.GetByExerciseAndStudentAsync(
            exerciseId,
            studentId,
            true,
            cancellationToken) ?? throw new NotFoundException("Submission was not found.");
        return Map(submission);
    }

    public async Task<PagedResult<SubmissionDto>> ListByExerciseAsync(
        Guid exerciseId,
        SubmissionQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateId(exerciseId, "Exercise id");
        ValidatePage(query.Page, query.PageSize);
        var exercise = await GetExerciseAsync(exerciseId, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        CourseAuthorization.EnsureCanManage(_currentUser, course);
        var submissions = await _submissions.ListSubmittedByExerciseAsync(
            exerciseId,
            (query.Page - 1) * query.PageSize,
            query.PageSize,
            cancellationToken);
        var count = await _submissions.CountSubmittedByExerciseAsync(exerciseId, cancellationToken);
        return new PagedResult<SubmissionDto>(
            submissions.Select(Map).ToArray(),
            query.Page,
            query.PageSize,
            count);
    }

    public async Task<FileContent> GetContentAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(submissionId, "Submission id");
        var submission = await _submissions.GetByIdAsync(submissionId, true, cancellationToken)
            ?? throw new NotFoundException("Submission was not found.");
        var exercise = await GetExerciseAsync(submission.ExerciseId, cancellationToken);
        var course = await GetCourseAsync(exercise.CourseId, cancellationToken);
        var actorId = _currentUser.GetRequiredUserId();
        if (_currentUser.GetRequiredRole() == Role.Student)
        {
            if (submission.StudentId != actorId)
            {
                throw new ForbiddenException("Students can only download their own submissions.");
            }

            await EnsureAcceptedEnrollmentAsync(actorId, exercise.CourseId, cancellationToken);
        }
        else
        {
            CourseAuthorization.EnsureCanManage(_currentUser, course);
        }

        var attachment = submission.Attachment
            ?? throw new NotFoundException("Submission attachment was not found.");
        var content = await _fileStorage.OpenReadAsync(attachment.ObjectKey, cancellationToken);
        return new FileContent(content, attachment.FileName, attachment.ContentType);
    }

    public async Task<SubmissionDto> GradeAsync(
        Guid submissionId,
        GradeSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(submissionId, "Submission id");
        if (request.Grade is < 0 or > 10 || request.Feedback?.Trim().Length > 4000)
        {
            throw new BadRequestException("Grade must be between 0 and 10 and feedback cannot exceed 4000 characters.");
        }

        SubmissionDto? result = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            var submission = await _submissions.GetByIdAsync(submissionId, true, transactionCancellationToken)
                ?? throw new NotFoundException("Submission was not found.");
            var exercise = await GetExerciseAsync(submission.ExerciseId, transactionCancellationToken);
            var course = await GetCourseAsync(exercise.CourseId, transactionCancellationToken);
            var graderId = CourseAuthorization.EnsureCanManage(_currentUser, course);
            try
            {
                submission.GradeSubmission(
                    request.Grade,
                    string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim(),
                    graderId,
                    _clock.UtcNow);
            }
            catch (InvalidOperationException exception)
            {
                throw new ConflictException(exception.Message);
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            result = Map(submission);
        }, cancellationToken);
        return result!;
    }

    private async Task<SubmissionDto> ChangeStatusAsync(
        Guid submissionId,
        bool submit,
        CancellationToken cancellationToken)
    {
        SubmissionDto? result = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            ValidateId(submissionId, "Submission id");
            var studentId = EnsureStudent();
            var submission = await _submissions.GetByIdAsync(submissionId, true, transactionCancellationToken)
                ?? throw new NotFoundException("Submission was not found.");

            if (submission.StudentId != studentId)
            {
                throw new ForbiddenException("Students can only manage their own submissions.");
            }

            var exercise = await GetExerciseAsync(submission.ExerciseId, transactionCancellationToken);
            if (exercise.Status != ExerciseStatus.Published)
            {
                throw new ConflictException("Submissions can only change while the exercise is published.");
            }
            if (exercise.DueAt is not null && exercise.DueAt <= _clock.UtcNow)
            {
                throw new ConflictException("The exercise deadline has passed.");
            }

            if (submit)
            {
                if (submission.Attachment is null)
                {
                    throw new ConflictException("A file must be uploaded before submitting work.");
                }

                if (submission.Status != SubmissionStatus.Draft)
                {
                    throw new ConflictException("Only draft submissions can be submitted.");
                }

                submission.Submit();
            }
            else
            {
                if (submission.Status != SubmissionStatus.Submitted)
                {
                    throw new ConflictException("Only submitted work can be unsubmitted.");
                }

                submission.Unsubmit();
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            result = Map(submission);
        }, cancellationToken);
        return result!;
    }

    private Guid EnsureStudent()
    {
        if (_currentUser.GetRequiredRole() != Role.Student)
        {
            throw new ForbiddenException("Only students can manage submissions.");
        }

        return _currentUser.GetRequiredUserId();
    }

    private async Task EnsureAcceptedEnrollmentAsync(
        Guid studentId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var enrollment = await _enrollments.GetByStudentAndCourseAsync(studentId, courseId, cancellationToken);
        if (enrollment?.Status != EnrollmentStatus.Accepted)
        {
            throw new ForbiddenException("An accepted course enrollment is required.");
        }
    }

    private async Task<Exercise> GetExerciseAsync(Guid exerciseId, CancellationToken cancellationToken) =>
        await _exercises.GetByIdAsync(exerciseId, false, cancellationToken)
        ?? throw new NotFoundException("Exercise was not found.");

    private async Task<Course> GetCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        await _courses.GetByIdAsync(courseId, false, cancellationToken)
        ?? throw new NotFoundException("Course was not found.");

    private static void ValidatePage(int page, int pageSize)
    {
        if (page <= 0 || pageSize is < 1 or > 100)
        {
            throw new BadRequestException("Page must be positive and page size must be between 1 and 100.");
        }
    }

    private static void ValidateId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new BadRequestException($"{fieldName} is required.");
        }
    }

    private static SubmissionDto Map(Submission submission) => new(
        submission.Id,
        submission.ExerciseId,
        submission.StudentId,
        submission.Student is null
            ? null
            : new SubmissionStudentDto(
                submission.Student.Id,
                submission.Student.FullName,
                submission.Student.Email),
        submission.Status,
        submission.SubmittedAt,
        submission.Grade,
        submission.Feedback,
        submission.GradedAt,
        submission.GradedById,
        submission.Attachment is null
            ? null
            : new FileAttachmentDto(
                submission.Attachment.Id,
                submission.Attachment.FileName,
                submission.Attachment.ObjectKey,
                submission.Attachment.ContentType,
                submission.Attachment.FileSize),
        submission.CreatedAt,
        submission.ModifiedAt);
}
