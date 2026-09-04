using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.Enums.Questions;

namespace LMS.Api.application.features.examinationattempts;

public sealed record StudentFillInBlankAnswerInput(int BlankOrder, string AnswerText);

public sealed record StudentAnswerDto(
    Guid Id,
    Guid ExaminationQuestionId,
    QuestionType QuestionType,
    Guid? SelectedOptionId,
    IReadOnlyList<StudentFillInBlankAnswerInput> FillInBlankAnswers,
    DateTime ModifiedAt);

public sealed record ExaminationAttemptDto(
    Guid Id,
    Guid ExaminationId,
    Guid StudentId,
    ExaminationAttemptStatus Status,
    ExaminationSubmitReason? SubmitReason,
    DateTime StartedAt,
    DateTime? SubmittedAt,
    DateTime? DisconnectedAt,
    int? RemainingSeconds,
    DateTime LastActivityAt,
    int ViolationCount,
    int CorrectAnswers,
    int TotalQuestions,
    decimal Score);

public sealed record StudentExamOptionDto(Guid Id, string Content, int Order);
public sealed record StudentExamQuestionDto(Guid Id, string Content, QuestionType Type, int Order, IReadOnlyList<StudentExamOptionDto> Options);

public interface IExaminationAttemptService
{
    Task<ExaminationAttemptDto> StartAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentExamQuestionDto>> GetQuestionsAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);
    Task<StudentAnswerDto> SaveMultipleChoiceAnswerAsync(
        Guid attemptId,
        Guid examinationQuestionId,
        Guid selectedOptionId,
        CancellationToken cancellationToken = default);
    Task<StudentAnswerDto> SaveFillInBlankAnswerAsync(
        Guid attemptId,
        Guid examinationQuestionId,
        IReadOnlyList<StudentFillInBlankAnswerInput> answers,
        CancellationToken cancellationToken = default);
    Task<ExaminationAttemptDto> DisconnectAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);
    Task<ExaminationAttemptDto> ReconnectAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);
    Task<ExaminationAttemptDto> RecordViolationAsync(
        Guid attemptId,
        ExaminationViolationType violationType,
        CancellationToken cancellationToken = default);
    Task<ExaminationAttemptDto> SubmitAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);
    Task<ExaminationAttemptDto> AutoSubmitAsync(
        Guid attemptId,
        ExaminationSubmitReason reason,
        CancellationToken cancellationToken = default);
}
