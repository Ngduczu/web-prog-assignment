using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.coursecontents;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public sealed class SubmissionApplicationTests
{
    [Fact]
    public async Task StudentCanRecoverOwnSubmissionAfterReload()
    {
        var fixture = CreateFixture(Role.Student);
        var submission = CreateSubmitted(fixture.Exercise.Id, fixture.Student.Id);
        fixture.Submissions.Items.Add(submission);

        var result = await fixture.Service.GetMineAsync(fixture.Exercise.Id);

        Assert.Equal(submission.Id, result.Id);
        Assert.Equal(SubmissionStatus.Submitted, result.Status);
    }

    [Fact]
    public async Task CourseTeacherCanListAndGradeSubmittedWork()
    {
        var fixture = CreateFixture(Role.Teacher);
        var submitted = CreateSubmitted(fixture.Exercise.Id, fixture.Student.Id);
        var draft = new Submission(fixture.Exercise.Id, Guid.NewGuid());
        fixture.Submissions.Items.AddRange([submitted, draft]);

        var listed = await fixture.Service.ListByExerciseAsync(
            fixture.Exercise.Id,
            new SubmissionQuery());
        var graded = await fixture.Service.GradeAsync(
            submitted.Id,
            new GradeSubmissionRequest(8.5m, " Good work "));

        Assert.Single(listed.Items);
        Assert.Equal(submitted.Id, listed.Items[0].Id);
        Assert.Equal(SubmissionStatus.Graded, graded.Status);
        Assert.Equal(8.5m, graded.Grade);
        Assert.Equal("Good work", graded.Feedback);
        Assert.Equal(fixture.Teacher.Id, graded.GradedById);
    }

    [Fact]
    public async Task StudentCannotUnsubmitGradedWork()
    {
        var fixture = CreateFixture(Role.Student);
        var submission = CreateSubmitted(fixture.Exercise.Id, fixture.Student.Id);
        submission.GradeSubmission(9m, null, fixture.Teacher.Id, fixture.Clock.UtcNow);
        fixture.Submissions.Items.Add(submission);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.UnsubmitAsync(submission.Id));
    }

    [Fact]
    public async Task CourseTeacherCanDownloadSubmissionAttachment()
    {
        var fixture = CreateFixture(Role.Teacher);
        var submission = CreateSubmitted(fixture.Exercise.Id, fixture.Student.Id);
        var attachment = new SubmissionAttachment(
            submission.Id,
            "answer.pdf",
            "submissions/answer.pdf",
            "application/pdf",
            3);
        typeof(Submission).GetProperty(nameof(Submission.Attachment))!.SetValue(submission, attachment);
        fixture.Submissions.Items.Add(submission);

        var result = await fixture.Service.GetContentAsync(submission.Id);
        await using var content = result.Content;

        Assert.Equal("answer.pdf", result.FileName);
        Assert.Equal(attachment.ObjectKey, fixture.Storage.LastOpenedObjectKey);
    }

    [Fact]
    public async Task UploadRejectsAndCleansNewFileWhenSubmissionIsSubmittedConcurrently()
    {
        var fixture = CreateFixture(Role.Student);
        var submission = new Submission(fixture.Exercise.Id, fixture.Student.Id);
        var attachment = new SubmissionAttachment(
            submission.Id,
            "old.pdf",
            "submissions/old.pdf",
            "application/pdf",
            3);
        typeof(Submission).GetProperty(nameof(Submission.Attachment))!.SetValue(submission, attachment);
        fixture.Submissions.Items.Add(submission);
        fixture.Transaction.BeforeExecute = submission.Submit;

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.UploadAsync(
            fixture.Exercise.Id,
            new FileUpload("new.pdf", "application/pdf", 3, new MemoryStream([1, 2, 3]))));

        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
        Assert.Equal("stored/file", fixture.Storage.LastDeletedObjectKey);
    }

    [Fact]
    public async Task FirstUploadReturnsPersistedAttachment()
    {
        var fixture = CreateFixture(Role.Student);

        var result = await fixture.Service.UploadAsync(
            fixture.Exercise.Id,
            new FileUpload("answer.pdf", "application/pdf", 3, new MemoryStream([1, 2, 3])));

        Assert.Equal(SubmissionStatus.Draft, result.Status);
        Assert.NotNull(result.Attachment);
        Assert.Equal("stored/file", result.Attachment.ObjectKey);
    }

    [Fact]
    public async Task GradeRechecksStatusInsideSerializableTransaction()
    {
        var fixture = CreateFixture(Role.Teacher);
        var submission = CreateSubmitted(fixture.Exercise.Id, fixture.Student.Id);
        fixture.Submissions.Items.Add(submission);
        fixture.Transaction.BeforeExecute = submission.Unsubmit;

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.GradeAsync(
            submission.Id,
            new GradeSubmissionRequest(9m, null)));

        Assert.Equal(SubmissionStatus.Draft, submission.Status);
        Assert.Null(submission.Grade);
    }

    private static Submission CreateSubmitted(Guid exerciseId, Guid studentId)
    {
        var submission = new Submission(exerciseId, studentId);
        submission.Submit();
        return submission;
    }

    private static Fixture CreateFixture(Role actorRole)
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var actor = actorRole == Role.Teacher ? teacher : student;
        var course = new Course("Course", null, teacher.Id, 30, CourseStatus.Open, teacher.Id);
        var clock = new FakeClock(new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var exercise = new Exercise(
            course.Id,
            "Exercise",
            null,
            clock.UtcNow.AddDays(1),
            teacher.Id,
            ExerciseStatus.Published);
        var enrollment = new Enrollment(student.Id, course.Id, EnrollmentStatus.Accepted);
        var submissions = new FakeSubmissionRepository();
        var storage = new FakeFileStorage();
        var transaction = new FakeTransactionManager();
        var service = new SubmissionService(
            submissions,
            new FakeExerciseRepository(exercise),
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(enrollment),
            storage,
            new FakeUnitOfWork(),
            new FakeCurrentUser(actor),
            clock,
            transaction);
        return new Fixture(service, submissions, storage, teacher, student, exercise, clock, transaction);
    }

    private static User CreateUser(Role role) => new(
        $"{Guid.NewGuid():N}@example.com",
        role.ToString(),
        null,
        null,
        role,
        AccountStatus.Active);

    private sealed record Fixture(
        SubmissionService Service,
        FakeSubmissionRepository Submissions,
        FakeFileStorage Storage,
        User Teacher,
        User Student,
        Exercise Exercise,
        FakeClock Clock,
        FakeTransactionManager Transaction);

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public List<Submission> Items { get; } = [];

        public Task<Submission?> GetByIdAsync(Guid id, bool includeAttachment, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<Submission?> GetByExerciseAndStudentAsync(Guid exerciseId, Guid studentId, bool includeAttachment, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.ExerciseId == exerciseId && item.StudentId == studentId));

        public Task<IReadOnlyList<Submission>> ListSubmittedByExerciseAsync(Guid exerciseId, int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Submission>>(Items
                .Where(item => item.ExerciseId == exerciseId && item.Status != SubmissionStatus.Draft)
                .Skip(skip)
                .Take(take)
                .ToArray());

        public Task<int> CountSubmittedByExerciseAsync(Guid exerciseId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Count(item => item.ExerciseId == exerciseId && item.Status != SubmissionStatus.Draft));

        public void Add(Submission submission) => Items.Add(submission);
        public void AddAttachment(SubmissionAttachment attachment) { }
    }

    private sealed class FakeExerciseRepository(Exercise exercise) : IExerciseRepository
    {
        public Task<Exercise?> GetByIdAsync(Guid id, bool includeAttachment, CancellationToken cancellationToken) =>
            Task.FromResult<Exercise?>(exercise.Id == id ? exercise : null);
        public Task<IReadOnlyList<Exercise>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Exercise>>(exercise.CourseId == courseId ? [exercise] : []);
        public void Add(Exercise value) { }
        public void AddAttachment(ExerciseAttachment attachment) { }
    }

    private sealed class FakeCourseRepository(Course course) : ICourseRepository
    {
        public Task<Course?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken) =>
            Task.FromResult<Course?>(course.Id == id ? course : null);
        public Task<IReadOnlyList<Course>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Course>>(ids.Contains(course.Id) ? [course] : []);
        public Task<IReadOnlyList<Course>> ListAsync(string? search, CourseStatus? status, Guid? teacherId, bool includeDeleted, int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Course>>([course]);
        public Task<int> CountAsync(string? search, CourseStatus? status, Guid? teacherId, bool includeDeleted, CancellationToken cancellationToken) => Task.FromResult(1);
        public void Add(Course value) { }
    }

    private sealed class FakeEnrollmentRepository(Enrollment enrollment) : IEnrollmentRepository
    {
        public Task<Enrollment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Enrollment?>(enrollment.Id == id ? enrollment : null);
        public Task<Enrollment?> GetByStudentAndCourseAsync(Guid studentId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<Enrollment?>(enrollment.StudentId == studentId && enrollment.CourseId == courseId ? enrollment : null);
        public Task<IReadOnlyList<Enrollment>> ListByStudentAsync(Guid studentId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Enrollment>>([enrollment]);
        public Task<IReadOnlyList<Enrollment>> ListByCourseAsync(Guid courseId, int skip, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Enrollment>>([enrollment]);
        public Task<int> CountByCourseAsync(Guid courseId, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<int> CountAcceptedByCourseAsync(Guid courseId, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<IReadOnlyDictionary<Guid, int>> CountAcceptedByCoursesAsync(
            IReadOnlyCollection<Guid> courseIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(
                courseIds.Contains(enrollment.CourseId) && enrollment.Status == EnrollmentStatus.Accepted
                    ? new Dictionary<Guid, int> { [enrollment.CourseId] = 1 }
                    : new Dictionary<Guid, int>());
        public void Add(Enrollment value) { }
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public string? LastOpenedObjectKey { get; private set; }
        public string? LastDeletedObjectKey { get; private set; }
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            LastOpenedObjectKey = objectKey;
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }
        public Task<StoredFile> UploadAsync(FileUpload upload, string directory, long? maximumSizeBytes = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredFile(upload.FileName, "stored/file", upload.ContentType, upload.Length));
        public Task DeleteIfExistsAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            LastDeletedObjectKey = objectKey;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUser(User user) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = user.Id;
        public Role? Role { get; } = user.Role;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeTransactionManager : ITransactionManager
    {
        public Action? BeforeExecute { get; set; }

        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            BeforeExecute?.Invoke();
            BeforeExecute = null;
            return action(cancellationToken);
        }
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
