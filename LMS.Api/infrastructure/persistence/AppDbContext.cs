using LMS.Api.application.abstractions.persistence;
using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence;

public class AppDbContext : DbContext, IUnitOfWork, ITransactionManager
{
    public  AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {}

    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalUser> ExternalUsers => Set<ExternalUser>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<ExerciseAttachment> ExerciseAttachments => Set<ExerciseAttachment>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionAttachment> SubmissionAttachments => Set<SubmissionAttachment>();
    public DbSet<QuestionBank> QuestionBanks => Set<QuestionBank>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<MultipleChoiceOption> MultipleChoiceOptions => Set<MultipleChoiceOption>();
    public DbSet<FillInBlankAnswerKey> FillInBlankAnswerKeys => Set<FillInBlankAnswerKey>();
    public DbSet<Examination> Examinations => Set<Examination>();
    public DbSet<ExaminationQuestionSelectionRule> ExaminationQuestionSelectionRules => Set<ExaminationQuestionSelectionRule>();
    public DbSet<ExaminationQuestion> ExaminationQuestions => Set<ExaminationQuestion>();
    public DbSet<ExaminationMultipleChoiceOption> ExaminationMultipleChoiceOptions => Set<ExaminationMultipleChoiceOption>();
    public DbSet<ExaminationFillInBlankAnswerKey> ExaminationFillInBlankAnswerKeys => Set<ExaminationFillInBlankAnswerKey>();
    public DbSet<ExaminationAttempt> ExaminationAttempts => Set<ExaminationAttempt>();
    public DbSet<ExaminationViolation> ExaminationViolations => Set<ExaminationViolation>();
    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();
    public DbSet<StudentFillInBlankAnswer> StudentFillInBlankAnswers => Set<StudentFillInBlankAnswer>();
    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
    public DbSet<ChatParticipant> ChatParticipants => Set<ChatParticipant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly
        );

        base.OnModelCreating(modelBuilder);
    }

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        var executionStrategy = Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
