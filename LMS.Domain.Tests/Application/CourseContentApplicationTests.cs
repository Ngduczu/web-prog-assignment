using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.features.coursecontents;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public class CourseContentApplicationTests
{
    [Fact]
    public async Task UploadMaterialStoresFileBeforePersistingMetadata()
    {
        var teacher = CreateTeacher();
        var course = CreateCourse(teacher);
        var lesson = new Lesson(course.Id, "Lesson", null, 0, teacher.Id);
        var materials = new FakeMaterialRepository();
        var storage = new FakeFileStorage();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateLessonService(teacher, course, lesson, materials, storage, unitOfWork);
        await using var content = new MemoryStream([1, 2, 3]);

        var result = await service.UploadMaterialAsync(
            lesson.Id,
            new FileUpload("lesson.pdf", "application/pdf", content.Length, content));

        Assert.Equal(1, storage.UploadCount);
        Assert.Equal("stored/object.pdf", result.ObjectKey);
        Assert.Single(materials.Items);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Empty(storage.DeletedObjectKeys);
    }

    [Fact]
    public async Task UploadMaterialDeletesStoredFileWhenDatabaseSaveFails()
    {
        var teacher = CreateTeacher();
        var course = CreateCourse(teacher);
        var lesson = new Lesson(course.Id, "Lesson", null, 0, teacher.Id);
        var storage = new FakeFileStorage();
        var service = CreateLessonService(
            teacher,
            course,
            lesson,
            new FakeMaterialRepository(),
            storage,
            new FakeUnitOfWork(shouldFail: true));
        await using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadMaterialAsync(
                lesson.Id,
                new FileUpload("lesson.pdf", "application/pdf", content.Length, content)));

        Assert.Equal(["stored/object.pdf"], storage.DeletedObjectKeys);
    }

    private static LessonService CreateLessonService(
        User teacher,
        Course course,
        Lesson lesson,
        FakeMaterialRepository materials,
        FakeFileStorage storage,
        FakeUnitOfWork unitOfWork) =>
        new(
            new FakeLessonRepository(lesson),
            materials,
            new FakeCourseRepository(course),
            storage,
            unitOfWork,
            new FakeCurrentUser(teacher));

    private static User CreateTeacher() => new(
        $"{Guid.NewGuid():N}@example.com",
        "Teacher",
        null,
        null,
        Role.Teacher,
        AccountStatus.Active);

    private static Course CreateCourse(User teacher) =>
        new("Course", null, teacher.Id, 30, CourseStatus.Open, teacher.Id);

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(User user)
        {
            UserId = user.Id;
            Role = user.Role;
        }

        public bool IsAuthenticated => true;
        public Guid? UserId { get; }
        public Role? Role { get; }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly bool _shouldFail;

        public FakeUnitOfWork(bool shouldFail = false)
        {
            _shouldFail = shouldFail;
        }

        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return _shouldFail
                ? Task.FromException<int>(new InvalidOperationException("Database failure"))
                : Task.FromResult(1);
        }
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public int UploadCount { get; private set; }
        public List<string> DeletedObjectKeys { get; } = [];

        public Task<StoredFile> UploadAsync(
            FileUpload upload,
            string directory,
            long? maximumSizeBytes = null,
            CancellationToken cancellationToken = default)
        {
            UploadCount++;
            return Task.FromResult(new StoredFile(
                upload.FileName,
                "stored/object.pdf",
                upload.ContentType,
                upload.Length));
        }

        public Task DeleteIfExistsAsync(
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            DeletedObjectKeys.Add(objectKey);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMaterialRepository : IMaterialRepository
    {
        public List<Material> Items { get; } = [];

        public Task<Material?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(material => material.Id == id));

        public void Add(Material material)
        {
            Items.Add(material);
        }

        public void Remove(Material material)
        {
            Items.Remove(material);
        }
    }

    private sealed class FakeLessonRepository : ILessonRepository
    {
        private readonly Lesson _lesson;

        public FakeLessonRepository(Lesson lesson)
        {
            _lesson = lesson;
        }

        public Task<Lesson?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Lesson?>(_lesson.Id == id ? _lesson : null);

        public Task<IReadOnlyList<Lesson>> ListByCourseAsync(
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Lesson>>([_lesson]);

        public Task<bool> OrderExistsAsync(
            Guid courseId,
            int order,
            Guid? excludingLessonId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public void Add(Lesson lesson)
        {
        }
    }

    private sealed class FakeCourseRepository : ICourseRepository
    {
        private readonly Course _course;

        public FakeCourseRepository(Course course)
        {
            _course = course;
        }

        public Task<Course?> GetByIdAsync(
            Guid id,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            Task.FromResult<Course?>(_course.Id == id ? _course : null);

        public Task<IReadOnlyList<Course>> ListByIdsAsync(
            IReadOnlyCollection<Guid> courseIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Course>>(
                courseIds.Contains(_course.Id) ? [_course] : []);

        public Task<IReadOnlyList<Course>> ListAsync(
            string? search,
            CourseStatus? status,
            Guid? teacherId,
            bool includeDeleted,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Course>>([_course]);

        public Task<int> CountAsync(
            string? search,
            CourseStatus? status,
            Guid? teacherId,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            Task.FromResult(1);

        public void Add(Course course)
        {
        }
    }
}
