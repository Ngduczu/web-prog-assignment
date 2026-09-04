using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class ExaminationQuestionSelection
{
    public Guid Id { get; private set; }
    public Guid ExaminationId { get; private set; }
    public Examination? Examination { get; private set; }
    public Guid QuestionBankId { get; private set; }
    public QuestionBank? QuestionBank { get; private set; }
    public QuestionDifficulty Difficulty { get; private set; }
    public int QuestionCount { get; private set; }

    private ExaminationQuestionSelection() { }
    public ExaminationQuestionSelection(Guid examinationId, Guid questionBankId, QuestionDifficulty difficulty, int questionCount)
    {
        Id = Guid.NewGuid(); ExaminationId = examinationId; QuestionBankId = questionBankId; Difficulty = difficulty; QuestionCount = questionCount;
    }
}
