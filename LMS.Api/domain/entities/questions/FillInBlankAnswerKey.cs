namespace LMS.Api.domain.Entities.Questions;

public class FillInBlankAnswerKey
{
    public Guid Id { get; private set; }
    public Guid QuestionId { get; private set; }
    public Question? Question { get; private set; }
    public int BlankOrder { get; private set; }
    public string ExpectedAnswer { get; private set; } = null!;

    private FillInBlankAnswerKey() { }

    public FillInBlankAnswerKey(Guid questionId, int blankOrder, string expectedAnswer)
    {
        Id = Guid.NewGuid();
        QuestionId = questionId;
        BlankOrder = blankOrder;
        ExpectedAnswer = expectedAnswer;
    }

    public void SetExpectedAnswer(string expectedAnswer)
    {
        ExpectedAnswer = expectedAnswer;
    }
}
