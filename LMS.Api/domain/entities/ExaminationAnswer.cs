namespace LMS.Api.domain.Entities;

public class ExaminationAnswer
{
    public Guid Id { get; private set; }
    public Guid ExaminationQuestionId { get; private set; }
    public ExaminationQuestion? ExaminationQuestion { get; private set; }
    public string Content { get; private set; } = null!;
    public bool IsCorrect { get; private set; }
    public int Order { get; private set; }

    private ExaminationAnswer() { }
    public ExaminationAnswer(Guid examinationQuestionId, string content, bool isCorrect, int order)
    {
        Id = Guid.NewGuid(); ExaminationQuestionId = examinationQuestionId; Content = content; IsCorrect = isCorrect; Order = order;
    }
}
