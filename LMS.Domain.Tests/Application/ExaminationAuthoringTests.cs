using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.examinations;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.Enums.Questions;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public class ExaminationAuthoringTests
{
    [Fact]
    public async Task CreateSnapshotsMultipleChoiceAndFillInBlankDefinitions()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.CreateAsync(
            fixture.Course.Id,
            fixture.CreateRequest(questionCount: 2));

        Assert.Equal(2, result.QuestionCount);
        Assert.Single(result.QuestionSelectionRules);
        Assert.Single(fixture.Examinations.Items);
        var snapshots = fixture.Examinations.Items[0].Questions;
        var multipleChoice = Assert.Single(snapshots, question => question.Type == QuestionType.MultipleChoice);
        Assert.Equal(2, multipleChoice.MultipleChoiceOptions.Count);
        Assert.Contains(multipleChoice.MultipleChoiceOptions, option => option.Id == multipleChoice.CorrectOptionId);
        var fillInBlank = Assert.Single(snapshots, question => question.Type == QuestionType.FillInBlank);
        Assert.Single(fillInBlank.FillInBlankAnswerKeys);
        Assert.Equal(1, fixture.TransactionManager.ExecutionCount);
        Assert.Equal(2, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task CreateRejectsRuleWhenQuestionBankHasTooFewQuestions()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.CreateAsync(fixture.Course.Id, fixture.CreateRequest(questionCount: 3)));

        Assert.Empty(fixture.Examinations.Items);
        Assert.Equal(1, fixture.TransactionManager.ExecutionCount);
    }

    [Fact]
    public async Task CreateCanSnapshotExplicitlySelectedQuestionsInRequestedOrder()
    {
        var fixture = CreateFixture();
        var sourceQuestions = await fixture.Questions.ListByQuestionBankAsync(
            fixture.QuestionBank.Id,
            CancellationToken.None);
        var requestedIds = sourceQuestions.Reverse().Select(question => question.Id).ToArray();
        var request = fixture.CreateRequest(questionCount: 2) with
        {
            SelectionMode = ExaminationQuestionSelectionMode.Manual,
            QuestionSelectionRules = [],
            QuestionIds = requestedIds
        };

        var result = await fixture.Service.CreateAsync(fixture.Course.Id, request);

        Assert.Equal(ExaminationQuestionSelectionMode.Manual, result.SelectionMode);
        Assert.Equal(requestedIds, result.SelectedQuestionIds);
        Assert.Empty(result.QuestionSelectionRules);
        Assert.Equal(
            requestedIds,
            fixture.Examinations.Items[0].Questions
                .OrderBy(question => question.Order)
                .Select(question => question.SourceQuestionId!.Value));
    }

    [Fact]
    public async Task UpdateQuestionSetReplacesDraftSnapshotAndSelectionRules()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(fixture.Course.Id, fixture.CreateRequest(questionCount: 2));
        var multipleChoice = Assert.Single(
            await fixture.Questions.ListByQuestionBankAsync(fixture.QuestionBank.Id, CancellationToken.None),
            question => question.Type == QuestionType.MultipleChoice);

        var updated = await fixture.Service.UpdateQuestionSetAsync(
            created.Id,
            new UpdateExaminationQuestionSetRequest(
                ExaminationQuestionSelectionMode.Manual,
                [],
                [multipleChoice.Id]));

        Assert.Equal(ExaminationQuestionSelectionMode.Manual, updated.SelectionMode);
        Assert.Equal([multipleChoice.Id], updated.SelectedQuestionIds);
        Assert.Empty(updated.QuestionSelectionRules);
        Assert.Equal(1, updated.QuestionCount);
        Assert.Single(fixture.Examinations.Items[0].Questions);
        Assert.Empty(fixture.Examinations.Items[0].QuestionSelectionRules);
        Assert.Equal(2, fixture.TransactionManager.ExecutionCount);
    }

    [Fact]
    public async Task UpdateQuestionSetRejectsPublishedExamination()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(fixture.Course.Id, fixture.CreateRequest(questionCount: 2));
        await fixture.Service.PublishAsync(created.Id);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.UpdateQuestionSetAsync(
            created.Id,
            new UpdateExaminationQuestionSetRequest(
                ExaminationQuestionSelectionMode.Manual,
                [],
                created.SelectedQuestionIds)));

        Assert.Equal(2, fixture.Examinations.Items[0].Questions.Count);
    }

    [Fact]
    public async Task CreateRejectsMixingRandomRulesWithExplicitQuestionIds()
    {
        var fixture = CreateFixture();
        var question = Assert.Single(
            await fixture.Questions.ListByQuestionBankAsync(
                fixture.QuestionBank.Id,
                CancellationToken.None),
            item => item.Type == QuestionType.MultipleChoice);
        var request = fixture.CreateRequest(questionCount: 1) with
        {
            QuestionIds = [question.Id]
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.CreateAsync(fixture.Course.Id, request));

        Assert.Equal(0, fixture.TransactionManager.ExecutionCount);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task CreateRejectsNonUtcSchedule(DateTimeKind kind)
    {
        var fixture = CreateFixture();
        var request = fixture.CreateRequest(questionCount: 2);
        request = request with
        {
            StartAt = DateTime.SpecifyKind(request.StartAt, kind),
            DueAt = DateTime.SpecifyKind(request.DueAt, kind)
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.CreateAsync(fixture.Course.Id, request));

        Assert.Empty(fixture.Examinations.Items);
        Assert.Equal(0, fixture.TransactionManager.ExecutionCount);
    }

    [Fact]
    public async Task ExaminationFollowsDraftPublishedClosedLifecycle()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(fixture.Course.Id, fixture.CreateRequest(questionCount: 2));

        var published = await fixture.Service.PublishAsync(created.Id);
        var closed = await fixture.Service.CloseAsync(created.Id);

        Assert.Equal(ExaminationStatus.Published, published.Status);
        Assert.Equal(ExaminationStatus.Closed, closed.Status);
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.PublishAsync(created.Id));
    }

    [Fact]
    public async Task ClosingExaminationAutoSubmitsEveryActiveAttempt()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(fixture.Course.Id, fixture.CreateRequest(questionCount: 2));
        await fixture.Service.PublishAsync(created.Id);
        var inProgress = new ExaminationAttempt(created.Id, Guid.NewGuid(), DateTime.UtcNow, 2);
        var disconnected = new ExaminationAttempt(created.Id, Guid.NewGuid(), DateTime.UtcNow, 2);
        disconnected.Disconnect(DateTime.UtcNow, 30);
        fixture.Attempts.Items.AddRange([inProgress, disconnected]);

        var closed = await fixture.Service.CloseAsync(created.Id);

        Assert.Equal(ExaminationStatus.Closed, closed.Status);
        Assert.All(fixture.Attempts.Items, attempt =>
        {
            Assert.Equal(ExaminationAttemptStatus.AutoSubmitted, attempt.Status);
            Assert.Equal(ExaminationSubmitReason.ExaminationClosed, attempt.SubmitReason);
            Assert.Equal(2, attempt.TotalQuestions);
        });
    }

    private static CreateExaminationRequest CreateRequest(int questionCount)
    {
        var now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new CreateExaminationRequest(
            "Final Examination",
            null,
            now.AddHours(1),
            now.AddHours(3),
            60,
            true,
            true,
            new ExaminationSecuritySettings(true, 3, 5, true, true, true, false, 10),
            [new QuestionSelectionRuleInput(Guid.Empty, QuestionDifficulty.Easy, questionCount)]);
    }

    private static Fixture CreateFixture()
    {
        var teacher = new User(
            $"{Guid.NewGuid():N}@example.com",
            "Teacher",
            null,
            null,
            Role.Teacher,
            AccountStatus.Active);
        var course = new Course("Course", null, teacher.Id, 30, CourseStatus.Open, teacher.Id);
        var questionBank = new QuestionBank(teacher.Id, "Bank", null, teacher.Id);
        var multipleChoice = new Question(
            questionBank.Id,
            "2 + 2 = ?",
            QuestionType.MultipleChoice,
            QuestionDifficulty.Easy,
            true,
            teacher.Id);
        var wrongOption = new MultipleChoiceOption(multipleChoice.Id, "3", 0);
        var correctOption = new MultipleChoiceOption(multipleChoice.Id, "4", 1);
        multipleChoice.MultipleChoiceOptions.Add(wrongOption);
        multipleChoice.MultipleChoiceOptions.Add(correctOption);
        multipleChoice.SetCorrectOption(correctOption.Id);
        var fillInBlank = new Question(
            questionBank.Id,
            "Vietnam's capital is ____.",
            QuestionType.FillInBlank,
            QuestionDifficulty.Easy,
            false,
            teacher.Id);
        fillInBlank.FillInBlankAnswerKeys.Add(new FillInBlankAnswerKey(fillInBlank.Id, 0, "Hanoi"));
        var examinations = new FakeExaminationRepository();
        var attempts = new FakeExaminationAttemptRepository();
        var unitOfWork = new FakeUnitOfWork();
        var transactionManager = new FakeTransactionManager();
        var questions = new FakeQuestionRepository(multipleChoice, fillInBlank);
        var service = new ExaminationService(
            examinations,
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(),
            new FakeQuestionBankRepository(questionBank),
            questions,
            attempts,
            unitOfWork,
            transactionManager,
            new FakeCurrentUser(teacher),
            new FakeClock(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        return new Fixture(service, course, questionBank, examinations, questions, attempts, unitOfWork, transactionManager);
    }

    private sealed record Fixture(
        ExaminationService Service,
        Course Course,
        QuestionBank QuestionBank,
        FakeExaminationRepository Examinations,
        FakeQuestionRepository Questions,
        FakeExaminationAttemptRepository Attempts,
        FakeUnitOfWork UnitOfWork,
        FakeTransactionManager TransactionManager)
    {
        public CreateExaminationRequest CreateRequest(int questionCount)
        {
            var request = ExaminationAuthoringTests.CreateRequest(questionCount);
            return request with
            {
                QuestionSelectionRules =
                [new QuestionSelectionRuleInput(QuestionBank.Id, QuestionDifficulty.Easy, questionCount)]
            };
        }
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

    private sealed record FakeClock(DateTime UtcNow) : IClock;

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeTransactionManager : ITransactionManager
    {
        public int ExecutionCount { get; private set; }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            await action(cancellationToken);
        }

        public Task ExecuteSerializableAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(action, cancellationToken);
    }

    private sealed class FakeExaminationRepository : IExaminationRepository
    {
        public List<Examination> Items { get; } = [];

        public Task<Examination?> GetByIdAsync(
            Guid id,
            bool includeDefinition,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(examination => examination.Id == id));

        public Task<Examination?> GetByIdForUpdateAsync(
            Guid id,
            bool includeDefinition,
            CancellationToken cancellationToken) => GetByIdAsync(id, includeDefinition, cancellationToken);

        public Task<IReadOnlyList<Examination>> ListByCourseAsync(
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Examination>>(
                Items.Where(examination => examination.CourseId == courseId).ToArray());

        public void Add(Examination examination)
        {
            Items.Add(examination);
        }

        public void RemoveQuestionDefinition(Examination examination)
        {
        }
    }

    private sealed class FakeExaminationAttemptRepository : IExaminationAttemptRepository
    {
        public List<ExaminationAttempt> Items { get; } = [];

        public Task<ExaminationAttempt?> GetByIdAsync(Guid id, bool includeAnswers, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(attempt => attempt.Id == id));

        public Task<ExaminationAttempt?> GetActiveAsync(Guid examinationId, Guid studentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(attempt =>
                attempt.ExaminationId == examinationId && attempt.StudentId == studentId &&
                attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected));

        public Task<ExaminationAttempt?> GetLatestAsync(Guid examinationId, Guid studentId, bool includeAnswers, CancellationToken cancellationToken) =>
            Task.FromResult(Items.LastOrDefault(attempt =>
                attempt.ExaminationId == examinationId && attempt.StudentId == studentId));

        public Task<IReadOnlyList<ExaminationAttempt>> ListActiveByExaminationAsync(Guid examinationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExaminationAttempt>>(Items.Where(attempt =>
                attempt.ExaminationId == examinationId &&
                attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected).ToArray());

        public Task<IReadOnlyList<ExaminationAttemptOverview>> ListByExaminationAsync(
            Guid examinationId,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExaminationAttemptOverview>>([]);

        public Task<int> CountByExaminationAsync(Guid examinationId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ExaminationAttempt>> ListExpiredAsync(DateTime now, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExaminationAttempt>>([]);

        public Task<StudentAnswer?> GetAnswerAsync(Guid attemptId, Guid examinationQuestionId, bool includeFillInBlankAnswers, CancellationToken cancellationToken) =>
            Task.FromResult<StudentAnswer?>(null);

        public void Add(ExaminationAttempt attempt) => Items.Add(attempt);
        public void Remove(ExaminationAttempt attempt) => Items.Remove(attempt);
        public void AddAnswer(StudentAnswer answer) { }
        public void AddFillInBlankAnswer(StudentFillInBlankAnswer answer) { }
        public void RemoveFillInBlankAnswer(StudentFillInBlankAnswer answer) { }
        public void AddViolation(ExaminationViolation violation) { }
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

    private sealed class FakeQuestionBankRepository : IQuestionBankRepository
    {
        private readonly QuestionBank _questionBank;

        public FakeQuestionBankRepository(QuestionBank questionBank)
        {
            _questionBank = questionBank;
        }

        public Task<QuestionBank?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<QuestionBank?>(_questionBank.Id == id ? _questionBank : null);

        public Task<IReadOnlyList<QuestionBank>> ListByIdsAsync(
            IReadOnlyCollection<Guid> questionBankIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuestionBank>>(
                questionBankIds.Contains(_questionBank.Id) ? [_questionBank] : []);

        public Task<IReadOnlyList<QuestionBank>> ListAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuestionBank>>([_questionBank]);

        public Task<IReadOnlyList<QuestionBank>> ListByTeacherAsync(
            Guid teacherId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuestionBank>>([_questionBank]);

        public void Add(QuestionBank questionBank)
        {
        }
    }

    private sealed class FakeQuestionRepository : IQuestionRepository
    {
        private readonly IReadOnlyList<Question> _questions;

        public FakeQuestionRepository(params Question[] questions)
        {
            _questions = questions;
        }

        public Task<Question?> GetByIdAsync(
            Guid id,
            bool includeAnswers,
            CancellationToken cancellationToken) =>
            Task.FromResult(_questions.SingleOrDefault(question => question.Id == id));

        public Task<IReadOnlyList<Question>> ListByQuestionBankAsync(
            Guid questionBankId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Question>>(
                _questions.Where(question => question.QuestionBankId == questionBankId).ToArray());

        public Task<IReadOnlyList<Question>> ListByQuestionBankIdsAsync(
            IReadOnlyCollection<Guid> questionBankIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Question>>(
                _questions.Where(question => questionBankIds.Contains(question.QuestionBankId)).ToArray());

        public Task<IReadOnlyList<Question>> ListByIdsAsync(
            IReadOnlyCollection<Guid> questionIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Question>>(
                _questions.Where(question => questionIds.Contains(question.Id)).ToArray());

        public void Add(Question question)
        {
        }
    }

    private sealed class FakeEnrollmentRepository : IEnrollmentRepository
    {
        public Task<Enrollment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Enrollment?>(null);

        public Task<Enrollment?> GetByStudentAndCourseAsync(
            Guid studentId,
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Enrollment?>(null);

        public Task<IReadOnlyList<Enrollment>> ListByStudentAsync(
            Guid studentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>([]);

        public Task<IReadOnlyList<Enrollment>> ListByCourseAsync(
            Guid courseId,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>([]);

        public Task<int> CountByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> CountAcceptedByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, int>> CountAcceptedByCoursesAsync(
            IReadOnlyCollection<Guid> courseIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

        public void Add(Enrollment enrollment)
        {
        }
    }
}
