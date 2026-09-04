using LMS.Api.domain.Enums.Examinations;

namespace LMS.Api.domain.Entities.Examinations;

public class ExaminationViolation
{
    public Guid Id { get; private set; }
    public Guid ExaminationAttemptId { get; private set; }
    public ExaminationAttempt? ExaminationAttempt { get; private set; }
    public ExaminationViolationType Type { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private ExaminationViolation() { }
    public ExaminationViolation(Guid examinationAttemptId, ExaminationViolationType type, DateTime occurredAt)
    {
        Id = Guid.NewGuid(); ExaminationAttemptId = examinationAttemptId; Type = type; OccurredAt = occurredAt;
    }
}
