using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Entities.Storage;
using LMS.Api.infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LMS.Domain.Tests.Persistence;

public class AppDbContextModelTests
{
    [Fact]
    public void ModelContainsEveryDomainEntity()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=lms_model_test;Username=lms;Password=lms")
            .Options;

        using var context = new AppDbContext(options);

        Type[] expectedEntityTypes =
        [
            typeof(User),
            typeof(ExternalUser),
            typeof(Course),
            typeof(Enrollment),
            typeof(Lesson),
            typeof(Material),
            typeof(Exercise),
            typeof(ExerciseAttachment),
            typeof(Submission),
            typeof(SubmissionAttachment),
            typeof(QuestionBank),
            typeof(Question),
            typeof(MultipleChoiceOption),
            typeof(FillInBlankAnswerKey),
            typeof(Examination),
            typeof(ExaminationQuestionSelectionRule),
            typeof(ExaminationQuestion),
            typeof(ExaminationMultipleChoiceOption),
            typeof(ExaminationFillInBlankAnswerKey),
            typeof(ExaminationAttempt),
            typeof(ExaminationViolation),
            typeof(StudentAnswer),
            typeof(StudentFillInBlankAnswer),
            typeof(ChatRoom),
            typeof(ChatParticipant),
            typeof(ChatMessage),
            typeof(ChatAttachment),
            typeof(FileCleanupTask)
        ];

        Assert.All(expectedEntityTypes, entityType =>
            Assert.NotNull(context.Model.FindEntityType(entityType)));
    }

    [Fact]
    public void CriticalBusinessIndexesAreUnique()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=lms_model_test;Username=lms;Password=lms")
            .Options;

        using var context = new AppDbContext(options);

        AssertUniqueIndex<Enrollment>(context, nameof(Enrollment.StudentId), nameof(Enrollment.CourseId));
        AssertUniqueIndex<ChatRoom>(context, nameof(ChatRoom.CourseId));
        AssertUniqueIndex<ChatRoom>(context, nameof(ChatRoom.DirectMessageKey));
        AssertUniqueIndex<ChatParticipant>(context, nameof(ChatParticipant.ChatRoomId), nameof(ChatParticipant.UserId));
        AssertUniqueIndex<StudentAnswer>(context, nameof(StudentAnswer.ExaminationAttemptId), nameof(StudentAnswer.ExaminationQuestionId));
    }

    [Fact]
    public void RelationalModelGeneratesCreateScript()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=lms_model_test;Username=lms;Password=lms")
            .Options;

        using var context = new AppDbContext(options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CREATE TABLE courses", script);
        Assert.Contains("CREATE TABLE examinations", script);
        Assert.Contains("CREATE TABLE chat_rooms", script);
        Assert.Contains("CREATE TABLE chat_messages", script);
    }

    [Fact]
    public void SubmissionUsesPostgresRowVersionForOptimisticConcurrency()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=lms_model_test;Username=lms;Password=lms")
            .Options;

        using var context = new AppDbContext(options);
        var version = context.Model.FindEntityType(typeof(Submission))?.FindProperty("Version");

        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("xmin", version.GetColumnName());
    }

    [Fact]
    public void UserAuthVersionIsPersistentAndConcurrencyProtected()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=lms_model_test;Username=lms;Password=lms")
            .Options;

        using var context = new AppDbContext(options);
        var authVersion = context.Model.FindEntityType(typeof(User))?.FindProperty(nameof(User.AuthVersion));

        Assert.NotNull(authVersion);
        Assert.True(authVersion.IsConcurrencyToken);
        Assert.Equal("auth_version", authVersion.GetColumnName());
        Assert.Equal(1L, authVersion.GetDefaultValue());
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure, true)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, false)]
    public void TransactionRetryOnlyHandlesConcurrencySqlStates(string sqlState, bool expected)
    {
        var postgresException = new PostgresException(
            "database error",
            "ERROR",
            "ERROR",
            sqlState);
        var wrapped = new DbUpdateException("save failed", postgresException);

        var result = AppDbContext.IsRetryableTransactionFailure(wrapped);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("IX_users_email", true)]
    [InlineData("IX_enrollments_student_id_course_id", true)]
    [InlineData("IX_unexpected_internal_index", false)]
    public void OnlyAllowlistedUniqueConstraintsAreBusinessConflicts(
        string constraintName,
        bool expected)
    {
        var postgresException = new PostgresException(
            "duplicate key",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName);
        var wrapped = new DbUpdateException("save failed", postgresException);

        var result = DatabaseConstraintPolicy.IsBusinessUniqueViolation(wrapped);

        Assert.Equal(expected, result);
    }

    private static void AssertUniqueIndex<TEntity>(AppDbContext context, params string[] propertyNames)
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity));
        var index = entityType?.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }
}
