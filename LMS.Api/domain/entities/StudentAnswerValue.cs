namespace LMS.Api.domain.Entities;

public class StudentAnswerValue
{
    public Guid Id { get; private set; }
    public Guid StudentAnswerId { get; private set; }
    public StudentAnswer? StudentAnswer { get; private set; }
    public int Order { get; private set; }
    public string AnswerText { get; private set; } = null!;

    private StudentAnswerValue() { }
    public StudentAnswerValue(Guid studentAnswerId, int order, string answerText)
    {
        Id = Guid.NewGuid(); StudentAnswerId = studentAnswerId; Order = order; AnswerText = answerText;
    }
}
