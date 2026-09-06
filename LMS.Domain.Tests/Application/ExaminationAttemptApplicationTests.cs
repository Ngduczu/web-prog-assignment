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
    public async Task SubmittedAttemptHidesScoreWhenShowScoreImmediatelyIsDisabled()
    {
        var fixture = CreateFixture(showScoreImmediately: false);
        var started = await fixture.Service.StartAsync(fixture.Examination.Id);
        Assert.Equal(2, started.TotalQuestions);
        Assert.Equal(0, started.CorrectAnswers);
        Assert.Equal(0m, started.Score);

        var submitted = await fixture.Service.SubmitAsync(started.Id);

        Assert.Equal(ExaminationAttemptStatus.Submitted, submitted.Status);
        Assert.Null(submitted.CorrectAnswers);
        Assert.Null(submitted.TotalQuestions);
        Assert.Null(submitted.Score);
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
        bool shuffleQuestions = false)
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
        examination.SetAntiCheat(true, 3, maxViolations, true, true, true, false, 10);
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
            new FakeCurrentUser(student),
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
