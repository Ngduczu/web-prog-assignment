using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.abstractions.time;
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
    private readonly IEnrollmentRepository _enrollments;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public SubmissionService(
        ISubmissionRepository submissions,
        IExerciseRepository exercises,
        IEnrollmentRepository enrollments,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IClock clock)
    {
        _submissions = submissions;
        _exercises = exercises;
        _enrollments = enrollments;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
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

        if (exercise.DueAt is not null && exercise.DueAt <= _clock.UtcNow)
        {
            throw new ConflictException("The exercise deadline has passed.");
        }

        var submission = await _submissions.GetByExerciseAndStudentAsync(
            exerciseId,
            studentId,
            true,
            cancellationToken);

        if (submission?.Status == SubmissionStatus.Submitted)
        {
            throw new ConflictException("Submitted work must be unsubmitted before replacing its file.");
        }

        var isNewSubmission = submission is null;
        submission ??= new Submission(exerciseId, studentId);
        var storedFile = await _fileStorage.UploadAsync(
            upload,
            $"courses/{exercise.CourseId:N}/submissions/{submission.Id:N}",
            cancellationToken: cancellationToken);
        var oldObjectKey = submission.Attachment?.ObjectKey;

        try
        {
            if (submission.Attachment is null)
            {
                if (isNewSubmission)
                {
                    _submissions.Add(submission);
                }

                _submissions.AddAttachment(new SubmissionAttachment(
                    submission.Id,
                    storedFile.FileName,
                    storedFile.ObjectKey,
                    storedFile.ContentType,
                    storedFile.FileSize));
            }
            else
            {
                submission.Attachment.SetFileMetadata(
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

        var refreshed = await _submissions.GetByExerciseAndStudentAsync(
            exerciseId,
            studentId,
            true,
            cancellationToken) ?? submission;
        return Map(refreshed);
    }

    public Task<SubmissionDto> SubmitAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(submissionId, true, cancellationToken);

    public Task<SubmissionDto> UnsubmitAsync(
        Guid submissionId,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(submissionId, false, cancellationToken);

    private async Task<SubmissionDto> ChangeStatusAsync(
        Guid submissionId,
        bool submit,
        CancellationToken cancellationToken)
    {
        ValidateId(submissionId, "Submission id");
        var studentId = EnsureStudent();
        var submission = await _submissions.GetByIdAsync(submissionId, true, cancellationToken)
            ?? throw new NotFoundException("Submission was not found.");

        if (submission.StudentId != studentId)
        {
            throw new ForbiddenException("Students can only manage their own submissions.");
        }

        var exercise = await GetExerciseAsync(submission.ExerciseId, cancellationToken);
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(submission);
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
        submission.Status,
        submission.SubmittedAt,
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
