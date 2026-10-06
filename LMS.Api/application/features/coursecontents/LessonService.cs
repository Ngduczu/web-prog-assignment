using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.features.courses;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.coursecontents;

public sealed class LessonService : ILessonService
{
    private readonly ILessonRepository _lessons;
    private readonly IMaterialRepository _materials;
    private readonly ICourseRepository _courses;
    private readonly IEnrollmentRepository _enrollments;
    private readonly IFileStorage _fileStorage;
    private readonly IFileCleanupQueue? _fileCleanup;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public LessonService(
        ILessonRepository lessons,
        IMaterialRepository materials,
        ICourseRepository courses,
        IEnrollmentRepository enrollments,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IFileCleanupQueue? fileCleanup = null)
    {
        _lessons = lessons;
        _materials = materials;
        _courses = courses;
        _enrollments = enrollments;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _fileCleanup = fileCleanup;
    }

    public async Task<LessonDto> CreateAsync(
        Guid courseId,
        CreateLessonRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        ValidateLesson(request.Name, request.Order, request.Status);
        var course = await GetCourseAsync(courseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);

        if (await _lessons.OrderExistsAsync(courseId, request.Order, null, cancellationToken))
        {
            throw new ConflictException("Lesson order already exists in this course.");
        }

        var lesson = new Lesson(
            courseId,
            request.Name.Trim(),
            NormalizeOptional(request.Content),
            request.Order,
            actorId,
            request.Status);
        _lessons.Add(lesson);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(lesson);
    }

    public async Task<IReadOnlyList<LessonDto>> ListAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        var course = await GetCourseAsync(courseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        var lessons = await _lessons.ListByCourseAsync(courseId, cancellationToken);

        if (!CourseAuthorization.IsCourseManager(_currentUser, course))
        {
            lessons = lessons
                .Where(lesson => lesson.Status == LessonStatus.Published)
                .ToArray();
        }

        return lessons.Select(Map).ToArray();
    }

