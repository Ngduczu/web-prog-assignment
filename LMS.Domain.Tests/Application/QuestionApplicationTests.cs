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
        var service = new QuestionService(
            new FakeQuestionBankRepository(questionBank),
            questions,
            new FakeUserRepository(teacher),
            unitOfWork,
            transactionManager,
            new FakeCurrentUser(teacher));
        return new Fixture(service, questionBank, questions, unitOfWork, transactionManager);
    }

    private sealed record Fixture(
        QuestionService Service,
        QuestionBank QuestionBank,
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

        public FakeQuestionBankRepository(QuestionBank questionBank)
        {
            _questionBank = questionBank;
        }

        public Task<QuestionBank?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<QuestionBank?>(_questionBank.Id == id ? _questionBank : null);

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
        public List<Question> Items { get; } = [];

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

        public void Add(Question question)
        {
            Items.Add(question);
        }
    }
}
