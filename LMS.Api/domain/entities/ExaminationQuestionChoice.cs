namespace LMS.Api.domain.Entities;

public class ExaminationQuestionChoice
{
    public Guid Id { get; private set; }
    public Guid ExaminationQuestionId { get; private set; }
    public ExaminationQuestion? ExaminationQuestion { get; private set; }
    public string Content { get; private set; } = null!;
    public int Order { get; private set; }

    private ExaminationQuestionChoice() { }
    public ExaminationQuestionChoice(Guid examinationQuestionId, string content, int order)
    {
        Id = Guid.NewGuid(); ExaminationQuestionId = examinationQuestionId; Content = content; Order = order;
    }
}