    public async Task<LessonDto> GetAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lessonId, "Lesson id");
        var lesson = await GetLessonAsync(lessonId, cancellationToken);
        var course = await GetCourseAsync(lesson.CourseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        EnsureStudentCanAccessPublishedLesson(course, lesson);
        return Map(lesson);
    }

    public async Task<LessonDto> UpdateAsync(
        Guid lessonId,
        UpdateLessonRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lessonId, "Lesson id");
        ValidateLesson(request.Name, request.Order, request.Status);
        var lesson = await GetLessonAsync(lessonId, cancellationToken);
        var course = await GetCourseAsync(lesson.CourseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);

        if (await _lessons.OrderExistsAsync(course.Id, request.Order, lesson.Id, cancellationToken))
        {
            throw new ConflictException("Lesson order already exists in this course.");
        }

        lesson.SetName(request.Name.Trim());
        lesson.SetContent(NormalizeOptional(request.Content));
        lesson.SetOrder(request.Order);
        lesson.SetStatus(request.Status);
        lesson.SetModifiedBy(actorId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(lesson);
    }

    public async Task<FileContent> GetMaterialContentAsync(
        Guid materialId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(materialId, "Material id");
        var material = await _materials.GetByIdAsync(materialId, cancellationToken)
            ?? throw new NotFoundException("Material was not found.");
        var lesson = await GetLessonAsync(material.LessonId, cancellationToken);
        var course = await GetCourseAsync(lesson.CourseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        EnsureStudentCanAccessPublishedLesson(course, lesson);
        var content = await _fileStorage.OpenReadAsync(material.ObjectKey, cancellationToken);
        return new FileContent(content, material.FileName, material.ContentType);
    }

    public async Task<IReadOnlyList<MaterialDto>> ListMaterialsAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lessonId, "Lesson id");
        var lesson = await GetLessonAsync(lessonId, cancellationToken);
        var course = await GetCourseAsync(lesson.CourseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        EnsureStudentCanAccessPublishedLesson(course, lesson);
        var materials = await _materials.ListByLessonAsync(lessonId, cancellationToken);
        return materials.Select(Map).ToArray();
    }

    public async Task<MaterialDto> UploadMaterialAsync(
        Guid lessonId,
        FileUpload upload,
        CancellationToken cancellationToken = default)
    {
        ValidateId(lessonId, "Lesson id");
        FileUploadPolicy.Validate(upload, FileUploadPolicy.MaximumCourseFileSizeBytes);
        var lesson = await GetLessonAsync(lessonId, cancellationToken);
        var course = await GetCourseAsync(lesson.CourseId, cancellationToken);
        var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);
        var storedFile = await _fileStorage.UploadAsync(
            upload,
            $"courses/{course.Id:N}/lessons/{lesson.Id:N}/materials",
            FileUploadPolicy.MaximumCourseFileSizeBytes,
            cancellationToken: cancellationToken);

        try
        {
            var material = new Material(
                lesson.Id,
                storedFile.FileName,
                storedFile.ObjectKey,
                storedFile.ContentType,
                storedFile.FileSize,
                actorId);
            _materials.Add(material);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Map(material);
        }
        catch
        {
            await CleanupFileAsync(storedFile.ObjectKey);
            throw;
        }
    }

    public async Task DeleteMaterialAsync(
        Guid materialId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(materialId, "Material id");
        var material = await _materials.GetByIdAsync(materialId, cancellationToken)
            ?? throw new NotFoundException("Material was not found.");
        var lesson = await GetLessonAsync(material.LessonId, cancellationToken);
        var course = await GetCourseAsync(lesson.CourseId, cancellationToken);
        CourseAuthorization.EnsureCanManage(_currentUser, course);

        _materials.Remove(material);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await CleanupFileAsync(material.ObjectKey);
    }

    public async Task DeleteAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        ValidateId(lessonId, "Lesson id");
        var lesson = await GetLessonAsync(lessonId, cancellationToken);
        var course = await GetCourseAsync(lesson.CourseId, cancellationToken);
        CourseAuthorization.EnsureCanManage(_currentUser, course);
        var materials = await _materials.ListByLessonAsync(lesson.Id, cancellationToken);
        _lessons.Remove(lesson);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var material in materials)
        {
            await CleanupFileAsync(material.ObjectKey);
        }
    }

    private Task CleanupFileAsync(string objectKey)
    {
        if (_fileCleanup is null)
        {
            return _fileStorage.DeleteIfExistsAsync(objectKey, CancellationToken.None);
        }

        _fileCleanup.Enqueue(objectKey);
        return Task.CompletedTask;
    }

    private void EnsureStudentCanAccessPublishedLesson(Course course, Lesson lesson)
    {
        if (CourseAuthorization.IsCourseManager(_currentUser, course))
        {
            return;
        }

        if (lesson.Status != LessonStatus.Published)
        {
            throw new NotFoundException("Lesson was not found.");
        }
    }

    private async Task<Course> GetCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        await _courses.GetByIdAsync(courseId, false, cancellationToken)
        ?? throw new NotFoundException("Course was not found.");

    private async Task<Lesson> GetLessonAsync(Guid lessonId, CancellationToken cancellationToken) =>
        await _lessons.GetByIdAsync(lessonId, cancellationToken)
        ?? throw new NotFoundException("Lesson was not found.");

    private static void ValidateLesson(string name, int order, LessonStatus status)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new BadRequestException("Lesson name must contain between 1 and 200 characters.");
        }

        if (order < 0)
        {
            throw new BadRequestException("Lesson order cannot be negative.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new BadRequestException("Lesson status is invalid.");
        }
    }

    private static void ValidateId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new BadRequestException($"{fieldName} is required.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LessonDto Map(Lesson lesson) => new(
        lesson.Id,
        lesson.CourseId,
        lesson.Name,
        lesson.Content,
        lesson.Order,
        lesson.Status,
        lesson.CreatedAt,
        lesson.ModifiedAt);

    private static MaterialDto Map(Material material) => new(
        material.Id,
        material.LessonId,
        material.FileName,
        material.ObjectKey,
        material.ContentType,
        material.FileSize,
        material.CreatedAt,
        material.ModifiedAt);
}
