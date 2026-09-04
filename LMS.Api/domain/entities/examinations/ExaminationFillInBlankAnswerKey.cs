namespace LMS.Api.domain.Entities.Examinations;

public class ExaminationFillInBlankAnswerKey
{
    public Guid Id { get; private set; }
    public Guid ExaminationQuestionId { get; private set; }
    public ExaminationQuestion? ExaminationQuestion { get; private set; }
    public int BlankOrder { get; private set; }
    public string ExpectedAnswer { get; private set; } = null!;

    private ExaminationFillInBlankAnswerKey() { }

    public ExaminationFillInBlankAnswerKey(Guid examinationQuestionId, int blankOrder, string expectedAnswer)
    {
        Id = Guid.NewGuid();
        ExaminationQuestionId = examinationQuestionId;
        BlankOrder = blankOrder;
        ExpectedAnswer = expectedAnswer;
    }
}
