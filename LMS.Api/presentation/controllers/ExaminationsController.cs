using LMS.Api.application.features.examinations;
using LMS.Api.application.features.examinationattempts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController, Route("api")]
[Authorize]
public sealed class ExaminationsController : ControllerBase
{
    [HttpPost("courses/{courseId:guid}/examinations")]
    public async Task<ActionResult<ExaminationDto>> Create(Guid courseId, CreateExaminationRequest request, IExaminationService service, CancellationToken ct) => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(courseId, request, ct));

    [HttpGet("courses/{courseId:guid}/examinations")]
    public async Task<ActionResult<IReadOnlyList<ExaminationDto>>> List(Guid courseId, IExaminationService service, CancellationToken ct) => Ok(await service.ListByCourseAsync(courseId, ct));

    [HttpGet("courses/{courseId:guid}/examinations/available")]
    public async Task<ActionResult<IReadOnlyList<StudentExaminationDto>>> ListAvailable(Guid courseId, IExaminationService service, CancellationToken ct) => Ok(await service.ListAvailableForStudentAsync(courseId, ct));

    [HttpGet("examinations/{id:guid}")]
    public async Task<ActionResult<ExaminationDto>> Get(Guid id, IExaminationService service, CancellationToken ct) => Ok(await service.GetForManagementAsync(id, ct));

    [HttpGet("examinations/{id:guid}/student")]
    public async Task<ActionResult<StudentExaminationDto>> GetForStudent(Guid id, IExaminationService service, CancellationToken ct) => Ok(await service.GetForStudentAsync(id, ct));

    [HttpGet("examinations/{id:guid}/my-attempt")]
    public async Task<ActionResult<ExaminationAttemptDto>> GetMyAttempt(Guid id, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.GetMineAsync(id, ct));

    [HttpGet("examinations/{id:guid}/attempts")]
    public async Task<ActionResult<LMS.Api.application.common.PagedResult<ExaminationAttemptResultDto>>> ListAttempts(
        Guid id,
        [FromQuery] ExaminationAttemptQuery query,
        IExaminationAttemptService service,
        CancellationToken ct) => Ok(await service.ListResultsAsync(id, query, ct));

    [HttpPut("examinations/{id:guid}")]
    public async Task<ActionResult<ExaminationDto>> Update(Guid id, UpdateExaminationRequest request, IExaminationService service, CancellationToken ct) => Ok(await service.UpdateAsync(id, request, ct));

    [HttpPut("examinations/{id:guid}/question-set")]
    public async Task<ActionResult<ExaminationDto>> UpdateQuestionSet(Guid id, UpdateExaminationQuestionSetRequest request, IExaminationService service, CancellationToken ct) => Ok(await service.UpdateQuestionSetAsync(id, request, ct));

    [HttpPost("examinations/{id:guid}/publish")]
    public async Task<ActionResult<ExaminationDto>> Publish(Guid id, IExaminationService service, CancellationToken ct) => Ok(await service.PublishAsync(id, ct));

    [HttpPost("examinations/{id:guid}/close")]
    public async Task<ActionResult<ExaminationDto>> Close(Guid id, IExaminationService service, CancellationToken ct) => Ok(await service.CloseAsync(id, ct));
}
