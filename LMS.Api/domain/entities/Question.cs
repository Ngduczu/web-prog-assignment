using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class Question
{
	public Guid Id { get; private set; }
	public Guid QuestionBankId { get; private set; }
	public QuestionBank? QuestionBank { get; private set; }

	public string Content { get; private set; } = null!;
	public QuestionType Type { get; private set; }
	public QuestionDifficulty Difficulty { get; private set; }
	public bool ShuffleAnswers { get; private set; }
	public ICollection<Answer> Answers { get; private set; } = new List<Answer>();

	public DateTime CreatedOn { get; private set; }
	public DateTime ModifiedOn { get; private set; }
	public Guid CreatedById { get; private set; }
	public Guid ModifiedById { get; private set; }
	public Users? CreatedBy { get; private set; }
	public Users? ModifiedBy { get; private set; }

	private Question() { }

	public Question(Guid questionBankId, string content, QuestionType type, QuestionDifficulty difficulty, bool shuffleAnswers, Guid createdById)
	{
		Id = Guid.NewGuid();
		QuestionBankId = questionBankId;
		Content = content;
		Type = type;
		Difficulty = difficulty;
		ShuffleAnswers = shuffleAnswers;
		CreatedOn = DateTime.UtcNow;
		ModifiedOn = DateTime.UtcNow;
		CreatedById = createdById;
		ModifiedById = createdById;
	}

	public void SetContent(string content) { Content = content; Touch(); }
	public void SetType(QuestionType type) { Type = type; Touch(); }
	public void SetDifficulty(QuestionDifficulty difficulty) { Difficulty = difficulty; Touch(); }
	public void SetShuffleAnswers(bool shuffleAnswers) { ShuffleAnswers = shuffleAnswers; Touch(); }
	public void SetModifiedBy(Guid modifiedById) { ModifiedById = modifiedById; Touch(); }
	private void Touch() { ModifiedOn = DateTime.UtcNow; }
}