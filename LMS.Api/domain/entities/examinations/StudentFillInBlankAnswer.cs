namespace LMS.Api.domain.Entities.Examinations;

public class StudentFillInBlankAnswer
{
    public Guid Id { get; private set; }
    public Guid StudentAnswerId { get; private set; }
    public StudentAnswer? StudentAnswer { get; private set; }
    public int BlankOrder { get; private set; }
    public string AnswerText { get; private set; } = null!;

    private StudentFillInBlankAnswer() { }
    public StudentFillInBlankAnswer(Guid studentAnswerId, int blankOrder, string answerText)
    {
        Id = Guid.NewGuid(); StudentAnswerId = studentAnswerId; BlankOrder = blankOrder; AnswerText = answerText;
    }

    public void SetAnswerText(string answerText)
    {
        AnswerText = answerText;
    }
}
