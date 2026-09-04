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
    public bool ShuffleChoices { get; private set; }
    public int Order { get; private set; }
    public ICollection<ExaminationQuestionChoice> Choices { get; private set; } = new List<ExaminationQuestionChoice>();
    public Guid? CorrectChoiceId { get; private set; }
    public ExaminationQuestionChoice? CorrectChoice { get; private set; }

    private ExaminationQuestion() { }
    public ExaminationQuestion(Guid examinationId, Guid? sourceQuestionId, string content, QuestionType type, QuestionDifficulty difficulty, bool shuffleChoices, int order)
    {
        Id = Guid.NewGuid(); ExaminationId = examinationId; SourceQuestionId = sourceQuestionId; Content = content;
        Type = type; Difficulty = difficulty; ShuffleChoices = shuffleChoices; Order = order;
    }

    public void SetCorrectChoice(Guid? correctChoiceId) { CorrectChoiceId = correctChoiceId; }
}
