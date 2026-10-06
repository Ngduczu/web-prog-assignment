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

    [Fact]
    public async Task MaterialLargerThanCourseLimitIsRejectedBeforeStorageUpload()
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
            new FakeUnitOfWork());
        await using var content = new MemoryStream([1]);

        await Assert.ThrowsAsync<LMS.Api.domain.exceptions.BadRequestException>(() =>
            service.UploadMaterialAsync(
                lesson.Id,
                new FileUpload("large.pdf", "application/pdf", 20_000_001, content)));

        Assert.Equal(0, storage.UploadCount);
    }

    [Fact]
    public async Task StudentListsOnlyPublishedLessonsWhenEnrolled()
    {
        var teacher = CreateTeacher();
        var student = CreateStudent();
        var course = CreateCourse(teacher);
        var published = new Lesson(course.Id, "Published", null, 0, teacher.Id, LessonStatus.Published);
        var draft = new Lesson(course.Id, "Draft", null, 1, teacher.Id, LessonStatus.Draft);
        var enrollment = new Enrollment(student.Id, course.Id, EnrollmentStatus.Accepted);
        var service = new LessonService(
            new FakeLessonRepository(published, draft),
            new FakeMaterialRepository(),
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(enrollment),
            new FakeFileStorage(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student));

        var result = await service.ListAsync(course.Id);

        Assert.Single(result);
        Assert.Equal(published.Id, result[0].Id);
        Assert.Equal(LessonStatus.Published, result[0].Status);
    }

    [Fact]
    public async Task StudentCannotGetDraftLesson()
    {
        var teacher = CreateTeacher();
        var student = CreateStudent();
        var course = CreateCourse(teacher);
        var draft = new Lesson(course.Id, "Draft", null, 0, teacher.Id, LessonStatus.Draft);
        var enrollment = new Enrollment(student.Id, course.Id, EnrollmentStatus.Accepted);
        var service = new LessonService(
            new FakeLessonRepository(draft),
            new FakeMaterialRepository(),
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(enrollment),
            new FakeFileStorage(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student));

        await Assert.ThrowsAsync<LMS.Api.domain.exceptions.NotFoundException>(() =>
            service.GetAsync(draft.Id));
    }

    [Fact]
    public async Task StudentListsOnlyPublishedExercisesWhenEnrolled()
    {
        var teacher = CreateTeacher();
        var student = CreateStudent();
        var course = CreateCourse(teacher);
        var draft = new Exercise(course.Id, "Draft", null, null, teacher.Id);
        var published = new Exercise(
            course.Id,
            "Published",
            null,
            DateTime.UtcNow.AddDays(1),
            teacher.Id,
            ExerciseStatus.Published);
        var service = new ExerciseService(
            new FakeExerciseRepository(draft, published),
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(new Enrollment(student.Id, course.Id, EnrollmentStatus.Accepted)),
            new FakeFileStorage(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student));

        var result = await service.ListAsync(course.Id);

        var item = Assert.Single(result);
        Assert.Equal(published.Id, item.Id);
        Assert.Equal(ExerciseStatus.Published, item.Status);
    }

    [Fact]
    public async Task StudentCanReadMaterialFromPublishedLessonWhenEnrolled()
    {
        var teacher = CreateTeacher();
        var student = CreateStudent();
        var course = CreateCourse(teacher);
        var lesson = new Lesson(course.Id, "Published", null, 0, teacher.Id, LessonStatus.Published);
        var material = new Material(
            lesson.Id,
            "lesson.pdf",
            "courses/course/material.pdf",
            "application/pdf",
            3,
            teacher.Id);
        var materials = new FakeMaterialRepository();
        materials.Items.Add(material);
        var storage = new FakeFileStorage();
        var service = new LessonService(
            new FakeLessonRepository(lesson),
            materials,
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(new Enrollment(student.Id, course.Id, EnrollmentStatus.Accepted)),
            storage,
            new FakeUnitOfWork(),
            new FakeCurrentUser(student));

        var result = await service.GetMaterialContentAsync(material.Id);
        await using var content = result.Content;

        Assert.Equal("lesson.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(material.ObjectKey, storage.LastOpenedObjectKey);
    }

    [Fact]
    public async Task StudentCannotReadMaterialFromDraftLesson()
    {
        var teacher = CreateTeacher();
        var student = CreateStudent();
        var course = CreateCourse(teacher);
        var lesson = new Lesson(course.Id, "Draft", null, 0, teacher.Id, LessonStatus.Draft);
        var material = new Material(
            lesson.Id,
            "lesson.pdf",
            "courses/course/material.pdf",
            "application/pdf",
            3,
            teacher.Id);
        var materials = new FakeMaterialRepository();
        materials.Items.Add(material);
        var storage = new FakeFileStorage();
        var service = new LessonService(
            new FakeLessonRepository(lesson),
            materials,
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(new Enrollment(student.Id, course.Id, EnrollmentStatus.Accepted)),
            storage,
            new FakeUnitOfWork(),
            new FakeCurrentUser(student));

        await Assert.ThrowsAsync<LMS.Api.domain.exceptions.NotFoundException>(() =>
            service.GetMaterialContentAsync(material.Id));

        Assert.Null(storage.LastOpenedObjectKey);
    }

    [Fact]
    public async Task DeleteLessonRemovesLessonAndStoredMaterials()
    {
        var teacher = CreateTeacher();
        var course = CreateCourse(teacher);
        var lesson = new Lesson(course.Id, "Lesson", null, 0, teacher.Id);
        var lessons = new FakeLessonRepository(lesson);
        var materials = new FakeMaterialRepository();
        materials.Items.Add(new Material(lesson.Id, "lesson.pdf", "lesson/key", "application/pdf", 3, teacher.Id));
        var storage = new FakeFileStorage();
        var service = new LessonService(
            lessons,
            materials,
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(),
            storage,
            new FakeUnitOfWork(),
            new FakeCurrentUser(teacher));

        await service.DeleteAsync(lesson.Id);

        Assert.Empty(lessons.Items);
        Assert.Equal(["lesson/key"], storage.DeletedObjectKeys);
    }

    [Fact]
    public async Task PublishedExerciseCannotBeDeleted()
    {
        var teacher = CreateTeacher();
        var course = CreateCourse(teacher);
        var exercise = new Exercise(course.Id, "Published", null, null, teacher.Id, ExerciseStatus.Published);
        var exercises = new FakeExerciseRepository(exercise);
        var service = new ExerciseService(
            exercises,
            new FakeCourseRepository(course),
            new FakeEnrollmentRepository(),
            new FakeFileStorage(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(teacher));

        await Assert.ThrowsAsync<LMS.Api.domain.exceptions.ConflictException>(() =>
            service.DeleteAsync(exercise.Id));

        Assert.Single(exercises.Items);
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
            new FakeEnrollmentRepository(),
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

    private static User CreateStudent() => new(
        $"{Guid.NewGuid():N}@example.com",
        "Student",
        null,
        null,
        Role.Student,
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
        public string? LastOpenedObjectKey { get; private set; }

        public Task<Stream> OpenReadAsync(
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            LastOpenedObjectKey = objectKey;
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }

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

        public Task<IReadOnlyList<Material>> ListByLessonAsync(
            Guid lessonId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Material>>(
                Items.Where(material => material.LessonId == lessonId).ToArray());

        public void Add(Material material)
        {
            Items.Add(material);
        }

        public void Remove(Material material)
        {
            Items.Remove(material);
        }
    }

    private sealed class FakeEnrollmentRepository : IEnrollmentRepository
    {
        private readonly Enrollment? _enrollment;

        public FakeEnrollmentRepository(Enrollment? enrollment = null)
        {
            _enrollment = enrollment;
        }

        public Task<Enrollment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_enrollment?.Id == id ? _enrollment : null);

        public Task<Enrollment?> GetByStudentAndCourseAsync(
            Guid studentId,
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _enrollment is not null &&
                _enrollment.StudentId == studentId &&
                _enrollment.CourseId == courseId
                    ? _enrollment
                    : null);

        public Task<IReadOnlyList<Enrollment>> ListByStudentAsync(
            Guid studentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(
                _enrollment is not null && _enrollment.StudentId == studentId ? [_enrollment] : []);

        public Task<IReadOnlyList<Enrollment>> ListByCourseAsync(
            Guid courseId,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(
                _enrollment is not null && _enrollment.CourseId == courseId ? [_enrollment] : []);

        public Task<int> CountByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(_enrollment is not null && _enrollment.CourseId == courseId ? 1 : 0);

        public Task<int> CountAcceptedByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(
                _enrollment is not null &&
                _enrollment.CourseId == courseId &&
                _enrollment.Status == EnrollmentStatus.Accepted
                    ? 1
                    : 0);

        public void Add(Enrollment enrollment)
        {
        }
    }

    private sealed class FakeLessonRepository : ILessonRepository
    {
        public List<Lesson> Items { get; }

        public FakeLessonRepository(params Lesson[] lessons)
        {
            Items = [.. lessons];
        }

        public Task<Lesson?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(lesson => lesson.Id == id));

        public Task<IReadOnlyList<Lesson>> ListByCourseAsync(
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Lesson>>(
                Items.Where(lesson => lesson.CourseId == courseId).ToArray());

        public Task<bool> OrderExistsAsync(
            Guid courseId,
            int order,
            Guid? excludingLessonId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public void Add(Lesson lesson)
        {
        }

        public void Remove(Lesson lesson) => Items.Remove(lesson);
    }

    private sealed class FakeExerciseRepository : IExerciseRepository
    {
        public List<Exercise> Items { get; }

        public FakeExerciseRepository(params Exercise[] exercises)
        {
            Items = [.. exercises];
        }

        public Task<Exercise?> GetByIdAsync(Guid id, bool includeAttachment, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(exercise => exercise.Id == id));

        public Task<IReadOnlyList<Exercise>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Exercise>>(
                Items.Where(exercise => exercise.CourseId == courseId).ToArray());

        public void Add(Exercise exercise) { }
        public void AddAttachment(ExerciseAttachment attachment) { }
        public void Remove(Exercise exercise) => Items.Remove(exercise);
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
