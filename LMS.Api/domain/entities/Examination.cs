using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class Examination
{
    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Course? Course { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public ExaminationStatus Status { get; private set; }
    public DateTime StartAt { get; private set; }
    public DateTime DueAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public bool ShuffleQuestions { get; private set; }
    public bool ShowScoreImmediately { get; private set; }
    public bool RequireFullscreen { get; private set; }
    public int FullscreenGraceSeconds { get; private set; } = 3;
    public int MaxViolations { get; private set; }
    public bool BlockCopyPaste { get; private set; }
    public bool BlockRightClick { get; private set; }
    public bool DetectTabChange { get; private set; }
    public bool DetectDevTools { get; private set; }
    public int MaxDisconnectMinutes { get; private set; } = 10;
    public ICollection<ExaminationQuestionSelectionRule> QuestionSelectionRules { get; private set; } = new List<ExaminationQuestionSelectionRule>();
    public ICollection<ExaminationQuestion> Questions { get; private set; } = new List<ExaminationQuestion>();
    public ICollection<ExaminationAttempt> Attempts { get; private set; } = new List<ExaminationAttempt>();
    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }
    public User? CreatedBy { get; private set; }
    public User? ModifiedBy { get; private set; }

    private Examination() { }

    public Examination(Guid courseId, string title, string? description, DateTime startAt, DateTime dueAt, int durationMinutes, Guid createdById)
    {
        Id = Guid.NewGuid(); CourseId = courseId; Title = title; Description = description;
        StartAt = startAt; DueAt = dueAt; DurationMinutes = durationMinutes; Status = ExaminationStatus.Draft;
        CreatedAt = DateTime.UtcNow; ModifiedAt = DateTime.UtcNow; CreatedById = createdById; ModifiedById = createdById;
    }

    public void SetStatus(ExaminationStatus status) { Status = status; Touch(); }
    public void SetSchedule(DateTime startAt, DateTime dueAt, int durationMinutes) { StartAt = startAt; DueAt = dueAt; DurationMinutes = durationMinutes; Touch(); }
    public void SetTitle(string title) { Title = title; Touch(); }
    public void SetDescription(string? description) { Description = description; Touch(); }
    public void SetAntiCheat(bool requireFullscreen, int fullscreenGraceSeconds, int maxViolations, bool blockCopyPaste, bool blockRightClick, bool detectTabChange, bool detectDevTools, int maxDisconnectMinutes)
    {
        RequireFullscreen = requireFullscreen; FullscreenGraceSeconds = fullscreenGraceSeconds; MaxViolations = maxViolations;
        BlockCopyPaste = blockCopyPaste; BlockRightClick = blockRightClick; DetectTabChange = detectTabChange; DetectDevTools = detectDevTools;
        MaxDisconnectMinutes = maxDisconnectMinutes; Touch();
    }
    public void SetDisplayOptions(bool shuffleQuestions, bool showScoreImmediately) { ShuffleQuestions = shuffleQuestions; ShowScoreImmediately = showScoreImmediately; Touch(); }
    public void SetModifiedBy(Guid modifiedById) { ModifiedById = modifiedById; Touch(); }
    private void Touch() { ModifiedAt = DateTime.UtcNow; }
}
