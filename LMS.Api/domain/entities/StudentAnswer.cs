namespace LMS.Api.domain.Entities;

public class StudentAnswer
{
    public Guid Id { get; private set; }
    public Guid ExaminationAttemptId { get; private set; }
    public ExaminationAttempt? ExaminationAttempt { get; private set; }
    public Guid ExaminationQuestionId { get; private set; }
    public ExaminationQuestion? ExaminationQuestion { get; private set; }
    public Guid? SelectedExaminationAnswerId { get; private set; }
    public ExaminationAnswer? SelectedExaminationAnswer { get; private set; }
    public ICollection<StudentAnswerValue> Values { get; private set; } = new List<StudentAnswerValue>();
    public DateTime CreatedOn { get; private set; }
    public DateTime ModifiedOn { get; private set; }

    private StudentAnswer() { }
    public StudentAnswer(Guid examinationAttemptId, Guid examinationQuestionId)
    {
        Id = Guid.NewGuid(); ExaminationAttemptId = examinationAttemptId; ExaminationQuestionId = examinationQuestionId;
        CreatedOn = DateTime.UtcNow; ModifiedOn = DateTime.UtcNow;
    }
    public void SelectAnswer(Guid? answerId) { SelectedExaminationAnswerId = answerId; ModifiedOn = DateTime.UtcNow; }
}
