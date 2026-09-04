using LMS.Api.application.abstractions.storage;
using LMS.Api.application.features.coursecontents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController, Route("api")]
[Authorize]
public sealed class CourseContentController : ControllerBase
{
    [HttpPost("courses/{courseId:guid}/lessons")]
    public async Task<ActionResult<LessonDto>> CreateLesson(Guid courseId, CreateLessonRequest request, ILessonService service, CancellationToken ct) => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(courseId, request, ct));
    [HttpGet("courses/{courseId:guid}/lessons")]
    public async Task<ActionResult<IReadOnlyList<LessonDto>>> ListLessons(Guid courseId, ILessonService service, CancellationToken ct) => Ok(await service.ListAsync(courseId, ct));
    [HttpPut("lessons/{lessonId:guid}")]
    public async Task<ActionResult<LessonDto>> UpdateLesson(Guid lessonId, UpdateLessonRequest request, ILessonService service, CancellationToken ct) => Ok(await service.UpdateAsync(lessonId, request, ct));
    [HttpPost("lessons/{lessonId:guid}/materials")]
    public async Task<ActionResult<MaterialDto>> UploadMaterial(Guid lessonId, IFormFile file, ILessonService service, CancellationToken ct) => Ok(await service.UploadMaterialAsync(lessonId, ToUpload(file), ct));
    [HttpDelete("materials/{materialId:guid}")]
    public async Task<IActionResult> DeleteMaterial(Guid materialId, ILessonService service, CancellationToken ct) { await service.DeleteMaterialAsync(materialId, ct); return NoContent(); }
    [HttpPost("courses/{courseId:guid}/exercises")]
    public async Task<ActionResult<ExerciseDto>> CreateExercise(Guid courseId, CreateExerciseRequest request, IExerciseService service, CancellationToken ct) => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(courseId, request, ct));
    [HttpGet("courses/{courseId:guid}/exercises")]
    public async Task<ActionResult<IReadOnlyList<ExerciseDto>>> ListExercises(Guid courseId, IExerciseService service, CancellationToken ct) => Ok(await service.ListAsync(courseId, ct));
    [HttpPut("exercises/{exerciseId:guid}")]
    public async Task<ActionResult<ExerciseDto>> UpdateExercise(Guid exerciseId, UpdateExerciseRequest request, IExerciseService service, CancellationToken ct) => Ok(await service.UpdateAsync(exerciseId, request, ct));
    [HttpPost("exercises/{exerciseId:guid}/attachment")]
    public async Task<ActionResult<ExerciseDto>> UploadAttachment(Guid exerciseId, IFormFile file, IExerciseService service, CancellationToken ct) => Ok(await service.UploadAttachmentAsync(exerciseId, ToUpload(file), ct));
    [HttpPost("exercises/{exerciseId:guid}/submissions")]
    public async Task<ActionResult<SubmissionDto>> UploadSubmission(Guid exerciseId, IFormFile file, ISubmissionService service, CancellationToken ct) => Ok(await service.UploadAsync(exerciseId, ToUpload(file), ct));
    [HttpPost("submissions/{submissionId:guid}/submit")]
    public async Task<ActionResult<SubmissionDto>> Submit(Guid submissionId, ISubmissionService service, CancellationToken ct) => Ok(await service.SubmitAsync(submissionId, ct));
    [HttpPost("submissions/{submissionId:guid}/unsubmit")]
    public async Task<ActionResult<SubmissionDto>> Unsubmit(Guid submissionId, ISubmissionService service, CancellationToken ct) => Ok(await service.UnsubmitAsync(submissionId, ct));
    private static FileUpload ToUpload(IFormFile? file) => file is null
        ? throw new LMS.Api.domain.exceptions.BadRequestException("A file is required.")
        : new(file.FileName, file.ContentType, file.Length, file.OpenReadStream());
}
