using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Enums.Questions;

namespace LMS.Api.domain.Entities.Examinations;

public class ExaminationQuestionSelectionRule
{
    public Guid Id { get; private set; }
    public Guid ExaminationId { get; private set; }
    public Examination? Examination { get; private set; }
    public Guid QuestionBankId { get; private set; }
    public QuestionBank? QuestionBank { get; private set; }
    public QuestionDifficulty Difficulty { get; private set; }
    public int QuestionCount { get; private set; }

    private ExaminationQuestionSelectionRule() { }
    public ExaminationQuestionSelectionRule(Guid examinationId, Guid questionBankId, QuestionDifficulty difficulty, int questionCount)
    {
        Id = Guid.NewGuid(); ExaminationId = examinationId; QuestionBankId = questionBankId; Difficulty = difficulty; QuestionCount = questionCount;
    }
}
