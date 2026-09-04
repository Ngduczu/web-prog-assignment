namespace LMS.Api.domain.Entities;

public class QuestionBank
{
	public Guid Id { get; private set; }
	public Guid TeacherId { get; private set; }
	public Users? Teacher { get; private set; }

	public string Name { get; private set; } = null!;
	public string? Description { get; private set; }
	public ICollection<Question> Questions { get; private set; } = new List<Question>();

	public DateTime CreatedOn { get; private set; }
	public DateTime ModifiedOn { get; private set; }
	public Guid CreatedById { get; private set; }
	public Guid ModifiedById { get; private set; }
	public Users? CreatedBy { get; private set; }
	public Users? ModifiedBy { get; private set; }

	private QuestionBank() { }

	public QuestionBank(Guid teacherId, string name, string? description, Guid createdById)
	{
		Id = Guid.NewGuid();
		TeacherId = teacherId;
		Name = name;
		Description = description;
		CreatedOn = DateTime.UtcNow;
		ModifiedOn = DateTime.UtcNow;
		CreatedById = createdById;
		ModifiedById = createdById;
	}

	public void SetName(string name) { Name = name; Touch(); }
	public void SetDescription(string? description) { Description = description; Touch(); }
	public void SetModifiedBy(Guid modifiedById) { ModifiedById = modifiedById; Touch(); }
	private void Touch() { ModifiedOn = DateTime.UtcNow; }
}