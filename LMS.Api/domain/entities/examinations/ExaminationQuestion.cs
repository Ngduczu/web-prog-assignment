using LMS.Api.domain.Enums.Questions;

namespace LMS.Api.domain.Entities.Examinations;

public class ExaminationQuestion
{
    public Guid Id { get; private set; }
    public Guid ExaminationId { get; private set; }
    public Examination? Examination { get; private set; }
    public Guid? SourceQuestionId { get; private set; }
    public string Content { get; private set; } = null!;
    public QuestionType Type { get; private set; }
    public QuestionDifficulty Difficulty { get; private set; }
    public bool ShuffleOptions { get; private set; }
    public int Order { get; private set; }
    public ICollection<ExaminationMultipleChoiceOption> MultipleChoiceOptions { get; private set; } = new List<ExaminationMultipleChoiceOption>();
    public Guid? CorrectOptionId { get; private set; }
    public ExaminationMultipleChoiceOption? CorrectOption { get; private set; }
    public ICollection<ExaminationFillInBlankAnswerKey> FillInBlankAnswerKeys { get; private set; } = new List<ExaminationFillInBlankAnswerKey>();

    private ExaminationQuestion() { }
    public ExaminationQuestion(Guid examinationId, Guid? sourceQuestionId, string content, QuestionType type, QuestionDifficulty difficulty, bool shuffleOptions, int order)
    {
        Id = Guid.NewGuid(); ExaminationId = examinationId; SourceQuestionId = sourceQuestionId; Content = content;
        Type = type; Difficulty = difficulty; ShuffleOptions = shuffleOptions; Order = order;
    }

    public void SetCorrectOption(Guid? correctOptionId) { CorrectOptionId = correctOptionId; }
}
