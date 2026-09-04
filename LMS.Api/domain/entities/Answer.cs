namespace LMS.Api.domain.Entities;

public class Answer
{
	public Guid Id { get; private set; }
	public Guid QuestionId { get; private set; }
	public Question? Question { get; private set; }

	public string Content { get; private set; } = null!;
	public int Order { get; private set; }
	public bool IsCorrect { get; private set; }

	private Answer() { }

	public Answer(Guid questionId, string content, int order, bool isCorrect)
	{
		Id = Guid.NewGuid();
		QuestionId = questionId;
		Content = content;
		Order = order;
		IsCorrect = isCorrect;
	}

	public void SetContent(string content) { Content = content; }
	public void SetOrder(int order) { Order = order; }
	public void SetCorrect(bool isCorrect) { IsCorrect = isCorrect; }
}