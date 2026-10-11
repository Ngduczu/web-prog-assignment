using LMS.Api.domain.Enums.Questions;

namespace LMS.Api.application.features.questions;

public sealed record CreateQuestionBankRequest(Guid TeacherId, string Name, string? Description);
public sealed record UpdateQuestionBankRequest(string Name, string? Description);
public sealed record ShareQuestionBankRequest(string RecipientEmail);

public sealed record QuestionBankDto(
    Guid Id,
    Guid TeacherId,
    string Name,
    string? Description,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record MultipleChoiceOptionInput(string Content, bool IsCorrect);
public sealed record FillInBlankAnswerInput(int BlankOrder, string ExpectedAnswer);

public sealed record CreateMultipleChoiceQuestionRequest(
    string Content,
    QuestionDifficulty Difficulty,
    bool ShuffleOptions,
    IReadOnlyList<MultipleChoiceOptionInput> Options);

public sealed record CreateFillInBlankQuestionRequest(
    string Content,
    QuestionDifficulty Difficulty,
    IReadOnlyList<FillInBlankAnswerInput> Answers);
public sealed record UpdateQuestionRequest(
    string Content,
    QuestionDifficulty Difficulty,
    bool? ShuffleOptions = null,
    IReadOnlyList<MultipleChoiceOptionInput>? Options = null,
    IReadOnlyList<FillInBlankAnswerInput>? Answers = null);

public sealed record MultipleChoiceOptionDto(Guid Id, string Content, int Order, bool IsCorrect);
public sealed record FillInBlankAnswerDto(Guid Id, int BlankOrder, string ExpectedAnswer);

public sealed record QuestionDto(
    Guid Id,
    Guid QuestionBankId,
    string Content,
    QuestionType Type,
    QuestionDifficulty Difficulty,
    bool ShuffleOptions,
    IReadOnlyList<MultipleChoiceOptionDto> Options,
    IReadOnlyList<FillInBlankAnswerDto> Answers,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public interface IQuestionService
{
    Task<QuestionBankDto> CreateQuestionBankAsync(
        CreateQuestionBankRequest request,
        CancellationToken cancellationToken = default);
    Task<QuestionBankDto> UpdateQuestionBankAsync(
        Guid questionBankId,
        UpdateQuestionBankRequest request,
        CancellationToken cancellationToken = default);
    Task DeleteQuestionBankAsync(Guid questionBankId, CancellationToken cancellationToken = default);
    Task<QuestionBankDto> ShareQuestionBankAsync(
        Guid questionBankId,
        ShareQuestionBankRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuestionBankDto>> ListQuestionBanksAsync(
        Guid? teacherId = null,
        CancellationToken cancellationToken = default);
    Task<QuestionDto> CreateMultipleChoiceAsync(
        Guid questionBankId,
        CreateMultipleChoiceQuestionRequest request,
        CancellationToken cancellationToken = default);
    Task<QuestionDto> CreateFillInBlankAsync(
        Guid questionBankId,
        CreateFillInBlankQuestionRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuestionDto>> ListQuestionsAsync(
        Guid questionBankId,
        CancellationToken cancellationToken = default);
    Task<QuestionDto> UpdateQuestionAsync(Guid questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default);
    Task DeleteQuestionAsync(Guid questionId, CancellationToken cancellationToken = default);
}
