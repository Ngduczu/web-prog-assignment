using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Entities.Users;
using LMS.Api.infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
            typeof(ChatAttachment)
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

    private static void AssertUniqueIndex<TEntity>(AppDbContext context, params string[] propertyNames)
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity));
        var index = entityType?.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }
}
