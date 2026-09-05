using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.exceptions;

namespace LMS.Api.domain.Entities.Examinations;

public class ExaminationAttempt
{
    public Guid Id { get; private set; }
    public Guid ExaminationId { get; private set; }
    public Examination? Examination { get; private set; }
    public Guid StudentId { get; private set; }
    public User? Student { get; private set; }
    public ExaminationAttemptStatus Status { get; private set; }
    public ExaminationSubmitReason? SubmitReason { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? DisconnectedAt { get; private set; }
    public int? RemainingSeconds { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public int ViolationCount { get; private set; }
    public int CorrectAnswers { get; private set; }
    public int TotalQuestions { get; private set; }
    public decimal Score { get; private set; }
    public ICollection<StudentAnswer> Answers { get; private set; } = new List<StudentAnswer>();
    public ICollection<ExaminationViolation> Violations { get; private set; } = new List<ExaminationViolation>();

    private ExaminationAttempt() { }
    public ExaminationAttempt(Guid examinationId, Guid studentId, DateTime startedAt, int totalQuestions)
    {
        Id = Guid.NewGuid(); ExaminationId = examinationId; StudentId = studentId; StartedAt = startedAt;
        LastActivityAt = startedAt; TotalQuestions = totalQuestions; Status = ExaminationAttemptStatus.InProgress;
    }

    public void TouchActivity(DateTime activityAt) { LastActivityAt = activityAt; }
    public void Disconnect(DateTime disconnectedAt, int remainingSeconds)
    {
        if (Status != ExaminationAttemptStatus.InProgress) throw new ConflictException("Only in-progress attempts can be disconnected.");
        Status = ExaminationAttemptStatus.Disconnected; DisconnectedAt = disconnectedAt; RemainingSeconds = remainingSeconds; LastActivityAt = disconnectedAt;
    }
    public void Reconnect(DateTime reconnectedAt)
    {
        if (Status != ExaminationAttemptStatus.Disconnected) throw new ConflictException("Only disconnected attempts can reconnect.");
        Status = ExaminationAttemptStatus.InProgress; LastActivityAt = reconnectedAt; DisconnectedAt = null;
    }
    public void Submit(ExaminationSubmitReason reason = ExaminationSubmitReason.StudentSubmitted) { Complete(ExaminationAttemptStatus.Submitted, reason); }
    public void AutoSubmit(ExaminationSubmitReason reason) { Complete(ExaminationAttemptStatus.AutoSubmitted, reason); }
    public void SetResult(int correctAnswers, int totalQuestions)
    {
        CorrectAnswers = correctAnswers; TotalQuestions = totalQuestions;
        Score = totalQuestions > 0 ? (decimal)correctAnswers / totalQuestions * 10 : 0;
    }
    public void AddViolation() { ViolationCount++; }
    private void Complete(ExaminationAttemptStatus status, ExaminationSubmitReason reason)
    {
        if (Status != ExaminationAttemptStatus.InProgress && Status != ExaminationAttemptStatus.Disconnected)
            throw new ConflictException("Only active attempts can be submitted.");
        Status = status; SubmitReason = reason; SubmittedAt = DateTime.UtcNow; LastActivityAt = SubmittedAt.Value;
    }
}
