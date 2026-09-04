namespace LMS.Api.domain.Entities.Examinations;

public class StudentAnswer
{
    public Guid Id { get; private set; }
    public Guid ExaminationAttemptId { get; private set; }
    public ExaminationAttempt? ExaminationAttempt { get; private set; }
    public Guid ExaminationQuestionId { get; private set; }
    public ExaminationQuestion? ExaminationQuestion { get; private set; }
    public Guid? SelectedChoiceId { get; private set; }
    public ExaminationMultipleChoiceOption? SelectedChoice { get; private set; }
    public ICollection<StudentFillInBlankAnswer> FillInBlankAnswers { get; private set; } = new List<StudentFillInBlankAnswer>();
    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    private StudentAnswer() { }
    public StudentAnswer(Guid examinationAttemptId, Guid examinationQuestionId)
    {
        Id = Guid.NewGuid(); ExaminationAttemptId = examinationAttemptId; ExaminationQuestionId = examinationQuestionId;
        CreatedAt = DateTime.UtcNow; ModifiedAt = DateTime.UtcNow;
    }
    public void SelectChoice(Guid? choiceId) { SelectedChoiceId = choiceId; ModifiedAt = DateTime.UtcNow; }
}
