using LMS.Api.application.abstractions.persistence;
using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Entities.Storage;
using LMS.Api.domain.exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace LMS.Api.infrastructure.Persistence;

public class AppDbContext : DbContext, IUnitOfWork, ITransactionManager
{
    private const int MaximumTransactionAttempts = 3;
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
    public DbSet<FileCleanupTask> FileCleanupTasks => Set<FileCleanupTask>();
    
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
        await ExecuteWithIsolationAsync(action, IsolationLevel.ReadCommitted, cancellationToken);
    }

    public async Task ExecuteSerializableAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithIsolationAsync(action, IsolationLevel.Serializable, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await SaveChangesWithIdempotentCleanupQueueAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("The data was changed by another request. Please retry.", exception);
        }
        catch (DbUpdateException exception) when (DatabaseConstraintPolicy.IsBusinessUniqueViolation(exception))
        {
            throw new ConflictException("The record already exists.", exception);
        }
    }

    private async Task<int> SaveChangesWithIdempotentCleanupQueueAsync(
        CancellationToken cancellationToken)
    {
        var cleanupEntries = ChangeTracker
            .Entries<FileCleanupTask>()
            .Where(entry => entry.State == EntityState.Added)
            .ToArray();
        if (cleanupEntries.Length == 0)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        var cleanupTasks = cleanupEntries
            .Select(entry => entry.Entity)
            .GroupBy(task => task.ObjectKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        foreach (var entry in cleanupEntries)
        {
            entry.State = EntityState.Detached;
        }

        if (Database.CurrentTransaction is not null)
        {
            return await SaveBusinessChangesAndUpsertCleanupTasksAsync(
                cleanupTasks,
                cancellationToken);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var affected = await SaveBusinessChangesAndUpsertCleanupTasksAsync(
            cleanupTasks,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected;
    }

    private async Task<int> SaveBusinessChangesAndUpsertCleanupTasksAsync(
        IReadOnlyCollection<FileCleanupTask> cleanupTasks,
        CancellationToken cancellationToken)
    {
        var affected = await base.SaveChangesAsync(cancellationToken);
        foreach (var task in cleanupTasks)
        {
            affected += await Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO file_cleanup_tasks
                    (id, object_key, attempt_count, next_attempt_at, last_error, created_at)
                VALUES
                    ({{task.Id}}, {{task.ObjectKey}}, {{task.AttemptCount}}, {{task.NextAttemptAt}}, NULL, {{task.CreatedAt}})
                ON CONFLICT (object_key) DO NOTHING
                """, cancellationToken);
        }

        return affected;
    }

    private async Task ExecuteWithIsolationAsync(
        Func<CancellationToken, Task> action,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumTransactionAttempts; attempt++)
        {
            try
            {
                await using var transaction = await Database.BeginTransactionAsync(isolationLevel, cancellationToken);
                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (IsRetryableTransactionFailure(exception))
            {
                ChangeTracker.Clear();
                if (attempt == MaximumTransactionAttempts)
                {
                    throw new ConflictException(
                        "The transaction conflicted with another request. Please retry.",
                        exception);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    internal static bool IsRetryableTransactionFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException &&
                postgresException.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                return true;
            }
        }

        return false;
    }
}
