using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.exceptions;

namespace LMS.Domain.Tests.Courses;

public class CourseTests
{
    [Fact]
    public void DeleteAndRestorePreserveCourseStatus()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);

        course.Delete(teacherId);

        Assert.True(course.IsDeleted);
        Assert.Equal(CourseStatus.Open, course.Status);
        Assert.NotNull(course.DeletedAt);
        Assert.Equal(teacherId, course.DeletedById);

        course.Restore();

        Assert.False(course.IsDeleted);
        Assert.Equal(CourseStatus.Open, course.Status);
        Assert.Null(course.DeletedAt);
        Assert.Null(course.DeletedById);
    }

    [Fact]
    public void DeleteAlreadyDeletedCourseThrows()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        course.Delete(teacherId);

        Assert.Throws<ConflictException>(() => course.Delete(teacherId));
    }
}
