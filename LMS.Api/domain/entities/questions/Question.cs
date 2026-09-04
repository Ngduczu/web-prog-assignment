using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Questions;

namespace LMS.Api.domain.Entities.Questions;

public class Question
{
	public Guid Id { get; private set; }
	public Guid QuestionBankId { get; private set; }
	public QuestionBank? QuestionBank { get; private set; }

	public string Content { get; private set; } = null!;
	public QuestionType Type { get; private set; }
	public QuestionDifficulty Difficulty { get; private set; }
	public bool ShuffleChoices { get; private set; }
	public ICollection<QuestionChoice> Choices { get; private set; } = new List<QuestionChoice>();
	public Guid? CorrectChoiceId { get; private set; }
	public QuestionChoice? CorrectChoice { get; private set; }

	public DateTime CreatedAt { get; private set; }
	public DateTime ModifiedAt { get; private set; }
	public Guid CreatedById { get; private set; }
	public Guid ModifiedById { get; private set; }
	public User? CreatedBy { get; private set; }
	public User? ModifiedBy { get; private set; }

	private Question() { }

	public Question(Guid questionBankId, string content, QuestionType type, QuestionDifficulty difficulty, bool shuffleChoices, Guid createdById)
	{
		Id = Guid.NewGuid();
		QuestionBankId = questionBankId;
		Content = content;
		Type = type;
		Difficulty = difficulty;
		ShuffleChoices = shuffleChoices;
		CreatedAt = DateTime.UtcNow;
		ModifiedAt = DateTime.UtcNow;
		CreatedById = createdById;
		ModifiedById = createdById;
	}

	public void SetContent(string content) { Content = content; Touch(); }
	public void SetType(QuestionType type) { Type = type; Touch(); }
	public void SetDifficulty(QuestionDifficulty difficulty) { Difficulty = difficulty; Touch(); }
	public void SetShuffleChoices(bool shuffleChoices) { ShuffleChoices = shuffleChoices; Touch(); }
	public void SetCorrectChoice(Guid? correctChoiceId) { CorrectChoiceId = correctChoiceId; Touch(); }
	public void SetModifiedBy(Guid modifiedById) { ModifiedById = modifiedById; Touch(); }
	private void Touch() { ModifiedAt = DateTime.UtcNow; }
}
