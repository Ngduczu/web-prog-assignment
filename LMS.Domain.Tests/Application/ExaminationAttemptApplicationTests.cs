using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.examinationattempts;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.Enums.Questions;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public class ExaminationAttemptApplicationTests
{
    [Fact]
    public async Task AcceptedStudentCanStartAnswerAndReceiveScoreFromSnapshot()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);
        var multipleChoice = fixture.Examination.Questions.Single(question => question.Type == QuestionType.MultipleChoice);
        var fillInBlank = fixture.Examination.Questions.Single(question => question.Type == QuestionType.FillInBlank);

        await fixture.Service.SaveMultipleChoiceAnswerAsync(
            started.Id,
            multipleChoice.Id,
            multipleChoice.CorrectOptionId!.Value);
        await fixture.Service.SaveFillInBlankAnswerAsync(
            started.Id,
            fillInBlank.Id,
            [new StudentFillInBlankAnswerInput(0, " hAnOi ")]);
        var submitted = await fixture.Service.SubmitAsync(started.Id);

        Assert.Equal(ExaminationAttemptStatus.Submitted, submitted.Status);
        Assert.Equal(ExaminationSubmitReason.StudentSubmitted, submitted.SubmitReason);
        Assert.Equal(2, submitted.CorrectAnswers);
        Assert.Equal(2, submitted.TotalQuestions);
        Assert.Equal(10m, submitted.Score);
    }

    [Fact]
    public async Task ReachingViolationLimitAutoSubmitsAttempt()
    {
        var fixture = CreateFixture(maxViolations: 1);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);

        var result = await fixture.Service.RecordViolationAsync(
            started.Id,
            ExaminationViolationType.TabChanged);

        Assert.Equal(ExaminationAttemptStatus.AutoSubmitted, result.Status);
        Assert.Equal(ExaminationSubmitReason.MaxViolationsExceeded, result.SubmitReason);
        Assert.Equal(1, result.ViolationCount);
        Assert.Single(fixture.Attempts.Violations);
    }

    [Fact]
    public async Task DisabledViolationTypeIsRejectedWithoutChangingAttempt()
    {
        var fixture = CreateFixture();
        fixture.Examination.SetAntiCheat(true, 3, 5, true, true, false, false, 10);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.RecordViolationAsync(started.Id, ExaminationViolationType.TabChanged));

        Assert.Equal(0, fixture.Attempts.Items.Single().ViolationCount);
        Assert.Empty(fixture.Attempts.Violations);
    }

    [Fact]
    public async Task FullscreenGraceDoesNotCountShortExitButCountsLongExit()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);

        var withinGrace = await fixture.Service.RecordViolationAsync(
            started.Id,
            ExaminationViolationType.ExitFullscreen,
            durationSeconds: 3);
        var beyondGrace = await fixture.Service.RecordViolationAsync(
            started.Id,
            ExaminationViolationType.ExitFullscreen,
            durationSeconds: 4);

        Assert.Equal(0, withinGrace.ViolationCount);
        Assert.Equal(1, beyondGrace.ViolationCount);
        Assert.Single(fixture.Attempts.Violations);
    }

    [Fact]
    public async Task DisconnectAfterDurationExpiresAutoSubmitsAttempt()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(61);

        var result = await fixture.Service.DisconnectAsync(started.Id);

        Assert.Equal(ExaminationAttemptStatus.AutoSubmitted, result.Status);
        Assert.Equal(ExaminationSubmitReason.DurationExpired, result.SubmitReason);
    }

    [Fact]
    public async Task ReconnectUsesTheDeadlineThatExpiredFirst()
    {
        var fixture = CreateFixture(maxDisconnectMinutes: 120);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);
        await fixture.Service.DisconnectAsync(started.Id);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(61);

        var result = await fixture.Service.ReconnectAsync(started.Id);

        Assert.Equal(ExaminationAttemptStatus.AutoSubmitted, result.Status);
        Assert.Equal(ExaminationSubmitReason.DurationExpired, result.SubmitReason);
    }

    [Fact]
    public async Task StudentMustKeepAcceptedEnrollmentDuringAttempt()
    {
        var fixture = CreateFixture(enrollmentAccepted: false);
        var attempt = new ExaminationAttempt(
            fixture.Examination.Id,
            fixture.Student.Id,
            fixture.Clock.UtcNow,
            fixture.Examination.Questions.Count);
        fixture.Attempts.Add(attempt);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.GetQuestionsAsync(attempt.Id));
    }

    [Fact]
    public async Task ActiveAndSubmittedAttemptHideScoreWhenImmediateReleaseIsDisabled()
    {
        var fixture = CreateFixture(showScoreImmediately: false);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);
        Assert.Null(started.TotalQuestions);
        Assert.Null(started.CorrectAnswers);
        Assert.Null(started.Score);

        var submitted = await fixture.Service.SubmitAsync(started.Id);

        Assert.Equal(ExaminationAttemptStatus.Submitted, submitted.Status);
        Assert.Null(submitted.CorrectAnswers);
        Assert.Null(submitted.TotalQuestions);
        Assert.Null(submitted.Score);
    }

    [Fact]
    public async Task ActiveAttemptHidesResultEvenWhenImmediateReleaseIsEnabled()
    {
        var fixture = CreateFixture(showScoreImmediately: true);

        var started = await fixture.Service.StartAsync(fixture.Examination.Id);

        Assert.Equal(ExaminationAttemptStatus.InProgress, started.Status);
        Assert.Null(started.CorrectAnswers);
        Assert.Null(started.TotalQuestions);
        Assert.Null(started.Score);
    }

    [Fact]
    public async Task HiddenScoreIsReleasedAfterExaminationDueTime()
    {
        var fixture = CreateFixture(showScoreImmediately: false);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);
        await fixture.Service.SubmitAsync(started.Id);
        fixture.Clock.UtcNow = fixture.Examination.DueAt;

        var result = await fixture.Service.GetMineAsync(fixture.Examination.Id);

        Assert.NotNull(result.CorrectAnswers);
        Assert.NotNull(result.TotalQuestions);
        Assert.NotNull(result.Score);
    }

    [Fact]
    public async Task StudentCannotStartAnExaminationTwice()
    {
        var fixture = CreateFixture();
        var first = await fixture.Service.StartAsync(fixture.Examination.Id);
        await fixture.Service.SubmitAsync(first.Id);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.StartAsync(fixture.Examination.Id));

        Assert.Equal("A student can attempt an examination only once.", exception.Message);
    }

    [Fact]
    public async Task RecoverAttemptReturnsNotFoundWhenStudentHasNotStartedExamination()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetMineAsync(fixture.Examination.Id));
    }

    [Theory]
    [InlineData(ExaminationSubmitReason.DurationExpired)]
    [InlineData(ExaminationSubmitReason.DisconnectTimeout)]
    [InlineData(ExaminationSubmitReason.MaxViolationsExceeded)]
    [InlineData(ExaminationSubmitReason.ExaminationClosed)]
    public async Task AdminCannotAutoSubmitBeforeRequestedReasonApplies(
        ExaminationSubmitReason reason)
    {
        var fixture = CreateFixture(currentUserRole: Role.Admin);
        var attempt = new ExaminationAttempt(
            fixture.Examination.Id,
            fixture.Student.Id,
            fixture.Clock.UtcNow,
            fixture.Examination.Questions.Count);
        fixture.Attempts.Add(attempt);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.AutoSubmitAsync(attempt.Id, reason));

        Assert.Equal(ExaminationAttemptStatus.InProgress, attempt.Status);
        Assert.Null(attempt.SubmitReason);
    }

    [Fact]
    public async Task AdminCanAutoSubmitAfterDurationDeadline()
    {
        var fixture = CreateFixture(currentUserRole: Role.Admin);
        var attempt = new ExaminationAttempt(
            fixture.Examination.Id,
            fixture.Student.Id,
            fixture.Clock.UtcNow,
            fixture.Examination.Questions.Count);
        fixture.Attempts.Add(attempt);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(61);

        var result = await fixture.Service.AutoSubmitAsync(
            attempt.Id,
            ExaminationSubmitReason.DurationExpired);

        Assert.Equal(ExaminationAttemptStatus.AutoSubmitted, result.Status);
        Assert.Equal(ExaminationSubmitReason.DurationExpired, result.SubmitReason);
    }

    [Fact]
    public async Task GetQuestionsKeepsSnapshotOrderWhenShuffleIsDisabled()
    {
        var fixture = CreateFixture(shuffleQuestions: false);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);

        var questions = await fixture.Service.GetQuestionsAsync(started.Id);

        Assert.Equal(
            fixture.Examination.Questions.OrderBy(question => question.Order).Select(question => question.Id),
            questions.Select(question => question.Id));
        Assert.Equal([0, 1], questions.Select(question => question.Order).ToArray());
    }

    [Fact]
    public async Task GetQuestionsAppliesStablePerAttemptShuffle()
    {
        var fixture = CreateFixture(shuffleQuestions: true);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);

        var first = await fixture.Service.GetQuestionsAsync(started.Id);
        var second = await fixture.Service.GetQuestionsAsync(started.Id);

        Assert.Equal(first.Select(question => question.Id), second.Select(question => question.Id));
        Assert.Equal(first.Select(question => question.Order), second.Select(question => question.Order));

        var multipleChoice = first.Single(question => question.Type == QuestionType.MultipleChoice);
        Assert.Equal([0, 1], multipleChoice.Options.Select(option => option.Order).ToArray());
        Assert.Equal(
            multipleChoice.Options.Select(option => option.Id).ToArray(),
            second.Single(question => question.Id == multipleChoice.Id).Options.Select(option => option.Id).ToArray());
    }

    private static Fixture CreateFixture(
        int maxViolations = 5,
        bool enrollmentAccepted = true,
        bool showScoreImmediately = true,
        bool shuffleQuestions = false,
        int maxDisconnectMinutes = 10,
        Role currentUserRole = Role.Student)
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var course = new Course("Course", null, teacher.Id, 30, CourseStatus.Open, teacher.Id);
        var now = new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var examination = new Examination(
            course.Id,
            "Examination",
            null,
            now.AddMinutes(-10),
            now.AddHours(2),
            60,
            teacher.Id);
        examination.SetDisplayOptions(shuffleQuestions, showScoreImmediately);
        examination.SetAntiCheat(true, 3, maxViolations, true, true, true, false, maxDisconnectMinutes);
        examination.SetStatus(ExaminationStatus.Published);

        var multipleChoice = new ExaminationQuestion(
            examination.Id,
            null,
            "2 + 2 = ?",
            QuestionType.MultipleChoice,
            QuestionDifficulty.Easy,
            true,
            0);
        var wrongOption = new ExaminationMultipleChoiceOption(multipleChoice.Id, "3", 0);
        var correctOption = new ExaminationMultipleChoiceOption(multipleChoice.Id, "4", 1);
        multipleChoice.MultipleChoiceOptions.Add(wrongOption);
        multipleChoice.MultipleChoiceOptions.Add(correctOption);
        multipleChoice.SetCorrectOption(correctOption.Id);
        var fillInBlank = new ExaminationQuestion(
            examination.Id,
            null,
            "Vietnam's capital is ____.",
            QuestionType.FillInBlank,
            QuestionDifficulty.Easy,
            false,
            1);
        fillInBlank.FillInBlankAnswerKeys.Add(new ExaminationFillInBlankAnswerKey(fillInBlank.Id, 0, "Hanoi"));
        examination.Questions.Add(multipleChoice);
        examination.Questions.Add(fillInBlank);

        var enrollment = new Enrollment(student.Id, course.Id);
        if (enrollmentAccepted)
        {
            enrollment.Accept();
        }
        var attempts = new FakeExaminationAttemptRepository();
        var clock = new FakeClock(now);
        var service = new ExaminationAttemptService(
            attempts,
            new FakeExaminationRepository(examination),
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(enrollment),
            new FakeUnitOfWork(),
            new FakeTransactionManager(),
            new FakeCurrentUser(currentUserRole == Role.Student ? student : CreateUser(currentUserRole)),
            clock);
        return new Fixture(service, student, examination, attempts, clock);
    }

    private static User CreateUser(Role role) => new(
        $"{Guid.NewGuid():N}@example.com",
        role.ToString(),
        null,
        null,
        role,
        AccountStatus.Active);

    private sealed record Fixture(
        ExaminationAttemptService Service,
        User Student,
        Examination Examination,
        FakeExaminationAttemptRepository Attempts,
        FakeClock Clock);

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(User user)
        {
            UserId = user.Id;
            Role = user.Role;
        }

        public bool IsAuthenticated => true;
        public Guid? UserId { get; }
        public Role? Role { get; }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeTransactionManager : ITransactionManager
    {
        public Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default) => action(cancellationToken);
    }

    private sealed class FakeExaminationAttemptRepository : IExaminationAttemptRepository
    {
        public List<ExaminationAttempt> Items { get; } = [];
        public List<ExaminationViolation> Violations { get; } = [];

        public Task<ExaminationAttempt?> GetByIdAsync(
            Guid id,
            bool includeAnswers,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(attempt => attempt.Id == id));

        public Task<ExaminationAttempt?> GetActiveAsync(
            Guid examinationId,
            Guid studentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(attempt =>
                attempt.ExaminationId == examinationId &&
                attempt.StudentId == studentId &&
                attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected));

        public Task<ExaminationAttempt?> GetLatestAsync(
            Guid examinationId,
            Guid studentId,
            bool includeAnswers,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items
                .Where(attempt =>
                    attempt.ExaminationId == examinationId &&
                    attempt.StudentId == studentId)
                .OrderByDescending(attempt =>
                    attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected)
                .ThenByDescending(attempt => attempt.StartedAt)
                .ThenByDescending(attempt => attempt.Id)
                .FirstOrDefault());

        public Task<IReadOnlyList<ExaminationAttempt>> ListActiveByExaminationAsync(
            Guid examinationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExaminationAttempt>>(Items.Where(attempt =>
                attempt.ExaminationId == examinationId &&
                attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected).ToArray());

        public Task<StudentAnswer?> GetAnswerAsync(
            Guid attemptId,
            Guid examinationQuestionId,
            bool includeFillInBlankAnswers,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items
                .Single(attempt => attempt.Id == attemptId)
                .Answers
                .SingleOrDefault(answer => answer.ExaminationQuestionId == examinationQuestionId));

        public Task<IReadOnlyList<ExaminationAttempt>> ListExpiredAsync(
            DateTime now,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExaminationAttempt>>(Items
                .Where(attempt =>
                    attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected)
                .Take(take)
                .ToArray());

        public void Add(ExaminationAttempt attempt)
        {
            Items.Add(attempt);
        }

        public void AddAnswer(StudentAnswer answer)
        {
            Items.Single(attempt => attempt.Id == answer.ExaminationAttemptId).Answers.Add(answer);
        }

        public void AddFillInBlankAnswer(StudentFillInBlankAnswer answer)
        {
        }

        public void RemoveFillInBlankAnswer(StudentFillInBlankAnswer answer)
        {
        }

        public void AddViolation(ExaminationViolation violation)
        {
            Violations.Add(violation);
            Items.Single(attempt => attempt.Id == violation.ExaminationAttemptId).Violations.Add(violation);
        }
    }

    private sealed class FakeExaminationRepository : IExaminationRepository
    {
        private readonly Examination _examination;

        public FakeExaminationRepository(Examination examination)
        {
            _examination = examination;
        }

        public Task<Examination?> GetByIdAsync(
            Guid id,
            bool includeDefinition,
            CancellationToken cancellationToken) =>
            Task.FromResult<Examination?>(_examination.Id == id ? _examination : null);

        public Task<IReadOnlyList<Examination>> ListByCourseAsync(
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Examination>>([_examination]);

        public void Add(Examination examination)
        {
        }
    }

    private sealed class FakeCourseRepository : ICourseRepository
    {
        private readonly Course _course;

        public FakeCourseRepository(Course course)
        {
            _course = course;
        }

        public Task<Course?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken) =>
            Task.FromResult<Course?>(_course.Id == id ? _course : null);

        public Task<IReadOnlyList<Course>> ListByIdsAsync(
            IReadOnlyCollection<Guid> courseIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Course>>(
                courseIds.Contains(_course.Id) ? [_course] : []);

        public Task<IReadOnlyList<Course>> ListAsync(
            string? search,
            CourseStatus? status,
            Guid? teacherId,
            bool includeDeleted,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Course>>([_course]);

        public Task<int> CountAsync(
            string? search,
            CourseStatus? status,
            Guid? teacherId,
            bool includeDeleted,
            CancellationToken cancellationToken) => Task.FromResult(1);

        public void Add(Course course)
        {
        }
    }

    private sealed class FakeEnrollmentRepository : IEnrollmentRepository
    {
        private readonly Enrollment _enrollment;

        public FakeEnrollmentRepository(Enrollment enrollment)
        {
            _enrollment = enrollment;
        }

        public Task<Enrollment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Enrollment?>(_enrollment.Id == id ? _enrollment : null);

        public Task<Enrollment?> GetByStudentAndCourseAsync(
            Guid studentId,
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Enrollment?>(
                _enrollment.StudentId == studentId && _enrollment.CourseId == courseId ? _enrollment : null);

        public Task<IReadOnlyList<Enrollment>> ListByStudentAsync(
            Guid studentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(
                _enrollment.StudentId == studentId ? [_enrollment] : []);

        public Task<IReadOnlyList<Enrollment>> ListByCourseAsync(
            Guid courseId,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>([_enrollment]);

        public Task<int> CountByCourseAsync(Guid courseId, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<int> CountAcceptedByCourseAsync(Guid courseId, CancellationToken cancellationToken) => Task.FromResult(1);

        public void Add(Enrollment enrollment)
        {
        }
    }
}
