using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class ExaminationQuestion
{
    public Guid Id { get; private set; }
    public Guid ExaminationId { get; private set; }
    public Examination? Examination { get; private set; }
    public Guid? SourceQuestionId { get; private set; }
    public string Content { get; private set; } = null!;
    public QuestionType Type { get; private set; }
    public QuestionDifficulty Difficulty { get; private set; }
    public bool ShuffleAnswers { get; private set; }
    public int Order { get; private set; }
    public ICollection<ExaminationAnswer> Answers { get; private set; } = new List<ExaminationAnswer>();

    private ExaminationQuestion() { }
    public ExaminationQuestion(Guid examinationId, Guid? sourceQuestionId, string content, QuestionType type, QuestionDifficulty difficulty, bool shuffleAnswers, int order)
    {
        Id = Guid.NewGuid(); ExaminationId = examinationId; SourceQuestionId = sourceQuestionId; Content = content;
        Type = type; Difficulty = difficulty; ShuffleAnswers = shuffleAnswers; Order = order;
    }
}
