using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.features.questions;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Questions;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public class QuestionApplicationTests
{
    [Fact]
    public async Task MultipleChoiceRequiresExactlyOneCorrectOption()
    {
        var fixture = CreateFixture();
        var request = new CreateMultipleChoiceQuestionRequest(
            "Question",
            QuestionDifficulty.Easy,
            true,
            [
                new MultipleChoiceOptionInput("A", true),
                new MultipleChoiceOptionInput("B", true)
            ]);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.CreateMultipleChoiceAsync(fixture.QuestionBank.Id, request));

        Assert.Empty(fixture.Questions.Items);
        Assert.Equal(0, fixture.TransactionManager.ExecutionCount);
    }

    [Fact]
    public async Task MultipleChoicePersistsOptionsThenCorrectOptionInsideTransaction()
    {
        var fixture = CreateFixture();
        var request = new CreateMultipleChoiceQuestionRequest(
            "Question",
            QuestionDifficulty.Medium,
            true,
            [
                new MultipleChoiceOptionInput("A", false),
                new MultipleChoiceOptionInput("B", true),
                new MultipleChoiceOptionInput("C", false)
            ]);

        var result = await fixture.Service.CreateMultipleChoiceAsync(fixture.QuestionBank.Id, request);

        Assert.Equal(QuestionType.MultipleChoice, result.Type);
        Assert.Equal(3, result.Options.Count);
        Assert.Single(result.Options, option => option.IsCorrect);
        Assert.Equal("B", result.Options.Single(option => option.IsCorrect).Content);
        Assert.Empty(result.Answers);
        Assert.Equal(1, fixture.TransactionManager.ExecutionCount);
        Assert.Equal(2, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task MultipleChoiceBuildsFreshAggregateWhenTransactionIsRetried()
    {
        var fixture = CreateFixture();
        fixture.UnitOfWork.FailOnSaveNumber = 2;
        fixture.TransactionManager.RetryOnce = true;
        var request = new CreateMultipleChoiceQuestionRequest(
            "Question",
            QuestionDifficulty.Medium,
            true,
            [
                new MultipleChoiceOptionInput("A", false),
                new MultipleChoiceOptionInput("B", true)
            ]);

        var result = await fixture.Service.CreateMultipleChoiceAsync(
            fixture.QuestionBank.Id,
            request);

        Assert.Equal(2, fixture.TransactionManager.ExecutionCount);
        Assert.Equal(4, fixture.UnitOfWork.SaveCount);
        Assert.Equal(2, fixture.Questions.AddedQuestions.Count);
        Assert.NotEqual(
            fixture.Questions.AddedQuestions[0].Id,
            fixture.Questions.AddedQuestions[1].Id);
        Assert.All(
            fixture.Questions.CorrectOptionIdsWhenAdded,
            correctOptionId => Assert.Null(correctOptionId));
        Assert.Equal(fixture.Questions.AddedQuestions[1].Id, result.Id);
        Assert.Single(result.Options, option => option.IsCorrect);
    }

    [Fact]
    public async Task FillInBlankUsesAnswerKeysAndDisablesOptionShuffle()
    {
        var fixture = CreateFixture();
        var request = new CreateFillInBlankQuestionRequest(
            "The capital of Vietnam is ____.",
            QuestionDifficulty.Easy,
            [new FillInBlankAnswerInput(0, "Hanoi")]);

        var result = await fixture.Service.CreateFillInBlankAsync(fixture.QuestionBank.Id, request);

        Assert.Equal(QuestionType.FillInBlank, result.Type);
        Assert.False(result.ShuffleOptions);
        Assert.Empty(result.Options);
        Assert.Single(result.Answers);
        Assert.Equal("Hanoi", result.Answers[0].ExpectedAnswer);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task UpdateQuestionChangesOnlyEditableSourceFields()
    {
        var fixture = CreateFixture();
        var question = new Question(
            fixture.QuestionBank.Id,
            "Old content",
            QuestionType.FillInBlank,
            QuestionDifficulty.Easy,
            false,
            fixture.QuestionBank.TeacherId);
        question.FillInBlankAnswerKeys.Add(new FillInBlankAnswerKey(question.Id, 0, "Hanoi"));
        fixture.Questions.Items.Add(question);

        var result = await fixture.Service.UpdateQuestionAsync(
            question.Id,
            new UpdateQuestionRequest("New content", QuestionDifficulty.Hard));

        Assert.Equal("New content", result.Content);
        Assert.Equal(QuestionDifficulty.Hard, result.Difficulty);
        Assert.Single(result.Answers);
        Assert.Equal("Hanoi", result.Answers[0].ExpectedAnswer);
    }

    [Fact]
    public async Task UpdateMultipleChoiceReplacesOptionsAndCorrectOption()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateMultipleChoiceAsync(
            fixture.QuestionBank.Id,
            new CreateMultipleChoiceQuestionRequest(
                "Question",
                QuestionDifficulty.Easy,
                true,
                [new MultipleChoiceOptionInput("A", true), new MultipleChoiceOptionInput("B", false)]));

        var result = await fixture.Service.UpdateQuestionAsync(
            created.Id,
            new UpdateQuestionRequest(
                "Question",
                QuestionDifficulty.Easy,
                false,
                [
                    new MultipleChoiceOptionInput("A", false),
                    new MultipleChoiceOptionInput("B", false),
                    new MultipleChoiceOptionInput("C", false),
                    new MultipleChoiceOptionInput("D", false),
                    new MultipleChoiceOptionInput("E", true)
                ]));

        Assert.Equal(5, result.Options.Count);
        Assert.False(result.ShuffleOptions);
        Assert.Equal("E", result.Options.Single(option => option.IsCorrect).Content);
    }

    [Fact]
    public async Task UpdateMultipleChoiceRejectsInvalidCorrectOptionCount()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateMultipleChoiceAsync(
            fixture.QuestionBank.Id,
            new CreateMultipleChoiceQuestionRequest(
                "Question",
                QuestionDifficulty.Easy,
                true,
                [new MultipleChoiceOptionInput("A", true), new MultipleChoiceOptionInput("B", false)]));

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.UpdateQuestionAsync(
            created.Id,
            new UpdateQuestionRequest(
                "Question",
                QuestionDifficulty.Easy,
                null,
                [new MultipleChoiceOptionInput("A", false), new MultipleChoiceOptionInput("B", false)])));
    }

    [Fact]
    public async Task UpdateFillInBlankReplacesAnswersAndRejectsOptions()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateFillInBlankAsync(
            fixture.QuestionBank.Id,
            new CreateFillInBlankQuestionRequest(
                "Capital of ___",
                QuestionDifficulty.Easy,
                [new FillInBlankAnswerInput(1, "Hanoi")]));

        var result = await fixture.Service.UpdateQuestionAsync(
            created.Id,
            new UpdateQuestionRequest(
                "Capital of ___",
                QuestionDifficulty.Easy,
                Answers: [new FillInBlankAnswerInput(1, "Ha Noi"), new FillInBlankAnswerInput(1, "Hanoi")]));

        Assert.Equal(2, result.Answers.Count);

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.UpdateQuestionAsync(
            created.Id,
            new UpdateQuestionRequest(
                "Capital of ___",
                QuestionDifficulty.Easy,
                Options: [new MultipleChoiceOptionInput("A", true), new MultipleChoiceOptionInput("B", false)])));
    }

    [Fact]
    public async Task DeleteQuestionRemovesSourceQuestion()
    {
        var fixture = CreateFixture();
        var question = new Question(
            fixture.QuestionBank.Id,
            "Question",
            QuestionType.FillInBlank,
            QuestionDifficulty.Easy,
            false,
            fixture.QuestionBank.TeacherId);
        fixture.Questions.Items.Add(question);

        await fixture.Service.DeleteQuestionAsync(question.Id);

        Assert.Empty(fixture.Questions.Items);
    }

    [Fact]
    public async Task ReferencedQuestionBankCannotBeDeleted()
    {
        var fixture = CreateFixture();
        fixture.QuestionBanks.IsReferenced = true;

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.DeleteQuestionBankAsync(fixture.QuestionBank.Id));

        Assert.False(fixture.QuestionBanks.WasRemoved);
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
        var questionBank = new QuestionBank(teacher.Id, "Bank", null, teacher.Id);
        var questions = new FakeQuestionRepository();
        var unitOfWork = new FakeUnitOfWork();
        var transactionManager = new FakeTransactionManager();
        var questionBanks = new FakeQuestionBankRepository(questionBank);
        var service = new QuestionService(
            questionBanks,
            questions,
            new FakeUserRepository(teacher),
            unitOfWork,
            transactionManager,
            new FakeCurrentUser(teacher));
        return new Fixture(service, questionBank, questionBanks, questions, unitOfWork, transactionManager);
    }

    private sealed record Fixture(
        QuestionService Service,
        QuestionBank QuestionBank,
        FakeQuestionBankRepository QuestionBanks,
        FakeQuestionRepository Questions,
        FakeUnitOfWork UnitOfWork,
        FakeTransactionManager TransactionManager);

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
        public int SaveCount { get; private set; }
        public int? FailOnSaveNumber { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveCount == FailOnSaveNumber)
            {
                throw new InvalidOperationException("Retryable transaction failure");
            }

            return Task.FromResult(1);
        }
    }

    private sealed class FakeTransactionManager : ITransactionManager
    {
        public int ExecutionCount { get; private set; }
        public bool RetryOnce { get; set; }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            try
            {
                await action(cancellationToken);
            }
            catch (InvalidOperationException) when (RetryOnce)
            {
                RetryOnce = false;
                ExecutionCount++;
                await action(cancellationToken);
            }
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly User _user;

        public FakeUserRepository(User user)
        {
            _user = user;
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(_user.Id == id ? _user : null);

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(_user.Email == normalizedEmail ? _user : null);

        public Task<User?> GetByExternalLoginAsync(ExternalProvider provider, string externalUserId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(null);

        public Task<ExternalUser?> GetExternalLoginAsync(Guid userId, ExternalProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalUser?>(null);

        public void Add(User user)
        {
            throw new NotSupportedException();
        }

        public void AddExternalUser(ExternalUser externalUser) => throw new NotSupportedException();
    }

    private sealed class FakeQuestionBankRepository : IQuestionBankRepository
    {
        private readonly QuestionBank _questionBank;
        public bool IsReferenced { get; set; }
        public bool WasRemoved { get; private set; }

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

        public Task<IReadOnlyList<QuestionBank>> ListByTeacherAsync(
            Guid teacherId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuestionBank>>([_questionBank]);

        public void Add(QuestionBank questionBank)
        {
        }

        public Task<bool> IsReferencedByExaminationAsync(
            Guid questionBankId,
            CancellationToken cancellationToken) => Task.FromResult(IsReferenced);

        public void Remove(QuestionBank questionBank) => WasRemoved = true;
    }

    private sealed class FakeQuestionRepository : IQuestionRepository
    {
        public List<Question> Items { get; } = [];
        public List<Question> AddedQuestions { get; } = [];
        public List<Guid?> CorrectOptionIdsWhenAdded { get; } = [];

        public Task<Question?> GetByIdAsync(
            Guid id,
            bool includeAnswers,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(question => question.Id == id));

        public Task<IReadOnlyList<Question>> ListByQuestionBankAsync(
            Guid questionBankId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Question>>(
                Items.Where(question => question.QuestionBankId == questionBankId).ToArray());

        public Task<IReadOnlyList<Question>> ListByQuestionBankIdsAsync(
            IReadOnlyCollection<Guid> questionBankIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Question>>(
                Items.Where(question => questionBankIds.Contains(question.QuestionBankId)).ToArray());

        public Task<IReadOnlyList<Question>> ListByIdsAsync(
            IReadOnlyCollection<Guid> questionIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Question>>(
                Items.Where(question => questionIds.Contains(question.Id)).ToArray());

        public void Add(Question question)
        {
            AddedQuestions.Add(question);
            CorrectOptionIdsWhenAdded.Add(question.CorrectOptionId);
            Items.Add(question);
        }

        public void Remove(Question question) => Items.Remove(question);
    }
}
