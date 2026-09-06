using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.Enums.Questions;

namespace LMS.Api.application.features.examinations;

public sealed record ExaminationSecuritySettings(
    bool RequireFullscreen,
    int FullscreenGraceSeconds,
    int MaxViolations,
    bool BlockCopyPaste,
    bool BlockRightClick,
    bool DetectTabChange,
    bool DetectDevTools,
    int MaxDisconnectMinutes);

public sealed record QuestionSelectionRuleInput(
    Guid QuestionBankId,
    QuestionDifficulty Difficulty,
    int QuestionCount);

public sealed record CreateExaminationRequest(
    string Title,
    string? Description,
    DateTime StartAt,
    DateTime DueAt,
    int DurationMinutes,
    bool ShuffleQuestions,
    bool ShowScoreImmediately,
    ExaminationSecuritySettings Security,
    IReadOnlyList<QuestionSelectionRuleInput> QuestionSelectionRules);

public sealed record UpdateExaminationRequest(
    string Title,
    string? Description,
    DateTime StartAt,
    DateTime DueAt,
    int DurationMinutes,
    bool ShuffleQuestions,
    bool ShowScoreImmediately,
    ExaminationSecuritySettings Security);

public sealed record QuestionSelectionRuleDto(
    Guid Id,
    Guid QuestionBankId,
    QuestionDifficulty Difficulty,
    int QuestionCount);

public sealed record ExaminationDto(
    Guid Id,
    Guid CourseId,
    string Title,
    string? Description,
    ExaminationStatus Status,
    DateTime StartAt,
    DateTime DueAt,
    int DurationMinutes,
    bool ShuffleQuestions,
    bool ShowScoreImmediately,
    ExaminationSecuritySettings Security,
    IReadOnlyList<QuestionSelectionRuleDto> QuestionSelectionRules,
    int QuestionCount,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record StudentExaminationDto(
    Guid Id,
    Guid CourseId,
    string Title,
    string? Description,
    ExaminationStatus Status,
    DateTime StartAt,
    DateTime DueAt,
    int DurationMinutes,
    bool ShuffleQuestions,
    bool ShowScoreImmediately,
    ExaminationSecuritySettings Security,
    int QuestionCount,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public interface IExaminationService
{
    Task<ExaminationDto> CreateAsync(
        Guid courseId,
        CreateExaminationRequest request,
        CancellationToken cancellationToken = default);
    Task<ExaminationDto> GetForManagementAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default);
    Task<StudentExaminationDto> GetForStudentAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExaminationDto>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentExaminationDto>> ListAvailableForStudentAsync(
        Guid courseId,
        CancellationToken cancellationToken = default);
    Task<ExaminationDto> UpdateAsync(
        Guid examinationId,
        UpdateExaminationRequest request,
        CancellationToken cancellationToken = default);
    Task<ExaminationDto> PublishAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default);
    Task<ExaminationDto> CloseAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default);
}
