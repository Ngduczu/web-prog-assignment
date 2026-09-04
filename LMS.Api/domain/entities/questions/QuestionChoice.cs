namespace LMS.Api.domain.Entities.Questions;

public class QuestionChoice
{
	public Guid Id { get; private set; }
	public Guid QuestionId { get; private set; }
	public Question? Question { get; private set; }

	public string Content { get; private set; } = null!;
	public int Order { get; private set; }
	private QuestionChoice() { }

	public QuestionChoice(Guid questionId, string content, int order)
	{
		Id = Guid.NewGuid();
		QuestionId = questionId;
		Content = content;
		Order = order;
	}

	public void SetContent(string content) { Content = content; }
	public void SetOrder(int order) { Order = order; }
}
